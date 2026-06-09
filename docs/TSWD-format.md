# TSWD Debug Format (decoded)

Clarion's embedded debug information. Tag `TSWD` (almost certainly "TopSpeed Watcom
Debug" — Clarion's toolchain descends from TopSpeed/JPI). **Custom format, NOT
CodeView/PDB** — no off-the-shelf parser (DIA/dbghelp) will read it.

All offsets/values below decoded from:
`...\HowToClarion\Browses\clbrws.exe` (Clarion 11, 32-bit, Full debug).

## Locating the blob

1. Read the PE optional header → Data Directory **index 6** (Debug Directory).
2. That points at a 28-byte `IMAGE_DEBUG_DIRECTORY` (it lives in the `.cwdebug`
   section, which is *only* those 28 bytes).
3. The entry's `Type` field = `0x44575354` = ASCII **`TSWD`** (a custom type code).
4. `SizeOfData` = size of the debug blob; `PointerToRawData` = its **file offset**.
   The blob is an **overlay appended after the last PE section**.

For clbrws.exe: blob at file offset `0x16CA00`, size `1,225,720` bytes. ImageBase `0x400000`.

## Header (TOC) — at blob+0x00, little-endian u32

| Offset | Value (this exe) | Meaning |
|--------|------------------|---------|
| +0x00 | `0x44575354` | magic `TSWD` |
| +0x04 | `0x3C` | header size (60 bytes) |
| +0x08 | `0x38` | → **module-name offset array** (u32 offsets into name pool) |
| +0x0C | `0x128` | → **module-name string pool** (null-term plaintext: `ABBROWSE.CLW`, `clbrws001.clw`, …) |
| +0x10 | `0x444` | → per-module table (~61 u32 entries; module code/line ranges — not fully decoded) |
| +0x14 | `0x624` | → **LINE TABLE A** (line-major) |
| +0x18 | `0x889A` | → **LINE TABLE B** (addr-major) |
| +0x1C | `0x32820` | → symbol/addr record table (not fully decoded) |
| +0x20 | `0x76CF0` | → **symbol-name string pool** (plaintext) |
| +0x24 | `0x3D` = 61 | **module count** |
| +0x28 | `0x87A94` | → symbol-name offset array (u32 offsets into symbol pool) |
| +0x2C | `0x87B88` | → table |
| +0x30 | `0x8B8` | count |
| +0x34 | `0x126E38` | → near-end table |

Module-name offset array example (at +0x38): `0x00, 0x0D, 0x19, 0x23, …` — byte offsets
into the pool at +0x128. Module 0 = `ABBROWSE.CLW` (12+NUL = 0x0D), module 1 at +0x0D, etc.

Symbol pool sample (at +0x76CF0): `_main`, `clbrws$$$__attach_process`,
`LFM_CFILE$CFG:RECORD`, `CFG:APPNAME`, `TIT:TITLEID`, `TITLES$TIT:RECORD`,
`R$BRW1::INITIALIZEBROWSE`, `QUEUE:BROWSE:1`, …

## ⚠️ CORRECTION — the line tables are PER-MODULE sub-tables

An earlier draft described `[0x624, 0x889A)` as a single flat array of 6-byte
`{line, rva}` records ("5,566 records, 100% in .text"). **That was misaligned.** Because
every code RVA is `0x0003xxxx`/`0x0008xxxx`, a u32 read at *any* 6-byte phase still lands
in `.text`, so the 100%-in-`.text` check passed even out of phase. Do not trust the flat
parse.

The correct model (decoded + verified):

- `[0x624, 0x889A)` is a **concatenation of per-module line sub-tables**.
- Each sub-table is a run of 6-byte records `struct { uint16 line; uint32 absoluteRVA; }`.
- The **`+0x10` table** (below) gives each module's byte-slice `[start, end)`.
- Each module's slice begins at a small **phase offset (0–5 bytes)** of lead-in before its
  first record grid — brute-force the phase per module (pick the one yielding the most
  `{small line, in-.text rva}` records). **51 of 56** code modules frame perfectly this way.

Verified module decodes (trustworthy ground truth):
```
ABBROWSE.CLW  phase 0:  26 recs, lines 152..179   e.g. line 152 -> RVA 0x3DA2F
ABEIP.CLW     phase 5:  75 recs, lines  47..150
ABFILE.CLW    phase 1:   4 recs, lines 648..652
ABTOOLBA.CLW  phase 2:   2 recs, lines  44..45
```

