using System;
using System.Collections.Generic;
using System.Text;
using ClarionDbg.Core;

namespace ClarionDbg.Cli
{
    internal sealed partial class DebugEngine
    {
        // ------------------------------------------------------------------ locals (Variables panel)

        /// <summary>EXPERIMENT: locals — the current frame's local variables (and, when paused inside a
        /// method/routine, the HOST procedure's locals too, since Clarion procedure data is in scope inside
        /// its methods/routines). Reads each frame at [its EBP + frameOffset]; the host procedure's frame is
        /// found by walking the EBP chain to the nearest Procedure-kind frame. Emits a `locals` event with
        /// two groups (method + procedure) for the host's Variables panel.</summary>
        private void HandleLocalsCommand(string[] parts, ref Native.CONTEXT_X86 ctx, bool haveCtx)
        {
            string scope = null, methodName = null, procName = null;
            var methodRows = new List<string>();
            var procRows = new List<string>();

            if (haveCtx)
            {
                var m = ModuleAt(ctx.Eip);
                ProcSymbol sym;
                if (m != null && m.Dbg != null && m.Dbg.ResolveSymbol(ctx.Eip - m.LoadBase, out sym))
                {
                    scope = sym.Kind.ToString().ToLowerInvariant();
                    var curRows = LocalRowsFor(m, sym.EntryRva, ctx.Ebp);
                    if (sym.Kind == SymbolKind.Procedure)
                    {
                        procName = sym.Name; procRows = curRows;          // paused in the procedure body itself
                    }
                    else
                    {
                        methodName = sym.Name; methodRows = curRows;       // in a method/routine
                        LoadedModule pm; ProcSymbol psym; uint pEbp;
                        if (FindHostProcedureFrame(ctx.Ebp, out pm, out psym, out pEbp))
                        { procName = psym.Name; procRows = LocalRowsFor(pm, psym.EntryRva, pEbp); }
                    }
                }
            }

            if (EmitJson)
                Console.WriteLine("@JSON {\"event\":\"locals\""
                    + ",\"scope\":" + Json.Str(scope)
                    + ",\"method\":" + Json.Str(methodName)
                    + ",\"methodItems\":[" + string.Join(",", methodRows) + "]"
                    + ",\"proc\":" + Json.Str(procName)
                    + ",\"procItems\":[" + string.Join(",", procRows) + "]}");
            else
                Console.WriteLine($"  locals: method {methodName} ({methodRows.Count}) / proc {procName} ({procRows.Count})");
        }

        /// <summary>Build the JSON rows for one frame's locals — its proc's entry RVA keys the local set,
        /// each value read live at [frameEbp + frameOffset]. Shared by the method and host-procedure groups.</summary>
        private List<string> LocalRowsFor(LoadedModule m, uint entryRva, uint frameEbp)
        {
            var rows = new List<string>();
            List<LocalSym> locals;
            if (m != null && m.Dbg != null && m.Dbg.ReadLocals().TryGetValue(entryRva, out locals))
                foreach (var l in locals)
                {
                    uint va = (uint)((long)frameEbp + l.FrameOff);
                    string type = ClarionTypeLabel(l.TypeCode, l.Target, l.Size, l.Places);
                    string val = FormatValueAt(l.TypeCode, l.Target, l.Size, l.Places, va);
                    rows.Add("{\"name\":" + Json.Str(l.Name) + ",\"type\":" + Json.Str(type)
                        + ",\"value\":" + Json.Str(val) + ",\"frameOff\":" + l.FrameOff + "}");
                }
            return rows;
        }

        /// <summary>Walk the EBP frame chain from <paramref name="startEbp"/> to the nearest Procedure-kind
        /// frame (the host procedure of the current method/routine). The frame whose return address is at
        /// [ebp+4] has its own base at [ebp]; that base is where its locals are read from.</summary>
        private bool FindHostProcedureFrame(uint startEbp, out LoadedModule pm, out ProcSymbol psym, out uint pEbp)
        {
            pm = null; psym = null; pEbp = 0;
            uint b = startEbp;
            for (int i = 0; i < 64 && b != 0; i++)
            {
                uint callerPc = ReadU32(b + 4);     // return address into the caller frame
                uint callerBase = ReadU32(b);       // the caller frame's EBP (saved here)
                var rm = ModuleAt(callerPc);
                ProcSymbol cs;
                if (rm != null && rm.Dbg != null && rm.Dbg.ResolveSymbol(callerPc - rm.LoadBase, out cs)
                    && cs.Kind == SymbolKind.Procedure)
                { pm = rm; psym = cs; pEbp = callerBase; return true; }
                if (callerBase <= b) break;          // bases strictly increase up the stack
                b = callerBase;
            }
            return false;
        }

