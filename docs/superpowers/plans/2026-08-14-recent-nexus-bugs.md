# Recent Nexus Bug Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate the existing PackRat 2.0 test-build fixes and resolve the remaining recent Nexus handover, Stack-performance, and embedded-layout bugs in one Mono/IL2CPP-equivalent branch.

**Architecture:** Port the two existing dirty-worktree fix sets into a clean integration worktree by behavior, then add three pure logic seams for auto-fill unit planning, Stack grouping, and UI bounds/session state. Unity/Harmony adapters remain in the existing patch classes; pure policies are covered by xUnit tests and runtime-only canvas/network behavior remains an explicit live-game gate.

**Tech Stack:** C# latest, .NET Standard 2.1 Mono target, .NET 6 IL2CPP target, Harmony, MelonLoader, Unity uGUI, FishNet, xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1.

## Global Constraints

- Work only in `E:\RiderProjects\PackRat\.worktrees\recent-nexus-bugs` on `fix/recent-nexus-bugs`.
- Treat the original checkout, `filter-lifecycle`, and `il2cpp-diagnostics-shop` worktrees as read-only evidence.
- Preserve feature equivalence in Mono and IL2CPP; use the existing `MONO` aliases and safe component/cast helpers.
- Keep game-owned employee inventory geometry, handover completion, and cash routing authoritative.
- Use `ModLogger`; do not add direct MelonLogger or Unity console calls.
- Write and run each regression test before its production implementation.
- Stage only named source/test/doc files; never stage generated `bin` or `obj` artifacts.
- A successful build installs its DLL into SIMM; verify and report installed hashes after final Release builds.
- Build/package proof is not live-game proof; retain the acceptance checklist from the design spec.

---

### Task 1: Port Capacity, Local-Ownership, Open-Diagnostic, and Shop-Polling Fixes

**Files:**
- Modify: `Config/Configuration.cs`
- Modify: `Config/ConfigSyncManager.cs`
- Modify: `Helpers/ModLogger.cs`
- Modify: `Networking/BackpackStateSyncManager.cs`
- Modify: `Patches/BackpackPurchasePatch.cs`
- Modify: `Patches/PlayerPatch.cs`
- Modify: `Patches/PlayerSpawnerPatch.cs`
- Modify: `Patches/StorageMenuPatch.cs`
- Modify: `PlayerBackpack.cs`
- Modify: `Shops/BackpackShopIntegration.cs`
- Modify: `ARCHITECTURE.md`
- Modify: `CODING_STANDARDS.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: committed PackRat 2.0 behavior at `8cceb9e` and the read-only diff in `fix/il2cpp-diagnostics-shop`.
- Produces: uncapped `TierSlotCounts`, local-owner-only `PlayerBackpack.Instance`, rate-limited Release diagnostics, and terminating shop integration polling.

- [ ] **Step 1: Record the exact source diff and integration branch boundary**

Run:

```powershell
git status --short
git -C ..\il2cpp-diagnostics-shop diff --stat
git -C ..\il2cpp-diagnostics-shop diff --check
```

Expected: only generated Debug files are modified in the integration worktree; the source worktree reports its existing dirty files and no whitespace errors.

- [ ] **Step 2: Port the capacity and storage-bootstrap behavior**

Apply the source worktree's behavior with these exact contracts:

```csharp
// PlayerBackpack.cs
public const int MinimumStorageSlots = 1;

public void Upgrade(int slotCount)
{
    if (slotCount < MinimumStorageSlots || _storage == null)
        return;

    if (_storage.SlotCount > int.MaxValue - slotCount)
    {
        ModLogger.Warn("Cannot upgrade backpack: requested capacity exceeds the supported integer range.");
        return;
    }

    UpdateSize(_storage.SlotCount + slotCount);
}
```

Remove every `MaxStorageSlots` clamp from configuration load/save/export, sync payload application, tier-setting controls,
upgrade logic, and shop copy. In `PlayerSpawnerPatch`, allocate the largest configured enabled tier before save data is
loaded:

```csharp
var initialSlotCount = Configuration.Instance.TierSlotCounts
    .Where((count, index) => Configuration.Instance.TierEnabled[index])
    .DefaultIfEmpty(Configuration.BackpackTiers[0].DefaultSlotCount)
    .Max();
storage.SlotCount = Math.Max(PlayerBackpack.MinimumStorageSlots, initialSlotCount);
```

- [ ] **Step 3: Port authoritative local-player ownership**

Use network-ready ownership in `PlayerPatch` and remove unconditional local initialization:

```csharp
[HarmonyPatch("OnStartClient")]
[HarmonyPostfix]
public static void OnStartClient(Player __instance)
{
    if (__instance == null || !__instance.IsOwner)
        return;

    var backpack = Utils.GetOrAddComponentSafe<PlayerBackpack>(__instance.gameObject);
    backpack?.InitializeForLocalPlayer();
}
```

`DeferredConfigureStorage` must call the actual ownership check rather than `OnStartClient(true)`. Remote components
must self-destroy without replacing an existing local `PlayerBackpack.Instance`.

- [ ] **Step 4: Port Release-visible rate-limited diagnostics**

Retain a single config-controlled debug channel and log the reason an open attempt exits:

```csharp
ModLogger.Debug($"[BackpackUI] Open blocked: reason={reason}, tier={_equippedTierIndex}, " +
    $"storageReady={_storage != null}, instanceLocal={ReferenceEquals(Instance, this)}");
