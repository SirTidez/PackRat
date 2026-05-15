using HarmonyLib;
using PackRat.Config;
using PackRat.Extensions;
using PackRat.Helpers;
using UnityEngine;
using UnityEngine.UI;

#if MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Money;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using S1TMP = TMPro.TextMeshProUGUI;
#else
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using S1TMP = Il2CppTMPro.TextMeshProUGUI;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patches for <see cref="StorageMenu"/>.
/// Expands the slot UI array to support up to <see cref="PlayerBackpack.MaxStorageSlots"/> slots
/// and adjusts the menu layout for large slot counts.
/// </summary>
[HarmonyPatch(typeof(StorageMenu))]
public static class StorageMenuPatch
{
    private sealed class BackpackPanelState
    {
        public RectTransform Container;
        public RectTransform SlotContainer;
        public GridLayoutGroup SlotGridLayout;
        public ItemSlotUI[] SlotUIs;
        public S1TMP TitleLabel;
        public S1TMP SubtitleLabel;
        public RectTransform PagingRoot;
        public Button PrevButton;
        public Button NextButton;
        public Text PageLabel;
        public Action PrevAction;
        public Action NextAction;
        public int CurrentPage;
        public int SlotsPerPage;
        public int LastPageInputFrame;
        public bool Initialized;
    }

    private const int StorageBackpackSlotsPerPage = 9;
    private const int StorageBackpackGridRows = 3;
    private const float StorageBackpackLeftOffset = 520f;

    private static readonly Dictionary<int, BackpackPanelState> BackpackPanels = new Dictionary<int, BackpackPanelState>();
    private static readonly List<ItemSlot> ActiveInventorySlots = new List<ItemSlot>();
    private static readonly List<ItemSlot> ActiveStorageSlots = new List<ItemSlot>();
    private static readonly List<ItemSlot> ActiveBackpackSlots = new List<ItemSlot>();
    private static bool _quickMoveActive;

    [HarmonyPatch("Awake")]
    [HarmonyPrefix]
    public static void Awake(StorageMenu __instance)
    {
        if (__instance.SlotsUIs.Length >= PlayerBackpack.MaxStorageSlots)
            return;

        var container = __instance.SlotContainer;
        var prefab = __instance.SlotsUIs[0]?.gameObject;
        if (prefab == null)
        {
            ModLogger.Error("StorageMenu prefab is null. Cannot create additional slots.");
            return;
        }

        var slots = new ItemSlotUI[PlayerBackpack.MaxStorageSlots];
        for (var i = 0; i < PlayerBackpack.MaxStorageSlots; i++)
        {
            if (i < __instance.SlotsUIs.Length)
            {
                slots[i] = __instance.SlotsUIs[i];
                continue;
            }

            var slot = UnityEngine.Object.Instantiate(prefab, container);
            slot.name = $"{prefab.name} ({i})";
            var slotUi = slot.GetComponent<ItemSlotUI>();
            if (slotUi != null)
                ResetSlotUi(slotUi);
            slot.gameObject.SetActive(false);
            slots[i] = slotUi;
        }

        __instance.SlotsUIs = slots;
    }

    [HarmonyPatch("Open", [typeof(string), typeof(string), typeof(IItemSlotOwner)])]
    [HarmonyPostfix]
    public static void Open(StorageMenu __instance, string title, string subtitle, IItemSlotOwner owner)
    {
        if (owner != null)
        {
            for (var i = 0; i < __instance.SlotsUIs.Length; i++)
            {
                var slotUi = __instance.SlotsUIs[i];
                if (slotUi == null)
                    continue;

                ResetSlotUi(slotUi);
                slotUi.ClearSlot();
                if (owner.ItemSlots.Count > i)
                {
                    slotUi.gameObject.SetActive(true);
                    slotUi.AssignSlot(owner.ItemSlots[i]);
                }
                else
                {
                    slotUi.gameObject.SetActive(false);
                }
            }
        }

        var spacing = __instance.SlotGridLayout.cellSize.y + __instance.SlotGridLayout.spacing.y;
        __instance.CloseButton.anchoredPosition = new Vector2(
            0f,
            __instance.SlotGridLayout.constraintCount * -spacing - __instance.CloseButton.sizeDelta.y
        );

        if (__instance.SlotGridLayout.constraintCount <= 4)
        {
            __instance.Container.localPosition = Vector3.zero;
        }
        else
        {
            __instance.Container.localPosition = new Vector3(
                0f,
                (__instance.SlotGridLayout.constraintCount - 4) * spacing,
                0f
            );
        }

        ApplyBackpackSidePanel(__instance, owner);
    }

