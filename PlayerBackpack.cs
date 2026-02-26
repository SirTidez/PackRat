using PackRat.Config;
using PackRat.Helpers;
using PackRat.Shops;
using UnityEngine;

#if MONO
using ScheduleOne.DevUtilities;
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
    public const int MaxStorageSlots = 40;

    private bool _backpackEnabled = true;
    private StorageEntity _storage;
    private int _lastTierIndex = -2; // sentinel: distinct from -1 (not unlocked) to force initial apply
    private string _openTitle;
    private int _highestPurchasedTierIndex = -1;

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
    /// Highest backpack tier index the player has purchased (0-4), or -1 if none.
    /// Set on load and when the player buys a tier at the hardware store.
    /// </summary>
    public int HighestPurchasedTierIndex => _highestPurchasedTierIndex;

    /// <summary>
    /// Sets the highest purchased tier (e.g. from save data or after a purchase). Clamps to valid range.
    /// </summary>
    public void SetHighestPurchasedTierIndex(int tierIndex)
    {
        _highestPurchasedTierIndex = tierIndex < 0 ? -1 : Math.Min(tierIndex, Configuration.BackpackTiers.Length - 1);
    }

    /// <summary>
    /// Returns the effective tier index (highest purchased tier that is enabled), or -1 if none.
    /// </summary>
    public int CurrentTierIndex
    {
        get
        {
            var cfg = Configuration.Instance;
            if (_highestPurchasedTierIndex < 0)
                return -1;
            for (var i = _highestPurchasedTierIndex; i >= 0; i--)
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
    public bool IsOpen => Singleton<StorageMenu>.Instance.IsOpen && Singleton<StorageMenu>.Instance.TitleLabel.text == _openTitle;

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
        var tierIdx = CurrentTierIndex;
        var slotCount = tierIdx >= 0
            ? Configuration.Instance.TierSlotCounts[tierIdx]
            : Configuration.BackpackTiers[0].DefaultSlotCount;
        UpdateSize(slotCount);
        OnStartClient(true);
    }

    private void Update()
    {
        var tierIdx = CurrentTierIndex;
        if (tierIdx != _lastTierIndex)
        {
            _lastTierIndex = tierIdx;
            ApplyCurrentTier(tierIdx);
        }

        if (!_backpackEnabled || !Input.GetKeyDown(Configuration.Instance.ToggleKey))
            return;

        try
        {
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
                return;
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
        if (tierIndex > _highestPurchasedTierIndex)
        {
            SetHighestPurchasedTierIndex(tierIndex);
            ApplyTierAfterPurchase(tierIndex);
            // Remove all tiers 0..tierIndex from the store (buying a tier counts as having all lower tiers)
            for (var i = 0; i <= tierIndex; i++)
                BackpackShopIntegration.RemoveTierListingFromAllShops(i);
            appliedTier = tierIndex;
        }
        return true;
    }

    private static int GetSelectedHotbarIndex(object playerInventory)
    {
        if (playerInventory == null) return -1;
        foreach (var name in new[] { "selectedSlotIndex", "SelectedSlotIndex", "selectedIndex", "SelectedIndex", "currentSlotIndex", "CurrentSlotIndex", "activeSlotIndex", "ActiveSlotIndex", "activeIndex", "ActiveIndex", "equippedSlotIndex", "EquippedSlotIndex", "SelectedSlot", "selectedSlot", "slotIndex", "SlotIndex" })
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
        if (ReflectionUtils.TrySetFieldOrProperty(slot, "ItemInstance", null))
            return;
        var type = slot.GetType();
        var clear = type.GetMethod("Clear", Type.EmptyTypes) ?? type.GetMethod("ClearSlot", Type.EmptyTypes);
        if (clear != null)
        {
            try { clear.Invoke(slot, null); } catch { }
        }
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
        if (_storage.SlotCount == targetSlots)
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
            ModLogger.Debug($"Backpack open blocked: not unlocked (CurrentTierIndex={CurrentTierIndex}, HighestPurchased={_highestPurchasedTierIndex}). Purchase a tier at the Hardware Store.");
            return;
        }
        if (_storage == null)
        {
            ModLogger.Warn("Backpack open blocked: no storage entity.");
            return;
        }
        if (Singleton<ManagementClipboard>.Instance.IsEquipped || Singleton<StorageMenu>.Instance.IsOpen || Phone.Instance.IsOpen)
            return;

        if (CameraLockedStateHelper.IsCameraLockedByUI())
        {
            ModLogger.Debug("Backpack blocked: player is in camera-locked UI (TV, ATM, dialogue, vehicle, etc.).");
            return;
        }

        _openTitle = CurrentTier?.Name ?? StorageName;
        var storageMenu = Singleton<StorageMenu>.Instance;
        storageMenu.SlotGridLayout.constraintCount = _storage.DisplayRowCount;

#if !MONO
        storageMenu.Open(_openTitle, string.Empty, _storage.Cast<IItemSlotOwner>());
#else
        storageMenu.Open(_openTitle, string.Empty, _storage);
#endif

        _storage.SendAccessor(Player.Local.NetworkObject);
    }

    /// <summary>
    /// Closes the backpack storage menu if it is open.
    /// </summary>
    public void Close()
    {
        if (!_backpackEnabled || !IsOpen)
            return;

        Singleton<StorageMenu>.Instance.CloseMenu();
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

#if !MONO
            var productInstance = itemSlot.ItemInstance.TryCast<ProductItemInstance>();
#else
            var productInstance = itemSlot.ItemInstance as ProductItemInstance;
#endif
            if (productInstance == null)
            {
                if (itemSlot.ItemInstance.Definition.legalStatus != ELegalStatus.Legal)
                    return true;

                continue;
            }

            if (productInstance.AppliedPackaging == null || productInstance.AppliedPackaging.StealthLevel <= maxStealthLevel)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds slots to the backpack up to <see cref="MaxStorageSlots"/>.
    /// </summary>
    /// <param name="slotCount">Number of slots to add.</param>
    // TODO: This method will be invoked by the future manual upgrade mechanic (e.g., backpack item equip).
    public void Upgrade(int slotCount)
    {
        if (slotCount is < 1 or > MaxStorageSlots)
            return;

        var newSlotCount = _storage.SlotCount + slotCount;
        if (newSlotCount > MaxStorageSlots)
        {
            ModLogger.Warn($"Cannot upgrade backpack to more than {MaxStorageSlots} slots.");
            return;
        }

        UpdateSize(newSlotCount);
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
        if (newSlotCount < 1)
            newSlotCount = 1;

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
            return;
        }

        Instance = this;

        // Pre-resolve reflection types so the first backpack keypress doesn't stall
        CameraLockedStateHelper.PrewarmCache();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