```

Do not perform extra scene scans to create diagnostics. Keep repeated identical open-block messages rate-limited.

- [ ] **Step 5: Port terminating Hardware Store integration**

Use one running coroutine and stop after a successful integration:

```csharp
private static bool _waitForIntegrationRunning;

private static IEnumerator WaitAndIntegrate()
{
    try
    {
        const int attempts = 60;
        for (var i = 0; i < attempts; i++)
        {
            yield return new WaitForSeconds(0.5f);
            if (AddToAllHardwareStoresInScene())
                yield break;
        }

        LogHardwareStoreNotFound();
    }
    finally
    {
        _waitForIntegrationRunning = false;
    }
}
```

Treat "all eligible tiers already present or already owned" as success rather than an injection failure.

- [ ] **Step 6: Build both runtimes**

Run:

```powershell
dotnet build -c "Debug IL2CPP"
dotnet build -c "Debug Mono"
```

Expected: both builds succeed with zero errors; existing nullable/unused-field warnings may remain.

- [ ] **Step 7: Commit the imported fix group**

```powershell
git add Config/Configuration.cs Config/ConfigSyncManager.cs Helpers/ModLogger.cs `
  Networking/BackpackStateSyncManager.cs `
  Patches/BackpackPurchasePatch.cs Patches/PlayerPatch.cs Patches/PlayerSpawnerPatch.cs `
  Patches/StorageMenuPatch.cs PlayerBackpack.cs Shops/BackpackShopIntegration.cs `
  ARCHITECTURE.md CODING_STANDARDS.md README.md
git commit -m "fix: restore large bags and local backpack ownership"
```

---

### Task 2: Port Filter Lifecycle, Employee Isolation, Cash Routing, and Station Layering

**Files:**
- Modify: `Config/Configuration.cs`
- Modify: `Patches/PlayerPatch.cs`
- Modify: `Patches/StationBackpackPanelPatch.cs`
- Modify: `Patches/StorageMenuPatch.cs`

**Interfaces:**
- Consumes: Task 1's integrated `StorageMenuPatch` and the read-only `fix/filter-lifecycle` diff.
- Produces: stable slot bindings, employee side-panel isolation, native cash routing, station rebind/refresh, and embedded favorite/overlay corrections.

- [ ] **Step 1: Compare overlapping files before editing**

Run:

```powershell
git diff HEAD -- Config/Configuration.cs Patches/PlayerPatch.cs Patches/StorageMenuPatch.cs
git -C ..\filter-lifecycle diff -- Config/Configuration.cs Patches/PlayerPatch.cs `
  Patches/StationBackpackPanelPatch.cs Patches/StorageMenuPatch.cs
```

Expected: the integration branch is clean after Task 1's commit; the source diff shows the filter-lifecycle behavior to port.

- [ ] **Step 2: Port employee/NPC ownership isolation**

Pass the title through `ApplyOpenedStorageMenu` and branch before generic geometry:

```csharp
if (IsNpcInventoryOwner(owner, title))
{
    RestoreNativeStorageMenuGeometry(menu);
    ApplyBackpackSidePanel(menu, owner, allowNpcInventory: true);
    return;
}
```

Detection must use concrete Mono/IL2CPP types, runtime type-name fallback, and only this narrow screen-context fallback:

```csharp
return !string.IsNullOrWhiteSpace(title) &&
    title.EndsWith("'s Inventory", StringComparison.OrdinalIgnoreCase);
```

Remove the unconditional `Container.localPosition = Vector3.zero` close mutation; restore captured geometry instead.

- [ ] **Step 3: Port native cash routing**

Add the same early return to storage and station quick-move patches:

```csharp
if (sourceSlot.ItemInstance is CashInstance)
    return;
```

Do not replace the game's target list for cash.

- [ ] **Step 4: Port stable filter and slot binding lifecycle**

Add one-time filter control bindings, persistent slot-to-view bindings, source fingerprints, and a throttled refresh from
the local player's update. A normal refresh must reuse slot UIs and mark only the local layout for rebuild:

```csharp
if (projectionChanged)
    LayoutRebuilder.MarkLayoutForRebuild(surface.SlotContainer);
```

Remove ordinary `Canvas.ForceUpdateCanvases()` and repeated `RemoveListener`/`AddListener` churn from filter refreshes.
Searching must close an open dropdown, and closing an embedded owner must clear transient dropdown/filter bindings.

- [ ] **Step 5: Port favorite, station, and metrics behavior**

Favorite editing must be hotkey-only:

