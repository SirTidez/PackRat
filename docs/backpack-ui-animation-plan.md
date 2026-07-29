# Backpack UI Animation Plan

## Goal and scope

Add restrained, responsive motion to PackRat's standalone backpack opened with the backpack
hotkey. Motion should clarify changes in UI state without changing inventory behavior. The game
continues to own item drag/drop, quick-move, blur, input blocking, cursor/camera state, and the
`StorageMenu` open/close lifecycle.

This work does **not** animate individual inventory items, slots being filtered/sorted, drag
targets, stack movement, station/deal/storage overlays, or the world blur. Search, filter, sort,
and pagination continue to replace their slot projection immediately.

## Technical approach

Implement a small internal `BackpackUiMotion` helper rather than importing LeanTween, DOTween, or
an Animator controller. The researched tutorial establishes the useful primitives—opacity, local
position, scale, easing, staging, and completion cleanup—but PackRat must stay dependency-free
and compatible with both Mono and IL2CPP.

The helper will use `MelonCoroutines`, `Time.unscaledDeltaTime`, `CanvasGroup`,
`RectTransform.localScale`, and `RectTransform.anchoredPosition`.

### Motion state and lifecycle

- Add a `BackpackUiMotionState` to `StandaloneBackpackState`, with one transition handle and
  generation counter for each independently animated root: backpack card, settings blocker/card,
  dropdown, search focus indicator, selected-tab indicator, and page feedback.
- Capture a root's resting alpha, scale, and anchored position once after its layout is final.
  Layout groups and the backpack grid retain ownership of every child position and size.
- Starting a new transition cancels the previous coroutine for that root and increments its
  generation. Completion callbacks verify their generation before deactivating anything.
- Scene change, menu close, or UI recreation cancels all handles, restores resting state, clears
  references, and never leaves a `CanvasGroup` blocking raycasts.
- Use `Utils.GetOrAddComponentSafe<CanvasGroup>` and `EventHelper` for all Mono/IL2CPP-sensitive
  Unity interaction. Keep the helper presentation-only; no Harmony patch may wait for a tween
  before allowing the game to finish closing the backpack.

### Easing and accessibility

- Define a compact set of pure easing functions: linear, ease-out cubic/quart, and ease-in cubic.
  Do not use elastic/back/bounce curves or perpetual ping-pong loops in the Schedule I UI.
- Add MelonPreferences-backed General settings:
  - `UI ANIMATIONS` — default on; when off, every motion snaps directly to its final state.
  - `REDUCED MOTION` — when on, retains only short fades and removes scale/position movement.
- Mirror both options in the existing backpack Settings modal. They affect only PackRat-created
  presentation roots and take effect on the next interaction without reopening the backpack.

## Motion specification

| Surface | Trigger | Motion | Duration | Input/lifecycle rule |
| --- | --- | --- | --- | --- |
| Backpack card | Successful standalone open | Alpha 0→1, scale 0.96→1.00, optional 8 px upward settle | 0.16 s | Run only after the game menu is already active; no exit tween blocks Done/Escape/hotkey close. |
| Header controls | Hover/press | Use game `Button` color state; optional 0.08 s presentation scale on a non-layout wrapper | 0.08 s | Labels/icons remain non-raycast targets; no controller navigation delay. |
| Search field | Select/deselect | Blue focus-border alpha or color shift | 0.10 s | Text focus has priority over the backpack hotkey exactly as it does now. |
| Filter/sort dropdown | Open/close/select | Root alpha 0→1 and scale 0.98→1; reverse on close | 0.10–0.12 s | It blocks its own clicks while visible; selection applies instantly and its close cannot survive menu close. |
| Pagination | Manual next/previous page | A clipped card-colour overlay starts over the grid, then wipes left for Next or right for Previous, revealing the newly assigned page with a blue edge | 0.13–0.16 s | The overlay is non-raycastable and never transforms slot UI. Skip it for search/filter/sort refreshes, drag state, disabled motion, or reduced motion. |
| Settings modal | Cog click | Blocker fade, then card alpha/scale 0.94→1 and 10 px upward settle | 0.12 s + 0.18 s card | Blocker activates before card animation. |
| Settings close | Close button/cog | Card alpha/scale to 0.96 and down 6 px, then blocker fade and deactivate | 0.12 s | Modal remains raycast-blocking until completion; rapid reopen cancels the stale close. |
| Settings tabs | Tab selection | Activate the new page immediately; animate only a separate underline/selected-state alpha | 0.10–0.14 s | Never change tab bounds or delay active-page rebuild, preserving the fixed desktop-tab overlap. |
| Settings toggles | Confirmed value change | Knob/track color transition on the toggle control only | 0.10 s | Preference write and session sync remain immediate and authoritative. |
| Page/filter feedback | Page or filter result changes | Brief page-label/result-count alpha emphasis | 0.10 s | Grid and slot assignment remain immediate; no item fly-in/reflow animation. |

## Delivery sequence

1. Add preferences, configuration defaults, and the cancellable motion helper with a pure
   no-motion code path. Add diagnostic logs only for unexpected cancellation/state errors.
2. Animate standalone backpack entry plus search focus and dropdown presentation. Add the
   direction-aware grid page wipe: establish it before reassigning slots, then reveal it only
   after the grid rebuild. Verify closing through Done, Escape, and the backpack hotkey always
   releases the game immediately.
3. Add the settings blocker/card lifecycle and rapid open/close cancellation handling.
4. Add tab indicator, toggle, and page/result feedback without changing layout-owned geometry.
5. Add the two Settings General toggles and verify MelonPreferences persistence in both runtimes.
6. Build Debug IL2CPP and Debug Mono, then live-test scale/resolution, client/host/local status,
   settings persistence, reload, scene transition, reconnect, and disabled/reduced-motion modes.

## Acceptance criteria

- Motion is visible but never exceeds 0.20 s for a primary UI transition.
- The backpack never traps the cursor, camera, player, blur, or overlay during rapid input.
- Search typing, filtering, sort, paging, and item drag/drop have no additional latency and no
  animated reordering of slots. Manual pagination may use only the short clipped wipe reveal.
- A cancelled close cannot deactivate a freshly reopened settings modal or dropdown.
- `UI ANIMATIONS` off produces the exact final visual state on the same frame; reduced motion
  avoids positional/scale movement.
- Debug Mono and Debug IL2CPP compile and install successfully; live behavior is confirmed in
  the target game builds before merging the feature worktree.

## Research basis

- [Coco Code — Master UI ANIMATIONS!](https://www.youtube.com/watch?v=YqMpVCPX2ls): full
  transcript analysis, Gemini visual analysis, and 24 sampled frames reviewed on 2026-07-29.
