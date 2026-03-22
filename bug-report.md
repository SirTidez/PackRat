# PackRat Bug Report

Date: 2026-03-22

## Scope

This report focuses on the reported issue where backpack-related items appear as AC Units in-game, especially under IL2CPP, and also calls out other concrete weak points in the backpack/storage code that can plausibly cause corruption, loss, or cross-runtime divergence.

## Summary

The strongest lead for the AC Unit symptom is in the IL2CPP fallback path that creates backpack-tier `StorableItemDefinition` objects by cloning an existing shop item and then only partially overwriting it. If the cloned template is an AC Unit, the new backpack-tier definition can retain AC Unit gameplay/prefab data while only its ID, name, icon, and a few metadata fields are changed.

There are also multiple independent storage/state bugs that are severe even if they are unrelated to the AC Unit symptom:

1. `PlayerBackpack.UpdateSize()` creates `ItemSlot`s without adding them to `_storage.ItemSlots`.
2. Backpack contents are loaded before the correct tier/slot count is applied.
3. Backpack storage lookup is not scoped to PackRat's own `StorageEntity`.

## Findings

### 1. [High] IL2CPP fallback definition cloning can preserve AC Unit internals

Likely root cause of the reported symptom.

`CreateBackpackTierDefinition()` tries to create a fresh `StorableItemDefinition` in IL2CPP. If that fails, it falls back to `CloneTemplateStorableItemDefinition()`, which clones the first existing `StorableItemDefinition` found in the hardware store. After cloning, the code only updates:

- `ID`
- `Name`
- icon-like fields
- purchase price
- required rank
- description-like fields

It does not clear or replace other gameplay-defining fields inherited from the template item, such as prefab/model/placement/category/internal references. If the template happens to be an AC Unit, the resulting backpack-tier item can still behave or render as an AC Unit.

References:

- `Shops/BackpackShopIntegration.cs:343-391`
- `Shops/BackpackShopIntegration.cs:400-428`

Why this is credible:

- The symptom is item identity/rendering mismatch, not pure save corruption.
- The issue is reported primarily on IL2CPP, which is exactly where the clone fallback exists.
- The fallback clones the first valid store item without constraining which template is safe to clone.

### 2. [High] PackRat intentionally lets the bad definition instantiate before consuming it

The purchase flow explicitly allows the base game to grant a real item first, and PackRat only later removes/consumes it when the player presses `B`.

That means if finding #1 creates an invalid or partially cloned definition, the wrong item is already visible in the world/inventory before PackRat cleans it up. This amplifies the AC Unit symptom instead of containing it.

References:

- `Patches/BackpackPurchasePatch.cs:20-49`
- `PlayerBackpack.cs:257-295`

### 3. [High] `UpdateSize()` allocates new `ItemSlot`s but never appends them

`PlayerBackpack.UpdateSize()` loops from the current slot count to `newSize`, creates and initializes `new ItemSlot()`, and then drops the reference. It never calls `_storage.ItemSlots.Add(itemSlot)`.

That means `SlotCount` and the actual slot list can diverge. Any code that assumes resize created usable slots is operating on a broken backing list.

References:

- `PlayerBackpack.cs:550-570`

Impact:

- Slot growth may silently fail.
- `ItemSet.LoadTo(backpackStorage.ItemSlots)` may load into too-small storage.
- Tier upgrades can appear applied while the underlying storage remains undersized.

### 4. [High] Backpack contents are loaded before the tier/slot count is applied

Both local-load and network-load paths deserialize backpack contents into `backpackStorage.ItemSlots` before the local backpack tier is applied.

References:

- `Patches/PlayerPatch.cs:128-148`
- `Patches/PlayerPatch.cs:187-218`
- `Networking/BackpackStateSyncManager.cs:690-706`

This is especially risky because `PlayerBackpack.Awake()` defers storage configuration by one frame:

- `PlayerBackpack.cs:132-158`

If the current backing slot list is still at its default or otherwise undersized when `LoadTo()` runs, items can truncate, fail to load, or end up in inconsistent state.

### 5. [High] Backpack storage lookup is not scoped to PackRat's storage entity

`GetBackpackStorage()` simply returns the first `StorageEntity` on `player.gameObject`. `EnsurePlayerBackpackSetup()` also reuses the first existing `StorageEntity` instead of identifying a PackRat-specific one.

