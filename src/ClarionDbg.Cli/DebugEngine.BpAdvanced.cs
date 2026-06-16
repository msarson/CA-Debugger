using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ClarionDbg.Core;

namespace ClarionDbg.Cli
{
    /// <summary>
    /// Advanced breakpoint behaviour — the centralized "should this hit actually pause?" decision shared
    /// by conditional breakpoints, hit counts, and tracepoints. All three hang off one path so the engine
    /// evaluates them in a fixed order: condition gate → hit-count rule → tracepoint (log+resume) → pause.
    ///
    /// Value access reuses the same resolution the Watch panel uses (<see cref="ResolveDataAcrossModules"/>
    /// + <see cref="FormatValueAt"/>), but reads SYNCHRONOUSLY at hit time. THREADed (.cwtls) data needs a
    /// func-eval round-trip (it can't be read inline), so for v1 a condition/token over threaded data is
    /// treated as indeterminate (condition ⇒ pause so the user notices; token ⇒ {?name}).
    /// </summary>
    internal sealed partial class DebugEngine
    {
        /// <summary>The logical breakpoint planted at this address, or null (a plain INT3 with no
        /// advanced properties, or an anonymous --rva breakpoint). Several gutter lines can snap to one
        /// address; the first match wins (advanced-property collisions on one address are unsupported).</summary>
        private UserBreakpoint FindBpAt(LoadedModule m, uint rva)
        {
            if (m == null) return null;
            foreach (var b in _bps)
                if (b.Owner == m && b.Rvas.Contains(rva)) return b;
            return null;
        }

        /// <summary>Centralized decision for a breakpoint that carries advanced properties. Returns true to
        /// pause (a real stop), false to resume silently. Order: condition gate, then hit-count rule (over
        /// condition-satisfied hits), then tracepoint (log + resume). Side effect: increments HitCount.</summary>
        private bool ShouldPauseAtBp(UserBreakpoint bp)
        {
            // 1) Condition gate — false ⇒ resume silently; indeterminate ⇒ pause and surface why.
            if (!string.IsNullOrEmpty(bp.Condition))
            {
                bool? cond = TryEvalCondition(bp.Condition);
                if (cond == false) return false;
                if (cond == null)
                {
                    Console.WriteLine($"  bp {bp.Module}:{bp.Line}: condition '{bp.Condition}' could not be evaluated — pausing");
                    return true;
                }
            }

            // 2) Hit count — counts hits whose condition passed; rule unmet ⇒ resume silently.
            bp.HitCount++;
            if (!string.IsNullOrEmpty(bp.HitMode))
            {
                bool satisfied;
                switch (bp.HitMode)
                {
                    case "eq":  satisfied = bp.HitCount == bp.HitValue; break;
                    case "gte": satisfied = bp.HitCount >= bp.HitValue; break;
                    case "mod": satisfied = bp.HitValue > 0 && (bp.HitCount % bp.HitValue) == 0; break;
                    default:    satisfied = true; break;
                }
                if (!satisfied) return false;
            }

            // 3) Tracepoint — interpolate {var} tokens, log, and keep running (never pauses).
            if (bp.Trace != null)
            {
                EmitTrace(bp, InterpolateTrace(bp.Trace));
                return false;
            }

            return true;
        }

        private void EmitTrace(UserBreakpoint bp, string message)
        {
            Console.WriteLine($"  [TRACE] {bp.Module}:{bp.Line}: {message}");
            if (EmitJson) Console.WriteLine("@JSON " + Json.Trace(bp.Module, bp.Line, message, bp.HitCount));
        }

        // ------------------------------------------------------------------ condition evaluation

        /// <summary>Evaluate a simple <c>LHS &lt;op&gt; RHS</c> condition against live target memory.
        /// LHS is a data name (global / module-static / record buffer / record field — the same scope the
        /// Watch panel resolves). RHS is a numeric literal, a quoted string, or another data name. Returns
        /// the boolean result, or null when it cannot be evaluated (unparseable, unresolvable, or threaded).</summary>
        private bool? TryEvalCondition(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return true;

            string op; int opPos, opLen;
            if (!FindOperator(expr, out op, out opPos, out opLen)) return null;

            string lhsName = expr.Substring(0, opPos).Trim();
            string rhsRaw = expr.Substring(opPos + opLen).Trim();
            if (lhsName.Length == 0 || rhsRaw.Length == 0) return null;

            double lnum; string lstr;
            int lk = ReadVarValue(lhsName, out lnum, out lstr);
            if (lk == 0) return null; // unresolvable / unreadable / threaded ⇒ indeterminate

            // Resolve RHS: quoted string literal, numeric literal, or a second data name.
            bool rhsString; double rnum = 0; string rstr = null;
            if (rhsRaw[0] == '\'' || rhsRaw[0] == '"')
            {
                rhsString = true; rstr = StripQuotes(rhsRaw);
            }
            else if (double.TryParse(rhsRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out rnum))
            {
                rhsString = false;
            }
            else
            {
                int rk = ReadVarValue(rhsRaw, out rnum, out rstr);
                if (rk == 0) return null;
                rhsString = rk == 2;
            }

            int cmp;
            if (lk == 2 || rhsString)
            {
                // string domain — render either side to text and compare ordinally
                string a = lk == 2 ? (lstr ?? "") : lnum.ToString(CultureInfo.InvariantCulture);
                string b = rhsString ? (rstr ?? "") : rnum.ToString(CultureInfo.InvariantCulture);
                cmp = string.CompareOrdinal(a, b);
            }
            else
            {
                cmp = lnum.CompareTo(rnum);
            }
            return ApplyOp(op, cmp);
        }

