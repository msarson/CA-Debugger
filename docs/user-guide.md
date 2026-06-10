# CA Debugger — User Guide

A modern, source-level debugger for the Clarion IDE. CA Debugger installs as a
managed IDE addin that brings its own 32-bit debug engine and a clean WebView2
front-end — breakpoints in the editor gutter, single-stepping, a real call stack
with procedure names, and watch-by-name over your file buffers and globals.

> **At a glance:** open the pad with **Ctrl+Alt+G**, pick (or auto-resolve) your
> Target EXE, set breakpoints in the editor gutter, and press **Start**.

---

## Requirements

- **Clarion 10, 11, or 12** (32-bit — Clarion has no 64-bit compiler).
- The target application must be **compiled with Full debug information**. This
  embeds the Clarion `TSWD` debug symbols the engine reads. Without them you'll
  see an error like *"…was the EXE built with Full debug info?"*
  - In Clarion: **Project → Properties → Debug** (or the build configuration) →
    set debug info to **Full**, then rebuild.

---

## Installing

### From a release build (recommended for end users)

1. Download the latest release archive from the
   [Releases page](https://github.com/ClarionLive/CA-Debugger/releases).
2. Close the Clarion IDE.
3. Extract the archive into your Clarion install under:
   ```
   <ClarionRoot>\accessory\addins\ClarionDebugger\
   ```
   (e.g. `C:\Clarion12\accessory\addins\ClarionDebugger\`)
4. Start Clarion. A **CA Debugger** entry appears under the **Tools** menu.

### From source (developers)

```powershell
.\deploy-addin.ps1 -Version 12      # or 11, 10, or all
```

This builds the engine + addin and copies the payload into the matching Clarion
install. Use `-Kill` to stop a running IDE first, `-NoBuild` to deploy without
rebuilding. See the project [README](../README.md) for the build toolchain.

---

## Opening the debugger pad

- Press **Ctrl+Alt+G**, or
- Use the menu: **Tools → CA Debugger**.

The pad docks like any other IDE tool window. It coexists with Clarion's built-in
debugger — you can use either.

---

## The window

```
┌───────────────────────────────────────────────────────────────┐
│ TARGET EXE  [ C:\...\MyApp.exe                    ]  [Browse…]  │
├───────────────────────────────────────────────────────────────┤
│ ▶Start  ▶Continue  ⤼Step Over  ⤓Step Into  ⤒Step Out  ■Stop  ● idle  ⚙ ◑ │
├───────────────────────────────────────────────────────────────┤
│  Status line — where execution is paused                       │
├──────────────────────────┬────────────────────────────────────┤
│ VARIABLES                │  Source view (current line)         │
│   Tables (file buffers)  │                                     │
│   Global Variables       │                                     │
│ WATCH                    │                                     │
│ CALL STACK               │                                     │
│ BREAKPOINTS              ├────────────────────────────────────┤
│ REGISTERS                │  DEBUG CONSOLE                      │
└──────────────────────────┴────────────────────────────────────┘
```

### Target EXE

- The field **auto-resolves** from the active IDE project (it reads the project's
  output type/name), so usually it's already filled in.
- Otherwise click **Browse…** and pick the `.exe` (built with Full debug info).

### Toolbar & run states

| Button | Action |
|--------|--------|
| **Start** | Launch the target EXE and begin a debug session |
| **Continue** | Resume until the next breakpoint or exception |
| **Step Over** | Execute the current line, stepping over procedure calls |
| **Step Into** | Execute the current line, stepping into a procedure call |
| **Step Out** | Run until the current procedure returns to its caller |
| **Stop** | Terminate the debug session and the target process |

The status dot shows **idle**, **launching…**, **running**, or **paused**.
Buttons enable/disable to match the state (e.g. stepping is only available while
paused).

---

## Setting breakpoints

Set breakpoints the normal Clarion way — **click the editor gutter** next to a
source line in a generated `.clw`. CA Debugger listens for gutter changes and
lists every breakpoint in the **Breakpoints** pane (`file : line`). When the
debuggee hits one, execution pauses and the source view jumps to and highlights
the current line (with a `●` marker in the gutter region of the source pane).

---

## Inspecting state (while paused)

### Variables

- **Tables** — your file record buffers (the `FILE$PREFIX:RECORD` groups). Expand
  a record to see its fields.
- **Global Variables** — module/global scalars and groups.

Values resolve live while a node is visible (the section is open, and for fields,
the parent record is expanded too). Collapsing a section stops watching its
members, keeping the session light.

### Watch

Watch any variable by name — four ways:

1. **Type a name** in the *Add watch* box (with type-ahead suggestions) and press
   Enter — e.g. `JOB:JOB_DESC`.
2. Click the **☆** star that appears when you hover a field in the Variables tree.
3. **Right-click** one or more selected rows in the Variables tree → *Add to
   Watch* (Ctrl-click / Shift-click to multi-select).
4. From a **hover data-tip** (see below), click the star to pin it.

Click a watch row's **▸** chevron to expand a long/clipped value; **✕** removes it.

### Call Stack

Shows the paused frames with procedure name and `module : line`. Click a frame to
jump the source view to that location.

### Registers

x86 register values (EIP, ESP, EBP, EAX, …) shown when paused.

### Hover data-tips

Hover a known variable name **in the source view** (or in a panel row) to get a
pop-up showing its name, type, and current value. From the tip you can **pin it
to Watch** (★) or **copy the value** (⧉).

### Debug Console

A running log of session events — breakpoint hits, pauses, commands, and errors.

---

## Keyboard shortcuts

Debug commands ship **unbound by default**. The Clarion IDE claims `F5 / F10 /
F11` before they reach the pad, so you choose your own combinations:

1. Click the **gear (⚙)** icon → **Settings**.
2. Under **Keyboard Shortcuts**, click **Rebind** next to a command.
3. Press a key combination the IDE doesn't use (e.g. `Ctrl+Alt+S` for Start).
   **Clear** removes a binding.

Bindings persist locally (per machine/user). Shortcuts fire only when the pad
has focus and the relevant button is enabled.

---

## Light / dark theme

Click the **◑** (half-circle) toolbar button to toggle between light and dark.
Your choice is remembered. (Identifiers, the primary button, and the Target-EXE
label are tuned for readable contrast in both themes.)

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| *"…built with Full debug info?"* on Start | Rebuild the target with **Full** debug information (not partial/none). |
| F5 / F10 / F11 do nothing | Expected — the IDE owns those keys. Bind your own shortcuts in **Settings (⚙)**. |
| Target EXE field is blank | Open/activate the project, or pick the EXE with **Browse…**. |
| No source shown on a hit | The generated `.clw` is resolved via the project's `.red` redirect; make sure the source still exists where the redirect points. |
| Stale theme colors after an update | Reopen the pad (the WebView may have cached the previous HTML). |
| Pad won't open | Confirm the addin is installed under `<ClarionRoot>\accessory\addins\ClarionDebugger\` and restart the IDE. |

---

## How it works (short version)

The native Clarion engine (`Cladbrun.dll`) is a sealed GUI box and can't be driven
headlessly, so CA Debugger ships its **own** Win32 debug engine
(`CreateProcess(DEBUG_ONLY_THIS_PROCESS)` + `WaitForDebugEvent`, INT3 breakpoints,
thread context). It decodes Clarion's **TSWD** debug-symbol format embedded in the
EXE to map source lines ↔ addresses and to locate and format variables. The IDE
provides the gutter, jump-to-line, and hover plumbing; the pad provides the modern
WebView2 UI. For the full technical story, see
[`feasibility-investigation.md`](feasibility-investigation.md) and
[`TSWD-format.md`](TSWD-format.md).
