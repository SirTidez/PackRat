# Editor-authored UI AssetBundle

## Architecture decision

The editor-authored AssetBundle is PackRat's approved UI architecture. Unity owns the serialized
visual hierarchy, anchors, layout groups, scalable sprites, and interaction hosts. PackRat's shared
C# runtime binds game data, game-owned item slots, behaviors, and Mono/IL2CPP-safe events to those
prefabs.

This implementation was developed and live-tested in the isolated
`experiment/editor-ui-assetbundle-a` worktree before promotion to `master`. The competing
runtime-created scaling experiment in `fix/ui-scaling` is not part of this implementation and
should not be merged as a second renderer. The established C# browser remains a failure fallback
when the embedded bundle or a required prefab contract cannot load.

## Why uGUI prefabs

Schedule I's configured Mono and IL2CPP installs both report Unity `2022.3.62f2`, and the matching
editor is installed. The A authoring project therefore targets that exact revision and exports a
Windows x64 AssetBundle.

The bundle contains only built-in Unity uGUI components. It intentionally excludes PackRat and
Schedule I MonoBehaviours so the same serialized layout can load in both runtimes. PackRat remains
responsible for:

- binding game-owned `ItemSlotUI` instances and item data;
- search, filters, sorting, favorites, pagination, and settings behavior;
- cursor, focus, input blocking, and open/close lifecycle;
- safe-area conversion into the owner Canvas's local coordinates;
- listener cleanup and scene-reload resilience.

## Runtime connection status

The hotkey backpack now binds the complete `PackRatStandalonePane` chrome: title/meta labels,
rectangular search field, filter row, joined sort tabs and active-tab overlay, stack and SVG settings
buttons, slot viewport framework, and footer paging/Done controls. Schedule I's native `ItemSlotUI`
objects remain direct children of the game-owned slot container and render over the prefab's empty
slot framework, preserving their drag/drop, tooltip, quantity, quality, and raycast behavior.

Embedded storage and station surfaces now instantiate the complete `PackRatEmbeddedPane`; handover
and deal surfaces instantiate the complete `PackRatHandoverPane`. The shared header, search,
filters, sort tabs, empty slot framework, paging, settings action, and Done action all bind through
the same browser state as the hotkey backpack. Storage owners additionally bind the authored bulk
selector and both transfer directions to the existing storage-transfer controller. Station owners
hide that storage-only row. Handover binds its authored backpack/vehicle mode buttons, auto-fill
action, and result label to the existing deal controller.

PackRat detaches each pane's authored `CollapseRail` and `CollapsedHandle` trees into a sibling
non-layout host, aligns that host to the complete editor visual in owner-canvas local coordinates,
and binds it to `EmbeddedPanelSession` through `EventHelper`. This lets the restore handle remain
visible while the complete browser and its native slot projection are hidden.

The handover overlay also instantiates `PackRatDedicatedCanvas` rather than recreating its Canvas,
scaler, raycaster, and safe-area roots in C#. Runtime converts `Screen.safeArea` to local offsets on
the authored `SafeAreaRoot`, places the handover card beneath `PaneHost`, and retains the established
game-owned slot/raycaster registration and tooltip sorting behavior.

The Unity authoring validator remains authoritative for the four serialized tooltip callbacks.
Schedule I beta's generated IL2CPP wrapper does not expose `EventTrigger.triggers`, so runtime code
checks the trigger component and exact tooltip/icon contract without calling that unavailable getter.
The beta runtime also ships `RectTransformUtility.CalculateRelativeRectTransformBounds` without an
unstripped method body. Runtime placement instead converts the four authored rectangle corners with
`TransformPoint` and `InverseTransformPoint`. That shared calculation aligns the empty authored
`SlotGrid` to the native slot-container origin and keeps the prefab first in sibling order so native
`ItemSlotUI` rendering and input remain above it.
If a bundle or required component cannot load, PackRat retains the established C# browser or
visibility controls. Closing an embedded surface restores its visible state so reopening the same
owner cannot strand the backpack behind a stale collapsed session.

