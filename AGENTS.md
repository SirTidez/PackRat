# PackRat - Schedule One Mod Development Guide

## Project Overview
PackRat is a Schedule One mod that introduces backpacks - portable storage entities that players can carry to store items outside their inventory. **Critical requirement**: This mod must be feature-equivalent in both Mono and IL2CPP runtimes.

## Build Commands

### Configurations
- `Debug Mono` - Debug build for Mono runtime (netstandard2.1)
- `Release Mono` - Release build for Mono runtime
- `Debug IL2CPP` - Debug build for IL2CPP runtime (net6)
- `Release IL2CPP` - Release build for IL2CPP runtime

### Build (Command Line)
```bash
dotnet build -c "Debug IL2CPP"
dotnet build -c "Debug Mono"
```

### Build All Configurations
```bash
dotnet build -c "Debug IL2CPP" && dotnet build -c "Debug Mono"
```

### Packaging for Distribution
Run after building both IL2CPP and Mono versions:
```powershell
.\assets\package-mod.ps1
```

## Code Style Guidelines

### Imports and Namespaces
```csharp
using System.Collections;
using MelonLoader;
using PackRat.Helpers;
using UnityEngine;

#if MONO
using ScheduleOne;
using FishNet;
#else
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppFishNet;
using Object = Il2CppSystem.Object;
#endif
```

### Type Aliases for Cross-Compatibility
Use type aliases at file scope to abstract runtime differences:
```csharp
#if MONO
using S1Storage = ScheduleOne.Storage;
using S1ItemFramework = ScheduleOne.ItemFramework;
#else
using S1Storage = Il2CppScheduleOne.Storage;
using S1ItemFramework = Il2CppScheduleOne.ItemFramework;
#endif
```

### Naming Conventions
- **Namespaces**: PascalCase, match folder structure (e.g., `PackRat.Helpers`, `PackRat.Storage`)
- **Classes**: PascalCase (e.g., `BackpackEntity`, `StorageManager`)
- **Methods**: PascalCase (e.g., `GetAllItems`, `WaitForPlayer`)
- **Private fields**: `_camelCase` with underscore prefix
- **Constants**: `PascalCase` or `UPPER_SNAKE_CASE`
- **Static readonly**: `_camelCase` or `PascalCase`

### Formatting
- Indent: 4 spaces
- Braces: Allman style (opening brace on new line)
- Max line length: ~120 characters
- One class per file, file name matches class name

### Null Handling
Project has `Nullable` disabled. Always null-check:
```csharp
if (obj == null)
    return;
    
if (obj?.Property == null)
    return;
```

### Error Handling
```csharp
try
{
}
catch (Exception ex)
{
    ModLogger.Error("Error in MethodName", ex);
}
```

## Logging

Use `ModLogger` for all logging. It provides consistent logging across Mono and IL2CPP:
```csharp
ModLogger.Info("Regular message");
ModLogger.Debug("Debug only - stripped in Release builds");
ModLogger.Warn("Something unexpected");
ModLogger.Error("Something failed");
ModLogger.Error("Operation failed", exception);  // With exception details
```

## Cross-Platform Compatibility (Critical)

### Conditional Compilation
- `MONO` - Defined for Mono builds
- `RELEASE` - Defined for release builds

### Type Casting Patterns
```csharp
#if !MONO
storageEntity = owner.TryCast<S1Storage.StorageEntity>();
#else
storageEntity = owner as S1Storage.StorageEntity;
#endif
```

### Interface Casting (IL2CPP requires explicit casting)
```csharp
#if !MONO
slot.SetSlotOwner(S1StorageEntity.Cast<S1ItemFramework.IItemSlotOwner>());
#else
slot.SetSlotOwner(S1StorageEntity);
#endif
```

### List Conversion
Use the provided `Il2CppListExtensions`:
```csharp
var items = someIl2CppList.AsEnumerable().Where(x => x != null).ToList();
var il2CppList = csharpList.ToIl2CppList();
```

### Type Checking Pattern
Use `Utils.Is<T>` for cross-platform type checking:
```csharp
if (Utils.Is<StorableItemDefinition>(item.Definition, out var definition))
{
}
```

### Safe Component Access (IL2CPP)
Use `Utils.GetComponentSafe<T>`, `Utils.AddComponentSafe<T>`, `Utils.GetOrAddComponentSafe<T>` instead of direct calls:
```csharp
var component = Utils.GetComponentSafe<MyComponent>(gameObject);
var newComponent = Utils.AddComponentSafe<MyComponent>(gameObject);
var existingOrNew = Utils.GetOrAddComponentSafe<MyComponent>(gameObject);
```

### Safe Object Finding (IL2CPP)
Use `Utils.FindObjectOfTypeSafe<T>` and `Utils.FindObjectsOfTypeSafe<T>`:
```csharp
var found = Utils.FindObjectOfTypeSafe<MyType>();
var allFound = Utils.FindObjectsOfTypeSafe<MyType>();
```

### JSON Serialization
Use `JsonHelper` for cross-platform JSON:
```csharp
string json = JsonHelper.SerializeObject(myObject);
MyObject obj = JsonHelper.DeserializeObject<MyObject>(json);
JsonHelper.PopulateObject(json, existingObject);
```
- **IL2CPP**: Uses `System.Text.Json`
- **Mono**: Uses `Newtonsoft.Json`

