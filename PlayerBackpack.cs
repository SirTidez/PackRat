using System.Collections;
using System.Reflection;
using PackRat.Config;
using PackRat.Helpers;
using PackRat.Networking;
using PackRat.Shops;
using UnityEngine;

#if MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.Core.Items.Framework;
using ScheduleOne.ItemFramework;
using ScheduleOne.Levelling;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Product;
using ScheduleOne.Product.Packaging;
using ScheduleOne.Storage;
using ScheduleOne.Tools;
using ScheduleOne.UI;
using ScheduleOne.UI.Phone;
#else
using MelonLoader;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Product.Packaging;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.Tools;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
using Il2CppSystem.Linq;
#endif

namespace PackRat;

/// <summary>
/// Core MonoBehaviour component that manages the player's backpack state.
/// Attached to the player's local GameObject by <see cref="Patches.PlayerSpawnerPatch"/>.
/// </summary>
#if !MONO
[RegisterTypeInIl2Cpp]
#endif
public class PlayerBackpack : MonoBehaviour
{
    public const string StorageName = "Backpack";
    public const int MinimumStorageSlots = 1;

    private bool _backpackEnabled = true;
    private StorageEntity _storage;
    private int _lastTierIndex = -2; // sentinel: distinct from -1 (not unlocked) to force initial apply
    private string _openTitle;
    private int _equippedTierIndex = -1;
    private const int TierCheckIntervalFrames = 60; // throttle tier lookup to reduce per-frame work
    private static Type _cachedStorageMenuType;
    private static MethodInfo[] _cachedStorageMenuOpenMethods = Array.Empty<MethodInfo>();
    private static readonly string[] SelectedHotbarIndexMemberNames =
    [
        "selectedSlotIndex", "SelectedSlotIndex", "selectedIndex", "SelectedIndex", "currentSlotIndex",
        "CurrentSlotIndex", "activeSlotIndex", "ActiveSlotIndex", "activeIndex", "ActiveIndex",
        "equippedSlotIndex", "EquippedSlotIndex", "SelectedSlot", "selectedSlot", "slotIndex", "SlotIndex"
    ];

#if !MONO
    public PlayerBackpack(IntPtr ptr) : base(ptr)
    {
    }
#endif

    /// <summary>
    /// The local player's backpack instance.
    /// </summary>
    public static PlayerBackpack Instance { get; private set; }

    /// <summary>
    /// Currently equipped backpack tier index (0-4), or -1 if none.
    /// </summary>
    public int EquippedTierIndex => _equippedTierIndex;

    /// <summary>
    /// Legacy alias retained for compatibility with older call sites and save-path code.
    /// </summary>
    public int HighestPurchasedTierIndex => EquippedTierIndex;

    /// <summary>
    /// Sets the equipped tier (e.g. from save data or after using a backpack item). Clamps to valid range.
    /// </summary>
    public void SetEquippedTierIndex(int tierIndex)
    {
        _equippedTierIndex = tierIndex < 0 ? -1 : Math.Min(tierIndex, Configuration.BackpackTiers.Length - 1);
    }

    /// <summary>
    /// Legacy alias retained for compatibility with older call sites.
    /// </summary>
    public void SetHighestPurchasedTierIndex(int tierIndex)
    {
        SetEquippedTierIndex(tierIndex);
    }

    /// <summary>
    /// Applies a restored purchased tier immediately so storage size matches before item data is loaded.
    /// </summary>
    public void RestorePurchasedTier(int tierIndex)
    {
        SetEquippedTierIndex(tierIndex);
        EnsureCorrectTierApplied();
    }

