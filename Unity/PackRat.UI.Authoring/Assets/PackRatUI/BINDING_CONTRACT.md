# PackRat editor UI binding contract

The asset bundle contains layout and presentation only. PackRat locates these nodes by path and
binds game-owned data and controls at runtime. Keep paths stable when editing the prefabs.

## Shared browser contract

- `Header/Title`
- `Header/Meta`
- `Header/PrimaryActions/StackButton`
- `Header/PrimaryActions/SettingsButton`
- `Header/PrimaryActions/SettingsButton/SettingsIcon`
- `Header/FilterRow/TypeButton`
- `Header/FilterRow/QualityButton`
- `Header/FilterRow/OrderButton`
- `Header/FilterRow/OrganizeButton`
- `Header/FilterRow/ClearButton`
- `Header/Search/InputText`
- `Header/Search/Placeholder`
- `Header/SortTabs/AllButton`
- `Header/SortTabs/FavoritesButton`
- `Header/SortTabs/NameButton`
- `Header/SortTabs/QuantityButton`
- `Header/SortTabs/QualityButton`
- `Header/SortTabs/TypeButton`
- `Header/SortTabs/RecentButton`
- `SlotViewport/SlotGrid`
- `Footer/PreviousButton`
- `Footer/PageLabel`
- `Footer/NextButton`
- `Footer/DoneButton`
- `OverlayHost/ActiveFilterTab`
- `OverlayHost/Dropdown`

## Standalone metrics drawer contract

- `OverlayHost/MetricsTray`
- `OverlayHost/MetricsTray/Panel/Accent`
- `OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content`
- `OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content/RowTemplate/Accent`
- `OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content/RowTemplate/Name`
- `OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content/RowTemplate/Details`
- `OverlayHost/MetricsTray/Panel/Scroll/Viewport/EmptyLabel`
- `OverlayHost/MetricsTray/Panel/Scroll/Scrollbar/SlidingArea/Handle`
- `OverlayHost/MetricsTray/Panel/Summary`
- `OverlayHost/MetricsToggle`
- `OverlayHost/MetricsToggle/OpenIcon`
- `OverlayHost/MetricsToggle/CloseIcon`

`MetricsTray` serializes inactive but retains its 200-pixel animation width: 190 pixels are exposed
outside the card and ten pixels overlap the card's left border. Its 423-pixel fixed height includes
the three-pixel cyan rule above the 420-pixel content-and-footer span. It starts at the divider's
upper edge and ends with the backpack footer, removing the former duplicate header while
keeping one continuous content/footer extension. Runtime code owns aggregation, packaging and drug
family binding, row cloning, scrolling, and motion; the prefab owns the panel, family accent line,
empty state, row presentation, summary, attached toggle, and scalable chevrons.

`SlotViewport/SlotGrid` is intentionally serialized with no children. It is the stretched, layout-owned
anchor host: runtime binding parents Schedule I's game-owned `ItemSlotUI` instances directly beneath it.
Those in-game assets remain authoritative for item rendering, drag/drop, quality stars, quantities,
and raycasts. The AssetBundle must not include example or placeholder storage-slot graphics.

## Embedded and handover additions

- `CollapseRail/HideButton`
- `CollapseRail/HideButton/CollapseIcon`
- `CollapseRail/Tooltip/Label`
- `CollapsedHandle/ShowButton`
- `CollapsedHandle/ShowButton/ExpandIcon`
- `CollapsedHandle/Tooltip/Label`
- `BulkTransferRow/BulkSelectorButton`
- `BulkTransferRow/MoveToStorageButton`
- `BulkTransferRow/MoveToBackpackButton`
- `ModeRow/BackpackButton`
- `ModeRow/VehicleButton`
- `TransferRow/AutoFillButton`
- `TransferRow/StatusLabel`

Runtime A binds the complete shared-browser contract for all three panes and the complete
`PackRatSettingsOverlay`. Storage owners bind `BulkTransferRow`; handover owners bind `ModeRow` and
`TransferRow`; station owners deliberately suppress the storage-only bulk row. Native `ItemSlotUI`
objects remain game-owned siblings aligned over the empty `SlotGrid` framework. The full
collapse/restore rail is detached from each live embedded/handover pane so its restore handle can
remain visible while the browser is hidden. A missing required path, icon, tooltip label, or button
invalidates that pane's runtime extraction and leaves the established C# implementation active.

The editor validator checks all four tooltip callbacks and their `EditorAndRuntime` listener state.
Runtime binding must not call `EventTrigger.triggers`: Schedule I beta's generated IL2CPP wrapper does
not expose that editor-visible getter.
Runtime binding must also not call `RectTransformUtility.CalculateRelativeRectTransformBounds`; its
body is stripped from the beta IL2CPP runtime. Convert authored rectangle corners between transform
spaces instead. The standalone pane must remain the first child of the game-owned slot container so
native slot views draw and receive input above the empty authored `SlotGrid` framework.