```csharp
if (!state.IsHotkeyBackpack)
{
    SetStandaloneFavoriteControlVisible(state, slotUi, false);
    return;
}
```

Reset station embedded state before cloned slot UIs are cleared, refresh open embedded browsers after transfers, lower
the station canvas from `parent + 200` to `parent + 1`, and retain configurable `MetricsFontScale` at 75%-150%.

- [ ] **Step 6: Build both runtimes and inspect the combined diff**

Run:

```powershell
dotnet build -c "Debug IL2CPP"
dotnet build -c "Debug Mono"
git diff --check
```

Expected: both builds succeed with zero errors; no whitespace errors.

- [ ] **Step 7: Commit the lifecycle fix group**

```powershell
git add Config/Configuration.cs Patches/PlayerPatch.cs `
  Patches/StationBackpackPanelPatch.cs Patches/StorageMenuPatch.cs
git commit -m "fix: stabilize embedded backpack UI lifecycle"
```

---

### Task 3: Add the Pure Logic Test Project and Unit-Aware Auto-Fill Planner

**Files:**
- Modify: `PackRat.csproj`
- Create: `Tests/PackRat.Logic.Tests/PackRat.Logic.Tests.csproj`
- Create: `Tests/PackRat.Logic.Tests/AutoFillPlannerTests.cs`
- Create: `Logic/AutoFillPlanner.cs`
- Modify: `Patches/HandoverScreenPatch.cs`

**Interfaces:**
- Consumes: `ProductList.Entry.Quantity` in product units and `ProductItemInstance.Amount` units per package.
- Produces: `AutoFillPlanner.Plan(AutoFillRequirement, IReadOnlyList<AutoFillCandidate>) -> AutoFillPlan`.

- [ ] **Step 1: Create the test project**

Exclude test sources from the mod project's default recursive compile glob:

```xml
<!-- PackRat.csproj -->
<ItemGroup>
  <Compile Remove="Tests/**/*.cs" />
</ItemGroup>
```

Then create the standalone test project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" PrivateAssets="all" />
    <Compile Include="../../Logic/AutoFillPlanner.cs" Link="Logic/AutoFillPlanner.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing auto-fill tests**

```csharp
using PackRat.Logic;

namespace PackRat.Logic.Tests;

public sealed class AutoFillPlannerTests
{
    [Fact]
    public void FiveUnitsPreferFiveBaggiesOverFiveJars()
    {
        var candidates = new[]
        {
            new AutoFillCandidate("PACK", 0, "product", 2, 5, 5, true, true),
            new AutoFillCandidate("PACK", 1, "product", 2, 1, 5, true, true)
        };

        var plan = AutoFillPlanner.Plan(new AutoFillRequirement("product", 2, 5), candidates);

        var move = Assert.Single(plan.Moves);
        Assert.Equal(1, move.SourceSlotIndex);
        Assert.Equal(5, move.PackageCount);
        Assert.Equal(5, move.ProductUnits);
        Assert.Equal(0, plan.OversuppliedUnits);
    }

    [Fact]
    public void FiveUnitsUseOneJarWhenNoBaggiesExist()
    {
        var candidates = new[]
        {
            new AutoFillCandidate("PACK", 0, "product", 2, 5, 5, true, true)
        };

        var plan = AutoFillPlanner.Plan(new AutoFillRequirement("product", 2, 5), candidates);

        var move = Assert.Single(plan.Moves);
        Assert.Equal(1, move.PackageCount);
        Assert.Equal(5, move.ProductUnits);
    }

    [Fact]
    public void RejectsUnpackagedLockedWrongQualityAndNativeRejectedCandidates()
    {
        var candidates = new[]
        {
            new AutoFillCandidate("PACK", 0, "product", 2, 1, 5, false, true),
            new AutoFillCandidate("PACK", 1, "product", 2, 1, 5, true, false),
            new AutoFillCandidate("PACK", 2, "product", 1, 1, 5, true, true),
            new AutoFillCandidate("PACK", 3, "product", 2, 1, 5, true, true)
        };

        var plan = AutoFillPlanner.Plan(new AutoFillRequirement("product", 2, 5), candidates);

        Assert.All(plan.Moves, move => Assert.Equal(3, move.SourceSlotIndex));
    }
}
```

- [ ] **Step 3: Run the tests and verify RED**

Run:

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
```

Expected: FAIL because `Logic/AutoFillPlanner.cs` and its types do not exist.

- [ ] **Step 4: Implement the minimal pure planner**

Create immutable classes and a deterministic planner. Avoid records because the Mono target does not provide
`IsExternalInit`:

