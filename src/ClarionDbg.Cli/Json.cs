namespace ClarionDbg.Cli
{
    /// <summary>Tiny hand-rolled JSON for the engine's machine-readable event output.</summary>
    internal static class Json
    {
        public static string Str(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        public static string Hit(string module, int line, uint rva, uint va, uint gap, bool resolved)
        {
            return "{\"event\":\"hit\""
                 + ",\"resolved\":" + (resolved ? "true" : "false")
                 + ",\"module\":" + Str(module)
                 + ",\"line\":" + line
                 + ",\"rva\":\"0x" + rva.ToString("X") + "\""
                 + ",\"va\":\"0x" + va.ToString("X") + "\""
                 + ",\"gap\":" + gap
                 + ",\"exact\":" + ((gap == 0 && resolved) ? "true" : "false")
                 + "}";
        }

        /// <summary>The set of breakable (record-carrying) source lines for a module — for gutter markers.</summary>
        public static string Lines(string module, System.Collections.Generic.List<int> lines)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"event\":\"lines\",\"module\":").Append(Str(module)).Append(",\"lines\":[");
            for (int i = 0; i < lines.Count; i++) { if (i > 0) sb.Append(','); sb.Append(lines[i]); }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
