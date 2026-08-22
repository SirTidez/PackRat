# PackRat Architecture

## Project Overview

PackRat is a MelonLoader mod for **Schedule One** that adds a persistent backpack storage entity to each player. The mod is fully cross-platform, supporting both:
- **IL2CPP** runtime (`net6`, default build)
- **Mono** runtime (`netstandard2.1`, `#if MONO`)

---

## Directory Layout

```
PackRat/
├── MainMod.cs                      # MelonMod entry point, scene hooks, config init
├── PlayerBackpack.cs               # Core MonoBehaviour: backpack state, open/close/toggle
├── Config/
│   ├── Configuration.cs            # MelonPreferences singleton (toggle key, slots, rank, etc.)
│   └── ConfigSyncManager.cs        # Host→client config sync via Steam lobby data
├── Extensions/
│   └── PlayerExtensions.cs         # GetBackpackStorage(this Player) extension method
├── Patches/
│   ├── PlayerSpawnerPatch.cs        # Attaches StorageEntity + PlayerBackpack to player prefab
│   ├── PlayerPatch.cs               # Save/load hooks (Awake, WriteData, Load, LoadInventory, etc.)
│   ├── PlayerManagerPatch.cs        # Appends backpack subfile to inventory string on load
│   ├── StorageMenuPatch.cs          # Expands UI slot array to 128; adjusts layout for tall grids
│   ├── LevelManagerPatch.cs         # Registers backpack as unlockable at configured rank
│   ├── BodySearchBehaviourPatch.cs  # Police search includes backpack when config enabled
│   ├── CartPatch.cs                 # Adjusts shop cart warning to account for backpack capacity
│   └── ShopInterfacePatch.cs        # Allows selling items directly from backpack
├── Helpers/                         # Shared cross-runtime and input/UI helpers
│   ├── Utils.cs                     # Cross-platform utilities, component helpers, WaitFor* coroutines
│   ├── ModLogger.cs                 # Centralized logging (wraps MelonLogger)
│   ├── ReflectionUtils.cs           # Reflection helpers for cross-runtime member access
│   ├── NetworkHelper.cs             # FishNet networking utilities
│   ├── JsonHelper.cs                # Cross-platform JSON (STJ in IL2CPP, Newtonsoft in Mono)
│   └── EventHelper.cs               # IL2CPP-safe Unity event subscription/unsubscription
│   ├── ControllerBackpackToggle.cs  # Guards native controller Interact before backpack toggle/tier use
│   └── ControllerUiSupport.cs        # Adds PackRat UI to Schedule I's controller-navigation graph
├── build/                           # MSBuild props/targets — do not modify
│   ├── paths.props
│   ├── conditions.props
│   ├── references/
│   └── events/
└── assets/
    ├── manifest.json                # Thunderstore manifest
    ├── backpack_icon.png            # Embedded resource for LevelManager unlockable
    └── package-mod.ps1              # Packaging script
```

---

## Component Relationships