```csharp
namespace PackRat.Logic;

public sealed class AutoFillRequirement
{
    public AutoFillRequirement(string productId, int minimumQuality, int remainingUnits)
    {
        ProductId = productId;
        MinimumQuality = minimumQuality;
        RemainingUnits = remainingUnits;
    }

    public string ProductId { get; }
    public int MinimumQuality { get; }
    public int RemainingUnits { get; }
}

public sealed class AutoFillCandidate
{
    public AutoFillCandidate(string source, int sourceSlotIndex, string productId, int quality,
        int packageAmount, int availablePackages, bool isPackaged, bool isNativeAcceptable)
    {
        Source = source;
        SourceSlotIndex = sourceSlotIndex;
        ProductId = productId;
        Quality = quality;
        PackageAmount = packageAmount;
        AvailablePackages = availablePackages;
        IsPackaged = isPackaged;
        IsNativeAcceptable = isNativeAcceptable;
    }

    public string Source { get; }
    public int SourceSlotIndex { get; }
    public string ProductId { get; }
    public int Quality { get; }
    public int PackageAmount { get; }
    public int AvailablePackages { get; }
    public bool IsPackaged { get; }
    public bool IsNativeAcceptable { get; }
}

public sealed class AutoFillMove
{
    public AutoFillMove(string source, int sourceSlotIndex, int packageCount, int productUnits)
    {
        Source = source;
        SourceSlotIndex = sourceSlotIndex;
        PackageCount = packageCount;
        ProductUnits = productUnits;
    }

    public string Source { get; }
    public int SourceSlotIndex { get; }
    public int PackageCount { get; }
    public int ProductUnits { get; }
}

public sealed class AutoFillPlan
{
    public AutoFillPlan(IReadOnlyList<AutoFillMove> moves, int filledUnits, int oversuppliedUnits)
    {
        Moves = moves;
        FilledUnits = filledUnits;
        OversuppliedUnits = oversuppliedUnits;
    }

    public IReadOnlyList<AutoFillMove> Moves { get; }
    public int FilledUnits { get; }
    public int OversuppliedUnits { get; }
}

public static class AutoFillPlanner
{
    private static readonly Dictionary<string, int> SourcePriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PACK"] = 0,
        ["VEHICLE"] = 1,
        ["INVENTORY"] = 2
    };

    public static AutoFillPlan Plan(AutoFillRequirement requirement,
        IReadOnlyList<AutoFillCandidate> candidates)
    {
        var eligible = candidates
            .Where(candidate => candidate.IsPackaged && candidate.IsNativeAcceptable &&
                candidate.AvailablePackages > 0 && candidate.PackageAmount > 0 &&
                candidate.Quality >= requirement.MinimumQuality &&
                string.Equals(candidate.ProductId, requirement.ProductId,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.PackageAmount)
            .ThenBy(candidate => SourcePriority.GetValueOrDefault(candidate.Source, int.MaxValue))
            .ThenBy(candidate => candidate.SourceSlotIndex)
            .ToArray();

        if (requirement.RemainingUnits <= 0 || eligible.Length == 0)
            return new AutoFillPlan(Array.Empty<AutoFillMove>(), 0, 0);

        var maxPackageAmount = eligible.Max(candidate => candidate.PackageAmount);
        for (var oversupply = 0; oversupply < maxPackageAmount; oversupply++)
        {
            var counts = new int[eligible.Length];
            if (!TryFillExact(eligible, 0, requirement.RemainingUnits + oversupply, counts))
                continue;

            var moves = eligible.Select((candidate, index) => new { candidate, count = counts[index] })
                .Where(entry => entry.count > 0)
                .Select(entry => new AutoFillMove(entry.candidate.Source,
                    entry.candidate.SourceSlotIndex, entry.count,
                    entry.count * entry.candidate.PackageAmount))
                .ToArray();
            var filled = moves.Sum(move => move.ProductUnits);
            return new AutoFillPlan(moves, filled,
                Math.Max(0, filled - requirement.RemainingUnits));
        }

        return new AutoFillPlan(Array.Empty<AutoFillMove>(), 0, 0);
    }

    private static bool TryFillExact(IReadOnlyList<AutoFillCandidate> candidates, int index,
        int remainingUnits, int[] counts)
    {
        if (remainingUnits == 0)
            return true;
        if (index >= candidates.Count || remainingUnits < 0)
            return false;

        var candidate = candidates[index];
        var maxCount = Math.Min(candidate.AvailablePackages,
            remainingUnits / candidate.PackageAmount);
        for (var count = maxCount; count >= 0; count--)
        {
            counts[index] = count;
            if (TryFillExact(candidates, index + 1,
                    remainingUnits - count * candidate.PackageAmount, counts))
                return true;
        }

        counts[index] = 0;
        return false;
    }
}
```

The completed method must first search exact non-oversupplying combinations, then choose the smallest oversupply only
when an exact plan is impossible.