    [HarmonyPatch("CloseMenu")]
    [HarmonyPrefix]
    public static void CloseMenu(StorageMenu __instance)
    {
        HideBackpackSidePanel(__instance);
        __instance.Container.localPosition = Vector3.zero;
        _quickMoveActive = false;
        ActiveInventorySlots.Clear();
        ActiveStorageSlots.Clear();
        ActiveBackpackSlots.Clear();
    }

    [HarmonyPatch(typeof(ItemUIManager), "GetQuickMoveSlots")]
    [HarmonyPostfix]
#if !MONO
    public static void GetQuickMoveSlots(ItemSlot sourceSlot, ref Il2CppSystem.Collections.Generic.List<ItemSlot> __result)
#else
    public static void GetQuickMoveSlots(ItemSlot sourceSlot, ref List<ItemSlot> __result)
#endif
    {
        if (!_quickMoveActive || sourceSlot == null || sourceSlot.ItemInstance == null)
            return;

        var targets = new List<ItemSlot>();
        if (ActiveInventorySlots.Contains(sourceSlot))
        {
            AddQuickMoveTargets(sourceSlot, ActiveStorageSlots, targets);
            AddQuickMoveTargets(sourceSlot, ActiveBackpackSlots, targets);
        }
        else if (ActiveStorageSlots.Contains(sourceSlot))
        {
            AddQuickMoveTargets(sourceSlot, ActiveInventorySlots, targets);
            AddQuickMoveTargets(sourceSlot, ActiveBackpackSlots, targets);
        }
        else if (ActiveBackpackSlots.Contains(sourceSlot))
        {
            AddQuickMoveTargets(sourceSlot, ActiveStorageSlots, targets);
            AddQuickMoveTargets(sourceSlot, ActiveInventorySlots, targets);
        }
        else
        {
            return;
        }

#if !MONO
        __result = targets.ToIl2CppList();
#else
        __result = targets;
#endif
    }

