---
name: packrat-unity-ui-ux
description: Build, repair, and validate PackRat's player-facing Schedule I backpack UI, including the standalone backpack, filters, settings, storage overlays, station and deal views, and their Mono or IL2CPP lifecycle seams. Use when changing PackRat uGUI hierarchy, layout, assets, focus, input, panel state, or responsive slot presentation.
---

# PackRat Unity UI/UX

Use the game-owned storage menu and its existing input lifecycle. Read
`references/runtime-layout-contract.md` before changing a visible panel. Read
`references/runtime-motion-contract.md` before adding or changing visible UI motion.

## Workflow

1. Identify the owner, open/close path, and Mono/IL2CPP seam before building controls.
2. Split every modal into a full-screen input blocker and a compact centered card.
3. Assign layout ownership once: card placement by its `RectTransform`; rows and tabs by layout
   groups; backpack cells by the grid; text by its control child.
4. Bind listeners once, refresh only projected visible data, and clean up state on close or scene
   rebuild.
5. Treat animation as presentation-only: it may not own game-menu activation, input release,
   inventory-slot layout, or the close lifecycle.
6. Test at the target display scale: open, search, filter, settings navigation, Escape, hotkey,
   Done, reopen, scene transition, and client reconnect.

## Required Rules

- Use `VerticalLayoutGroup` for settings rows and `HorizontalLayoutGroup` for row columns,
  tab strips, and icon-plus-text buttons. Do not position their children by calculated offsets.
- Give each row a `LayoutElement` height; give label, value, and action controls explicit width
  contracts. Do not let a content panel resize to the number of filtered slots.
- Use a PNG `Sprite` for custom icons. Preserve aspect ratio and disable the icon's raycast target.
- Use a nine-sliced sprite for resizable buttons, tabs, and rows so corner radii survive resizing.
- Treat settings tabs as a selected-state controller: activate exactly one sibling page, preserve
  a distinct selected visual through hover, and rebuild only the active page's rows.
- Treat search-input focus as higher priority than the backpack hotkey. Never toggle the backpack
  while an active input field is consuming text.
- Keep backing inventory order immutable; render a filtered/sorted projection into slots.
- Use a single cancellable, unscaled-time coroutine per animated presentation root. Preserve
  baseline transform and CanvasGroup state, and snap to the final state when UI Animations is off.
- Do not add LeanTween, DOTween, an Animator controller, or persistent motion to PackRat's
  injected UI. Keep close paths immediate at the game owner; only an internal modal may complete
  a brief exit transition before deactivation.

## Runtime Compatibility

- Keep IL2CPP-safe listener registration through `EventHelper` and cleanup explicit.
- Do not retain game-owned panel references across scene loads.
- Use `ModLogger` for recoverable resource or lifecycle diagnostics.
- Build both `Debug Mono` and `Debug IL2CPP`; a successful build is not gameplay validation.

## References

- `references/runtime-layout-contract.md`
- `references/runtime-motion-contract.md`