- [ ] **Step 5: Run the tests and verify GREEN**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
```

Expected: all auto-fill tests pass.

- [ ] **Step 6: Adapt handover items to planner candidates**

In `HandoverScreenPatch`, read `ProductItemInstance.Amount`, packaging presence, quality, locks, and whether any customer
slot accepts the item. Call the planner per remaining requirement. Convert planned product units back to package count:

```csharp
var transfer = sourceItem.GetCopy(move.PackageCount);
destination.AddItem(transfer);
source.ChangeQuantity(-move.PackageCount);
```

After each move, recompute remaining product units from customer slots as
`ProductItemInstance.Quantity * ProductItemInstance.Amount`. Report any oversupply in the PackRat status label and call
the native customer-items update path.

- [ ] **Step 7: Verify the adapter and commit**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
dotnet build -c "Debug IL2CPP"
dotnet build -c "Debug Mono"
git diff --check
git add PackRat.csproj Logic/AutoFillPlanner.cs Tests/PackRat.Logic.Tests/PackRat.Logic.Tests.csproj `
  Tests/PackRat.Logic.Tests/AutoFillPlannerTests.cs Patches/HandoverScreenPatch.cs
git commit -m "fix: make deal auto-fill package-unit aware"
```

---

### Task 4: Preserve the Native Done Button and Bound the Handover Overlay

**Files:**
- Create: `Logic/UiBoundsPolicy.cs`
- Create: `Tests/PackRat.Logic.Tests/UiBoundsPolicyTests.cs`
- Modify: `Tests/PackRat.Logic.Tests/PackRat.Logic.Tests.csproj`
- Modify: `Patches/HandoverScreenPatch.cs`

**Interfaces:**
- Consumes: screen safe-area rectangle, desired PackRat rectangle, and saved user offset.
- Produces: `UiBoundsPolicy.Clamp(FloatRect desired, FloatRect safeArea) -> FloatRect` and a handover overlay that does not move or cover the native Done button.

- [ ] **Step 1: Link the new production policy into the test project**

```xml
<Compile Include="../../Logic/UiBoundsPolicy.cs" Link="Logic/UiBoundsPolicy.cs" />
```

- [ ] **Step 2: Write failing safe-area tests**

```csharp
using PackRat.Logic;

namespace PackRat.Logic.Tests;

public sealed class UiBoundsPolicyTests
{
    [Theory]
    [InlineData(1920, 1080, 1.0f)]
    [InlineData(2560, 1440, 0.8f)]
    [InlineData(3840, 2160, 1.5f)]
    public void ClampKeepsAllCardEdgesInsideSafeArea(float width, float height, float scale)
    {
        var safe = new FloatRect(0, 0, width, height);
        var desired = new FloatRect(-180 * scale, height - 760 * scale, 520 * scale, 720 * scale);

        var actual = UiBoundsPolicy.Clamp(desired, safe);

        Assert.True(actual.Left >= safe.Left);
        Assert.True(actual.Bottom >= safe.Bottom);
        Assert.True(actual.Right <= safe.Right);
        Assert.True(actual.Top <= safe.Top);
    }
}
```

- [ ] **Step 3: Run and verify RED**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
```

Expected: FAIL because `UiBoundsPolicy` does not exist.

- [ ] **Step 4: Implement the minimal bounds policy**

```csharp
namespace PackRat.Logic;

public readonly struct FloatRect
{
    public FloatRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public float Left => X;
    public float Right => X + Width;
    public float Bottom => Y;
    public float Top => Y + Height;
}

public static class UiBoundsPolicy
{
    public static FloatRect Clamp(FloatRect desired, FloatRect safeArea)
    {
        var width = Math.Min(desired.Width, safeArea.Width);
        var height = Math.Min(desired.Height, safeArea.Height);
        var x = Math.Clamp(desired.X, safeArea.Left, safeArea.Right - width);
        var y = Math.Clamp(desired.Y, safeArea.Bottom, safeArea.Top - height);
        return new FloatRect(x, y, width, height);
    }
}
```

- [ ] **Step 5: Run and verify GREEN**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
```

Expected: all tests pass.

- [ ] **Step 6: Stop moving the native Done button**

In `UpdateDedicatedOverlayLayout`, retain the initial geometry capture for close restoration but delete assignments to
the native Done button's anchors, pivot, scale, and position. Keep its game-owned listener and canvas unchanged.

Change `PackRat_BackpackCard` from a full-screen interactive rectangle to the card's actual visual bounds. Decorative
card/header/accent images must have `raycastTarget = false`; only slot UIs, paging, filter, hide/show, and auto-fill
buttons may receive raycasts. Apply `UiBoundsPolicy.Clamp` after converting the card's world corners and `Screen.safeArea`
to the overlay canvas coordinate space.

- [ ] **Step 7: Verify and commit**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
dotnet build -c "Debug IL2CPP"
dotnet build -c "Debug Mono"
git diff --check
git add Logic/UiBoundsPolicy.cs Tests/PackRat.Logic.Tests/UiBoundsPolicyTests.cs `
  Tests/PackRat.Logic.Tests/PackRat.Logic.Tests.csproj Patches/HandoverScreenPatch.cs