    /// <summary>
    /// Returns the effective tier index (highest purchased tier that is enabled), or -1 if none.
    /// </summary>
    public int CurrentTierIndex
    {
        get
        {
            var cfg = Configuration.Instance;
            if (_equippedTierIndex < 0)
                return -1;
            for (var i = _equippedTierIndex; i >= 0; i--)
            {
                if (i < cfg.TierEnabled.Length && cfg.TierEnabled[i])
                    return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// Returns the current tier definition, or null if the backpack is not yet unlocked.
    /// </summary>
#if !MONO
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
#endif
    public BackpackTierDefinition CurrentTier
    {
        get
        {
            var idx = CurrentTierIndex;
            return idx >= 0 ? Configuration.BackpackTiers[idx] : null;
        }
    }

    /// <summary>
    /// Whether the backpack has been unlocked at the current player rank.
    /// </summary>
    public bool IsUnlocked => CurrentTierIndex >= 0;

    /// <summary>
    /// Whether police body searches include the backpack (true at tier 2 and above).
    /// </summary>
    public bool IsPoliceSearchable => CurrentTierIndex >= 2;

    /// <summary>
    /// Whether the backpack storage menu is currently open.
    /// </summary>
    public bool IsOpen
    {
        get
        {
            var storageMenu = Singleton<StorageMenu>.Instance;
            return storageMenu != null
                && storageMenu.IsOpen
                && storageMenu.TitleLabel != null
                && storageMenu.TitleLabel.text == _openTitle;
        }
    }

#if !MONO
    public Il2CppSystem.Collections.Generic.List<ItemSlot> ItemSlots =>
        _storage.ItemSlots.Cast<Il2CppSystem.Collections.Generic.IEnumerable<ItemSlot>>().ToList();
#else
    public List<ItemSlot> ItemSlots => _storage.ItemSlots.ToList();
#endif

    private void Awake()
    {
        _storage = gameObject.GetComponentInParent<StorageEntity>();
        if (_storage == null)
        {
            ModLogger.Error("Player does not have a BackpackStorage component!");
            return;
        }

        ModLogger.Info("Configuring backpack storage...");
        ModLogger.Debug($"[BackpackUI] Awake: object='{name}', storage='{_storage.name}'.");
        // Defer configuration to next frame to avoid triggering MonoMod/Harmony detour compilation
        // during initial JIT (fatal CLR error 0x80131506 in DetourRuntimeNETCore30Platform.CompileMethodHook).
        MelonLoader.MelonCoroutines.Start(DeferredConfigureStorage(this));
    }

    private static IEnumerator DeferredConfigureStorage(PlayerBackpack instance)
    {
        yield return null;
        if (instance == null || instance._storage == null)
            yield break;
        var tierIdx = instance.CurrentTierIndex;
        var slotCount = tierIdx >= 0
            ? Configuration.Instance.TierSlotCounts[tierIdx]
            : Configuration.BackpackTiers[0].DefaultSlotCount;
        instance.UpdateSize(slotCount);
        instance.OnStartClient(instance.IsOwnedByLocalPlayer());
    }

    private void Update()
    {
        // A PackRat-owned mouse click explicitly returns the shared browser to mouse mode. This
        // leaves the click available for the game's normal drag/drop and tooltip handling.
        if (IsOpen)
            Patches.StorageMenuPatch.ClearStandaloneBackpackKeyboardFocusOnPointerInput();

        // Capture a replacement hotkey from the backpack settings pane before this frame's
        // normal toggle processing can close the menu with the old binding.
        if (IsOpen && Patches.StorageMenuPatch.HandleStandaloneBackpackSettingsInput())
            return;

        // Throttle tier check to every N frames to avoid per-frame config/array access (reduces hitches).
        var keyDown = Input.GetKeyDown(Configuration.Instance.ToggleKey);
        if (keyDown || (Time.frameCount % TierCheckIntervalFrames == 0))
        {
            var tierIdx = CurrentTierIndex;
            if (tierIdx != _lastTierIndex)
            {
                _lastTierIndex = tierIdx;
                ApplyCurrentTier(tierIdx);
            }
        }

        if (!_backpackEnabled)
            return;

        try
        {
            if (IsOpen && Patches.StorageMenuPatch.HandleStandaloneBackpackKeyboardNavigation())
                return;

            if (!keyDown)
                return;

            // The toggle key is also a valid character in the live search field. Let the focused
            // InputField consume it before considering an open/close backpack action.
            if (IsOpen && Patches.StorageMenuPatch.IsStandaloneBackpackSearchFocused())
                return;

            // If the player has a backpack tier item selected in the hotbar, consuming it applies the tier and opens the backpack
            if (TryConsumeSelectedHotbarBackpackItem(out var appliedTier))
            {
                if (appliedTier >= 0)
                    ModLogger.Info($"Backpack tier {appliedTier} ({Configuration.BackpackTiers[appliedTier].Name}) applied; opening backpack.");
                if (IsOpen)
                    Close();
                else
                    Open();
                return;
            }

            // Otherwise open/close only if already unlocked
            if (!IsUnlocked)
            {
                ModLogger.Debug($"[BackpackUI] Hotkey ignored: no enabled backpack tier (equipped={_equippedTierIndex}, current={CurrentTierIndex}).");
                return;
            }
            if (IsOpen)
                Close();
            else
                Open();
        }
        catch (Exception e)
        {
            ModLogger.Error("Error toggling backpack", e);
        }
    }

    /// <summary>
    /// If the currently selected hotbar slot (or any hotbar slot if selected is unknown) contains a PackRat backpack-tier item,
    /// removes it and applies the tier.
    /// </summary>
    /// <param name="appliedTier">The tier index that was applied, or -1 if no item was consumed.</param>
    /// <returns>True if we consumed an item (or already had that tier); then caller should open/close. False if no backpack item in hotbar.</returns>
    private bool TryConsumeSelectedHotbarBackpackItem(out int appliedTier)
    {
        appliedTier = -1;
        try
        {
#if MONO
            var inv = PlayerInventory.Instance;
#else
            var inv = PlayerSingleton<PlayerInventory>.Instance;
#endif
            if (inv == null)
                return false;
            var hotbarSlots = ReflectionUtils.TryGetFieldOrProperty(inv, "hotbarSlots");
            if (hotbarSlots == null)
                return false;
            var count = ReflectionUtils.TryGetListCount(hotbarSlots);
            if (count <= 0)
                return false;

            var selectedIndex = GetSelectedHotbarIndex(inv);
            if (selectedIndex >= 0 && selectedIndex < count)
            {
                if (TryConsumeBackpackItemFromSlot(inv, hotbarSlots, selectedIndex, out appliedTier))
                    return true;
            }

            // Fallback: selected index not found or slot didn't have our item - scan all hotbar slots and consume first backpack item
            for (var i = 0; i < count; i++)
            {
                if (TryConsumeBackpackItemFromSlot(inv, hotbarSlots, i, out appliedTier))
                    return true;
            }

            // Fallback 2: hotbarSlots might not be where the visible hotbar lives - scan every list on inventory for our item
            var allLists = ReflectionUtils.TryGetAllListLikeMembers(inv);
            foreach (var list in allLists)
            {
                if (list == hotbarSlots) continue;
                var listCount = ReflectionUtils.TryGetListCount(list);
                for (var i = 0; i < listCount; i++)
                {
                    if (TryConsumeBackpackItemFromSlot(inv, list, i, out appliedTier))
                        return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            ModLogger.Error("TryConsumeSelectedHotbarBackpackItem", ex);
            return false;
        }
    }

#if !MONO
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
#endif
    private bool TryConsumeBackpackItemFromSlot(object playerInventory, object slotsList, int index, out int appliedTier)
    {
        appliedTier = -1;
        var slot = ReflectionUtils.TryGetListItem(slotsList, index);
        if (slot == null)
            return false;
        var itemInstance = ReflectionUtils.TryGetFieldOrProperty(slot, "ItemInstance");
        if (itemInstance == null)
            return false;
        var def = ReflectionUtils.TryGetFieldOrProperty(itemInstance, "Definition");
        if (def == null)
            return false;
        var idObj = ReflectionUtils.TryGetFieldOrProperty(def, "ID") ?? ReflectionUtils.TryGetFieldOrProperty(def, "id");
        var id = idObj as string ?? idObj?.ToString();
        if (string.IsNullOrEmpty(id) || !id.StartsWith(BackpackShopIntegration.BackpackItemIdPrefix, StringComparison.Ordinal))
            return false;
        if (!BackpackShopIntegration.IsBackpackTierPurchase(id, out var tierIndex) || tierIndex < 0)
            return false;
        ClearSlotItem(slot);
        RefreshInventoryUIAfterSlotChange(playerInventory, slot);
        if (tierIndex != _equippedTierIndex)
        {
            SetEquippedTierIndex(tierIndex);
            ApplyTierAfterPurchase(tierIndex);
            BackpackShopIntegration.RefreshBackpackListingsInAllShops();
            appliedTier = tierIndex;
        }
        return true;
    }

    private static int GetSelectedHotbarIndex(object playerInventory)
    {
        if (playerInventory == null) return -1;
        foreach (var name in SelectedHotbarIndexMemberNames)
        {
            var val = ReflectionUtils.TryGetFieldOrProperty(playerInventory, name);
            if (val == null) continue;
            if (val is int i && i >= 0) return i;
            if (val is byte b) return b;
            if (val is short s && s >= 0) return s;
            if (val is long l && l >= 0 && l <= int.MaxValue) return (int)l;
        }
        return -1;
    }

    private static void ClearSlotItem(object slot)
    {
        if (slot == null) return;
        var type = slot.GetType();
        var clear = ReflectionUtils.GetMethod(type, "ClearStoredInstance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? ReflectionUtils.GetMethod(type, "Clear", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? ReflectionUtils.GetMethod(type, "ClearSlot", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (clear != null)
        {
            try
            {
                clear.Invoke(slot, null);
                return;
            }
            catch
            {
            }
        }

        ReflectionUtils.TrySetFieldOrProperty(slot, "ItemInstance", null);
    }

    /// <summary>
    /// After clearing a slot, trigger UI refresh so the hotbar/inventory display updates (avoids visual glitch where slot still shows old item).
    /// </summary>
    private static void RefreshInventoryUIAfterSlotChange(object playerInventory, object slotThatChanged)
    {
        if (slotThatChanged != null)
            ReflectionUtils.TryInvokeParameterlessCallback(slotThatChanged, "onItemDataChanged", "OnItemDataChanged", "ItemDataChanged");
        if (playerInventory != null)
        {
            ReflectionUtils.TryInvokeParameterlessCallback(playerInventory, "Refresh", "RefreshUI", "UpdateDisplay", "OnInventoryChanged", "NotifySlotsChanged", "Rebuild");
        }
    }

    /// <summary>
    /// Applies the slot count for the given tier, resizing storage if needed.
    /// </summary>
    private void ApplyCurrentTier(int tierIdx)
    {
        if (tierIdx < 0)
            return;

        var targetSlots = Configuration.Instance.TierSlotCounts[tierIdx];
        if (_storage.SlotCount == targetSlots && _storage.ItemSlots.Count == targetSlots)
            return;

        ModLogger.Info($"Backpack upgraded to {Configuration.BackpackTiers[tierIdx].Name} ({targetSlots} slots).");
        UpdateSize(targetSlots);
    }

    /// <summary>
    /// Called after the player purchases a backpack tier at the hardware store. Applies the tier (resize storage).
    /// </summary>
    public void ApplyTierAfterPurchase(int tierIdx)
    {
        if (tierIdx < 0)
            return;
        _lastTierIndex = tierIdx;
        ApplyCurrentTier(tierIdx);
    }

    /// <summary>
    /// Ensures the storage is correctly sized for the current tier. Called after loading a saved tier index.
    /// </summary>
    public void EnsureCorrectTierApplied()
    {
        _lastTierIndex = CurrentTierIndex;

        if (_storage == null || _lastTierIndex < 0)
            return;

        ApplyCurrentTier(_lastTierIndex);
    }

    /// <summary>
    /// Enables or disables the backpack. Closes if currently open when disabled.
    /// </summary>
    /// <param name="state">True to enable; false to disable.</param>
    public void SetBackpackEnabled(bool state)
    {
        if (!state)
            Close();

        _backpackEnabled = state;
    }

    /// <summary>
    /// Opens the backpack storage menu if conditions allow.
    /// </summary>
    public void Open()
    {
        if (!_backpackEnabled)
        {
            ModLogger.Debug("Backpack open blocked: backpack disabled.");
            return;
        }
        if (!IsUnlocked)
        {
            ModLogger.Debug($"Backpack open blocked: not unlocked (CurrentTierIndex={CurrentTierIndex}, EquippedTier={_equippedTierIndex}). Purchase a tier at the Hardware Store.");
            return;
        }
        if (_storage == null)
        {
            ModLogger.Warn("Backpack open blocked: no storage entity.");
            return;
        }
        var clipboard = Singleton<ManagementClipboard>.Instance;
        if (clipboard != null && clipboard.IsEquipped)
        {
            ModLogger.Debug("[BackpackUI] Open blocked: management clipboard is equipped.");
            return;
        }

        var storageMenu = Singleton<StorageMenu>.Instance;
        if (storageMenu == null)
        {
            ModLogger.Warn("[BackpackUI] Open blocked: StorageMenu is not available yet.");
            return;
        }

        if (storageMenu.IsOpen)
        {
            ModLogger.Debug("[BackpackUI] Open blocked: another storage menu is already open.");
            return;
        }

        if (Phone.Instance != null && Phone.Instance.IsOpen)
        {
            ModLogger.Debug("[BackpackUI] Open blocked: phone is open.");
            return;
        }

        if (CameraLockedStateHelper.IsCameraLockedByUI())
        {
            ModLogger.Debug("Backpack blocked: player is in camera-locked UI (TV, ATM, dialogue, vehicle, etc.).");
            return;
        }

        _openTitle = CurrentTier?.Name ?? StorageName;
        ModLogger.Info($"[BackpackUI] PlayerBackpack.Open -> StorageMenu.Open: title='{_openTitle}', slots={_storage.ItemSlots.Count}.");
        BackpackStateSyncManager.BeginLocalBackpackEdit();
        // Keep the regular backpack view at a predictable four-row grid. The storage-menu patch
        // pages the backing slots, so larger bags do not force the game to shrink the slot UI.
        storageMenu.SlotGridLayout.constraintCount = 4;

#if !MONO
        OpenStorageMenu(storageMenu, _storage.Cast<IItemSlotOwner>(), _openTitle, string.Empty);
#else
        OpenStorageMenu(storageMenu, _storage, _openTitle, string.Empty);
#endif

        _storage.SendAccessor(Player.Local.NetworkObject);
    }

    private static void OpenStorageMenu(StorageMenu storageMenu, IItemSlotOwner owner, string title, string subtitle)
    {
        if (storageMenu == null || owner == null)
            return;

        PrewarmStorageMenuOpenMethods(storageMenu);
        var methods = _cachedStorageMenuOpenMethods;
        for (var i = 0; i < methods.Length; i++)
        {
            var method = methods[i];
            if (method.Name != "Open")
                continue;

            var parameters = method.GetParameters();
            try
            {
                if (parameters.Length == 4
                    && parameters[0].ParameterType.IsInstanceOfType(owner)
                    && parameters[1].ParameterType == typeof(string)
                    && parameters[2].ParameterType == typeof(string))
                {
                    method.Invoke(storageMenu, new object[] { owner, title, subtitle, null });
                    return;
                }

                if (parameters.Length == 3
                    && parameters[0].ParameterType == typeof(string)
                    && parameters[1].ParameterType == typeof(string)
                    && parameters[2].ParameterType.IsInstanceOfType(owner))
                {
                    method.Invoke(storageMenu, new object[] { title, subtitle, owner });
                    return;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Failed to open backpack storage menu", ex);
                return;
            }
        }

        ModLogger.Error("Failed to find a compatible StorageMenu.Open overload for the backpack.");
    }

    private static void PrewarmStorageMenuOpenMethods(StorageMenu storageMenu)
    {
        if (storageMenu == null)
            return;

        var storageMenuType = storageMenu.GetType();
        if (_cachedStorageMenuType == storageMenuType && _cachedStorageMenuOpenMethods.Length > 0)
            return;

        var candidates = storageMenuType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var openMethods = new System.Collections.Generic.List<MethodInfo>();
        for (var i = 0; i < candidates.Length; i++)
        {
            if (candidates[i].Name == "Open")
                openMethods.Add(candidates[i]);
        }

        _cachedStorageMenuType = storageMenuType;
        _cachedStorageMenuOpenMethods = openMethods.ToArray();
    }

    private static void PrewarmPlayerInventoryReflection()
    {
#if MONO
        var inventory = PlayerInventory.Instance;
#else
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
#endif
        if (inventory == null)
            return;

        var inventoryType = inventory.GetType();
        ReflectionUtils.PrewarmReadableMembers(inventoryType, "hotbarSlots");
        ReflectionUtils.PrewarmReadableMembers(inventoryType, SelectedHotbarIndexMemberNames);
        ReflectionUtils.PrewarmListLikeMembers(inventoryType);

        var hotbarSlots = ReflectionUtils.TryGetFieldOrProperty(inventory, "hotbarSlots");
        var count = ReflectionUtils.TryGetListCount(hotbarSlots);
        for (var i = 0; i < count; i++)
        {
            var slot = ReflectionUtils.TryGetListItem(hotbarSlots, i);
            if (slot == null)
                continue;
            ReflectionUtils.PrewarmReadableMembers(slot.GetType(), "ItemInstance");
            var item = ReflectionUtils.TryGetFieldOrProperty(slot, "ItemInstance");
            if (item == null)
                continue;
            ReflectionUtils.PrewarmReadableMembers(item.GetType(), "Definition");
            var definition = ReflectionUtils.TryGetFieldOrProperty(item, "Definition");
            if (definition != null)
                ReflectionUtils.PrewarmReadableMembers(definition.GetType(), "ID", "id");
        }
    }

    /// <summary>
    /// Closes the backpack storage menu if it is open.
    /// </summary>
    public void Close()
    {
        if (!_backpackEnabled || !IsOpen)
            return;

        // CloseMenu only hides the internal storage panel. Close performs the matching UI-state
        // exit that releases the cursor, camera, and full-screen overlay just like Done/Escape.
        Singleton<StorageMenu>.Instance.Close();
        _storage.SendAccessor(null);
    }

    /// <summary>
    /// Checks whether the backpack contains items that would be flagged during a police search.
    /// </summary>
    /// <param name="maxStealthLevel">Maximum stealth level that passes without triggering detection.</param>
    /// <returns>True if any item in the backpack would trigger detection.</returns>
    public bool ContainsItemsOfInterest(EStealthLevel maxStealthLevel)
    {
        for (var i = 0; i < _storage.ItemSlots.Count; i++)
        {
#if !MONO
            var itemSlot = _storage.ItemSlots[new Index(i)].Cast<ItemSlot>();
#else
            var itemSlot = _storage.ItemSlots[i];
#endif
            if (itemSlot?.ItemInstance == null)
                continue;

            var productInstance = itemSlot.ItemInstance as ProductItemInstance;
            if (productInstance == null)
            {
                var legalStatus = ReflectionUtils.TryGetFieldOrProperty(itemSlot.ItemInstance.Definition, "legalStatus")
                    ?? ReflectionUtils.TryGetFieldOrProperty(itemSlot.ItemInstance.Definition, "LegalStatus");
                var legalStatusName = legalStatus?.ToString();
                if (!string.Equals(legalStatusName, "Legal", StringComparison.Ordinal))
                    return true;

                continue;
            }

            if (productInstance.AppliedPackaging == null || productInstance.AppliedPackaging.StealthLevel <= maxStealthLevel)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds slots to the backpack. Capacity is controlled by the configured tier slot counts.
    /// </summary>
    /// <param name="slotCount">Number of slots to add.</param>
    // TODO: This method will be invoked by the future manual upgrade mechanic (e.g., backpack item equip).
    public void Upgrade(int slotCount)
    {
        if (slotCount < MinimumStorageSlots || _storage == null)
            return;

        if (_storage.SlotCount > int.MaxValue - slotCount)
        {
            ModLogger.Warn("Cannot upgrade backpack: requested capacity exceeds the supported integer range.");
            return;
        }

        UpdateSize(_storage.SlotCount + slotCount);
    }

    /// <summary>
    /// Removes slots from the backpack. Will not reduce below 1 slot.
    /// </summary>
    /// <param name="slotCount">Number of slots to remove.</param>
    /// <param name="force">If true, removes slots even if they contain items.</param>
    // TODO: This method will be invoked by the future manual upgrade mechanic (e.g., backpack item equip).
    public void Downgrade(int slotCount, bool force = false)
    {
        if (slotCount < 1)
            return;

        if (!force && slotCount >= _storage.SlotCount)
        {
            ModLogger.Warn("Cannot downgrade backpack to zero slots. A minimum of one must remain.");
            return;
        }

        var newSlotCount = _storage.SlotCount - slotCount;
        if (newSlotCount < MinimumStorageSlots)
            newSlotCount = MinimumStorageSlots;

        if (force)
        {
            UpdateSize(newSlotCount);
            return;
        }

        var isSafeToRemove = true;
        var removedSlots = _storage.ItemSlots.GetRange(newSlotCount, _storage.SlotCount - newSlotCount);
        for (var i = 0; i < removedSlots.Count; i++)
        {
#if !MONO
            var itemSlot = removedSlots[new Index(i)].Cast<ItemSlot>();
#else
            var itemSlot = removedSlots[new Index(i)] as ItemSlot;
#endif
            if (itemSlot?.ItemInstance == null)
                continue;

            ModLogger.Warn($"Downgrading backpack will remove item: {itemSlot.ItemInstance.Definition.name}");
            isSafeToRemove = false;
        }

        if (!isSafeToRemove)
        {
            ModLogger.Warn("Cannot downgrade backpack due to items present in removed slots.");
            return;
        }

        UpdateSize(newSlotCount);
    }

    private void UpdateSize(int newSize)
    {
        newSize = Math.Max(MinimumStorageSlots, newSize);
        _storage.SlotCount = newSize;
        _storage.DisplayRowCount = newSize switch
        {
            <= 20 => (int)Math.Ceiling(newSize / 5.0),
            <= 80 => (int)Math.Ceiling(newSize / 10.0),
            _ => (int)Math.Ceiling(newSize / 16.0)
        };

        if (_storage.ItemSlots.Count > newSize)
        {
            _storage.ItemSlots.RemoveRange(newSize, _storage.ItemSlots.Count - newSize);
            return;
        }

        for (var i = _storage.ItemSlots.Count; i < newSize; i++)
        {
            var itemSlot = new ItemSlot();
            var slotCountBeforeOwnerAssignment = _storage.ItemSlots.Count;
#if !MONO
            if (itemSlot.onItemDataChanged == null)
                itemSlot.onItemDataChanged = (Il2CppSystem.Action)_storage.ContentsChanged;
            else
                itemSlot.onItemDataChanged.CombineImpl((Il2CppSystem.Action)_storage.ContentsChanged);

            itemSlot.SetSlotOwner(_storage.Cast<IItemSlotOwner>());
#else
            itemSlot.onItemDataChanged += _storage.ContentsChanged;
            itemSlot.SetSlotOwner(_storage);
#endif
            // Newer game builds can register the slot during owner assignment.
            if (_storage.ItemSlots.Count == slotCountBeforeOwnerAssignment)
                _storage.ItemSlots.Add(itemSlot);
        }
    }

    private void OnStartClient(bool isOwner)
    {
        if (!isOwner)
        {
            ModLogger.Info($"Destroying non-local player backpack on: {name}");
            Destroy(this);
            return;
        }

        if (Instance != null)
        {
            ModLogger.Warn($"Multiple instances of {name} exist. Keeping prior instance reference.");
            Destroy(this);
            return;
        }

        Instance = this;

        // The local player is created after the Main scene's UI singletons. Perform the one
        // scene-wide compatibility discovery now, during spawn, rather than on first input.
        CameraLockedStateHelper.RefreshSceneCache();
        PrewarmStorageMenuOpenMethods(Singleton<StorageMenu>.Instance);
        PrewarmPlayerInventoryReflection();
        Patches.StorageMenuPatch.PrewarmStandaloneAssets();
    }

    private bool IsOwnedByLocalPlayer()
    {
        var player = gameObject.GetComponentInParent<Player>();
        if (player == null)
        {
            ModLogger.Debug("[BackpackUI] Ownership check could not find Player; retaining local component fallback.");
            return true;
        }

        return player.IsOwner;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
