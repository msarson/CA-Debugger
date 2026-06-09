using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClarionDbg.Core
{
    /// <summary>A single PE section header (the fields we care about).</summary>
    public sealed class PeSection
    {
        public string Name;
        public uint VirtualAddress;
        public uint VirtualSize;
        public uint PointerToRawData;
        public uint SizeOfRawData;

        public uint Span { get { return Math.Max(VirtualSize, SizeOfRawData); } }
        public bool ContainsRva(uint rva) { return rva >= VirtualAddress && rva < VirtualAddress + Span; }
    }

    /// <summary>
    /// Minimal PE32 reader: enough to find the image base, the section table
    /// (for RVA-&gt;file-offset mapping and the .text range), and the Debug Directory.
    /// </summary>
    public sealed class PeImage
    {
        public byte[] Bytes { get; private set; }
        public ushort Machine { get; private set; }
        public uint ImageBase { get; private set; }
        public uint EntryPointRva { get; private set; }
        public uint DebugDirRva { get; private set; }
        public uint DebugDirSize { get; private set; }
        public List<PeSection> Sections { get; private set; }

        public bool IsX86 { get { return Machine == 0x014C; } }

        public static PeImage Load(string path)
        {
            return new PeImage(File.ReadAllBytes(path));
        }

        public PeImage(byte[] bytes)
        {
            Bytes = bytes;
            Sections = new List<PeSection>();

            int peOff = BitConverter.ToInt32(bytes, 0x3C);
            if (BitConverter.ToUInt32(bytes, peOff) != 0x00004550) // "PE\0\0"
                throw new InvalidDataException("Not a PE image (missing PE signature).");

            Machine = BitConverter.ToUInt16(bytes, peOff + 4);
            ushort numSec = BitConverter.ToUInt16(bytes, peOff + 6);
            ushort optSize = BitConverter.ToUInt16(bytes, peOff + 20);
            int optOff = peOff + 24;
            ushort magic = BitConverter.ToUInt16(bytes, optOff);
            if (magic != 0x10B)
                throw new InvalidDataException("Only PE32 (32-bit) images are supported; Clarion targets are 32-bit.");

            // PE32 optional header: AddressOfEntryPoint @+16, ImageBase @+28.
            EntryPointRva = BitConverter.ToUInt32(bytes, optOff + 16);
            ImageBase = BitConverter.ToUInt32(bytes, optOff + 28);

            // Data directories begin at +96 in PE32; Debug Directory is index 6 (8 bytes each).
            int debugDir = optOff + 96 + 6 * 8;
            DebugDirRva = BitConverter.ToUInt32(bytes, debugDir);
            DebugDirSize = BitConverter.ToUInt32(bytes, debugDir + 4);

            int secOff = optOff + optSize;
            for (int i = 0; i < numSec; i++)
            {
                int o = secOff + i * 40;
                Sections.Add(new PeSection
                {
                    Name = Encoding.ASCII.GetString(bytes, o, 8).TrimEnd('\0'),
                    VirtualSize = BitConverter.ToUInt32(bytes, o + 8),
                    VirtualAddress = BitConverter.ToUInt32(bytes, o + 12),
                    SizeOfRawData = BitConverter.ToUInt32(bytes, o + 16),
                    PointerToRawData = BitConverter.ToUInt32(bytes, o + 20),
                });
            }
        }

        public PeSection Text
        {
            get { return Sections.Find(s => s.Name == ".text"); }
        }

        /// <summary>Map a virtual address (RVA) to a file offset, or -1 if not in any section.</summary>
        public long RvaToOffset(uint rva)
        {
            foreach (var s in Sections)
                if (s.ContainsRva(rva))
                    return s.PointerToRawData + (rva - s.VirtualAddress);
            return -1;
        }

        public bool RvaInText(uint rva)
        {
            var t = Text;
            return t != null && rva >= t.VirtualAddress && rva < t.VirtualAddress + t.VirtualSize;
        }

        /// <summary>Holds the first IMAGE_DEBUG_DIRECTORY entry.</summary>
        public struct DebugEntry { public uint Type; public uint SizeOfData; public uint PointerToRawData; }

        public DebugEntry ReadFirstDebugEntry()
        {
            long off = RvaToOffset(DebugDirRva);
            if (off < 0) throw new InvalidDataException("Debug directory RVA not mapped to a section.");
            int o = (int)off;
            return new DebugEntry
            {
                Type = BitConverter.ToUInt32(Bytes, o + 12),
                SizeOfData = BitConverter.ToUInt32(Bytes, o + 16),
                PointerToRawData = BitConverter.ToUInt32(Bytes, o + 24),
            };
        }
    }
}