git commit -m "fix: preserve native handover completion controls"
```

---

### Task 5: Add a Deterministic Stack Planner and Incremental Executor

**Files:**
- Create: `Logic/StackPlanBuilder.cs`
- Create: `Tests/PackRat.Logic.Tests/StackPlanBuilderTests.cs`
- Modify: `Tests/PackRat.Logic.Tests/PackRat.Logic.Tests.csproj`
- Modify: `Patches/StorageMenuPatch.cs`

**Interfaces:**
- Consumes: stable slot descriptors and native compatibility results collected by `StorageMenuPatch`.
- Produces: `StackPlanBuilder.Build(IReadOnlyList<StackSlot>) -> StackPlan`; the patch executes planned transfers in bounded coroutine batches.

- [ ] **Step 1: Link Stack logic into the tests**

```xml
<Compile Include="../../Logic/StackPlanBuilder.cs" Link="Logic/StackPlanBuilder.cs" />
```

- [ ] **Step 2: Write failing planner tests**

```csharp
using PackRat.Logic;

namespace PackRat.Logic.Tests;

public sealed class StackPlanBuilderTests
{
    [Fact]
    public void DoesNotCompareSlotsAcrossCompatibilityBuckets()
    {
        var slots = new[]
        {
            new StackSlot(0, "weed|standard|baggie", 10, 20, false),
            new StackSlot(1, "weed|standard|baggie", 5, 20, false),
            new StackSlot(2, "coke|standard|baggie", 5, 20, false)
        };

        var plan = StackPlanBuilder.Build(slots);

        Assert.Single(plan.Transfers);
        var transfer = Assert.Single(plan.Transfers);
        Assert.Equal(1, transfer.SourceSlotIndex);
        Assert.Equal(0, transfer.TargetSlotIndex);
        Assert.Equal(5, transfer.Quantity);
        Assert.DoesNotContain(plan.Comparisons, pair => pair.Left == 2 || pair.Right == 2);
    }

    [Fact]
    public void ProtectedSlotsNeverMoveOrReceiveItems()
    {
        var slots = new[]
        {
            new StackSlot(0, "weed|standard|baggie", 10, 20, true),
            new StackSlot(1, "weed|standard|baggie", 5, 20, false)
        };

        var plan = StackPlanBuilder.Build(slots);

        Assert.Empty(plan.Transfers);
        Assert.Empty(plan.Compaction);
    }

    [Fact]
    public void MoveOrderIsDeterministicByTargetThenSourceSlot()
    {
        var slots = new[]
        {
            new StackSlot(0, "weed|standard|baggie", 10, 20, false),
            new StackSlot(1, "weed|standard|baggie", 4, 20, false),
            new StackSlot(2, "weed|standard|baggie", 3, 20, false)
        };

        var first = StackPlanBuilder.Build(slots);
        var second = StackPlanBuilder.Build(slots);

        Assert.Equal(
            first.Transfers.Select(x => (x.SourceSlotIndex, x.TargetSlotIndex, x.Quantity)),
            second.Transfers.Select(x => (x.SourceSlotIndex, x.TargetSlotIndex, x.Quantity)));
    }
}
```

- [ ] **Step 3: Run and verify RED**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
```

Expected: FAIL because the Stack planning types do not exist.

- [ ] **Step 4: Implement bucketed planning**

```csharp
namespace PackRat.Logic;

public sealed class StackSlot
{
    public StackSlot(int slotIndex, string compatibilityKey, int quantity, int capacity, bool isProtected)
    {
        SlotIndex = slotIndex;
        CompatibilityKey = compatibilityKey;
        Quantity = quantity;
        Capacity = capacity;
        IsProtected = isProtected;
    }

    public int SlotIndex { get; }
    public string CompatibilityKey { get; }
    public int Quantity { get; }
    public int Capacity { get; }
    public bool IsProtected { get; }
}

public sealed class StackPair
{
    public StackPair(int left, int right) { Left = left; Right = right; }
    public int Left { get; }
    public int Right { get; }
}

public sealed class StackTransfer
{
    public StackTransfer(int sourceSlotIndex, int targetSlotIndex, int quantity)
    {
        SourceSlotIndex = sourceSlotIndex;
        TargetSlotIndex = targetSlotIndex;
        Quantity = quantity;
    }

    public int SourceSlotIndex { get; }
    public int TargetSlotIndex { get; }
    public int Quantity { get; }
}

public sealed class StackAssignment
{
    public StackAssignment(int slotIndex, int? sourceSlotIndex)
    {
        SlotIndex = slotIndex;
        SourceSlotIndex = sourceSlotIndex;
    }

    public int SlotIndex { get; }
    public int? SourceSlotIndex { get; }
}

public sealed class StackPlan
{
    public StackPlan(IReadOnlyList<StackTransfer> transfers,
        IReadOnlyList<StackAssignment> compaction, IReadOnlyList<StackPair> comparisons)
    {
        Transfers = transfers;
        Compaction = compaction;
        Comparisons = comparisons;
    }

    public IReadOnlyList<StackTransfer> Transfers { get; }
    public IReadOnlyList<StackAssignment> Compaction { get; }
    public IReadOnlyList<StackPair> Comparisons { get; }
}

public static class StackPlanBuilder
{
    public static StackPlan Build(IReadOnlyList<StackSlot> slots)
    {
        var comparisons = new List<StackPair>();
        var transfers = new List<StackTransfer>();
        var remaining = slots.ToDictionary(slot => slot.SlotIndex, slot => slot.Quantity);

        foreach (var bucket in slots.Where(slot => !slot.IsProtected && slot.Quantity > 0)
                     .GroupBy(slot => slot.CompatibilityKey, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = bucket.OrderBy(slot => slot.SlotIndex).ToArray();
            for (var targetIndex = 0; targetIndex < ordered.Length; targetIndex++)
            {
                var target = ordered[targetIndex];
                for (var sourceIndex = targetIndex + 1; sourceIndex < ordered.Length; sourceIndex++)
                {
                    var source = ordered[sourceIndex];
                    comparisons.Add(new StackPair(target.SlotIndex, source.SlotIndex));
                    var capacity = target.Capacity - remaining[target.SlotIndex];
                    var amount = Math.Min(capacity, remaining[source.SlotIndex]);
                    if (amount <= 0)
                        continue;

                    transfers.Add(new StackTransfer(source.SlotIndex, target.SlotIndex, amount));
                    remaining[target.SlotIndex] += amount;
                    remaining[source.SlotIndex] -= amount;
                }
            }
        }

        var movable = slots.Where(slot => !slot.IsProtected)
            .OrderBy(slot => slot.SlotIndex).ToArray();
        var survivors = movable.Where(slot => remaining[slot.SlotIndex] > 0)
            .Select(slot => slot.SlotIndex).ToArray();
        var compaction = new List<StackAssignment>();
        for (var index = 0; index < movable.Length; index++)
        {
            int? source = index < survivors.Length ? survivors[index] : null;
            if (source != movable[index].SlotIndex)
                compaction.Add(new StackAssignment(movable[index].SlotIndex, source));
        }

        return new StackPlan(transfers, compaction, comparisons);
    }
}
```

