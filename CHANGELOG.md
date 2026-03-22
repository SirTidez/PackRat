# Changelog

## 1.0.7

### Backpack definition and storage fixes
- Added a safe backpack item template for IL2CPP fallback definition creation so backpack tier purchases no longer clone arbitrary hardware-store items.
- Fixed backpack storage lookup to target PackRat's own `StorageEntity` instead of blindly taking the first storage component on the player.
- Hardened legality checks to read legal status by reflection so both Mono and IL2CPP continue working across game API changes.

### Save/load and sizing fixes
- Ensured the saved purchased backpack tier is applied before restoring saved contents.
- Fixed slot growth so newly created backpack slots are actually appended to the underlying storage slot list.

### Compatibility
- Added the new core assembly references required by the latest Schedule One update for both Mono and IL2CPP builds.

---

## 1.0.6

### Game update compatibility
- Updated Mono and IL2CPP references for the latest Schedule One game version, including the new `ScheduleOne.Core` / `Il2CppScheduleOne.Core` assembly split.
- Removed hard dependency on game assemblies that are no longer present in newer installs so both runtimes compile cleanly against the current game files.
- Adjusted cross-runtime item/core type handling to match the updated game API surface.

### Backpack save/load fixes
- Fixed backpack restore order so the purchased backpack tier and slot count are applied before backpack contents are deserialized.
- Fixed larger backpacks only restoring the first 8 slots by ensuring restored item data loads into the correctly resized storage.
- Applied the same pre-resize restore flow to host-synced multiplayer backpack snapshots.

### Arrest and respawn stability
- Removed backpack enable/disable lifecycle hooks tied to player arrest/exit flows to avoid respawn activation issues after arrest.
- Fixed the post-arrest local player lockup where the game could leave the player unable to move after respawn.

---

## 1.0.5

### Handover UI performance
- Fixed severe handover UI lag when moving items from the backpack panel or player inventory by removing broad hierarchy-wide label scans.
- Reduced repeated header reapply passes and limited backpack label updates to local storage-panel operations so handover paging and item movement remain responsive on both Mono and IL2CPP.

---

## 1.0.4

### Multiplayer backpack sync
- Added host-driven backpack state sync so clients request and receive the host-authoritative backpack snapshot in multiplayer sessions.
- Added chunked backpack snapshot transfer and host-to-client backpack pull/response handling to keep larger backpack inventories synchronized reliably.
- Non-host clients now skip local backpack persistence and instead load/apply the synchronized host state.
- Added `BackpackSyncDebugLogging` configuration to enable verbose multiplayer backpack sync diagnostics when needed.

### Runtime and UI robustness
- Improved IL2CPP string assignment handling in reflection utilities so managed strings can be written safely to `Il2CppSystem.String` members.
- Added supporting save/load and variable-database hooks required for multiplayer backpack synchronization.
- Hardened backpack/player initialization paths so backpack components and synced state are attached/applied more reliably across runtime variants.

### Documentation
- Updated hosted README screenshots and project links for the 1.0.4 release.

---

## 1.0.3

### Deal handover backpack panel
- Added backpack storage integration to the deal handover UI with paged slot rendering for larger backpack tiers.
- Added pager controls (`<`, page indicator, `>`) and ensured page switching updates slot bindings correctly.
- Added a vehicle/backpack view toggle under the pager; backpack view is default and toggle is only shown when vehicle storage is available within 20 meters.

### Handover UI behavior and stability
- Fixed item duplication regression introduced during early slot-initialization experiments.
- Hardened handover panel lifecycle checks against stale/destroyed components to prevent open-time exceptions in IL2CPP.
- Improved pager setup robustness for IL2CPP by recreating/repairing missing controls and guarding fragile component access paths.

### Handover UI placement and visuals
- Reworked pager layout anchoring so controls are placed beneath the backpack storage area.
- Added a dedicated pager background element and aligned it with the pager controls.

---

## 1.0.1

### IL2CPP Hardware Store fixes
- Fixed IL2CPP backpack tier listing creation by using IL2CPP-safe `TryCast` and a template-clone fallback when creating `StorableItemDefinition` instances.
- Improved Hardware Store integration flow to use game-native listing UI refresh methods and to avoid false "added" success logs.
- Added targeted diagnostics for resource lookup and tier-listing pipeline failures to make IL2CPP troubleshooting actionable.

### Backpack tier purchase and use (Hardware Store)
- Backpack tiers are now **purchased at the Hardware Store** (both locations) with **account funds**; they no longer unlock automatically by rank.
- After purchase, a **backpack tier item** appears in your inventory/hotbar (real `StorableItemDefinition` with tier icon). **Select it and press B** to consume it, apply that tier to your backpack, and open the backpack.
- Added config options `Tier{n}_Price` (defaults: 25, 75, 150, 300, 500) for store prices.
- Inventory UI is refreshed after consuming a backpack item so the hotbar slot updates immediately.
- **Already-purchased tiers no longer show in the Hardware Store:** when adding listings we only add tiers the player has not yet purchased; when the player uses a backpack item (press B) we remove that tier and all lower tiers’s from all stores (each tier is an upgrade; buying Tactical Pack means only Hiking Backpack remains).

### Removed / simplified
- Removed patches that blocked the backpack item from being added or that cleaned it up in the background; the item is now a normal purchasable that you use with B.
- Removed `PlayerInventoryPatch` and `InventoryAddBlockPatch`; removed OnEnable patch from `ShopInterfacePatch`. Purchase and payment are handled entirely by the game; our shop patch only adds tier listings and returns without running purchase logic for them.

---

## 1.0.0
- Initial port of ScheduleOne-Backpack (v1.8.1) by D-Kay into the PackRat project structure
- Full cross-runtime support: IL2CPP (net6) and Mono (netstandard2.1)
- Backpack storage entity attached to player prefab via PlayerSpawner patch
- Configurable toggle key (default: B), slot count (default: 12), and unlock rank (default: Hoodlum)
- Backpack contents persist across save/load sessions
- Network save data appended to inventory string with `|||` delimiter
- Host-to-client config sync via Steam lobby metadata
- Optional police body search includes backpack when `EnableSearch = true`
- Shop cart warning accounts for backpack overflow capacity
- Items can be sold directly from backpack via ShopInterface
- StorageMenu expanded to support up to 128 slots dynamically
- Backpack registered as an unlockable in the levelling system with embedded icon