## Settings overlay contract

- `Blocker`
- `Card/Header/Title`
- `Card/Header/CloseButton`
- `Card/SessionStatus/Value`
- `Card/Tabs/GeneralButton`
- `Card/Tabs/ThemeButton`
- `Card/Tabs/TiersButton`
- `Card/Tabs/LayoutButton`
- `Card/Tabs/RoutingButton`
- `Card/Tabs/MetricsButton`
- `Card/Content/Viewport`
- `Card/Content/Viewport/GeneralPage`
- `Card/Content/Viewport/ThemePage`
- `Card/Content/Viewport/TiersPage`
- `Card/Content/Viewport/LayoutPage`
- `Card/Content/Viewport/RoutingPage`
- `Card/Content/Viewport/MetricsPage`

The blocker and card retain their authored modal animation groups. Runtime disables and removes the
preview rows before first display, then populates the six page hosts with live settings controls.
Boolean rows use a button-backed switch rather than `Toggle.onValueChanged`, which is absent from the
Schedule I beta IL2CPP wrapper.

## Layout contract

- Buttons, tabs, and the search field use the `RoundedControl` nine-sliced sprite with an 8-pixel
  corner radius. No runtime prefab uses the capsule-shaped `Pill` sprite.
- The complete sort-tab row overlaps the slot panel and renders behind it. `ActiveFilterTab` is one
  non-raycasting control rendered in the foreground and clamped precisely to the cyan divider. It uses
  the modest-radius `RoundedTopControl` nine-slice so the exposed top corners stay rounded while its
  divider edge is square; it must never enter the item area. Runtime filter selection moves and
  relabels that overlay to the selected button; no tab may append a separate bridge or join strip.
  On the first player frame, runtime preserves the serialized `ALL` geometry until the tab layout
  has resolved, then remeasures the selected button. This keeps the semantic default and visible
  selected state synchronized from the instant the backpack appears.
- `ActiveFilterTab` uses the exact `Accent` cyan assigned to the divider bar. The header and slot
  viewport have identical horizontal bounds. `RoundedTopPanel` gives the header only outer top
  corners, and `RoundedBottomPanel` gives the slot area only outer bottom corners, leaving the shared
  internal seam square on both sides.
- `SettingsIcon` is authored from `assets/settings-sliders-ui.svg` and baked deterministically to a
  256px, mipmapped, uncompressed standard uGUI Sprite. The SVG remains the editable source of truth;
  the bundle stays free of optional Vector Graphics runtime components in both game runtimes.
- Embedded and handover panes use a matching `CollapseRail` / `CollapsedHandle` pair anchored to the
  midpoint of the outer left edge. The expanded rail hides the PackRat pane without closing the
  surrounding storage session; the collapsed rail restores it. Neither control belongs in the title
  row or uses a plain back-arrow glyph.
- The rail icons are the MIT-licensed Feather `chevrons-left` and `chevrons-right` SVGs, baked by the
  same deterministic high-density icon pipeline. `HideButton` shows a non-raycasting `Hide backpack`
  tooltip on pointer hover and keyboard/controller selection; `ShowButton` mirrors it with
  `Show backpack`. The built-in serialized events must also hide each tooltip on exit or deselection.
- The standalone metrics handle reuses those scalable chevrons on a left-rounded, flat-right rail.
  It remains attached to the exposed drawer edge throughout the width animation and sits on the
  backpack's left seam while collapsed. Its vertical anchor follows the midpoint of the inventory
  and footer extension rather than the removed metrics header.
- The product-metrics `ScrollRect` owns a narrow cyan vertical scrollbar with `AutoHide` visibility.
  It appears only when product rows exceed the drawer viewport; scrolling remains clamped and the
  row framework keeps the rail separate from product-family accent strips.
- A PackRat-owned Canvas uses `Scale With Screen Size`, reference `1920x1080`, and height match
  (`1.0`) because these are tall cards and height is the constrained axis at ultrawide ratios.
- When injected into a game-owned Canvas, preserve that Canvas and treat its rect as logical space.
- `PackRatDedicatedCanvas/SafeAreaRoot/PaneHost` is the contract for PackRat-owned overlays. The
  runtime converts `Screen.safeArea` into `SafeAreaRoot` owner-local coordinates before placement.
- Compact cards use centered fixed anchors. Full-screen blockers and overlay hosts stretch.
- Layout groups exclusively own repeated button, tab, page-row, and runtime item-slot placement.
- The runtime may change grid constraint count and card size only through a shared responsive policy.
- Popups remain under `OverlayHost`; never calculate their width in world space.
- User zoom is applied to one card/presentation root and is not multiplied by another screen ratio.