References:

- `Extensions/PlayerExtensions.cs:23-32`
- `Patches/PlayerSpawnerPatch.cs:58-78`
- `PlayerBackpack.cs:134`

Risk:

- If the base game or another mod adds a `StorageEntity` to the player, PackRat can bind save/load/UI logic to the wrong storage.
- This can cause apparent item remapping, save corruption, or items showing up in the wrong container.

### 6. [Medium] Registry insertion has no duplicate-ID protection

`RegisterDefinition()` blindly calls `Registry.AddToRegistry(def)`, while shop integration can be attempted from multiple paths and over time.

References:

- `Shops/BackpackShopIntegration.cs:59-75`
- `Shops/BackpackShopIntegration.cs:78-118`
- `Shops/BackpackShopIntegration.cs:431-456`

This is not proven to cause the AC Unit symptom, but duplicate or conflicting IDs in the registry are a credible source of unstable definition resolution.

### 7. [Medium] `TryGetPlayerData()` can index with `-1`

`PlayerManagerPatch.TryGetPlayerData()` uses `loadedPlayerData.IndexOf(data)` to index `loadedPlayerDataPaths` without checking whether the index exists.

References:

- `Patches/PlayerManagerPatch.cs:25-42`

If `IndexOf(data)` returns `-1`, both runtimes can hit an out-of-range access.

### 8. [Medium] Backpack network payload uses a raw `|||` delimiter

Backpack data is appended to the inventory payload as `inventoryString += "|||" + backpackString`, and the load path splits on that delimiter.

References:

- `Patches/PlayerManagerPatch.cs:42`
- `Patches/PlayerPatch.cs:166-175`

This is brittle and unversioned. Any upstream format change or unexpected delimiter collision will silently break parsing.

### 9. [Medium] Tier application only checks `SlotCount`, not backing-list health

The tier application path relies on slot-count comparisons, but given the resize bug above, `SlotCount` can claim one thing while `_storage.ItemSlots.Count` says another.

Reference:

- `PlayerBackpack.cs` around `ApplyCurrentTier`

This can suppress corrective resizing because the sentinel logic thinks the correct tier is already active.

### 10. [Medium] Slot clearing bypasses game invariants via reflection-first nulling

`ClearSlotItem()` first tries to set `ItemInstance` to `null` directly through reflection, only falling back to `Clear()`/`ClearSlot()` if that fails.

Reference:

- `PlayerBackpack.cs:300-335`

In IL2CPP especially, direct field/property mutation can skip bookkeeping that a proper clear API would perform, leaving stale UI/network state or ghost items.

### 11. [Low] Open/close state relies on UI title text equality

`IsOpen` checks `StorageMenu.TitleLabel.text == _openTitle`.

References:

- `PlayerBackpack.cs:121`
- `PlayerBackpack.cs:422-428`

If another mod or the game mutates the title while the menu is open, PackRat can mis-detect state and skip cleanup such as `_storage.SendAccessor(null)`.

### 12. [Low] Existing backpack-file read failures are silently swallowed

`TryReadExistingData()` suppresses all exceptions and falls back quietly.

Reference:

- `Patches/PlayerPatch.cs:96-112`

This makes malformed or incompatible backpack saves harder to diagnose.

## Priority Order

Recommended investigation/fix order:

1. Remove or redesign the IL2CPP clone fallback for `StorableItemDefinition`.
2. Fix `UpdateSize()` so newly created slots are actually added to `_storage.ItemSlots`.
3. Ensure tier/slot count is applied before any `ItemSet.LoadTo(...)`.
4. Scope storage lookup to PackRat's own `StorageEntityName == "Backpack"` or otherwise tag the correct component.
5. Add duplicate-ID protection around runtime definition registration.

## Most Likely Explanation For The Reported AC Unit Bug

The most likely explanation is:

1. IL2CPP fails to create a clean `StorableItemDefinition`.
2. PackRat clones the first hardware-store item definition as a fallback.
3. That template carries AC Unit internals.
4. PackRat changes only surface metadata.
5. The base game instantiates the purchased backpack tier item before PackRat consumes it.
6. Players see an AC Unit instead of a backpack item.

## Notes

These findings were produced by local code inspection plus parallel sub-agent review, then cross-checked against the current repository state before writing this report.
