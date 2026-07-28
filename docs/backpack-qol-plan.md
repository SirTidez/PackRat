# Backpack Quality-of-Life Plan

## Scope and boundaries

This work applies only to the standalone backpack opened with the PackRat hotkey. It does not
alter the backpack side panel shown with another storage container, station panels, deal handover,
or shop UI. The native `StorageMenu` remains the owner of opening, closing, input blocking, cursor
state, and camera lock.

The existing card must retain its capacity-based size. Search, filtering, and sorting only change
the display projection; they never reorder or mutate the physical `ItemSlot` collection.

## Shared browse state

Extend `StandaloneBackpackState` with one state object that owns:

- Search text and parsed qualifier tokens.
- Type and quality filters, each defaulting to `All`.
- Sort mode and direction: slot order, name, quantity, quality, type, and recent.
- Current page, selected page capacity, match count, and a per-frame input guard.
- Session-only recent-item timestamps.
- Persistent favorite definition IDs.

Build a `BackpackSlotView` projection containing the source `ItemSlot`, original slot index,
definition ID/name/category, quality, quantity, favorite flag, and recent timestamp. Every redraw
uses this pipeline:

`all slots -> metadata -> query/filter -> sort -> page -> ItemSlotUI assignment`

This preserves quick-move targets and the authoritative storage contents while allowing every
read-only control to compose predictably.

## Controls and presentation

- Keep the current live search input and fixed-capacity card.
- Add a compact control row below search: `TYPE`, `QUALITY`, `SORT`, and a clear-filter action.
- Show active filters and `N MATCHES` in the header metadata.
- Support `name:`, `type:`, `quality:`, and `fav:` qualifiers alongside normal terms. Unknown
  qualifier-like text remains a normal text term.
- Highlight matching slot frames in blue and favorite slot frames in gold without modifying the
  game-owned item graphics.
- Map `LeftArrow` and `UpArrow` to previous page, and `RightArrow` and `DownArrow` to next page.
  Ignore all four while the search input owns focus, so caret/navigation typing remains native.
- Add an optional `RECENT` sort/filter affordance. Recent markers are session-only and disappear
  after the backpack is closed or the scene changes.

## Persistent favorites

Favorites are definition-level bookmarks, not individual item-instance bookmarks: every stack of a
favorite item is marked and can be sorted first. Add the list to `BackpackSaveData` with a safe
empty default, then preserve it through local save/load, network snapshot construction, cached
host snapshots, and snapshot fingerprinting. Old saves remain valid when the field is absent.

Favorite toggling must be an explicit UI action and must schedule the same changed-on-close sync
path used for inventory edits; it must not send a bespoke packet while the menu is open.

## Consolidate stacks

Do not write quantities directly. First confirm the current game build's item transfer/stack API,
then use its capacity and move methods to merge compatible partial stacks. The operation will:

1. Snapshot display metadata and identify only compatible partial stacks.
2. Merge using game-owned transfer methods, respecting hard filters and capacity.
3. Refresh the projection and recent markers after each successful merge.
4. Rely on the existing close-time snapshot/ack/retry flow for client-to-host persistence.
5. Abort cleanly on the first invalid source, target, or transfer result; no best-effort direct
   quantity changes.

## Delivery sequence

1. Add the shared projection model and keyboard page navigation.
2. Add filter/sort controls, qualifier parsing, match feedback, and visual highlights.
3. Add session-only recent tracking and a user-facing favorite toggle.
4. Extend save and sync data for favorites, then verify host/client reconnect restoration.
5. Implement consolidation only after game API research confirms the safe transfer path.

## Validation

For each stage, build Debug Mono and Debug IL2CPP and test: search focus vs hotkeys, all arrow
directions, sorting/filter combinations over multiple pages, open/close/reopen, scene transition,
and normal item movement. For favorites and consolidation, also test save/reload, host/client
reconnect, acknowledged close-time sync, and no duplicate/lost stack contents.
