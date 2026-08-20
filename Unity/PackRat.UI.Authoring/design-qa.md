# PackRat side-mounted collapse rail design QA

## Evidence

- Source visual truth: `PreviewArtifacts/DesignReferences/selected-side-mounted-collapse-rail.png`
- Source pixels: 1448x1086, normalized to 960x720 for comparison.
- Implementation: `PreviewArtifacts/SideRailRevisionR6/01-embedded-side-rail-expanded.png`
- Tooltip state: `PreviewArtifacts/SideRailRevisionR6/02-embedded-hide-backpack-tooltip.png`
- Collapsed state: `PreviewArtifacts/SideRailRevisionR6/05-collapsed-restore-rail.png`
- Full comparison: `PreviewArtifacts/SideRailRevisionR6/00-selected-concept-left-unity-r6-right.png`
- Focused comparison: `PreviewArtifacts/SideRailRevisionR6/00-focused-side-rail-comparison.png`
- Implementation pixels: 960x720 at device density 1.
- Unity logical surface: 390x520, rendered with the 720/1080 canvas scale and 150% user zoom.
- State: embedded backpack expanded, rail idle; tooltip and restore-rail states captured separately.
- Runtime: Unity 2022.3.62f2 uGUI prefab rendering. Browser/CSS size and console checks do not apply.

## Findings

No actionable P0, P1, or P2 visual differences remain in the selected component scope.

- Fonts and typography: the existing R5 Unity font, title hierarchy, and small-control optical weights are
  preserved. The title and metadata return to the standard 12 logical pixel inset.
- Spacing and layout rhythm: the 30x64 rail sits at the midpoint of the outer left edge with 22 logical
  pixels outside the card and 8 inside, matching the selected spatial-collapse concept. Expanded and
  restore states use identical geometry.
- Colors and visual tokens: the rail uses the existing `Control` surface and its icon uses the canonical
  `Accent` cyan. The tooltip uses the same control radius and a restrained accent outline.
- Image quality and asset fidelity: the left/right chevrons are the official MIT-licensed Feather SVGs,
  deterministically baked to 128px uncompressed mipmapped uGUI Sprites. They remain sharp at the target
  18 logical pixel icon size.
- Copy and content: the expanded tooltip reads exactly `Hide backpack`; the reciprocal tooltip reads
  `Show backpack`.
- Interaction and accessibility: serialized pointer-enter, pointer-exit, select, and deselect events were
  invoked on a prefab instance. Mouse hover and keyboard/controller focus show the tooltip; exit and focus
  loss hide it. Tooltip graphics do not raycast.

The concept image changes other R5 proportions and typography as an ImageGen artifact. Those differences
are intentionally excluded because the approved scope was the rail; the existing panel, seam, controls,
and resolution behavior were hard constraints.

## Comparison history

1. Initial R6 behavior test: the pointer-enter callback was serialized but configured as runtime-only, so
   the editor interaction preview could not reveal the tooltip. Result: blocked.
2. Fix: persisted all four tooltip callbacks as editor-and-runtime listeners and added callback invocation
   assertions for enter, exit, select, and deselect.
3. Post-fix evidence: expanded, tooltip, collapsed, full-comparison, and focused-comparison renders completed.
   The focused 368x160 comparison shows equivalent side attachment, height, rounded silhouette, direction,
   and accent treatment. Result: passed.

## Follow-up polish

- No P3 visual changes are required for the selected target.
- PackRat now binds `CollapseRail/HideButton` and `CollapsedHandle/ShowButton` to the existing
  embedded-session hide/show behavior. Live beta iteration confirmed the connected rail placement;
  the remaining Mono smoke coverage is tracked in `../../docs/UI_EDITOR_ASSETBUNDLE.md`.

final result: passed