```
MainMod (PackRat : MelonMod)
  ├── OnInitializeMelon
  │     └── Configuration.Instance.Load()
  │     └── Configuration.Instance.Save()   ← forces config file creation
  └── OnSceneWasLoaded("Main")
        ├── Configuration.Instance.Reset()
        └── ConfigSyncManager.StartSync()

ConfigSyncManager
  ├── IsHost → SyncToClients()
  │     └── Lobby.SetLobbyData("PackRat_Config", payload)
  └── IsClient → WaitForPayload()
        └── SteamMatchmaking.GetLobbyData(...)
              └── SyncFromHost(payload)
                    └── Configuration.Instance.{UnlockLevel, EnableSearch, StorageSlots} = ...

PlayerSpawnerPatch (postfix on PlayerSpawner.InitializeOnce)
  └── StorageEntity added to player.gameObject (128 slots max)
  └── PlayerBackpack added to player.LocalGameObject

PlayerBackpack (MonoBehaviour on player.LocalGameObject)
  ├── Awake
  │     └── _storage = GetComponentInParent<StorageEntity>()
  │     └── UpdateSize(Configuration.Instance.StorageSlots)
  ├── Update
  │     └── Input.GetKeyDown(ToggleKey) → Toggle (Open/Close)
  ├── Open()
  │     └── StorageMenu.Open(StorageName, "", _storage [as IItemSlotOwner in IL2CPP])
  │     └── _storage.SendAccessor(Player.Local.NetworkObject)
  ├── Close()
  │     └── StorageMenu.CloseMenu()
  │     └── _storage.SendAccessor(null)
  ├── ContainsItemsOfInterest(maxStealthLevel)
  │     └── Iterates slots, checks legal status / stealth packaging
  └── Upgrade(slotCount) / Downgrade(slotCount, force)

PlayerPatch (6 hooks on Player)
  ├── Awake [prefix]           → register "Backpack" in LocalExtraFiles
  ├── WriteData [postfix]      → serialize ItemSet → write "Backpack" subfile
  ├── Load [prefix]            → load "Backpack" subfile → ItemSet.TryDeserialize → LoadTo
  ├── LoadInventory [prefix]   → split "|||" → left=inventory, right=backpack data
  ├── RequestSavePlayer [prefix] → request host backpack snapshot for local player
  └── Update [prefix]          → apply pending host snapshot fallback for non-host clients

PlayerManagerPatch (postfix on PlayerManager.TryGetPlayerData)
  └── Appends "|||" + backpackString to inventoryString for network sync

StorageMenuPatch
  ├── Awake [prefix]    → prepare the paginated backpack browser surface
  ├── Open [postfix]    → bind one page of the configured capacity to pooled slot views
  └── CloseMenu [prefix]→ restore the shared StorageMenu for vanilla containers

LevelManagerPatch (postfix on LevelManager.Awake)
  └── Load embedded "PackRat.assets.backpack_icon.png"
  └── AddUnlockable(new Unlockable(Configuration.Instance.UnlockLevel, "Backpack", sprite))

BodySearchBehaviourPatch (prefix on BodySearchBehaviour.SearchClean)
  └── If unlocked + EnableSearch → replace body search with backpack check coroutine

CartPatch (postfix on Cart.GetWarning)
  └── If unlocked + cart won't fit inventory → check backpack capacity, update warning

ShopInterfacePatch (postfix on ShopInterface.GetAvailableSlots)
  └── If unlocked → inject backpack slots after hotbar slots in result list
```

---

## Controller Input and UI Navigation

PackRat deliberately reuses Schedule I's controller systems instead of registering a separate input action or replacing the game's UI navigation.

### Opening the backpack

`PlayerBackpack.Update()` checks both the configurable keyboard toggle and `ControllerBackpackToggle.WasPressedThisFrame()`.

```
ControllerBackpackToggle
  ├── GameInput.GetCurrentInputDeviceIsGamepad()
  ├── GameInput.IsTyping == false
  ├── GameInput.GetButtonDown(ButtonCode.Interact)
  └── InteractionManager has no hovered or active interactable
        └── PlayerBackpack.Toggle path
              ├── consume selected PackRat tier item, if present
              └── otherwise open/close an unlocked backpack
```

The hover/active-interaction guard preserves vanilla priority. PackRat never claims **Interact** while the player can use a world object.

### UI surfaces and selection graph

`ControllerUiSupport` presents each active PackRat browser or handover surface to the same native `UIPanel` that already owns the surrounding item slots. It registers PackRat-owned UGUI controls as native `UISelectable` objects, keeps them separate from the game-owned slots during cleanup, and restores them when the surface closes.

`MainMod.OnUpdate()` calls `ControllerUiSupport.Tick()` so controls created by paging, dropdowns, metrics, or settings are re-scanned while a gamepad surface is active. `StorageMenuPatch` presents the standalone and embedded backpack browser; `HandoverScreenPatch` presents the deal-handover controls.

