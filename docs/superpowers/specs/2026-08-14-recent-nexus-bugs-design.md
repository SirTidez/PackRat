# Recent Nexus Bug Repair Design

## Purpose

Produce one releasable PackRat patch that combines the relevant fixes currently split between
`fix/filter-lifecycle` and `fix/il2cpp-diagnostics-shop`, then resolves the recent Nexus reports that those worktrees do
not address. The release must preserve feature equivalence between Mono and IL2CPP and must distinguish automated
build evidence from live Schedule I validation.

The source window is 31 July through 14 August 2026. The Nexus bug list had ten reports with activity in that window;
the next page's newest activity was 28 July and was excluded.

## Workspace and Preservation Boundary

Implementation will occur on `fix/recent-nexus-bugs` in
`E:\RiderProjects\PackRat\.worktrees\recent-nexus-bugs`, created from committed `master` at `8cceb9e`.

The following workspaces are read-only sources of previously implemented fixes:

- `E:\RiderProjects\PackRat\.worktrees\filter-lifecycle`
- `E:\RiderProjects\PackRat\.worktrees\il2cpp-diagnostics-shop`
- the original `E:\RiderProjects\PackRat` checkout

Their tracked and untracked changes must not be modified, stashed, committed, or discarded. Relevant behavior will be
ported into the integration worktree with a file-by-file comparison. Generated Debug binaries are not source inputs.

PackRat's build targets automatically install successful builds into the configured SIMM Mods folders. Verification
must therefore report both build output and installed-DLL hashes, and the final handoff must identify exactly which
branch supplied the installed test binaries.

## Report Disposition

### Import as already addressed

The integration branch will carry forward these local test-build fixes:

- restrict favorite-star editing to the hotkey backpack so embedded filter controls remain usable;
- preserve native employee/NPC inventory geometry and render PackRat independently;
- remove the obsolete 40-slot ceiling across configuration, storage bootstrap, save restoration, upgrades, shop copy,
  and multiplayer configuration synchronization;
- attach the local backpack component only after network ownership is authoritative and expose rate-limited open
  diagnostics;
- stop Hardware Store discovery after successful integration and avoid false failure warnings;
- keep cash on the game's native cash/locker routing path;
- reset and refresh embedded station/storage browsers at their lifecycle boundaries;
- reuse filter slot bindings and event listeners instead of rebuilding them on every refresh;
- keep dragged items and transient tooltips above embedded PackRat panels;
- preserve the configurable metrics font scale.

These are not considered release-verified until they exist together on the integration branch and pass the verification
matrix below.

### Resolve in this repair

The remaining code changes are divided into three independently testable units.

1. Handover correctness: clickable Done behavior and unit-aware auto-fill.
2. Stack responsiveness: bounded planning and incremental execution for large bags.
3. Embedded UI containment: dismissibility, safe-area clamping, and native-UI layering.

### Diagnose rather than guess

The generic "stuttering from install" report has no log or reliable reproduction. Known sources addressed above will be
integrated, and rate-limited timing telemetry will cover backpack opening, Stack execution, and shop integration. No
additional speculative performance change will be made without evidence.

The reported post-sleep mouse cursor problem is out of scope because the reporter reproduced it without PackRat and
attributed it to another client mod.

## Architecture

### 1. Integration layer

Existing worktree fixes will be compared against `master` and ported by behavior, not by blindly combining whole dirty
trees. Each imported group must retain its original runtime guards and must compile before the next independent group is
added. Overlapping edits in `StorageMenuPatch.cs` must be reconciled around the integration branch's current method
structure rather than accepting either worktree wholesale.

The combined state must keep game-owned transforms and routing authoritative:

- employee inventory slots, title, and Done button remain game-owned;
- deal completion remains owned by `HandoverScreen.DoneButton` and `DonePressed`;
- cash remains owned by native CashSlot routing;
- PackRat owns only its browser, settings, filter, paging, and dismiss controls.