        /// <summary>EXPERIMENT: moduledata — list the CURRENT module's module-scope data (the data declared
        /// in this module's DATA section), read live. Excludes file record buffers (*:RECORD) which already
        /// show in the file-buffer tree. Emits a `moduledata` event for the host's Variables panel.</summary>
        private void HandleModuleDataCommand(string[] parts, ref Native.CONTEXT_X86 ctx, bool haveCtx)
        {
            var rows = new List<string>();
            string module = null;

            if (haveCtx)
            {
                var m = ModuleAt(ctx.Eip);
                ProcSymbol sym;
                if (m != null && m.Dbg != null && m.Dbg.ResolveSymbol(ctx.Eip - m.LoadBase, out sym))
                {
                    int mi = sym.ModuleIdx;
                    module = m.Dbg.ModuleNameForIdx(mi);
                    var syms = m.Dbg.DataSymbols;
                    foreach (var ds in syms ?? new List<DataSymbol>())
                    {
                        if (ds.ModuleIdx != mi) continue;
                        if (ds.Name != null && ds.Name.EndsWith(":RECORD", StringComparison.OrdinalIgnoreCase))
                            continue;   // file record buffer — belongs to the file-buffer tree, not module data
                        uint va = m.LoadBase + ds.Rva;
                        string type = ClarionTypeLabel(ds.TypeCode, 0, ds.Size, 0);
                        string val = FormatValueAt(ds.TypeCode, 0, ds.Size, 0, va);
                        rows.Add("{\"name\":" + Json.Str(ds.Name)
                            + ",\"type\":" + Json.Str(type)
                            + ",\"value\":" + Json.Str(val) + "}");
                    }
                }
            }

            if (EmitJson)
                Console.WriteLine("@JSON {\"event\":\"moduledata\",\"module\":" + Json.Str(module)
                    + ",\"items\":[" + string.Join(",", rows) + "]}");
            else
                Console.WriteLine($"  module data ({rows.Count}) in {module ?? "(unknown)"}");
        }

        /// <summary>The single Clarion type-label authority (e.g. LONG, STRING(20), DECIMAL(7,2)). Shared by
        /// the Locals panel and watch/globals so every scope labels a type the same way.</summary>
        internal static string ClarionTypeLabel(byte code, byte target, uint size, int places)
        {
            switch (code)
            {
                case 0x11: return size == 2 ? "SHORT" : size == 4 ? "LONG" : "SIGNED";
                case 0x12: return size == 1 ? "BYTE" : size == 2 ? "USHORT" : size == 4 ? "ULONG" : "UNSIGNED";
                case 0x13: case 0x25: return size == 8 ? "REAL" : "SREAL";
                case 0x18: return "STRING(" + size + ")";   // STRING/CSTRING/PSTRING not yet distinguished
                case 0x23: return "DECIMAL(" + DecimalDigits(size) + "," + places + ")";
                case 0x24: return "PDECIMAL(" + DecimalDigits(size) + "," + places + ")";
                case 0x16:
                    // a by-ref STRING is, to the user, just a STRING(N) — the pointer is an ABI detail
                    if (target == 0x18) return "STRING(" + size + ")";
                    return target == 0x08 ? "&GROUP" : target == 0x05 ? "&CLASS" : "&REF";
                case 0x08: return "GROUP";
                default:   return "TYPE(0x" + code.ToString("X2") + ")";
            }
        }

        // a Clarion packed-decimal of N bytes carries 2N-1 significant digits (the remaining nibble is the sign)
        private static int DecimalDigits(uint sizeBytes) { return sizeBytes > 0 ? (int)(sizeBytes * 2 - 1) : 0; }

