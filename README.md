# ClarionDebugger

A modern source-level debugger for the Clarion IDE — built as a managed addin that
plugs into the IDE's existing debugger contract and supplies its own debug engine.

## Why

Clarion devs avoid the built-in debugger. The complaint is the **experience**, not the
engine: the standalone `Cladb.exe` debugger is a dated MDI app (Procedures / Stack Trace /
Watch / Disassembly child windows). Most devs gave up and print-debug instead
(`Message()`, `Stop()`, `OutputDebugString` + DebugView / DebugView++).

The opportunity is **not** a new debug engine — it's a modern front-end over a
re-implemented engine, reusing the IDE's existing breakpoint/watch UI plumbing.

## Feasibility: PROVEN (spike complete)

See [`docs/feasibility-investigation.md`](docs/feasibility-investigation.md) and
[`docs/TSWD-format.md`](docs/TSWD-format.md).

Three things were established:

1. **The native engine is a sealed box.** `Cladbrun.dll` exports exactly one function
   (`D32$StartDebugger`); engine + GUI are welded together. You cannot drive it
   headlessly. → We must bring our own engine.
2. **The IDE gives us the debug UX for free.** `ICSharpCode.SharpDevelop.Debugging.IDebugger`
   + `DebuggerService` provide the breakpoint gutter, hover-eval tooltips, jump-to-line,
   and start/stop wiring. Register a custom `IDebugger` via `DebuggerDoozer` codon and we
   become `DebuggerService.CurrentDebugger`.
3. **The Clarion debug symbol format (`TSWD`) is fully decodable.** Embedded in the EXE as
   a PE-debug-directory overlay. Both line-number tables decoded and validated at 100% —
   source-line→address (set breakpoints) and address→source-line (resolve hits). Mapped
   addresses confirmed to land on real instruction boundaries via disassembly.

## Scope note

Clarion has **no 64-bit compiler** (32-bit only, as of Clarion 11/12). So this is a
32-bit debugger. (Earlier notes about a "64-bit differentiator" are void — Clarion can't
produce 64-bit binaries.) The complement to this debugger for richer runtime tracing is
the existing **DebugView++ fork** (`H:\DevLaptop\DebugViewPP`).

## Architecture

| Layer | Source | Effort |
|-------|--------|--------|
| Breakpoint gutter, watch tooltips, jump-to-line, start/stop | IDE — free via `DebuggerService` / `IDebugger` | none |
| Modern watch / stack / queue / file-buffer panes | Us — reuse Modern Embeditor WebView2/Monaco stack | low |
| `IDebugger` impl + codon registration | Us — become `CurrentDebugger` | low |
| Win32 debug loop (`DebugActiveProcess`/`WaitForDebugEvent`, INT3 breakpoints, `ReadProcessMemory`, thread context) | Us — well-trodden | medium |
| **TSWD parser** (TOC → module / line / symbol / type tables) | Us — *the novel part* | medium-hard |
| Clarion value formatter (render STRING/CSTRING/DECIMAL/GROUP/QUEUE/RECORD from memory) | Us — reuse dictionary/FileSchema type knowledge | low-medium |

## Phased plan

