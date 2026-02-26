# Changelog

## Unreleased

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
