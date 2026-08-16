# PackRat Developer Profiler

## Purpose and privacy

PackRat includes an opt-in Developer Profiler for diagnosing slow or unresponsive UI behavior. It is disabled by
default. While disabled, it does not create a trace directory or file and its measurement calls return without
starting timers or reading garbage-collection counters.

When enabled, the profiler records PackRat UI timings and state transitions to a local, pipe-delimited text file. The
trace contains feature and operation names, timings, Unity frame and managed-thread identifiers, garbage-collection
deltas, and contextual UI state. Review a capture before sharing it because contextual state can include search text,
item names, or other gameplay details useful to reproduce a report.

Traces are written under:

`<Schedule I>/UserData/PackRatProfiler/packrat-ui-YYYYMMDD-HHMMSSfff-<runtime>.log`

## Enable or disable

Open the backpack settings, visit **General**, and toggle **Developer Profiler**. The setting takes effect immediately
and persists for later launches. Enabling starts a new trace; disabling writes the final buffered events and closes
the file. Leave it disabled for ordinary play when a capture is not needed.

Only one PackRat DLL should be installed. The profiler is part of the normal Mono and IL2CPP builds; the historical
one-off `PackRat-Profiler` assembly is neither required nor compatible with a normal PackRat DLL loaded at the same
time.

## Likely latency sources

The following areas are useful starting hypotheses, ranked by how broadly they affect UI interactions:

1. **Browser projection and slot rebinding** — search, filters, sorting, paging, transfers, and opening a surface all
   converge on filtering and sorting slots, updating chrome, rebinding item-slot views, and marking layout dirty.
2. **Cold open and prewarming** — scene scans, reflection discovery, and sprite decoding can move work from the first
   hotkey press to player initialization. Compare cold and warm opens rather than averaging them together.
3. **Settings reconstruction and persistence** — switching pages rebuilds controls, while changes may save preferences,
   update layout, refresh shops, or synchronize configuration.
4. **Product metrics aggregation** — the metrics tray walks items and resolves identities, prices, and orders after
   inventory changes and periodic safety refreshes.
5. **Embedded browser refresh fan-out** — station, storage, and handover transfers can trigger browser projection,
   quick-move rebuilding, owner cleanup, and related UI refreshes.
6. **Stack and organize operations** — both mutate backing slots and can trigger another complete projection pass;
   distinguish stack planning from its multi-frame execution.
7. **Handover construction and auto-fill** — first use builds a dedicated surface, while auto-fill scans requirements
   and sources, plans package combinations, performs transfers, and refreshes handover state.
8. **Station first-open construction** — the first interaction clones slot UI and builds paging and chrome; warm opens
   should reuse that state.
9. **Presentation motion** — configured animations last roughly 0.10 to 0.20 seconds and can feel slow even when CPU
   spans are short. Compare with UI Animations disabled before assigning that latency to computation.

At 60 Hz, one frame is 16,667 microseconds. Routine repeated work should preferably stay below half a frame, and
repeated interactions should not consistently coincide with Gen 0, 1, or 2 collections.

## Suggested capture

Use a representative large backpack with empty, partial, favorited, and mixed-product slots. Pause for about two
seconds between scenarios so transitions are easy to identify in the trace.

1. Enable Developer Profiler, then remain idle for five seconds.
2. Open and close the backpack once for a cold capture, then five times for warm captures.
3. Type and clear a search, change each filter and sort, reverse sorting, and page in both directions.
4. Visit each settings page, change one reversible value, then restore it.
5. Expand and collapse metrics; transfer items while it is expanded and collapsed.
6. Run Organize and Stack on a fragmented bag, then Stack again after consolidation.
7. Transfer in both directions with ordinary storage, employee inventory, a vehicle, and at least two station types.
8. Open a deal handover, switch source when available, and run Auto-Fill for both a match and a no-match case.
9. Repeat the slowest scenarios with UI Animations disabled.
10. Disable Developer Profiler to close the trace before copying or uploading it.

Keep Mono and IL2CPP, and host and client, in separate traces. Include runtime, network role, resolution, UI scale,
backpack size, approximate item count, and the installed mod list with the bug report.

## Summarize a trace

Run:

```powershell
.\tools\summarize-ui-profile.ps1 -Path "C:\path\to\packrat-ui-...log"
```

Review p95 and maximum duration first, then correlate the matching state rows by session timestamp. A GC delta means a
collection occurred during the span; it does not prove that span allocated enough memory to cause the collection, so
repeat the scenario before assigning causality.

## Optional dotTrace correlation

Start with **Sampling**, attach to the running Schedule I process, collect only the reproduction window, take a
snapshot, and detach. If the process is missing, use **Show All Processes**, which may request elevation. Some Unity
runtime combinations are not recognized as an attachable managed process; the PackRat flat-file trace remains usable
in that case.

Use **Timeline** only when sampling works and thread, GC, or I/O chronology is needed. On Windows it relies on ETW and
the JetBrains ETW Host Service with administrative privileges. Running-process attach does not support every profiling
mode, so avoid beginning with higher-overhead tracing or line-by-line collection.