### Unity Events
Use `EventHelper` for IL2CPP-safe Unity event subscription:
```csharp
EventHelper.AddListener(MyCallback, unityEvent);
EventHelper.RemoveListener(MyCallback, unityEvent);
EventHelper.AddListener<MyType>(MyCallback, unityEvent);
EventHelper.RemoveListener<MyType>(MyCallback, unityEvent);
```

## Good Practices (Add as discovered)

### Use XML Documentation
```csharp
/// <summary>
/// Opens the backpack inventory UI.
/// </summary>
/// <param name="player">The player opening the backpack.</param>
/// <returns>True if the backpack was opened successfully.</returns>
public bool OpenBackpack(Player player)
```

### Use Coroutines for Async Operations
```csharp
MelonCoroutines.Start(Utils.WaitForPlayer(DoStuff()));
MelonCoroutines.Start(Utils.WaitForNetwork(DoNetworkStuff()));
```

### Use ModLogger for Logging
```csharp
ModLogger.Info("Regular message");
ModLogger.Debug("Debug only - stripped in Release");
ModLogger.Warn("Something unexpected");
ModLogger.Error("Something failed");
ModLogger.Error("Operation failed", exception);  // With exception
```

### Wrap Game Types for API Stability
Create wrapper classes around game types to insulate from game updates and provide cross-platform abstraction (see S1API `StorageEntity` pattern).

### Use Events for Mod Extensibility
```csharp
public static event Action<BackpackEventArgs> OnBackpackOpened;
public static void RaiseBackpackOpened(BackpackEventArgs args) => OnBackpackOpened?.Invoke(args);
```

### Handle Multiplayer State
Always check network role:
```csharp
var nm = InstanceFinder.NetworkManager;
if (nm.IsServer && nm.IsClient)
else if (!nm.IsServer && !nm.IsClient)
else if (nm.IsClient && !nm.IsServer)
else if (nm.IsServer && !nm.IsClient)
```

### Log Type Resolution Failures Once
When IL2CPP type resolution fails, log only once per type to avoid log spam:
```csharp
private static readonly HashSet<string> _typeResolutionFailuresLogged = new();
```

### Use System.Text.Json in IL2CPP for Generic Types
Il2CppNewtonsoft.Json generic methods fail with managed types. Use System.Text.Json instead.

## Bad Practices (Add as discovered)

### DO NOT: Use reflection for IL2CPP generic methods
Generic method invocation via reflection fails in IL2CPP. Use manual parsing or non-generic alternatives.

### DO NOT: Assume type casting works the same in both runtimes
IL2CPP requires `TryCast<T>()` or `Cast<T>()`, Mono uses standard C# casting.

### DO NOT: Directly access Il2Cpp array internals without bounds checking
Always use `.Count` or `.Length` and validate before access.

### DO NOT: Store references to game objects across scene loads
Use scene state cleanup pattern to reset state on scene changes.

### DO NOT: Use `dynamic` keyword
Not supported properly in IL2CPP context.

### DO NOT: Use UnityAction directly in IL2CPP
UnityAction constructor can fail in IL2CPP. Use `EventHelper` or `System.Action` instead.

### DO NOT: Use JsonConvert generic methods in IL2CPP
Il2CppNewtonsoft.Json generic methods fail. Use `JsonHelper` or `System.Text.Json` instead.

### DO NOT: Access fields as properties across runtimes
Some fields become properties in IL2CPP. Use `ReflectionUtils.TryGetFieldOrProperty` for cross-runtime compatibility.

## Project Structure
```
PackRat/
├── MainMod.cs                 # MelonMod entry point
├── Helpers/
│   ├── Utils.cs               # Cross-platform utilities, component helpers
│   ├── ModLogger.cs           # Centralized logging utility
│   ├── ReflectionUtils.cs     # Reflection helpers for cross-runtime
│   ├── NetworkHelper.cs       # FishNet networking utilities
│   ├── JsonHelper.cs          # Cross-platform JSON serialization
│   └── EventHelper.cs         # IL2CPP-safe Unity event handling
├── build/
│   ├── paths.props            # Game directory paths
│   ├── conditions.props       # Build conditions
│   ├── references/            # Mono/IL2CPP assembly references
│   └── events/                # Pre/post build targets
└── assets/
    ├── manifest.json          # Thunderstore manifest
    └── package-mod.ps1        # Packaging script
```

## S1API Reference
Located at: `D:\Schedule 1 Modding\S1API\S1API`

Key patterns to follow from S1API:
- Type alias pattern for cross-compatibility
- Wrapper classes for game types (`StorageEntity`)
- Event-based API design (`StorageEvents`)
- CrossType utility class pattern

## Behind Bars Reference
Located at: `D:\Schedule 1 Modding\Behind Bars`

Key IL2CPP patterns from Behind Bars:
- Safe component methods with type resolution caching
- EventHelper for UnityAction compatibility
- JsonHelper using System.Text.Json in IL2CPP
- NetworkHelper for safe network ID handling
- One-time logging for type resolution failures
- ModLogger for centralized, consistent logging
