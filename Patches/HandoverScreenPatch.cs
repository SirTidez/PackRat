using HarmonyLib;
using PackRat.Extensions;
using PackRat.Helpers;
using UnityEngine;
using UnityEngine.UI;

#if MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Handover;
using ScheduleOne.UI.Items;
#else
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Handover;
using Il2CppScheduleOne.UI.Items;
#endif

namespace PackRat.Patches;

/// <summary>
/// Adds backpack storage support to the handover screen with paging.
/// </summary>
[HarmonyPatch(typeof(HandoverScreen))]
public static class HandoverScreenPatch
{
    private const float VehicleOffsetWhenBackpackVisible = 60f;
    private const float VehicleMaxDistance = 20f;

    private sealed class PanelState
    {
        public RectTransform BackpackContainer;
        public RectTransform VehicleContainer;
        public Component TitleLabel;
        public Component SubtitleLabel;
        public ItemSlotUI[] SlotUIs;
        public Button PrevButton;
        public Button NextButton;
        public Text PageLabel;
        public Action PrevAction;
        public Action NextAction;
        public Vector2 VehicleOriginalAnchoredPos;
        public int CurrentPage;
        public int SlotsPerPage;
        public bool Initialized;
    }

    private static readonly Dictionary<int, PanelState> States = new Dictionary<int, PanelState>();

    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    public static void Start(HandoverScreen __instance)
    {
        try
        {
            EnsurePanel(__instance);
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.Start", ex);
        }
    }

    [HarmonyPatch("Open")]
    [HarmonyPostfix]
    public static void Open(HandoverScreen __instance)
    {
        try
        {
            if (!HasBackpack())
            {
                HidePanelAndRestoreVehicle(__instance);
                return;
            }

            var panel = EnsurePanel(__instance);
            if (panel == null || panel.BackpackContainer == null)
                return;

            panel.CurrentPage = 0;
            panel.SlotsPerPage = panel.SlotUIs != null ? panel.SlotUIs.Length : 0;
            panel.BackpackContainer.gameObject.SetActive(true);

            if (panel.TitleLabel != null)
                SetLabelText(panel.TitleLabel, PlayerBackpack.Instance.CurrentTier?.Name ?? PlayerBackpack.StorageName);

            if (panel.SubtitleLabel != null)
                SetLabelText(panel.SubtitleLabel, "Items from your backpack.");

            var hasVehicle = HasNearbyVehicleStorage();
            if (__instance.NoVehicle != null)
                __instance.NoVehicle.SetActive(!hasVehicle && !panel.BackpackContainer.gameObject.activeSelf);

            if (hasVehicle)
            {
                panel.BackpackContainer.anchoredPosition = panel.VehicleOriginalAnchoredPos;
                panel.VehicleContainer.anchoredPosition = panel.VehicleOriginalAnchoredPos + new Vector2(0f, -VehicleOffsetWhenBackpackVisible);
                panel.VehicleContainer.gameObject.SetActive(true);
            }
            else
            {
                panel.BackpackContainer.anchoredPosition = panel.VehicleOriginalAnchoredPos;
                panel.VehicleContainer.anchoredPosition = panel.VehicleOriginalAnchoredPos;
                panel.VehicleContainer.gameObject.SetActive(false);
                if (__instance.NoVehicle != null)
                    __instance.NoVehicle.SetActive(false);
            }

            ApplyBackpackPage(panel);
            RebuildQuickMove(__instance, hasVehicle);
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.Open", ex);
        }
    }

    [HarmonyPatch("Close")]
    [HarmonyPostfix]
    public static void Close(HandoverScreen __instance)
    {
        try
        {
            if (!States.TryGetValue(__instance.GetInstanceID(), out var panel))
                return;

            ClearSlotAssignments(panel);
            if (panel.BackpackContainer != null)
                panel.BackpackContainer.gameObject.SetActive(false);
            if (panel.VehicleContainer != null)
                panel.VehicleContainer.anchoredPosition = panel.VehicleOriginalAnchoredPos;
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.Close", ex);
        }
    }