        /// <summary>Apply a comparison operator to the sign of (LHS - RHS).</summary>
        private static bool ApplyOp(string op, int cmp)
        {
            switch (op)
            {
                case "==": case "=": return cmp == 0;
                case "!=": case "<>": return cmp != 0;
                case ">":  return cmp > 0;
                case ">=": return cmp >= 0;
                case "<":  return cmp < 0;
                case "<=": return cmp <= 0;
                default:   return false;
            }
        }

        /// <summary>Locate the comparison operator, preferring the earliest two-char operator, then the
        /// earliest single-char one. Good enough for the supported grammar (operators never appear inside
        /// a bare data name; a stray operator char inside a quoted RHS sits after the real operator).</summary>
        private static bool FindOperator(string s, out string op, out int pos, out int len)
        {
            op = null; pos = -1; len = 0;
            int best = int.MaxValue; string bestOp = null;
            foreach (var o in new[] { ">=", "<=", "==", "!=", "<>" })
            {
                int idx = s.IndexOf(o, StringComparison.Ordinal);
                if (idx >= 0 && idx < best) { best = idx; bestOp = o; }
            }
            if (bestOp != null) { op = bestOp; pos = best; len = 2; return true; }
            foreach (var o in new[] { ">", "<", "=" })
            {
                int idx = s.IndexOf(o, StringComparison.Ordinal);
                if (idx >= 0 && idx < best) { best = idx; bestOp = o; }
            }
            if (bestOp != null) { op = bestOp; pos = best; len = 1; return true; }
            return false;
        }

        // ------------------------------------------------------------------ tracepoint interpolation

        /// <summary>Substitute <c>{name}</c> tokens with the live value of each data name. Unresolvable or
        /// threaded names render as <c>{?name}</c> so the message still logs.</summary>
        private string InterpolateTrace(string template)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            return Regex.Replace(template, @"\{([^{}]+)\}", mm =>
            {
                string nm = mm.Groups[1].Value.Trim();
                double n; string s;
                int k = ReadVarValue(nm, out n, out s);
                if (k == 1) return n.ToString(CultureInfo.InvariantCulture);
                if (k == 2) return s ?? string.Empty;
                return "{?" + nm + "}";
            });
        }

        // ------------------------------------------------------------------ synchronous value read

        /// <summary>Read a data name's CURRENT value synchronously at hit time. Returns 0 = not found /
        /// unreadable / threaded, 1 = numeric (num set), 2 = string (str set). Reuses the Watch panel's
        /// name resolution; numeric scalars are decoded raw (locale/quote-proof), everything else falls
        /// back to the shared display formatter.</summary>
        private int ReadVarValue(string name, out double num, out string str)
        {
            num = 0; str = null;
            TswdDebugInfo.DataLocation loc; LoadedModule owner;
            if (!ResolveDataAcrossModules(name, out owner, out loc)) return 0;

            uint va = owner.LoadBase + loc.Rva;
            bool threaded = owner.CwtlsHi != 0 && loc.Rva >= owner.CwtlsLo && loc.Rva < owner.CwtlsHi;
            if (threaded) return 0; // .cwtls needs a func-eval; can't read inline at hit time (v1)

            byte code = loc.TypeCode;
            switch (code)
            {
                case 0x11: case 0x12: case 0x13: case 0x25:
                    return ReadScalarNumeric(va, code, loc.Size, out num) ? 1 : 0;
                case 0x23: case 0x24: // DECIMAL / PDECIMAL — formatted then parsed
                {
                    string disp = FormatValueAt(code, 0, loc.Size, 0, va);
                    double d;
                    if (double.TryParse(disp, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) { num = d; return 1; }
                    str = disp; return 2;
                }
                default: // STRING / GROUP / reference / unknown — compare as text
                    str = StripQuotes(FormatValueAt(code, 0, loc.Size, 0, va));
                    return 2;
            }
        }

        /// <summary>Decode a scalar numeric type (LONG/ULONG/SHORT/BYTE/SREAL/REAL) to a double.</summary>
        private bool ReadScalarNumeric(uint va, byte code, uint size, out double val)
        {
            val = 0;
            int need = (code == 0x13 || code == 0x25) ? (size == 8 ? 8 : 4)
                                                      : (size == 1 ? 1 : size == 2 ? 2 : 4);
            var b = new byte[need];
            if (ReadBlock(va, b) < need) return false;
            switch (code)
            {
                case 0x11: val = need == 1 ? (sbyte)b[0] : need == 2 ? BitConverter.ToInt16(b, 0) : BitConverter.ToInt32(b, 0); return true;
                case 0x12: val = need == 1 ? b[0]        : need == 2 ? BitConverter.ToUInt16(b, 0) : BitConverter.ToUInt32(b, 0); return true;
                case 0x13: case 0x25: val = need == 8 ? BitConverter.ToDouble(b, 0) : BitConverter.ToSingle(b, 0); return true;
                default: return false;
            }
        }

        /// <summary>Strip one wrapping pair of single or double quotes (the display formatter wraps
        /// strings in single quotes; a quoted RHS literal uses either).</summary>
        private static string StripQuotes(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 2) return s;
            char a = s[0], z = s[s.Length - 1];
            if ((a == '\'' && z == '\'') || (a == '"' && z == '"')) return s.Substring(1, s.Length - 2);
            return s;
        }
    }
}