The planner's quantity arithmetic is predictive only. The Unity adapter must still run native hard-filter,
`CanStackWith`, and capacity checks immediately before executing every transfer.

- [ ] **Step 5: Run and verify GREEN**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
```

Expected: all Stack planner tests pass.

- [ ] **Step 6: Replace the synchronous Stack loop with an executor coroutine**

Add `IsConsolidating`, progress label state, and one active coroutine per hotkey backpack. On click:

```csharp
if (state.IsConsolidating)
    return;

state.IsConsolidating = true;
state.ConsolidateButton.interactable = false;
MelonCoroutines.Start(ExecuteStackPlan(state, plan));
```

`ExecuteStackPlan` must process at most 24 native comparisons/transfers per frame, `yield return null`, revalidate source
identity and quantity before every mutation, and restore UI state in `finally`. Refresh the projection once after success
or abort. Log slot count, comparisons, transfers, compaction assignments, and elapsed milliseconds only under debug or
when elapsed time exceeds 250 ms.

- [ ] **Step 7: Verify and commit**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
dotnet build -c "Debug IL2CPP"
dotnet build -c "Debug Mono"
git diff --check
git add Logic/StackPlanBuilder.cs Tests/PackRat.Logic.Tests/StackPlanBuilderTests.cs `
  Tests/PackRat.Logic.Tests/PackRat.Logic.Tests.csproj Patches/StorageMenuPatch.cs
git commit -m "fix: keep large backpack stacking responsive"
```

---

### Task 6: Add Embedded Hide/Show Session State and Safe-Area Containment

**Files:**
- Create: `Logic/EmbeddedPanelSession.cs`
- Create: `Tests/PackRat.Logic.Tests/EmbeddedPanelSessionTests.cs`
- Modify: `Tests/PackRat.Logic.Tests/PackRat.Logic.Tests.csproj`
- Modify: `Patches/StorageMenuPatch.cs`
- Modify: `Patches/StationBackpackPanelPatch.cs`
- Modify: `Patches/HandoverScreenPatch.cs`

**Interfaces:**
- Consumes: an owner/session identity and `UiBoundsPolicy` from Task 4.
- Produces: `EmbeddedPanelSession.Open(ownerId)`, `Hide()`, `Show()`, and `IsHidden` with reset on owner change.

- [ ] **Step 1: Link session logic into the test project**

```xml
<Compile Include="../../Logic/EmbeddedPanelSession.cs" Link="Logic/EmbeddedPanelSession.cs" />
```

- [ ] **Step 2: Write failing session tests**

```csharp
using PackRat.Logic;

namespace PackRat.Logic.Tests;

public sealed class EmbeddedPanelSessionTests
{
    [Fact]
    public void HiddenStatePersistsForSameOwnerAndResetsForDifferentOwner()
    {
        var session = new EmbeddedPanelSession();
        session.Open(10);
        session.Hide();

        session.Open(10);
        Assert.True(session.IsHidden);

        session.Open(11);
        Assert.False(session.IsHidden);
    }