Runtime-created `UISelectable` objects do not receive the serialized `NavigationOverride` data that Schedule I's prefab controls have. `ControllerUiSupport` initializes those overrides and builds a local directional graph. It prefers PackRat controls, then the nearest backpack slot, which prevents a control selection from jumping unexpectedly to the vanilla hotbar. Two explicit routes make the browser reliable:

- Moving **up** from any sort tab selects Search; moving **down** from a sort tab returns to the nearest backpack slot.
- Moving **down** from Search returns to the nearest sort tab, keeping the search, sort, and item grid lanes reciprocal.

Only the direct hotkey backpack browser owns the native Back action. Embedded storage and handover panels keep their host UI's close behavior.

### Controller search and Steam keyboard

Unity's runtime-injected `InputField` is not a stable native selection target, so `ControllerUiSupport` creates an invisible controller-only proxy over Search. The proxy has no visual or raycast target, leaving mouse behavior with the real input field; controller focus is rendered on that real field.

On controller submit, the proxy activates the input and uses Schedule I's native `UITrigger.OnTrigger` callback to request `OnScreenKeyboard`. Steam's gamepad keyboard is only available when a gamepad is active, the Steam overlay is enabled, and the game is running on a Steam Deck or in Big Picture mode. Outside those modes PackRat keeps Search focused and logs a one-time availability warning; this is expected behavior, not a navigation failure.

---

## Configuration

```
Configuration.Instance
  ├── ToggleKey         KeyCode.B          Key to open/close backpack
  ├── EnableSearch      false              Police search checks backpack
  ├── UnlockLevel       ERank.Hoodlum, 1   Required rank to use backpack
  └── StorageSlots      12 (max 128)       Slot count for backpack storage
```

Stored in `UserData/PackRat.cfg` via MelonPreferences. General options remain in `PackRat`; diagnostics,
routing, metrics, theme, overlay layouts, and each backpack tier use `PackRat_*` categories. On upgrade,
`LegacyPreferenceMigration` moves existing values out of the original all-in-one category before saving.

---

## Data Flow: Save / Load

```
SAVE (local):
  PlayerPatch.WriteData
    → new ItemSet(backpackStorage.ItemSlots).GetJSON()
    → ISaveable.WriteSubfile(parentPath, "Backpack", json)

LOAD (local):
  PlayerPatch.Load
    → Player.Loader.TryLoadFile(containerPath, "Backpack", out backpackData)
    → ItemSet.TryDeserialize(backpackData, out itemSet)
    → itemSet.LoadTo(backpackStorage.ItemSlots)

LOAD (network / other player):
  PlayerManagerPatch.TryGetPlayerData
    → loader.TryLoadFile(dataPath, "Backpack", out backpackString)
    → inventoryString += "|||" + backpackString

  PlayerPatch.LoadInventory (on receiving player)
    → split contentsString on "|||"
    → contentsString = left part (inventory)
    → ItemSet.TryDeserialize(right part) → LoadTo backpack slots
```

---

## Networking Strategy

- **FishNet** handles player synchronization
- Backpack data appended to inventory string with `|||` delimiter for network payloads
- Config synced via Steam lobby metadata (host only, clients poll on join)
- Version mismatch in config payload → warning logged, sync aborted
- `PlayerBackpack` is a local-only component; non-owner instances are destroyed in `OnStartClient`

---

## Embedded Resources

| Resource | Path in Project | Embedded Name |
|----------|----------------|---------------|
| Backpack icon | `assets/backpack_icon.png` | `PackRat.assets.backpack_icon.png` |

Loaded via `Assembly.GetManifestResourceStream("PackRat.assets.backpack_icon.png")`.

---

## Build Configurations

| Configuration | Runtime | Symbol | Output |
|--------------|---------|--------|--------|
| Debug IL2CPP | IL2CPP | (none) | `net6` DLL, debug |
| Release IL2CPP | IL2CPP | `RELEASE` | `net6` DLL, optimized |
| Debug Mono | Mono | `MONO` | `netstandard2.1` DLL, debug |
| Release Mono | Mono | `MONO;RELEASE` | `netstandard2.1` DLL, optimized |
