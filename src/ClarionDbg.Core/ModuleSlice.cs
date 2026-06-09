using System.Collections.Generic;

namespace ClarionDbg.Core
{
    /// <summary>
    /// One module's line sub-table within the TSWD Table-A region. The +0x10 map gives the
    /// blob-relative byte slice [SliceStart, SliceEnd); Phase is the 0-5 byte lead-in before
    /// the 6-byte { line, absoluteRVA } record grid begins.
    /// </summary>
    public sealed class ModuleSlice
    {
        public int Index;
        public string Name;
        public uint SliceStart;   // blob-relative
        public uint SliceEnd;     // blob-relative
        public int Phase;
        public List<LineRec> Records = new List<LineRec>();

        /// <summary>True if the slice points into the line-table region and was parsed.</summary>
        public bool InRegion;

        public bool HasCode { get { return (SliceStart != 0 || SliceEnd != 0) && SliceEnd > SliceStart; } }

        public override string ToString()
        {
            return string.Format("[{0,2}] {1,-16} slice 0x{2:X}..0x{3:X} phase {4} recs {5}",
                Index, Name, SliceStart, SliceEnd, Phase, Records.Count);
        }
    }
}