    private static PanelState EnsurePanel(HandoverScreen screen)
    {
        if (screen == null)
            return null;

        var id = screen.GetInstanceID();
        if (States.TryGetValue(id, out var existing) && existing.Initialized)
            return existing;

        var state = existing ?? new PanelState();
        state.VehicleContainer = screen.VehicleContainer;
        if (state.VehicleContainer == null)
            return null;

        state.VehicleOriginalAnchoredPos = state.VehicleContainer.anchoredPosition;

        if (state.BackpackContainer == null)
        {
            var clone = UnityEngine.Object.Instantiate(state.VehicleContainer, state.VehicleContainer.parent);
            clone.name = "BackpackContainer";
            clone.anchoredPosition = state.VehicleOriginalAnchoredPos;
            clone.localScale = state.VehicleContainer.localScale;
            clone.gameObject.SetActive(false);
            state.BackpackContainer = clone;
        }

        state.SlotUIs = state.BackpackContainer.GetComponentsInChildren<ItemSlotUI>(includeInactive: true);
        ResolveLabels(state);
        EnsurePagingControls(state);
        state.Initialized = true;
        States[id] = state;
        return state;
    }

    private static void ResolveLabels(PanelState state)
    {
        var labels = state.BackpackContainer.GetComponentsInChildren<Component>(true)
            .Where(IsTextLikeComponent)
            .ToArray();
        state.TitleLabel = null;
        state.SubtitleLabel = null;

        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (label == null)
                continue;

            var text = (GetLabelText(label) ?? string.Empty).Trim();
            if (state.TitleLabel == null && text.Equals("Vehicle", StringComparison.OrdinalIgnoreCase))
            {
                state.TitleLabel = label;
                continue;
            }

            if (state.SubtitleLabel == null && text.Contains("vehicle you last drove", StringComparison.OrdinalIgnoreCase))
            {
                state.SubtitleLabel = label;
            }
        }

