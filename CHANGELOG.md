# Changelog

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