    private static void ResetSlotUi(ItemSlotUI slotUi)
    {
        if (slotUi == null)
            return;

        var itemUi = slotUi.ItemUI;
        if (itemUi != null)
        {
            itemUi.Destroy();
            slotUi.ItemUI = null;
        }

        if (slotUi.ItemContainer != null)
        {
            for (var i = slotUi.ItemContainer.childCount - 1; i >= 0; i--)
            {
                var child = slotUi.ItemContainer.GetChild(i);
                if (child != null)
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    private static void ApplyBackpackSidePanel(StorageMenu menu, IItemSlotOwner openedOwner)
    {
        try
        {
            HideBackpackSidePanel(menu);

            if (menu == null || openedOwner == null || IsBackpackOwner(openedOwner))
                return;

            var backpackSlots = GetBackpackSlots();
            if (backpackSlots.Count == 0)
                return;

            var panel = EnsureBackpackPanel(menu);
            if (panel?.Container == null || panel.SlotUIs == null)
                return;

            panel.CurrentPage = 0;
            SetPanelHeader(menu, panel.Container);
            PositionSideBySide(menu, panel);
            AssignBackpackSlots(panel, backpackSlots);
            panel.Container.gameObject.SetActive(true);
            RebuildStorageQuickMove(openedOwner, backpackSlots);
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.ApplyBackpackSidePanel", ex);
        }
    }

    private static BackpackPanelState EnsureBackpackPanel(StorageMenu menu)
    {
        if (menu == null || menu.Container == null)
            return null;

        var id = menu.GetInstanceID();
        if (BackpackPanels.TryGetValue(id, out var existing)
            && existing.Initialized
            && existing.Container != null
            && existing.SlotContainer != null
            && existing.SlotGridLayout != null
            && existing.SlotUIs != null)
        {
            EnsurePagingControls(existing);
            return existing;
        }

        var panel = existing ?? new BackpackPanelState();

        var rootObject = new GameObject("PackRat_BackpackStoragePanel");
        var root = rootObject.AddComponent<RectTransform>();
        root.SetParent(menu.Container.parent, worldPositionStays: false);
        root.anchorMin = menu.Container.anchorMin;
        root.anchorMax = menu.Container.anchorMax;
        root.pivot = menu.Container.pivot;
        root.sizeDelta = menu.Container.sizeDelta;
        root.localScale = menu.Container.localScale;
        root.gameObject.SetActive(false);

        panel.Container = root;
        panel.TitleLabel = CloneLabel(menu.TitleLabel, menu.Container, root);
        panel.SubtitleLabel = CloneLabel(menu.SubtitleLabel, menu.Container, root);

        var slotContainer = UnityEngine.Object.Instantiate(menu.SlotContainer, root);
        slotContainer.name = "PackRat_BackpackSlotContainer";
        CopyRectTransform(menu.Container, menu.SlotContainer, root, slotContainer);
        panel.SlotContainer = slotContainer;
        panel.SlotGridLayout = slotContainer.GetComponent<GridLayoutGroup>();
        panel.SlotUIs = slotContainer.GetComponentsInChildren<ItemSlotUI>(includeInactive: true);
        panel.SlotsPerPage = StorageBackpackSlotsPerPage;
        panel.Initialized = true;
        BackpackPanels[id] = panel;

        SetPanelHeader(menu, root);
        EnsurePagingControls(panel);
        return panel;
    }

    private static void PositionSideBySide(StorageMenu menu, BackpackPanelState panel)
    {
        var clone = panel.Container;
        if (menu?.Container == null || clone == null)
            return;

        var original = menu.Container;
        var config = Configuration.Instance;
        clone.localPosition = new Vector3(
            original.localPosition.x - StorageBackpackLeftOffset + config.StorageOverlayOffsetX,
            original.localPosition.y + config.StorageOverlayOffsetY,
            original.localPosition.z
        );
    }

    private static void AssignBackpackSlots(BackpackPanelState panel, List<ItemSlot> backpackSlots)
    {
        if (panel.SlotGridLayout != null)
            panel.SlotGridLayout.constraintCount = StorageBackpackGridRows;

        var slotsPerPage = Mathf.Max(1, panel.SlotsPerPage > 0 ? panel.SlotsPerPage : StorageBackpackSlotsPerPage);
        var totalPages = Mathf.Max(1, Mathf.CeilToInt(backpackSlots.Count / (float)slotsPerPage));
        if (panel.CurrentPage < 0)
            panel.CurrentPage = 0;
        if (panel.CurrentPage >= totalPages)
            panel.CurrentPage = totalPages - 1;

        for (var i = 0; i < panel.SlotUIs.Length; i++)
        {
            var slotUi = panel.SlotUIs[i];
            if (slotUi == null)
                continue;

            ResetSlotUi(slotUi);
            slotUi.ClearSlot();
            if (i >= slotsPerPage)
            {
                slotUi.gameObject.SetActive(false);
                continue;
            }

            var slotIndex = panel.CurrentPage * slotsPerPage + i;
            if (slotIndex >= 0 && slotIndex < backpackSlots.Count)
            {
                slotUi.gameObject.SetActive(true);
                slotUi.AssignSlot(backpackSlots[slotIndex]);
            }
            else
            {
                slotUi.gameObject.SetActive(false);
            }
        }

        UpdatePagerControls(panel, totalPages);
    }

    private static void EnsurePagingControls(BackpackPanelState panel)
    {
        if (panel == null || panel.Container == null)
            return;

        if (panel.PagingRoot == null)
        {
            var pagingGo = new GameObject("PackRat_StorageBackpackPaging");
            var pagingRt = pagingGo.AddComponent<RectTransform>();
            pagingRt.SetParent(panel.Container, worldPositionStays: false);
            pagingRt.anchorMin = new Vector2(0.5f, 0.5f);
            pagingRt.anchorMax = new Vector2(0.5f, 0.5f);
            pagingRt.pivot = new Vector2(0.5f, 1f);
            pagingRt.anchoredPosition = new Vector2(0f, -260f);
            pagingRt.sizeDelta = new Vector2(180f, 40f);
            panel.PagingRoot = pagingRt;
        }

        if (panel.PrevButton == null)
            panel.PrevButton = CreatePagerButton("<", panel.PagingRoot, new Vector2(-70f, -10f));
        if (panel.NextButton == null)
            panel.NextButton = CreatePagerButton(">", panel.PagingRoot, new Vector2(70f, -10f));
        if (panel.PageLabel == null)
            panel.PageLabel = CreatePagerLabel(panel.PagingRoot, new Vector2(0f, -10f));

        if (panel.PrevAction == null)
            panel.PrevAction = () =>
            {
                if (panel.LastPageInputFrame == Time.frameCount || panel.CurrentPage <= 0)
                    return;

                panel.LastPageInputFrame = Time.frameCount;
                panel.CurrentPage--;
                AssignBackpackSlots(panel, GetBackpackSlots());
            };

        if (panel.NextAction == null)
            panel.NextAction = () =>
            {
                if (panel.LastPageInputFrame == Time.frameCount)
                    return;

                var totalPages = Mathf.Max(1, Mathf.CeilToInt(GetBackpackSlots().Count / (float)StorageBackpackSlotsPerPage));
                if (panel.CurrentPage >= totalPages - 1)
                    return;

                panel.LastPageInputFrame = Time.frameCount;
                panel.CurrentPage++;
                AssignBackpackSlots(panel, GetBackpackSlots());
            };

        if (panel.PrevButton != null)
        {
            EventHelper.RemoveListener(panel.PrevAction, panel.PrevButton.onClick);
            EventHelper.AddListener(panel.PrevAction, panel.PrevButton.onClick);
        }

        if (panel.NextButton != null)
        {
            EventHelper.RemoveListener(panel.NextAction, panel.NextButton.onClick);
            EventHelper.AddListener(panel.NextAction, panel.NextButton.onClick);
        }
    }

    private static Button CreatePagerButton(string text, Transform parent, Vector2 anchoredPos)
    {
        var buttonGo = new GameObject("PackRat_StorageBackpack" + (text == "<" ? "Prev" : "Next") + "Button");
        buttonGo.transform.SetParent(parent, worldPositionStays: false);

        var rt = buttonGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(24f, 24f);

        var image = buttonGo.AddComponent<Image>();
        image.color = new Color32(60, 60, 60, 210);

        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = image;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(buttonGo.transform, worldPositionStays: false);
        var labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        var label = labelGo.AddComponent<Text>();
        label.text = text;
        label.fontSize = 17;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.resizeTextForBestFit = false;
        label.font = ResolveUiFont(parent);
        label.raycastTarget = false;

        return button;
    }

    private static Text CreatePagerLabel(Transform parent, Vector2 anchoredPos)
    {
        var labelGo = new GameObject("PackRat_StorageBackpackPageLabel");
        labelGo.transform.SetParent(parent, worldPositionStays: false);

        var rt = labelGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(104f, 22f);

        var label = labelGo.AddComponent<Text>();
        label.text = "Page 1/1";
        label.fontSize = 13;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color32(220, 220, 220, 255);
        label.resizeTextForBestFit = false;
        label.font = ResolveUiFont(parent);
        label.raycastTarget = false;
        return label;
    }

    private static Font ResolveUiFont(Transform context)
    {
        if (context != null)
        {
            var textLabels = context.GetComponentsInParent<Text>(true);
            for (var i = 0; i < textLabels.Length; i++)
            {
                var text = textLabels[i];
                if (text != null && text.font != null)
                    return text.font;
            }
        }

        var arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (arial != null)
            return arial;

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static void UpdatePagerControls(BackpackPanelState panel, int totalPages)
    {
        var showPaging = panel != null && totalPages > 1;

        if (panel?.PagingRoot != null)
            panel.PagingRoot.gameObject.SetActive(true);

        if (panel?.PageLabel != null)
        {
            panel.PageLabel.gameObject.SetActive(true);
            panel.PageLabel.text = $"Page {panel.CurrentPage + 1}/{Mathf.Max(1, totalPages)}";
        }

        if (panel?.PrevButton != null)
        {
            panel.PrevButton.gameObject.SetActive(showPaging);
            panel.PrevButton.interactable = showPaging && panel.CurrentPage > 0;
        }

        if (panel?.NextButton != null)
        {
            panel.NextButton.gameObject.SetActive(showPaging);
            panel.NextButton.interactable = showPaging && panel.CurrentPage < totalPages - 1;
        }
    }

    private static void HideBackpackSidePanel(StorageMenu menu)
    {
        if (menu == null)
            return;

        if (!BackpackPanels.TryGetValue(menu.GetInstanceID(), out var panel))
            return;

        if (panel.SlotUIs != null)
        {
            for (var i = 0; i < panel.SlotUIs.Length; i++)
            {
                var slotUi = panel.SlotUIs[i];
                if (slotUi == null)
                    continue;

                ResetSlotUi(slotUi);
                slotUi.ClearSlot();
            }
        }

        if (panel.Container != null)
            panel.Container.gameObject.SetActive(false);
    }

    private static void RebuildStorageQuickMove(IItemSlotOwner openedOwner, List<ItemSlot> backpackSlots)
    {
        _quickMoveActive = false;
        ActiveInventorySlots.Clear();
        ActiveStorageSlots.Clear();
        ActiveBackpackSlots.Clear();

#if MONO
        var inventory = PlayerInventory.Instance;
#else
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
#endif
        if (inventory == null || openedOwner?.ItemSlots == null)
            return;

        foreach (var slot in inventory.GetAllInventorySlots().AsEnumerable())
        {
            if (slot != null)
                ActiveInventorySlots.Add(slot);
        }

        foreach (var slot in openedOwner.ItemSlots.AsEnumerable())
        {
            if (slot != null)
                ActiveStorageSlots.Add(slot);
        }

        foreach (var slot in backpackSlots)
        {
            if (slot != null)
                ActiveBackpackSlots.Add(slot);
        }

        var secondarySlots = new List<ItemSlot>(ActiveStorageSlots);
        secondarySlots.AddRange(ActiveBackpackSlots);

#if !MONO
        Singleton<ItemUIManager>.Instance.EnableQuickMove(ActiveInventorySlots.ToIl2CppList(), secondarySlots.ToIl2CppList());
#else
        Singleton<ItemUIManager>.Instance.EnableQuickMove(ActiveInventorySlots, secondarySlots);
#endif
        _quickMoveActive = ActiveInventorySlots.Count > 0 && (ActiveStorageSlots.Count > 0 || ActiveBackpackSlots.Count > 0);
    }

    private static List<ItemSlot> GetBackpackSlots()
    {
        var result = new List<ItemSlot>();
        try
        {
            var backpack = PlayerBackpack.Instance;
            if (backpack == null || !backpack.IsUnlocked || Player.Local == null)
                return result;

            var storage = Player.Local.GetBackpackStorage();
            if (storage?.ItemSlots == null)
                return result;

            foreach (var slot in storage.ItemSlots.AsEnumerable())
            {
                if (slot != null)
                    result.Add(slot);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.GetBackpackSlots", ex);
        }

        return result;
    }

    private static bool IsBackpackOwner(IItemSlotOwner owner)
    {
        if (owner == null || Player.Local == null)
            return false;

        try
        {
            var backpackStorage = Player.Local.GetBackpackStorage();
            if (backpackStorage == null)
                return false;

#if !MONO
            var ownerStorage = owner.TryCast<StorageEntity>();
            return ownerStorage != null && ownerStorage.Pointer == backpackStorage.Pointer;
#else
            return ReferenceEquals(owner, backpackStorage);
#endif
        }
        catch
        {
            return false;
        }
    }

    private static void AddQuickMoveTargets(ItemSlot sourceSlot, List<ItemSlot> candidates, List<ItemSlot> targets)
    {
        if (sourceSlot?.ItemInstance == null || candidates == null)
            return;

        if (sourceSlot.ItemInstance is CashInstance)
        {
            AddCashQuickMoveTargets(sourceSlot, candidates, targets);
            return;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!CanQuickMoveToSlot(sourceSlot, candidate, targets))
                continue;
            if (candidate.GetCapacityForItem(sourceSlot.ItemInstance, false) <= 0)
                continue;

            targets.Add(candidate);
        }
    }

    private static void AddCashQuickMoveTargets(ItemSlot sourceSlot, List<ItemSlot> candidates, List<ItemSlot> targets)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!CanQuickMoveToSlot(sourceSlot, candidate, targets))
                continue;
            if (!(candidate.ItemInstance is CashInstance cash) || GetCashCapacity(candidate, cash) <= 0f)
                continue;

            targets.Add(candidate);
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!CanQuickMoveToSlot(sourceSlot, candidate, targets))
                continue;
            if (candidate.ItemInstance != null)
                continue;

            targets.Add(candidate);
        }
    }