The standalone product-metrics drawer is now part of `PackRatStandalonePane` rather than a legacy
C#-drawn side panel. Its authored 190-pixel visible width overlaps the card border by ten pixels, and
its fixed vertical span begins at the cyan divider and ends with the backpack footer. The duplicate
metrics header is intentionally gone. The AssetBundle supplies the empty row host, inactive row
template, drug-family accent line, unpackaged-product thumbnail frame, summary, empty state, attached
scalable-chevron handle, and an auto-hiding cyan scrollbar that appears only when product rows
overflow. C# binds the same `ProductDefinition.Icon` used by Schedule I's phone product selectors,
preserves its aspect ratio, and deliberately avoids the package-dependent item-instance icon. It
also retains product aggregation, packaging counts, unit/total value binding, measured name
ellipsis, scroll height, and the presentation-only width animation. The divider uses a small
authored overlap to prevent fractional-scale raster gaps, and the search field now keeps equal
left/right header margins. Product quantity is calculated with Schedule I's container model:
stack quantity counts containers, each packaging definition supplies the units per container, and
the row separately lists nonzero bags, jars, bricks, or unpackaged counts. The default `ALL` overlay
also remains on its serialized editor geometry during the first player frame, then is remeasured
after the tab layout resolves.

The settings affordance is authored as `assets/settings-sliders-ui.svg`. The deterministic editor
pipeline rasterizes that source at 256px with 4x4 antialiasing, imports it as an uncompressed
mipmapped Sprite, and displays it at 21 logical pixels. This preserves a clean scale source and
high-density rendering while avoiding an optional Vector Graphics runtime dependency in either game
runtime.

`PackRatSettingsOverlay` is now runtime-bound for every shared browser surface: the full-screen blocker,
centered card, session status, six tabs, scroll viewport, and live page hosts all come from the
AssetBundle. Authoring preview rows are disabled before first display and replaced with the existing
configuration-backed rows. Boolean preferences use button-backed switches so the interaction path is
identical in Mono and IL2CPP and never accesses the stripped `Toggle.onValueChanged` member.

Embedded and handover surfaces use a side-mounted collapse rail rather than a header back glyph.
The rail preserves the surrounding storage/station session while hiding only PackRat, and its paired
restore rail occupies the same left-edge position. Hovering or selecting the rail shows an immediate
`Hide backpack` tooltip; pointer exit or focus loss dismisses it. The chevrons come from the
MIT-licensed Feather icon set under `assets/ui-icons/feather` and are baked into dependency-free,
mipmapped standard uGUI Sprites for the bundle.

## Authored contracts

- `PackRatStandalonePane`: hotkey backpack browser.
- `PackRatEmbeddedPane`: storage and station browser with a separate bulk-transfer row.
- `PackRatHandoverPane`: handover browser with independent pagination, mode, and auto-fill rows.
- `PackRatSettingsOverlay`: full-stretch blocker plus a centered, scrollable settings card.
- `PackRatDedicatedCanvas`: PackRat-owned overlay canvas and safe-area host.

Each browser prefab contains an empty `SlotViewport/SlotGrid` with its responsive `GridLayoutGroup`.
It is only an anchor host for game-owned `ItemSlotUI` instances; the editor bundle deliberately
contains no example storage cards, item icons, stars, quantities, or slot raycasters.

The embedded prefab is authored at `420x606` and the handover prefab at `420x660`. Those dimensions
provide a complete framework for the canonical five-column by four-row 72 px game slot projection.
At runtime the game-owned grid remains the measurement source: PackRat expands the authored root on
either axis by exactly the amount a larger native grid exceeds its empty `SlotGrid`. It never scales
individual `ItemSlotUI` children or distorts one axis independently.

The binding paths are documented in
`Unity/PackRat.UI.Authoring/Assets/PackRatUI/BINDING_CONTRACT.md`.

## Scaling policy

PackRat-owned canvases use `Scale With Screen Size`, reference `1920x1080`, and height match
(`matchWidthOrHeight = 1`). Height is the constrained axis for these tall cards; this keeps the
logical height at 1080 on ultrawide displays. Game-owned canvases are never reconfigured.

The pure `UiScalePolicy` tests cover:

| Class | Resolution |
| --- | --- |
| Minimum 16:9 | 1280x720 |
| Baseline 16:9 | 1920x1080 |
| High-density 16:9 | 2560x1440 and 3840x2160 |
| 16:10 | 1920x1200 |
| 4:3 | 1280x960 |
| 21:9 | 3440x1440 |
| 32:9 | 5120x1440 |

All three browser cards are checked at the current maximum user zoom of 150%, with 24 logical
pixels reserved on every edge. The runtime fit policy can reduce a requested uniform zoom only when
the complete expanded surface would otherwise cross that safe area. The settings card is checked at
its unscaled modal size.

## Build pipeline

The authoring project is `Unity/PackRat.UI.Authoring`. In the editor use:

1. `PackRat UI/Create or Refresh Prefabs`
2. `PackRat UI/Validate Prefabs`
3. `PackRat UI/Build Windows AssetBundle`