        if (state.TitleLabel == null && labels.Length > 0)
            state.TitleLabel = labels[0];
        if (state.SubtitleLabel == null && labels.Length > 1)
            state.SubtitleLabel = labels[1];
    }

    private static void EnsurePagingControls(PanelState state)
    {
        if (state.BackpackContainer == null)
            return;

        var pagingRoot = state.BackpackContainer.Find("PackRat_Paging");
        if (pagingRoot == null)
        {
            var rootGo = new GameObject("PackRat_Paging");
            pagingRoot = rootGo.transform;
            pagingRoot.SetParent(state.BackpackContainer, worldPositionStays: false);

            var rootRt = rootGo.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(1f, 1f);
            rootRt.anchorMax = new Vector2(1f, 1f);
            rootRt.pivot = new Vector2(1f, 1f);
            rootRt.anchoredPosition = new Vector2(-8f, -8f);
            rootRt.sizeDelta = new Vector2(112f, 24f);

            var prev = CreatePagerButton("<", pagingRoot, new Vector2(-76f, 0f));
            var next = CreatePagerButton(">", pagingRoot, new Vector2(-2f, 0f));
            var label = CreatePagerLabel(pagingRoot, new Vector2(-39f, 0f));

            state.PrevButton = prev;
            state.NextButton = next;
            state.PageLabel = label;
        }
        else
        {
            var buttons = pagingRoot.GetComponentsInChildren<Button>(includeInactive: true);
            foreach (var button in buttons)
            {
                if (button == null)
                    continue;
                if (button.name.IndexOf("Prev", StringComparison.OrdinalIgnoreCase) >= 0)
                    state.PrevButton = button;
                else if (button.name.IndexOf("Next", StringComparison.OrdinalIgnoreCase) >= 0)
                    state.NextButton = button;
            }

            if (state.PageLabel == null)
                state.PageLabel = pagingRoot.GetComponentInChildren<Text>(true);
        }

        if (state.PrevAction == null)
            state.PrevAction = () =>
            {
                if (state.CurrentPage <= 0)
                    return;
                state.CurrentPage--;
                ApplyBackpackPage(state);
            };

        if (state.NextAction == null)
            state.NextAction = () =>
            {
                var totalPages = GetTotalPages(state);
                if (state.CurrentPage >= totalPages - 1)
                    return;
                state.CurrentPage++;
                ApplyBackpackPage(state);
            };

        if (state.PrevButton != null)
            EventHelper.AddListener(state.PrevAction, state.PrevButton.onClick);
        if (state.NextButton != null)
            EventHelper.AddListener(state.NextAction, state.NextButton.onClick);
    }

    private static Button CreatePagerButton(string text, Transform parent, Vector2 anchoredPos)
    {
        var buttonGo = new GameObject("PackRat_" + (text == "<" ? "Prev" : "Next") + "Button");
        buttonGo.transform.SetParent(parent, worldPositionStays: false);

        var rt = buttonGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(20f, 20f);

        var image = buttonGo.AddComponent<Image>();
        image.color = new Color32(85, 85, 85, 190);

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
        label.fontSize = 16;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.resizeTextForBestFit = false;
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        return button;
    }

    private static Text CreatePagerLabel(Transform parent, Vector2 anchoredPos)
    {
        var labelGo = new GameObject("PackRat_PageLabel");
        labelGo.transform.SetParent(parent, worldPositionStays: false);

        var rt = labelGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(50f, 20f);

        var label = labelGo.AddComponent<Text>();
        label.text = "1/1";
        label.fontSize = 12;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color32(220, 220, 220, 255);
        label.resizeTextForBestFit = false;
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return label;
    }

    private static void ApplyBackpackPage(PanelState state)
    {
        if (state == null || state.SlotUIs == null)
            return;

        var backpackSlots = GetBackpackSlots();
        var slotsPerPage = Mathf.Max(1, state.SlotUIs.Length);
        state.SlotsPerPage = slotsPerPage;

        var totalPages = Mathf.Max(1, Mathf.CeilToInt(backpackSlots.Count / (float)slotsPerPage));
        if (state.CurrentPage < 0)
            state.CurrentPage = 0;
        if (state.CurrentPage >= totalPages)
            state.CurrentPage = totalPages - 1;

        for (var i = 0; i < state.SlotUIs.Length; i++)
        {
            var ui = state.SlotUIs[i];
            if (ui == null)
                continue;

            ui.ClearSlot();

            var slotIndex = state.CurrentPage * slotsPerPage + i;
            if (slotIndex >= 0 && slotIndex < backpackSlots.Count)
            {
                ui.AssignSlot(backpackSlots[slotIndex]);
                ui.gameObject.SetActive(true);
            }
            else
            {
                ui.gameObject.SetActive(false);
            }
        }

        if (state.PageLabel != null)
            state.PageLabel.text = $"{state.CurrentPage + 1}/{totalPages}";

        var pagingVisible = totalPages > 1;
        if (state.PageLabel != null)
            state.PageLabel.gameObject.SetActive(pagingVisible);
        if (state.PrevButton != null)
        {
            state.PrevButton.gameObject.SetActive(pagingVisible);
            state.PrevButton.interactable = state.CurrentPage > 0;
        }

        if (state.NextButton != null)
        {
            state.NextButton.gameObject.SetActive(pagingVisible);
            state.NextButton.interactable = state.CurrentPage < totalPages - 1;
        }
    }

    private static List<ItemSlot> GetBackpackSlots()
    {
        var result = new List<ItemSlot>();
        try
        {
            if (!HasBackpack())
                return result;

            var storage = Player.Local != null ? Player.Local.GetBackpackStorage() : null;
            if (storage == null || storage.ItemSlots == null)
                return result;

            foreach (var slot in storage.ItemSlots.AsEnumerable())
            {
                if (slot != null)
                    result.Add(slot);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.GetBackpackSlots", ex);
        }

        return result;
    }

    private static void ClearSlotAssignments(PanelState panel)
    {
        if (panel?.SlotUIs == null)
            return;

        for (var i = 0; i < panel.SlotUIs.Length; i++)
        {
            var ui = panel.SlotUIs[i];
            if (ui == null)
                continue;

            ui.ClearSlot();
            ui.gameObject.SetActive(true);
        }
    }

    private static void RebuildQuickMove(HandoverScreen screen, bool hasVehicle)
    {
        if (screen == null)
            return;

#if MONO
        var inventory = PlayerInventory.Instance;
#else
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
#endif
        if (inventory == null)
            return;

        var allSlots = inventory.GetAllInventorySlots();
        if (allSlots == null)
            return;

        if (hasVehicle && Player.Local?.LastDrivenVehicle?.Storage?.ItemSlots != null)
        {
            foreach (var slot in Player.Local.LastDrivenVehicle.Storage.ItemSlots.AsEnumerable())
            {
                if (slot != null)
                    allSlots.Add(slot);
            }
        }

        foreach (var slot in GetBackpackSlots())
            allSlots.Add(slot);

        var customerSlots = ReflectionUtils.TryGetFieldOrProperty(screen, "CustomerSlots");
        if (customerSlots == null)
            return;

        var secondaryManaged = new List<ItemSlot>();
        var count = ReflectionUtils.TryGetListCount(customerSlots);
        for (var i = 0; i < count; i++)
        {
            var item = ReflectionUtils.TryGetListItem(customerSlots, i);
            if (item == null)
                continue;

            if (!Utils.Is<ItemSlot>(item, out var slot))
                continue;
            if (slot != null)
                secondaryManaged.Add(slot);
        }

#if !MONO
        Singleton<ItemUIManager>.Instance.EnableQuickMove(allSlots, secondaryManaged.ToIl2CppList());
#else
        Singleton<ItemUIManager>.Instance.EnableQuickMove(allSlots, secondaryManaged);
#endif
    }

    private static bool IsTextLikeComponent(Component component)
    {
        if (component == null)
            return false;
        var typeName = component.GetType().Name;
        return typeName.Contains("Text", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLabelText(Component component)
    {
        if (component == null)
            return string.Empty;
        var value = ReflectionUtils.TryGetFieldOrProperty(component, "text");
        return value as string ?? value?.ToString() ?? string.Empty;
    }

    private static void SetLabelText(Component component, string text)
    {
        if (component == null)
            return;
        ReflectionUtils.TrySetFieldOrProperty(component, "text", text ?? string.Empty);
    }

    private static bool HasBackpack()
    {
        return PlayerBackpack.Instance != null && PlayerBackpack.Instance.IsUnlocked;
    }

    private static bool HasNearbyVehicleStorage()
    {
        var player = Player.Local;
        if (player == null || player.LastDrivenVehicle == null)
            return false;

        var storage = player.LastDrivenVehicle.Storage;
        if (storage == null)
            return false;

        return Vector3.Distance(player.LastDrivenVehicle.transform.position, player.transform.position) < VehicleMaxDistance;
    }

    private static int GetTotalPages(PanelState state)
    {
        var slotCount = GetBackpackSlots().Count;
        var perPage = Mathf.Max(1, state.SlotsPerPage);
        return Mathf.Max(1, Mathf.CeilToInt(slotCount / (float)perPage));
    }

    private static void HidePanelAndRestoreVehicle(HandoverScreen screen)
    {
        if (screen == null)
            return;

        if (!States.TryGetValue(screen.GetInstanceID(), out var state))
            return;

        if (state.BackpackContainer != null)
            state.BackpackContainer.gameObject.SetActive(false);
        if (state.VehicleContainer != null)
            state.VehicleContainer.anchoredPosition = state.VehicleOriginalAnchoredPos;
    }
}