    private static bool CanQuickMoveToSlot(ItemSlot sourceSlot, ItemSlot candidate, List<ItemSlot> targets)
    {
        if (sourceSlot?.ItemInstance == null || candidate == null || candidate == sourceSlot || targets.Contains(candidate))
            return false;
        if (candidate.IsLocked || candidate.IsAddLocked || candidate.IsRemovalLocked)
            return false;
        return candidate.DoesItemMatchHardFilters(sourceSlot.ItemInstance);
    }

    private static float GetCashCapacity(ItemSlot slot, CashInstance cash)
    {
        if (slot == null || cash == null)
            return 0f;

        var maxBalance = slot is CashSlot ? float.MaxValue : 1000f;
        return Math.Max(0f, maxBalance - cash.Balance);
    }

    private static void SetPanelHeader(StorageMenu menu, RectTransform container)
    {
        if (menu == null || container == null)
            return;

        if (!BackpackPanels.TryGetValue(menu.GetInstanceID(), out var panel))
            return;

        var title = PlayerBackpack.Instance?.CurrentTier?.Name ?? PlayerBackpack.StorageName;
        if (panel.TitleLabel != null)
            panel.TitleLabel.text = title;
        if (panel.SubtitleLabel != null)
            panel.SubtitleLabel.text = "Items from your backpack.";
    }

    private static S1TMP CloneLabel(S1TMP sourceLabel, RectTransform sourceRoot, RectTransform targetRoot)
    {
        if (sourceLabel == null || sourceRoot == null || targetRoot == null)
            return null;

        var clone = UnityEngine.Object.Instantiate(sourceLabel.gameObject, targetRoot);
        clone.name = $"PackRat_{sourceLabel.gameObject.name}";
        var cloneRect = clone.GetComponent<RectTransform>();
        CopyRectTransform(sourceRoot, sourceLabel.GetComponent<RectTransform>(), targetRoot, cloneRect);
        return clone.GetComponent<S1TMP>();
    }

    private static void CopyRectTransform(RectTransform sourceRoot, RectTransform source, RectTransform targetRoot, RectTransform target)
    {
        if (sourceRoot == null || source == null || targetRoot == null || target == null)
            return;

        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.anchoredPosition = source.anchoredPosition;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
    }

}