    [Fact]
    public void ShowRestoresSameOwnerSession()
    {
        var session = new EmbeddedPanelSession();
        session.Open(10);
        session.Hide();
        session.Show();

        Assert.False(session.IsHidden);
    }
}
```

- [ ] **Step 3: Run and verify RED**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
```

Expected: FAIL because `EmbeddedPanelSession` does not exist.

- [ ] **Step 4: Implement minimal session state**

```csharp
namespace PackRat.Logic;

public sealed class EmbeddedPanelSession
{
    private int? _ownerId;
    public bool IsHidden { get; private set; }

    public void Open(int ownerId)
    {
        if (_ownerId != ownerId)
        {
            _ownerId = ownerId;
            IsHidden = false;
        }
    }

    public void Hide() => IsHidden = true;
    public void Show() => IsHidden = false;
}
```

- [ ] **Step 5: Run and verify GREEN**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
```

Expected: all tests pass.

- [ ] **Step 6: Add PackRat-owned hide/show controls**

For every embedded state, create one `HIDE PACK` button inside the PackRat header and a compact `SHOW PACK` button under
the same PackRat-owned parent. Hiding must deactivate only PackRat browser visuals/raycasters; it must not change owner
slots, title, buttons, canvas, or transforms. Opening a different owner instance ID resets hidden state.

Use `EventHelper` and bind each action once. Destroyed owner/panel references must be pruned without logging per frame.

- [ ] **Step 7: Clamp every embedded card after scale and user offsets**

Convert the card bounds and `Screen.safeArea` to the owning canvas coordinate space, call `UiBoundsPolicy.Clamp`, and
apply only the effective position. Do not rewrite saved X/Y preferences. Reapply after resolution, canvas scale, or
surface-scale changes. Preserve these sorting rules:

```text
stationary native surface < PackRat card < drag icon / tooltip / modal / native completion control
```

Employee/NPC inventory remains on the dedicated isolation branch and no native geometry mutation is permitted.

- [ ] **Step 8: Verify and commit**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
dotnet build -c "Debug IL2CPP"
dotnet build -c "Debug Mono"
git diff --check
git add Logic/EmbeddedPanelSession.cs Tests/PackRat.Logic.Tests/EmbeddedPanelSessionTests.cs `
  Tests/PackRat.Logic.Tests/PackRat.Logic.Tests.csproj Patches/StorageMenuPatch.cs `
  Patches/StationBackpackPanelPatch.cs Patches/HandoverScreenPatch.cs
git commit -m "fix: contain and dismiss embedded backpack panels"
```

---

### Task 7: Run the Full Verification Matrix and Prepare the Live-Test Handoff

**Files:**
- Modify only if verification exposes a defect in a file changed by Tasks 1-6.

**Interfaces:**
- Consumes: all integrated commits and the approved design acceptance checklist.
- Produces: fresh test/build/static evidence, installed Release hash proof, and a truthful list of remaining live-game gates.

- [ ] **Step 1: Run the complete automated test suite**

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release --no-restore
```

Expected: all tests pass with zero failures.

- [ ] **Step 2: Run all four fresh builds**

```powershell
dotnet build -c "Debug IL2CPP"
dotnet build -c "Debug Mono"
dotnet build -c "Release IL2CPP"
dotnet build -c "Release Mono"
```

Expected: all builds succeed with zero errors. Record warning counts separately for each target.

- [ ] **Step 3: Verify static cleanliness and scope**

```powershell
git diff --check master...HEAD
git status --short
git log --oneline --decorate master..HEAD
git diff --stat master...HEAD
```

Expected: no whitespace errors; generated Debug/Release artifacts may be modified but are not staged or committed.

- [ ] **Step 4: Verify installed Release hashes**

```powershell
Get-FileHash 'bin\Release IL2CPP\net6\PackRat-IL2CPP.dll' -Algorithm SHA256
Get-FileHash 'C:\Users\itide\SIMM\beta\Mods\PackRat-IL2CPP.dll' -Algorithm SHA256
Get-FileHash 'bin\Release Mono\netstandard2.1\PackRat-Mono.dll' -Algorithm SHA256
Get-FileHash 'C:\Users\itide\SIMM\alternate-beta\Mods\PackRat-Mono.dll' -Algorithm SHA256
```

Expected: each build-output hash exactly equals its configured installed target.

- [ ] **Step 5: Re-read the design and classify every report**

Use these exact status labels:

```text
Integrated + automated verification passed
Installed test build; live-game confirmation required
Needs diagnostic reproduction
Confirmed not PackRat
```

Do not label any canvas, multiplayer, transfer, or responsiveness report fixed until the live-game checklist is run.

- [ ] **Step 6: Commit any verification-only source correction, otherwise leave HEAD unchanged**

If verification required a source correction, repeat the failing test's RED/GREEN proof and commit only the affected
source and test files with a specific `fix:` message. Do not create a release package, tag, merge, or publish.

- [ ] **Step 7: Invoke branch-finishing workflow**

Use `superpowers:finishing-a-development-branch`, present the verified branch state, and retain the branch/worktree until
the user completes live-game acceptance.