### 2. Handover correctness

#### Done button

The current patch moves the native Done button while PackRat renders through a higher-sorting overlay canvas with its own
raycaster. This can leave the button visible but below an interactive PackRat surface.

The repair will:

- capture and restore native Done-button geometry but stop repositioning it into PackRat's overlay;
- size the PackRat card around its own header, slots, paging, and auto-fill controls instead of using a full-screen
  interactive surface;
- set decorative graphics to `raycastTarget = false` and enable raycasts only on PackRat-owned controls and slots;
- leave the native button's listener and `HandoverScreen.DonePressed` path untouched;
- invoke the native customer-change/update methods after auto-fill so interactability reflects the delivered items.

If the native button cannot remain visually usable at a supported resolution, the fallback is a PackRat-owned proxy
button that invokes the native `DoneButton.onClick`; it must mirror `activeInHierarchy` and `interactable` every refresh.
The native-preservation path is preferred and will be tried first.

#### Unit-aware auto-fill

`ProductList.Entry.Quantity` is measured in product units. A packaged `ProductItemInstance` contributes
`Quantity * Amount` units. The current auto-fill subtracts and transfers stack quantity as if it were product units,
which can move five jars for a five-unit requirement.

Auto-fill will use a pure planning seam:

```text
requirement: product id, minimum quality, remaining product units
candidate: source, slot index, product id, quality, package amount, package count, native-acceptable flag
move: source slot, package count, contributed product units
```

Candidate selection will:

1. reject unpackaged, locked, wrong-product, below-quality, or destination-incompatible candidates;
2. prefer combinations that meet the remaining units without oversupply;
3. when several exact fills exist, prefer the smaller package amount, then existing source priority
   (`PACK`, `VEHICLE`, `INVENTORY`), then slot order;
4. permit the smallest possible oversupply only when no exact fill exists and report it in the status line;
5. convert product units to package counts before calling `GetCopy`, `AddItem`, and `ChangeQuantity`;
6. recompute remaining units from the native customer slots after every planned transfer batch.

The game-owned hard filters and `Contract.GetProductListMatch` remain the final authority. PackRat will not invent a
packaging requirement because the contract entry does not contain one.

### 3. Stack responsiveness

The current Stack path scans every source against every target synchronously and performs all native mutations in one
frame. That grows quadratically with configurable large bags and can freeze the main thread.

The repair will separate planning from execution:

- build stable candidate buckets from item definition, quality, packaging identifier/amount, and relevant lock/favorite
  state;
- run native `DoesItemMatchHardFilters`, `CanStackWith`, and capacity checks only within plausible buckets;
- emit an ordered plan of native transfers followed by slot-compaction assignments;
- execute the plan in a coroutine with a bounded work budget per frame;
- disable Stack while execution is active, display progress, and reject a second invocation;
- abort safely if a source or target no longer matches the captured quantity/identity;
- preserve locked slots, removal/add locks, protected favorites, quantities, and surviving item order;
- refresh the visible projection once after completion or abort rather than after every transfer.

The initial budget will be operation-count based and deterministic, avoiding frame-time APIs in the pure planner.
Runtime timing logs will record slot count, candidate comparisons, native transfers, compaction count, and elapsed time
only when debug logging is enabled or a conservative slow-operation threshold is exceeded.

### 4. Embedded UI containment

Storage, station, vehicle, and handover browsers need explicit ownership and screen bounds.

Each embedded surface will receive:

- a PackRat-owned `HIDE PACK` control that hides only the adjacent browser for the current owner session;
- a corresponding compact `SHOW PACK` control that restores it without closing the game-owned UI;
- reset of the hidden state when a different owner/session opens;
- safe-area clamping after surface-specific scale and offset values are applied;
- geometry calculated from the PackRat card's real bounds rather than an assumed 1920x1080 rectangle;
- overlay sorting that keeps PackRat above the stationary owner surface but below game tooltips, drag icons, modal
  controls, and native completion buttons.