| Phase | Deliverable | Status |
|-------|-------------|--------|
| 0 — Spike | TSWD line tables decoded & validated | ✅ done |
| 1a — Parser | `ClarionDbg.Core` (PE + TSWD parser) + `ClarionDbg` CLI (`dump`/`resolve`); C# at parity with the spike (5566/28651/100%). Build: VS2022 MSBuild, net48/x86. | ✅ done |
| 1b — Module map | Per-module range table (TOC +0x10) **decoded** + module-scoped resolver in `ClarionDbg.Core` (`TryResolve`, `LineToRvasInModule`). Verified round-trip (ABBROWSE line 152 ↔ RVA 0x3DA2F, exact; mid-statement correct); 99.9% `.text` coverage; garbage app-member modules gated out by the line-region check. | ✅ done |
| 1c — Engine | x86 debug engine **working**: `CreateProcess(DEBUG_ONLY_THIS_PROCESS)` + `WaitForDebugEvent` loop, INT3 plant/restore, `GetThreadContext`, ASLR-correct (`loadBase`+RVA), hit → `TryResolve` → module+line, honest exact/nearest reporting, clean teardown. Proven on clbrws.exe (`break --entry --once`). | ✅ done |
| 1d — Startup lines | **Done** — full round-trip proven: `break --line 296 --module clbrws011` resolves line→RVA, plants INT3, stops at startup, resolves the hit back to `clbrws011.clw line 296` (exact). Startup frame found empirically via `--all-entries` discovery (no need to decode the gated `clbrws000–003` modules — they're global/dict init, off the startup path). | ✅ done |
| 1e — IDE glue | **Built** (pending deploy+test in live IDE). Non-invasive `CA Debugger` pad (Ctrl+Alt+G) in the ClarionAssistant addin: `ClarionDebuggerService` launches the engine with `--json`, streams hits, resolves the module→generated-source path via `RedFileService.Active.Resolve`, and calls `DebuggerService.JumpToCurrentLine` to light the editor's current-line bar. Coexists with the built-in debugger. Compiles clean (addin v5.0.315). | deploy+test |

> Generated `.clw` source IS on disk — under the `.red` redirect (e.g. `..\v8Source\clbrws011.clw`); resolved via the addin's existing `RedFileService`. (Earlier "not persisted" assumption was wrong.)
> Engine discovery: `ClarionDebuggerService.FindEngine()` looks next to the addin, then falls back to the dev build path. For production, add `ClarionDbg.exe` + `ClarionDbg.Core.dll` to `deploy.ps1`.

### Deferred (non-blocking)
- Decode the small-offset MEMBER modules (`clbrws.clw`/`001–003`) + the line-region head `[0x624,0x711)`; reconcile Table B. Not on the startup path, so not needed for the engine — revisit for 100% line coverage.
- Symbol record table + Clarion type codes (Phase 3 watches).
| 2 — Stepping | Step over/into/out, continue, **EBP-chain call-stack** pane | ✅ done |
| 3 — Watches | Decode symbol table + Clarion type codes; render locals/globals/buffers/queues in WebView2; hover-eval | |
| 4 — Polish | Conditional breakpoints, watch expressions, set-next-statement (`SetInstructionPointer`) | |

### Recently landed (engine)

- **Multi-DLL debugging foundation.** The engine now tracks every image mapped into the
  target (EXE + DLLs) in a module table. All live-address math is rebased per-image, so
  breakpoints, hits, and stack frames resolve correctly across DLL boundaries — not just in
  the main EXE. Solution DLLs named by the host are pre-parsed so their breakpoints bind
  before launch; runtime-discovered DLLs register on load and drop cleanly on unload.
- **Cleaner call stack.** Frames are walked via the EBP chain (with an at-entry caller
  frame and monotonic-frame guards), replacing the old over-inclusive stack scan.
- **Pause / break-into.** A running target — stuck in a loop or idling in its message pump —
  can now be paused on demand. The engine injects a break via `DebugBreakProcess`, then
  reports the app thread actually in Clarion code (across any loaded module). A Pause button
  in the pad is enabled only while running.
- **Honour hardcoded breakpoints.** An `int3` the program executes itself —
  `DebugBreak()` / `__debugbreak()` (e.g. a Clarion `DebugBreak` under `IsDebuggerPresent()`) —
  is now honoured as a "break here" instead of being swallowed. EIP is handled correctly (a
  hardcoded `int3` *is* the instruction, so it is not rewound).
- **Honest reporting in external code.** When a thread is paused/broken outside the image's
  `.text` (a system call, the OS, another module), the resolver reports external code instead
  of mislabelling it as the nearest Clarion record; the call stack still shows the real
  Clarion frames below.
- **Breakpoint-removal fix.** Removing one of several gutter lines that snapped to the same
  code line no longer leaves the shared `int3` planted and firing. Each gutter line is now an
  independently-removable logical breakpoint, and the physical `int3` is ref-counted so it is
  only unplanted when the last breakpoint referencing it is gone.

## Still to decode (Phase 3 prerequisite)

- Symbol record table: name offset → `{address / frame-offset, type code}`
- Clarion type-code enumeration (for value formatting)

Both appear to be the same tractable TSWD structure — see `docs/TSWD-format.md`.

## Reference assets (oracles to diff against)

- `C:\Clarion12\bin\Cladb.exe` — working reference decoder of TSWD
- `C:\Clarion12\bin\ClaDebugger.chm` — debugger help / format notes
- Test binary: `C:\Users\Public\Documents\SoftVelocity\Clarion11\Examples\HowToClarion\Browses\clbrws.exe` (Full debug build)

## Documentation

- **[User Guide](https://htmlpreview.github.io/?https://github.com/ClarionLive/CA-Debugger/blob/main/docs/user-guide.html)**
  ([source](docs/user-guide.html)) — installing, opening the pad, setting
  breakpoints, watches, shortcuts, theming, and troubleshooting.
- [Feasibility investigation](docs/feasibility-investigation.md) — the spike that
  proved this was buildable.
- [TSWD format notes](docs/TSWD-format.md) — the Clarion debug-symbol format.

## Building from source

Requires **Visual Studio 2022 / MSBuild** with the .NET Framework 4.8 targeting
pack. The engine and addin target **.NET Framework 4.8 (x86)**.

```powershell
.\deploy-addin.ps1 -Version 12      # build + deploy to a Clarion install (10 | 11 | 12 | all)
```

Use `-Kill` to stop a running IDE first, or `-NoBuild` to deploy existing output.

## Built with

- **C# / .NET Framework 4.8 (x86)** — the `ClarionDbg` debug engine (`Core` + CLI)
  and the IDE addin.
- **Win32 Debugging API** — `CreateProcess(DEBUG_ONLY_THIS_PROCESS)`,
  `WaitForDebugEvent`, INT3 breakpoints, thread context.
- **WebView2** (HTML / CSS / JavaScript) — the debugger pad front-end.
- **PowerShell** — build & deployment tooling.

## Authors

- **ClarionLive** — [github.com/ClarionLive](https://github.com/ClarionLive)
- Maintainer: [@peterparker57](https://github.com/peterparker57)

Contributions and issues are welcome via the
[GitHub repository](https://github.com/ClarionLive/CA-Debugger).

## License

Released under the **MIT License** — see [LICENSE](LICENSE).
