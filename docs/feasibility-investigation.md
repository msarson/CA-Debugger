# Feasibility Investigation — log

Chronological record of the investigation that established this project is buildable.

## 1. Premise

Devs avoid the Clarion debugger and print-debug instead. Question: can we build a
debugger they'd actually like? Three tiers considered:

- **Tier 1 — full native engine from scratch** (DebugActiveProcess + symbol parser +
  expression eval + stack walk). Multi-person-year; competes with the existing engine on
  its own terms. **Rejected.**
- **Tier 2 — drive the existing engine with a better UX.** Depends on whether the engine
  is separable from its GUI.
- **Tier 3 — modern trace/logpoint debugging** (no engine). Already covered by John's
  DebugView++ fork.

## 2. What the Clarion debugger actually is

From `C:\Clarion12\bin` + `ClarionHelp` / `ClaDebugger` docs:

- Standalone debugger = **`Cladb.exe`** (+ `Cladbne.exe` no-environment variant).
- Engine runtime = **`Cladbrun.dll`**.
- Real native debug engine: uses `DebugActiveProcess()`, can install as the Windows
  "system debugger," debugs multiple processes.
- **32-bit only.** (Clarion has no 64-bit compiler at all.)
- Requires a **Full** debug-mode compile.
- Threaded variables invisible unless `DEBUGHOOK(var)` placed after `CODE`.

## 3. Tier 2 killed: the engine is a sealed box

`dumpbin /EXPORTS Cladbrun.dll` → **exactly one export: `D32$StartDebugger`**.

No API to set a breakpoint, step, read memory, or walk the stack. Engine and its dated
MDI windows are welded together. You cannot render a modern UI over it — it only draws
its own windows. **"Front-end swap over existing engine" is dead.**

## 4. The real seam: the IDE's debugger contract

From the Clarion IDE API (`query_docs library='Clarion IDE API'`):

- **`ICSharpCode.SharpDevelop.Debugging.IDebugger`** — `Attach`, `Break`, `Continue`,
  `StepInto/Over/Out`, `SetInstructionPointer`, `GetValueAsString(var)`, `Start`;
  events `DebugStarted/Stopped`. (`DefaultDebugger` is a stub impl.)
- **`DebuggerService`** (static) — live `Breakpoints` list (`BreakpointBookmark`),
  `ToggleBreakpointAt`, `JumpToCurrentLine`, current-line marker, hover-eval tooltips
  wired to `GetValueAsString`.
- **`DebuggerDoozer` / `DebuggerDescriptor`** — register a custom `IDebugger` via codon at
  an AddInTree path → becomes `DebuggerService.CurrentDebugger`.

→ Implement `IDebugger` as a managed addin and the IDE supplies the entire debug UX for
free. We only build the engine behind the interface.

## 5. The crux: is the symbol format decodable?

A debugger lives or dies on two lookups: **source line → address** and
**address → source line + variable name → memory/type**. Investigated a Full-debug build
(`clbrws.exe`):

- No `.map` / `.pdb` / `.sym` sidecar. Debug info is **embedded** in the EXE.
- PE Debug Directory → a custom entry, `Type = 'TSWD'`, pointing at a **1.2 MB overlay**
  appended after the last section.
- Blob starts with a **TOC header** of sub-table offsets; module names and symbol names
  are **plaintext**; two **6-byte line-number tables** (line-major and address-major).

Full layout in [`TSWD-format.md`](TSWD-format.md).

## 6. Spike: decode + validate the line tables

`spikes/decode-tswd.ps1`:

- **Table A** `{u16 line, u32 rva}` @0x624: 5,566 records, **100% of RVAs in `.text`**,
  lines 9..1525.
- **Table B** `{u32 rva, u16 line}` @0x889A: 28,651 records, **100% in `.text`**,
  lines 5..6345.
- 34,217 records, 0 outside `.text`. A wrong record format would scatter garbage; 100%
  is conclusive.
- Disassembly cross-check: mapped addresses land on exact instruction boundaries
  (`line 203 → 0x4855E8 = 'push 101Ch'`, etc.).

**Both lookup directions a debugger needs are decodable. Engine feasibility PROVEN.**

## 7. Verdict

Build = **"implement `IDebugger` + bring our own engine"**:

1. Win32 debug loop (`DebugActiveProcess`/`WaitForDebugEvent`, INT3, `ReadProcessMemory`,
   thread context).
2. TSWD parser (line tables done; symbol + type tables remain).
3. Clarion value formatter (reuse dictionary/FileSchema type knowledge).
4. WebView2 panes for watch/stack/queue (reuse Modern Embeditor stack).

Next: **Phase 1 vertical slice** — set a breakpoint, hit it, see the current-line bar in
the IDE editor.