**Use:** within a module's slice, source line → code address (set a breakpoint), and the
same records sorted by RVA give address → line for that module.

## LINE TABLE B — @blob+0x889A, runs to 0x32820

6-byte records, **address-major** (sorted per module):

```
struct { uint32 codeRVA; uint16 line; }   // 6 bytes
```

**Use:** code address → source line (resolve a breakpoint hit / current line).

Validated: **28,651 records, 100% within `.text`**, lines 5..6345.

## Ground-truth confirmation

Mapped addresses land on exact instruction boundaries (`dumpbin /DISASM:BYTES`):

```
line 203 -> 0x4855E8:  push  101Ch       ← statement / proc start
line 217 -> 0x4856B6:  mov   ebx,4C970Ch
line 237 -> 0x4856CC:  mov   ebx,4C9668h
line 248 -> 0x4856E2:  mov   ebx,4C954Ch
```

## Open items (needed for watch/value support, Phase 3)

- **Symbol record table** — tie each name (via the +0x87A94 offset array) to its
  `{address or stack frame-offset, type code}`. Likely the +0x32820 and/or +0x444 tables.
- **Clarion type-code enumeration** — map type codes → STRING / CSTRING / PSTRING /
  BYTE / SHORT / LONG / DECIMAL / REAL / GROUP / QUEUE / `&FILE:RECORD` etc., with
  picture/size, so raw memory can be rendered. Reuse our dictionary/FileSchema type model.
- **Threaded variables** — instances allocated at runtime; resolved via a runtime
  library call (same `DEBUGHOOK` caveat the native debugger has). Not a regression.

## Module → range map (TOC +0x10 / 0x444) — DECODED

Spans `[0x444, 0x624)` = 480 bytes for 60 modules = **2× u32 per module** =
`{ uint32 sliceStart; uint32 sliceEnd; }`, both **absolute blob offsets into the
Table-A region** `[0x624, 0x889A)`. `{0,0}` for the 4 modules with no code (56 have code).

The slices **tile the region contiguously** (proof the decode is right) — e.g. in
address order: `ABWINDOW [0x54A1,0x56D6] · ABRESIZE [0x56D7,0x56E0] ·
ABFILE [0x56E1,0x56FA] · ABERROR [0x56FB,0x5927] · ABUTIL [0x5928,0x5C41] …` — each
module's end == the next module's start. Slice lengths are **not** multiples of 6 (there's
the per-module phase lead-in described above).

Resolution algorithm (per module): take its `[start,end)` slice → find the record phase →
parse 6-byte `{line, rva}` records. For line→addr, match the line within the module. For
addr→line, the owning module is the one whose decoded records bracket the address; search
its records for the greatest rva ≤ target.

> The flat `TswdDebugInfo.TryAddrToLine` / `RvasForLine` in `ClarionDbg.Core` are
> diagnostics only — they must be replaced by the module-scoped parse above.

## Parser gate + remaining nuance (implemented in ClarionDbg.Core)

`TswdDebugInfo.BuildModules` parses a module's slice **only if it lies inside the line
region** `[OffLineTableA, OffLineTableB)` = `[0x624, 0x889A)`. This is required: a few app
modules (`clbrws.clw`, `clbrws001..003` — the MEMBER/data modules) carry small offsets
*below* the region (`0x0..0x710`) and, parsed blindly, yield garbage records (bogus lines
like 14336) that pollute the address index. Gated out, the resolver gives clean,
verified round-trips and ~99.9% `.text` coverage over 56 modules / 5,349 records.

Open nuance: those small-offset app modules tile their own `[0, ~0x710]` space (distinct
from the `0x54A1+` library cluster), so their line data — if any — is referenced by a
scheme not yet pinned down. The generated *procedure* modules (`clbrws004+`, offsets
`≥ 0x624`) parse fine, so real app code is covered; only the thin MEMBER/data modules are
skipped. Resolve definitively with a ground-truth oracle (Cladb.exe or regenerated
source) during 1c.

## Table B (@0x889A) — still to reconcile

`[0x889A, 0x32820)` parses flat as 28,651 6-byte `{u32 rva, u16 line}` records, mostly
address-ascending (1,285 resets). Given the Table-A lesson, treat its flat validation with
suspicion until confirmed — it may likewise be per-module, or it may be the genuine global
address→line index. Decide by cross-checking a few addresses against the module-scoped
Table-A decode. (Open: why the +0x24 count field = 61 while 60 name-offset entries exist.)