Or run the complete batch entry point:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe'
$repository = (Resolve-Path '.').Path
$project = Join-Path $repository 'Unity\PackRat.UI.Authoring'
$log = Join-Path $project 'packrat-ui-build.log'
$arguments = @(
    '-batchmode', '-nographics', '-quit', '-buildTarget', 'Win64',
    '-projectPath', $project,
    '-executeMethod', 'PackRat.UI.Authoring.Editor.PackRatUiBundleBuilder.BuildAndValidateAll',
    '-logFile', $log
)
Start-Process -FilePath $unity -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
```

`BuildAndValidateAll` packages the currently serialized prefabs and does not recreate them. The
separate `PackRat UI/Create or Refresh Prefabs` menu command is a destructive template-regeneration
tool and should be used only when replacing manual editor refinements is intentional.

Successful export writes:

- `assets/packrat_ui_windows.bundle`
- `assets/packrat_ui_windows.bundle.manifest`

The mod project embeds the bundle only when it exists. `EditorUiAssetBundle` prewarms and caches it
for Mono and IL2CPP; if no bundle is embedded or Unity rejects it, PackRat retains its C# UI.

Visual review uses `PackRat UI/Render Review Previews`, or the batch method
`PackRat.UI.Authoring.Editor.PackRatUiPreviewRenderer.RenderReviewPreviews` without
`-nographics`. It renders serialized prefab assets rather than rebuilding the layout in a separate
mockup system. Generated PNGs are intentionally ignored under `PreviewArtifacts`. The review matrix
includes `standalone-metrics-expanded-1920x1080.png`, which activates preview-only text rows without
serializing product or item examples into the runtime prefab.

Focused review of the rectangular search, joined filter tabs, and empty runtime slot host uses
`PackRat UI/Render Framework Revision Previews` or
`PackRat.UI.Authoring.Editor.PackRatUiPreviewRenderer.RenderFrameworkRevisionPreviews`.
The active-tab foreground uses a dedicated top-only rounded control: its upper corners retain the
approved modest radius, while its lower edge is square and terminates exactly at the cyan divider.
The current framework renderer writes the cache-busted `PreviewArtifacts/SideRailRevisionR7` set.
It includes expanded embedded and handover panes, the `Hide backpack` tooltip state, the collapsed
restore rail, and a normalized side-by-side comparison with the selected concept image.

## Validation evidence and remaining release gates

Completed:

- exact editor/game version compatibility verified locally;
- Unity serialized all five prefab contracts, reopened the generated Windows AssetBundle, and
  exported it into `assets`;
- editor-side geometric assertions cover binding paths, stretch/fixed-anchor ownership, minimum
  grid cells, adjacent-region separation, dedicated-canvas scaling, and the full resolution matrix;
- fourteen Unity-rendered review PNGs cover every pane at 1920x1080, default and 150% zoom browser
  states at 1280x720, 4:3, ultrawide, and super-ultrawide compositions;
- a fifteenth Unity-rendered preview covers the expanded editor-authored metrics drawer and its
  divider-to-footer seam, packaging/value lines, and drug-family accents;
- visual review caught and corrected the initial header/slot and settings/status overlaps before
  the final export;
- authoring C# compiles against the exact Unity `2022.3.62f2` assemblies;
- PackRat Debug Mono and Debug IL2CPP builds pass;
- all 41 logic tests pass, including the full UI resolution/zoom matrix, content-driven framework
  expansion, uniform safe-area fitting, and embedded close/reopen reset;
- iterative live beta validation confirmed the editor-backed standalone backpack appearance,
  game-owned item-slot alignment and input order, settings controls, active tabs, metrics drawer,
  and representative storage, station, and handover framework sizing;
- existing `asset-work` and variant-B worktrees were not modified.

Still required before a release can claim full runtime and resolution coverage:

1. Run the complete editor-backed smoke suite in Mono: open, drag/drop, search, dropdowns, settings,
   metrics expand/collapse/scroll, rails, close/reopen, scene transition, and unload.
2. Recheck the resized empty slot framework against native 5x4 projections on storage racks,
   representative stations, and NPC/deal handover at 1280x720, 1920x1080, 2560x1440, and an
   ultrawide resolution. Confirm the cyan divider remains above the first row, all four rows remain
   inside the slot viewport, and the footer/bulk or transfer rows remain below the last row.
3. Exercise the failure fallback once with the bundle deliberately unavailable and verify that the
   established C# browser remains usable without duplicate canvases or stale input ownership.

Approval makes the AssetBundle implementation canonical for `master`; it does not turn editor
validation, successful builds, or one live runtime into proof of the remaining release matrix.
