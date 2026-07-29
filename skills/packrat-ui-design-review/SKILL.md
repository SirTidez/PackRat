---
name: packrat-ui-design-review
description: Review and design PackRat's player-facing Schedule I backpack UI before implementation, including visual hierarchy, modal composition, filters, settings, tabs, adaptive slot grids, disabled/empty/loading states, and motion. Use when proposing a UI change, evaluating a screenshot, or repairing a visible backpack UI issue.
---

# PackRat UI Design Review

Use this before changing PackRat UI behavior. It keeps visual design decisions separate from
runtime ownership and ensures the standalone backpack, station/storage variants, and settings
modal remain legible at different screen sizes and bag capacities.

Read `references/player-ui-design-contract.md` before proposing visual changes. Use
`packrat-unity-ui-ux` for the actual Harmony, lifecycle, listener, and cross-runtime work.

## Review Workflow

1. Identify the player goal, UI owner, and whether the surface is blocking, informational, or
   transitional.
2. Define the state model before visuals: normal, hover, selected, focused, disabled, empty,
   loading, and error where applicable.
3. Sketch the surface in layers: full-screen blocker, compact card, title/status, controls,
   content region, and persistent exit route.
4. Establish the geometry contract: fixed card bounds, stable grid viewport, min slot size,
   and responsive behavior for more pages rather than uncontrolled card growth.
5. Review one representative screenshot at the intended resolution, then test focus, paging,
   filtering, Escape/hotkey close, and reopen behavior.

## Required Decisions

- Preserve Schedule I's flat, high-contrast game language. Reuse its typography, spacing,
  subdued dark surfaces, and blue selection accent; do not introduce a visually unrelated theme.
- Assign a single visual signal to each meaning. Selection, focus, warning, disabled state, and
  content status must remain distinguishable from one another.
- Keep the visible backpack card stable when search, filters, or pagination changes. Project
  results into the fixed grid; never let result count reshape the surrounding panel.
- Make tabs a single-selection model and always expose a close/back route from settings.
- Use light, code-driven fade/scale/position transitions only when they clarify a state change.
  They must not delay close, compete with input, or depend on a runtime Animator controller.
- Present empty or temporarily unavailable choices as disabled and explain why they are absent
  when that would otherwise be confusing.

## Handoff to Implementation

State the visual/state contract, target hierarchy, layout owner for each region, and the
validation cases. Then use `packrat-unity-ui-ux` to implement it safely for Mono and IL2CPP.

## References

- `references/player-ui-design-contract.md`
