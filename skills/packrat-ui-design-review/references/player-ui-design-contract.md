# PackRat Player UI Design Contract

This guidance applies the reviewed Game Dev Guide Unity UI Episodes playlist to PackRat's
runtime-created Schedule I UI. It complements, rather than replaces, the game-owned UI hierarchy
and the project runtime contract.

## Visual System

- Use a deliberately small palette: near-black overlay/card surfaces, the game's blue interaction
  accent, readable light text, and existing game status colours. Color must communicate state,
  not decoration.
- Keep one readable font family and differentiate hierarchy through size, weight, uppercase, and
  spacing. Titles identify the surface; compact status text reports slots, page, or result count.
- Build in layers: background blocker, compact card, header/controls, content, and close route.
  A full-screen blocker must never double as the compact content surface.
- Use real UI sprites for bespoke icons and nine-sliced sprites for resizable rounded controls.
  Do not substitute text glyphs for icons where a branded, crisp game-style icon is required.

## State and Interaction

- Design every interactive control for normal, hover, selected/focused, disabled, and pressed
  feedback. Selected must persist after the pointer leaves.
- A tab strip is a controller-owned single-selection set: exactly one selected tab maps to exactly
  one sibling page. The prior tab is deselected before the next page is enabled.
- A focused text field owns typed keys. Escape and backpack hotkeys resume their normal close
  behavior only after focus is released or the field is not actively consuming input.
- Disabled filter choices should remain visibly unavailable instead of opening an empty option.
  Empty results deserve a clear result count or empty-state message, not a collapsed card.

## Geometry and Change

- Let layout groups position repeated rows and controls; let the slot grid own its cell geometry,
  spacing, and padding. Do not combine layout ownership with per-child offset math.
- Preserve a stable card and grid viewport while search/filter/sort changes. Pagination absorbs
  capacity changes; result count never changes the card's intended footprint.
- Use explicit min/preferred sizes for header, rows, action controls, icons, and grid cells.
  Validate at the user's target display scale and with a small pack plus a large multi-page pack.
- For uniform inventory cells, a normal `GridLayoutGroup` is acceptable when its fixed cell and
  viewport contract are deliberate. Only introduce a responsive/adaptive layout when the required
  columns, min cell size, and card bounds cannot be expressed safely with the owned game grid.

## Motion and Transitional Feedback

- Prefer small code-driven tweens for fade, scale, and position rather than Animator-driven UI
  animation. Give every transition a deterministic end state and cancel/replace it when the UI
  closes or changes state.
- Use motion to explain a surface opening, a modal taking focus, a dropdown appearing, or a
  selected tab changing. Keep search, filters, sort, paging, and inventory-slot projection
  immediate; do not animate item positions or drag-and-drop behavior owned by the game.
- Keep motion restrained: short ease-out entry (0.12-0.20 s), short exit (0.10-0.14 s), no
  continuous decorative loops, and an explicit setting that snaps transitions off for players
  who prefer no UI animation.
- Use a blocking transitional curtain only for actual asynchronous or multi-stage work. It should
  provide immediate feedback, block conflicting input, report meaningful stage/progress if work is
  perceptible, and disappear only when all required work is complete.
- Do not add loading treatment to a synchronous filter or settings-tab refresh simply for visual
  flair; immediate content replacement is clearer.

## Review Checklist

1. Can the player identify the panel, selected state, status, and exit route at a glance?
2. Do filter/search/pagination changes preserve card bounds, slot order projection, and cell grid?
3. Does every current page have keyboard, mouse, and hotkey-safe interaction behavior?
4. Do modal open/close and asynchronous transitions end in one known state with no stranded input?

## Transcript Sources

- [Visual system and layout rules](https://www.youtube.com/watch?v=HwdweCX5aMI)
- [Code-driven UI tweening](https://www.youtube.com/watch?v=Ll3yujn9GVQ)
- [Controller-owned custom tabs](https://www.youtube.com/watch?v=211t6r12XPQ)
- [Adaptive grid decisions](https://www.youtube.com/watch?v=CGsEJToeXmA)
- [Transition/loading feedback](https://www.youtube.com/watch?v=iXWFTgFNRdM)
- [Procedural Unity UI animation](https://www.youtube.com/watch?v=YqMpVCPX2ls)
