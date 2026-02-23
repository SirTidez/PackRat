# PackRat Coding Standards

This document defines coding conventions and cross-runtime compatibility rules for all PackRat source files.

---

## Namespace Convention

- All PackRat code lives in `PackRat` or sub-namespaces
  - `PackRat` — top-level classes (e.g., `PlayerBackpack`, `PackRat`)
  - `PackRat.Config` — configuration classes
  - `PackRat.Extensions` — C# extension methods
  - `PackRat.Patches` — Harmony patch classes
  - `PackRat.Helpers` — utility and helper classes (template-provided, do not modify)
- No code in global namespace except `BuildInfo` and assembly attributes in `MainMod.cs`

---

## File Structure

- One class per file; filename matches class name exactly
- Folder structure mirrors namespace:
  - `Config/` → `PackRat.Config`
  - `Extensions/` → `PackRat.Extensions`
  - `Patches/` → `PackRat.Patches`
  - `Helpers/` → `PackRat.Helpers`

---

## Imports and Conditional Compilation

PackRat uses `MONO` for Mono builds and no symbol for IL2CPP. Always use:
- `#if MONO` for Mono-only code
- `#else` for IL2CPP-only code (NOT `#if IL2CPP`)
- `#if !MONO` is equivalent to IL2CPP

```csharp
#if MONO
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
#else
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
#endif
```

### Type Aliases (recommended for files with heavy namespace use)

```csharp
#if MONO
using S1Storage = ScheduleOne.Storage;
using S1ItemFramework = ScheduleOne.ItemFramework;
#else
using S1Storage = Il2CppScheduleOne.Storage;
using S1ItemFramework = Il2CppScheduleOne.ItemFramework;
#endif
```

---

## Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Namespaces | PascalCase | `PackRat.Config` |
| Classes | PascalCase | `PlayerBackpack` |
| Methods | PascalCase | `GetBackpackStorage` |
| Private fields | `_camelCase` | `_storage`, `_backpackEnabled` |
| Constants | `PascalCase` or `UPPER_SNAKE_CASE` | `MaxStorageSlots` |
| Static readonly | `_camelCase` or `PascalCase` | `_instance`, `ModVersion` |

---

## Formatting

- Indent: 4 spaces (no tabs)
- Braces: Allman style (opening brace on new line)
- Max line length: ~120 characters
- One class per file

---

## Null Handling

`Nullable` is disabled project-wide. Always null-check before use:

```csharp
if (obj == null)
    return;

if (obj?.Property == null)
    return;
```

---

## Error Handling

```csharp
try
{
    // ...
}
catch (Exception ex)
{
    ModLogger.Error("Error in MethodName", ex);
}
```

---

## Logging

Use `ModLogger` exclusively. Never use `MelonLogger` or `Melon<T>.Logger` directly.

```csharp
ModLogger.Info("Regular message");
ModLogger.Debug("Debug only — stripped in Release builds");
ModLogger.Warn("Something unexpected");
ModLogger.Error("Something failed");
ModLogger.Error("Operation failed", exception);
```

---

## Cross-Runtime Rules (Critical)

### Component Access

Never call Unity component methods directly when cross-runtime safe versions exist:

```csharp
// WRONG
var comp = gameObject.GetComponent<MyType>();
var comp = gameObject.AddComponent<MyType>();

// CORRECT
var comp = Utils.GetComponentSafe<MyType>(gameObject);
var comp = Utils.AddComponentSafe<MyType>(gameObject);
var comp = Utils.GetOrAddComponentSafe<MyType>(gameObject);
```

### Type Checking

```csharp
// WRONG (fails in IL2CPP)
if (obj is StorableItemDefinition def) { }

// CORRECT
if (Utils.Is<StorableItemDefinition>(obj, out var def)) { }
```

### Type Casting

```csharp
// IL2CPP requires TryCast/Cast
#if !MONO
var entity = owner.TryCast<StorageEntity>();
var asInterface = storage.Cast<IItemSlotOwner>();
#else
var entity = owner as StorageEntity;
var asInterface = storage; // implicit in Mono
#endif
```

### Unity Events

Never construct `UnityAction` directly in IL2CPP. Use `EventHelper`:

```csharp
// WRONG in IL2CPP
myEvent.AddListener(new UnityAction(MyMethod));

// CORRECT
EventHelper.AddListener(MyMethod, myEvent);
EventHelper.RemoveListener(MyMethod, myEvent);
```

Exception: Removing game-internal listeners (not added via EventHelper) may require `new Action(method)` directly in `#if !MONO` blocks.

### Il2Cpp List Handling

```csharp
// Convert Il2Cpp list to C# enumerable
var items = someIl2CppList.AsEnumerable().Where(x => x != null).ToList();

// Convert C# list to Il2Cpp list
var il2CppList = csharpList.ToIl2CppList();
```

### Il2Cpp Array Index Access

```csharp
#if !MONO
var item = list[new Index(i)].TryCast<ItemSlot>();
#else
var item = list[i];
#endif
```

### JSON Serialization

Never use `JsonConvert` or `System.Text.Json` directly. Use `JsonHelper`:

```csharp
string json = JsonHelper.SerializeObject(myObject);
MyType obj = JsonHelper.DeserializeObject<MyType>(json);
JsonHelper.PopulateObject(json, existingObject);
```

### Object Finding

```csharp
var found = Utils.FindObjectOfTypeSafe<MyType>();
var allFound = Utils.FindObjectsOfTypeSafe<MyType>();
```

---

## Harmony Patches

- All patches in `PackRat.Patches` namespace
- Class name pattern: `[TargetClass]Patch`
- Apply `[HarmonyPatch(typeof(TargetClass))]` on the class
- All patch methods must be `static`
- Prefer `[HarmonyPostfix]` unless return value or early-exit is needed
- Use `[HarmonyPrefix]` when you need to block the original method

```csharp
[HarmonyPatch(typeof(SomeClass))]
public static class SomeClassPatch
{
    [HarmonyPatch("MethodName")]
    [HarmonyPostfix]
    public static void MethodName(SomeClass __instance)
    {
        // ...
    }
}
```

---

## Configuration

- All config lives in `PackRat.Config.Configuration` singleton (`Configuration.Instance`)
- MelonPreferences category name: `"PackRat"`
- Config file path: `UserData/PackRat.cfg`
- Access config values via properties, not entries directly

---

## Save/Load

- Register extra save files in `Player.Awake` patch via `LocalExtraFiles.Add("Backpack")`
- Network payload combines inventory and backpack data with `|||` delimiter
- Serialize/deserialize via game's `ItemSet` class

---

## Prohibited Patterns

| Pattern | Reason |
|---------|--------|
| `dynamic` keyword | Not supported in IL2CPP |
| `new UnityAction(...)` in IL2CPP | Delegate construction fails; use `EventHelper` |
| `JsonConvert` generic methods in IL2CPP | Use `JsonHelper` instead |
| Storing GameObject refs across scene loads | Use null-check cleanup on scene change |
| Direct `GetComponent<T>()` in cross-platform code | Use `Utils.GetComponentSafe<T>()` |
| `MelonLogger` directly | Use `ModLogger` |

---

## IL2CPP MonoBehaviour Registration

Any MonoBehaviour subclass used in IL2CPP must be decorated:

```csharp
#if !MONO
[MelonLoader.RegisterTypeInIl2Cpp]
#endif
public class MyBehaviour : MonoBehaviour
{
#if !MONO
    public MyBehaviour(IntPtr ptr) : base(ptr) { }
#endif
}
```
