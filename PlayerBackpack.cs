using PackRat.Config;
using PackRat.Helpers;
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
    public const int MaxStorageSlots = 128;

    private bool _backpackEnabled = true;
    private StorageEntity _storage;

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
    /// Whether the backpack has been unlocked at the current player rank.
    /// </summary>
    public bool IsUnlocked => NetworkSingleton<LevelManager>.Instance.GetFullRank() >= Configuration.Instance.UnlockLevel;

    /// <summary>
    /// Whether the backpack storage menu is currently open.
    /// </summary>
    public bool IsOpen => Singleton<StorageMenu>.Instance.IsOpen && Singleton<StorageMenu>.Instance.TitleLabel.text == StorageName;

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
        UpdateSize(Configuration.Instance.StorageSlots);
        OnStartClient(true);
    }

    private void Update()
    {
        if (!_backpackEnabled || !IsUnlocked || !Input.GetKeyDown(Configuration.Instance.ToggleKey))
            return;

        try
        {
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
        if (!_backpackEnabled || !IsUnlocked || Singleton<ManagementClipboard>.Instance.IsEquipped
            || Singleton<StorageMenu>.Instance.IsOpen || Phone.Instance.IsOpen)
            return;

        var storageMenu = Singleton<StorageMenu>.Instance;
        storageMenu.SlotGridLayout.constraintCount = _storage.DisplayRowCount;

#if !MONO
        storageMenu.Open(StorageName, string.Empty, _storage.Cast<IItemSlotOwner>());
#else
        storageMenu.Open(StorageName, string.Empty, _storage);
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
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
