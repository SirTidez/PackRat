# PackRat Runtime Motion Contract

This guidance applies the Coco Code procedural UI-animation tutorial to PackRat's injected
Schedule I backpack UI. Motion supports state changes; it must never delay the game-owned
storage menu lifecycle or interfere with inventory drag and drop.

## Runtime Model

- Use a small coroutine-based motion helper built on `Time.unscaledDeltaTime`, `CanvasGroup`,
  `RectTransform.localScale`, and `RectTransform.anchoredPosition`. Do not add LeanTween,
  DOTween, Animator controllers, or another third-party dependency to PackRat.
- Store every target's resting alpha, scale, and anchored position before its first animation.
  Animate only presentation roots; layout groups and the inventory grid keep ownership of child
  geometry.
- A target has at most one active transition. Starting a new transition cancels the prior
  coroutine, restores a deterministic baseline where needed, and uses a generation token so an
  obsolete completion callback cannot hide a newly reopened element.
- Use `CanvasGroup.blocksRaycasts` and `interactable` deliberately: a settings modal blocks
  its background throughout its exit, then becomes non-interactive and inactive on completion.
  Never leave the main backpack's game-owned close path waiting for an exit tween.

## PackRat Motion Budget

- Main backpack card open: 0.14-0.18 s, alpha 0 to 1, scale 0.96 to 1.00, optional 8 px upward
  settle using an ease-out curve. Do not animate the game blur, cursor lock, player, or storage
  menu activation.
- Settings modal open: blocker alpha 0 to target over 0.12 s; card alpha 0 to 1 plus scale
  0.94 to 1.00 and 10 px upward settle over 0.16-0.20 s. Close is the inverse over 0.10-0.14 s
  and deactivates only after completion.
- Tabs: selected content replaces immediately; animate only a 0.10-0.14 s selected-tab color/
  scale emphasis or a small 4 px underline/indicator movement. Never animate the layout-owned
  tab bounds or delay page activation.
- Search and filter controls: use the game Button's normal color transition for hover/pressed
  state. On focus, fade or color-shift the search border over at most 0.10 s. Search results,
  filters, sort, and pagination update immediately with no per-slot fly-in or rearrangement
  animation.
- Dropdown: fade/scale its presentation root in over 0.10-0.12 s. It remains click-blocking for
  the full visible interval; close it immediately when the backpack closes or an item is chosen.
- Settings rows and toggles: one 0.08-0.12 s color/knob transition after a confirmed setting
  change. Avoid perpetual pulses, bounce loops, or animations on inventory items.

## Accessibility and Validation

- Add a MelonPreferences-backed `UI Animations` setting, enabled by default. When disabled,
  snap every transition to its final state; do not merely shorten it. A future reduced-motion
  setting may retain a short opacity fade while removing scale and position changes.
- Test rapid open/close/hotkey presses, Escape, Done, settings close/reopen, tab switching,
  focused search typing, dropdown selection, scene transition, and mod unload. The final state
  must always restore game input, cursor, camera, and raycast behavior.

## Source

- [Coco Code: Master UI ANIMATIONS! - Unity UI tutorial](https://www.youtube.com/watch?v=YqMpVCPX2ls)
  — local evidence pack included transcript, Gemini analysis, and 24 sampled frames on 2026-07-29.