        /// <summary>The single Clarion value-rendering authority: read and format a typed value live from
        /// <paramref name="va"/> on the paused thread. Shared by the Locals panel and watch/globals (and, in
        /// future, module data) so every scope renders a value identically. References (&amp;STRING) are
        /// dereferenced here — which is why this lives engine-side: only the engine can read target memory.</summary>
        internal string FormatValueAt(byte code, byte target, uint size, int places, uint va)
        {
            int len;
            switch (code)
            {
                case 0x18: len = (int)Math.Min(size == 0 ? 1u : size, 1024u); break;
                case 0x23: case 0x24: len = (int)(size == 0 ? 1u : size); break;
                case 0x11: case 0x12: case 0x13: case 0x25: len = (int)(size == 0 ? 4u : size); break;
                case 0x16: len = 4; break;
                default:   len = size > 0 ? (int)Math.Min(size, 64u) : 4; break;
            }
            var buf = new byte[len];
            int got = ReadBlock(va, buf);
            if (got <= 0) return "<unreadable>";

            switch (code)
            {
                case 0x11:   // signed
                    if (size == 1) return ((sbyte)buf[0]).ToString();
                    if (size == 2) return BitConverter.ToInt16(buf, 0).ToString();
                    return BitConverter.ToInt32(buf, 0).ToString();
                case 0x12:   // unsigned
                    if (size == 1) return buf[0].ToString();
                    if (size == 2) return BitConverter.ToUInt16(buf, 0).ToString();
                    return BitConverter.ToUInt32(buf, 0).ToString();
                case 0x13: case 0x25:   // float (SREAL / REAL)
                    return size == 8 ? BitConverter.ToDouble(buf, 0).ToString("R")
                                     : BitConverter.ToSingle(buf, 0).ToString("R");
                case 0x18:   // STRING/CSTRING/PSTRING — show the text (cut at NUL, else trim trailing spaces)
                {
                    int n = Array.IndexOf(buf, (byte)0, 0, got);
                    if (n < 0) n = got;
                    return "'" + Encoding.ASCII.GetString(buf, 0, n).TrimEnd(' ') + "'";
                }
                case 0x23: return FormatBcd(buf, got, places, packed: false);  // DECIMAL (sign-first)
                case 0x24: return FormatBcd(buf, got, places, packed: true);   // PDECIMAL (sign-last)
                case 0x16:   // reference: the stack slot holds a pointer
                {
                    uint ptr = BitConverter.ToUInt32(buf, 0);
                    if (ptr == 0) return "(null)";
                    if (target == 0x18)   // &STRING — deref and show the fixed N-char buffer (space-padded)
                    {
                        int sn = size > 0 && size <= 4096 ? (int)size : 256;
                        var sbuf = new byte[sn];
                        int sg = ReadBlock(ptr, sbuf);
                        if (sg <= 0) return "&0x" + ptr.ToString("X") + " <unreadable>";
                        int nz = Array.IndexOf(sbuf, (byte)0, 0, sg);   // CSTRING terminator, if any
                        if (nz < 0) nz = sg;
                        return "'" + Encoding.ASCII.GetString(sbuf, 0, nz).TrimEnd(' ') + "'";
                    }
                    return "&0x" + ptr.ToString("X");
                }
                case 0x08:   // GROUP / CLASS instance — a composite, not a scalar; members not yet expanded
                    return "{…}";
                default:
                {
                    var sb = new StringBuilder("0x");
                    for (int i = 0; i < got; i++) sb.Append(buf[i].ToString("X2"));
                    return sb.ToString();
                }
            }
        }

        /// <summary>Decode a Clarion packed-BCD decimal. Two layouts (verified in clarion-pdb):
        /// DECIMAL (sign-first): sign = high nibble of byte 0 (non-zero = negative); digits = byte0.low,
        /// then byte_i.high, byte_i.low (MSB first). PDECIMAL (sign-last/IBM): sign = low nibble of the last
        /// byte (0x0D = negative); digits = byte_i.high, byte_i.low up to the last byte's high nibble.</summary>
        private static string FormatBcd(byte[] b, int size, int places, bool packed)
        {
            var digits = new StringBuilder();
            bool neg;
            if (!packed)
            {
                neg = (b[0] >> 4) != 0;
                digits.Append((b[0] & 0xf).ToString());
                for (int i = 1; i < size; i++) { digits.Append(((b[i] >> 4) & 0xf)); digits.Append((b[i] & 0xf)); }
            }
            else
            {
                neg = (b[size - 1] & 0xf) == 0xd;
                for (int i = 0; i < size - 1; i++) { digits.Append(((b[i] >> 4) & 0xf)); digits.Append((b[i] & 0xf)); }
                digits.Append(((b[size - 1] >> 4) & 0xf));
            }

            string ds = digits.ToString();
            string intPart, fracPart = "";
            if (places > 0)
            {
                if (ds.Length <= places) ds = ds.PadLeft(places + 1, '0');
                intPart = ds.Substring(0, ds.Length - places);
                fracPart = ds.Substring(ds.Length - places);
            }
            else intPart = ds;

            intPart = intPart.TrimStart('0');
            if (intPart.Length == 0) intPart = "0";
            string val = places > 0 ? intPart + "." + fracPart : intPart;
            return (neg && val != "0") ? "-" + val : val;
        }
    }
}
