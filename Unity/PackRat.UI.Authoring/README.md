# PackRat UI Authoring

This Unity `2022.3.62f2` project is the source of PackRat's approved editor-authored UI AssetBundle.
The mod embeds the Windows bundle and binds runtime data, game-owned item slots, and behavior from
shared Mono/IL2CPP C# code. The established runtime-created browser remains only as a safe fallback
when the bundle or a required binding contract is unavailable.

Open this directory as a Unity project, then use these menu items:

- `PackRat UI/Create or Refresh Prefabs` deliberately replaces the five prefab contracts from the
  C# templates. Use it only when regeneration is intended; it overwrites manual prefab refinements.
- `PackRat UI/Validate Prefabs` checks required bindings, anchors, layout ownership, and minimum sizes.
- `PackRat UI/Build Windows AssetBundle` validates and exports `assets/packrat_ui_windows.bundle`.
- `PackRat UI/Render Review Previews` writes editor-rendered resolution and zoom PNGs to
  `PreviewArtifacts`.
- `PackRat UI/Build and Validate All` validates and packages the current serialized prefabs without
  regenerating them.

The generated prefabs use only built-in Unity components. Dynamic Schedule I `ItemSlotUI` objects,
data, listeners, fonts, item sprites, and runtime-specific behavior are intentionally supplied by
PackRat after loading. This avoids serializing Mono-only or IL2CPP-only script types into the bundle.

Buttons and tabs use the generated `RoundedControl` nine-slice. The more strongly rounded `Pill`
sprite is intentionally limited to the search field.

The prefab hierarchy names are a runtime API. See
[`Assets/PackRatUI/BINDING_CONTRACT.md`](Assets/PackRatUI/BINDING_CONTRACT.md) before renaming objects.

Editor builds and review PNGs prove serialization, bundle structure, and authored composition.
Runtime interaction, drag/drop, scene lifecycle, and cross-runtime parity still require the live
smoke matrix documented in `../../docs/UI_EDITOR_ASSETBUNDLE.md`.