The employee/NPC path is special: native geometry must never be moved. PackRat may appear only as an independent sibling
panel and may be hidden without changing the employee screen.

The existing per-surface user offsets remain supported. Clamping affects the effective on-screen position, not the saved
preference, so changing resolution does not destructively rewrite a user's layout.

## Error Handling and Diagnostics

- Use `ModLogger` for all messages.
- Log runtime type-resolution failures once per type.
- Handover auto-fill failures must leave source and destination quantities consistent and show a concise in-UI status.
- Stack aborts must identify the changed source or target in Debug logs and re-enable the button.
- UI recovery must tolerate destroyed Unity objects during scene or owner transitions.
- Open, Stack, and shop timings must be rate-limited and must not perform scene scans merely to produce telemetry.
- No report will be marked fixed solely because both projects compile.

## Test Strategy

### Automated logic tests

Add a small game-independent test project for pure planning and geometry policies. Tests must be written first and watched
failing before production code is added.

Required behaviors:

- a five-unit requirement with five one-unit baggies and five five-unit jars plans five baggies, not five jars;
- a five-unit requirement with only five-unit jars plans one jar;
- mixed package sizes choose an exact fill before an oversupplying fill;
- below-quality, unpackaged, locked, and native-rejected candidates are excluded;
- Stack planning never compares candidates across incompatible buckets;
- Stack planning preserves protected/locked slots and produces deterministic move order;
- a second Stack invocation is rejected while a plan is active;
- safe-area clamping keeps every PackRat card edge visible at 1920x1080, 2560x1440, and 3840x2160 with 80%, 100%,
  and 150% interface scales;
- hiding an embedded browser does not mutate native owner geometry and resets for a different owner session.

Tests assert planner output and geometry results, not source text or mocks of the planner itself.

### Build and static verification

Run fresh, complete commands:

```powershell
dotnet test Tests\PackRat.Logic.Tests\PackRat.Logic.Tests.csproj -c Release
dotnet build -c "Debug IL2CPP"
dotnet build -c "Debug Mono"
dotnet build -c "Release IL2CPP"
dotnet build -c "Release Mono"
git diff --check
```

Record warning and error counts. Confirm installed DLL hashes equal the Release build outputs after the final builds.

### Live-game acceptance gate

Automated verification cannot prove Unity canvas ordering, live FishNet ownership, or gameplay transfers. Before release,
test at minimum:

- Mono and IL2CPP: first and repeated `B` opens;
- employee inventory: both transfer directions, close/reopen, then ordinary storage;
- contract handover: manually insert product, click native Done, and repeat using auto-fill;
- auto-fill: baggies and jars for the same five-unit contract requirement;
- Stack: fragmented 40-slot and at least 128-slot bags while monitoring frame responsiveness and item totals;
- largest locker: every native slot remains reachable;
- mixing, cauldron, oven, and packaging stations: tooltips and drag icons remain visible;
- embedded hide/show during the same session and reset on a new owner;
- 1920x1080, 2560x1440, and 3840x2160 at default and 80% interface scaling;
- multiplayer host/client: tier capacity sync, backpack open, and save/reload snapshot restore.

## Delivery and Status Language

The completed branch will report four distinct states:

1. ported to the integration branch;
2. automated tests passing;
3. Mono/IL2CPP builds and installed hashes verified;
4. live-game acceptance passed or still awaiting a human tester.

Nexus issues must not be marked fixed from states 1-3 alone. Generic stuttering remains "needs diagnostic confirmation" unless
the reporter or a local reproduction supplies evidence matching one of the corrected paths.

## Non-Goals

- Redesigning the approved backpack visual language.
- Replacing Schedule I's contract-matching or cash systems.
- Adding dedicated-server support.
- Changing saved UI preferences solely because the current resolution differs.
- Editing Nexus issue statuses or replying to reporters.
- Packaging or publishing a release before live-game acceptance.
