using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SweaxMorph.Core.Converters;

/// <summary>
/// Desktop fonts ⇄ web fonts, written from the specs: TTF/OTF → WOFF (zlib) and WOFF2 (Brotli), and back,
/// including WOFF2's transformed glyph tables. No outside tools.
/// </summary>
public sealed class FontConverter : Converter
{
    public override string Name => "SweaxMorph";

    public override IEnumerable<Edge> Edges()
    {
        foreach (var e in Cross(["ttf", "otf"], ["woff", "woff2"], 1)) yield return e;
        foreach (var e in Cross(["woff", "woff2"], ["ttf", "otf", "woff", "woff2"], 1)) yield return e;
    }

    public override Task<string[]> Run(Step s) => Task.Run(() =>
    {
        byte[] data = File.ReadAllBytes(s.Input);
        var font = s.From.Id switch
        {
            "woff" => Woff.Read(data),
            "woff2" => Woff2.Read(data),
            _ => Sfnt.Read(data),
        };
        s.Progress.Report(0.5);
        byte[] result = s.To.Id switch
        {
            "woff" => Woff.Write(font),
            "woff2" => Woff2.Write(font),
            _ => Sfnt.Write(font),
        };
        string outFile = s.Out();
        File.WriteAllBytes(outFile, result);
        s.Progress.Report(1);
        return new[] { outFile };
    }, s.Cancel);
}

/// <summary>A font as its tables: what every format here wraps differently.</summary>
public sealed class FontData
{
    public uint Flavor;
    public SortedDictionary<string, byte[]> Tables = new(StringComparer.Ordinal);
}

static class Be
{
    public static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o));
    public static short I16(byte[] b, int o) => BinaryPrimitives.ReadInt16BigEndian(b.AsSpan(o));
    public static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));
    public static string Tag(byte[] b, int o) => Encoding.ASCII.GetString(b, o, 4);
    public static void W16(Stream s, int v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
    public static void W32(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
    public static void Tag(Stream s, string t) => s.Write(Encoding.ASCII.GetBytes(t.PadRight(4)[..4]));
    public static int Pad4(int n) => (n + 3) & ~3;
    public static void Pad(Stream s) { while (s.Position % 4 != 0) s.WriteByte(0); }

    public static uint Checksum(byte[] t)
    {
        uint sum = 0;
        int n = Pad4(t.Length);
        for (int i = 0; i < n; i += 4)
        {
            uint v = 0;
            for (int k = 0; k < 4; k++) v = (v << 8) | (i + k < t.Length ? t[i + k] : 0u);
            sum = unchecked(sum + v);
        }
        return sum;
    }
}

/// <summary>Plain TrueType / OpenType files.</summary>
public static class Sfnt
{
    public static FontData Read(byte[] b)
    {
        uint flavor = Be.U32(b, 0);
        if (flavor == 0x74746366) throw new ConvertException("Font collections (.ttc) aren't supported yet.");
        var f = new FontData { Flavor = flavor };
        int n = Be.U16(b, 4);
        for (int i = 0; i < n; i++)
        {
            int e = 12 + i * 16;
            string tag = Be.Tag(b, e);
            int off = (int)Be.U32(b, e + 8), len = (int)Be.U32(b, e + 12);
            f.Tables[tag] = b.AsSpan(off, len).ToArray();
        }
        return f;
    }

    public static byte[] Write(FontData f)
    {
        var tables = f.Tables.Where(t => t.Key != "DSIG").ToList();
        int n = tables.Count;
        int pow = 1, log = 0;
        while (pow * 2 <= n) { pow *= 2; log++; }
        var ms = new MemoryStream();
        Be.W32(ms, f.Flavor);
        Be.W16(ms, n);
        Be.W16(ms, pow * 16);
        Be.W16(ms, log);
        Be.W16(ms, n * 16 - pow * 16);
        int offset = 12 + n * 16;
        int headOffset = -1;
        foreach (var (tag, data) in tables)
        {
            if (tag == "head") { headOffset = offset; ZeroAdjustment(data); }
            Be.Tag(ms, tag);
            Be.W32(ms, Be.Checksum(data));
            Be.W32(ms, (uint)offset);
            Be.W32(ms, (uint)data.Length);
            offset += Be.Pad4(data.Length);
        }
        foreach (var (_, data) in tables) { ms.Write(data); Be.Pad(ms); }
        byte[] font = ms.ToArray();
        if (headOffset >= 0)
        {
            uint adjust = unchecked(0xB1B0AFBA - Be.Checksum(font));
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(headOffset + 8), adjust);
        }
        return font;
    }

    static void ZeroAdjustment(byte[] head) { if (head.Length >= 12) head.AsSpan(8, 4).Clear(); }
}

/// <summary>WOFF 1.0: each table zlib-compressed on its own.</summary>
public static class Woff
{
    public static FontData Read(byte[] b)
    {
        if (Be.Tag(b, 0) != "wOFF") throw new ConvertException("Not a WOFF font.");
        var f = new FontData { Flavor = Be.U32(b, 4) };
        int n = Be.U16(b, 12);
        for (int i = 0; i < n; i++)
        {
            int e = 44 + i * 20;
            string tag = Be.Tag(b, e);
            int off = (int)Be.U32(b, e + 4), comp = (int)Be.U32(b, e + 8), orig = (int)Be.U32(b, e + 12);
            if (comp < orig)
            {
                using var z = new ZLibStream(new MemoryStream(b, off, comp), CompressionMode.Decompress);
                var outBuf = new byte[orig];
                z.ReadExactly(outBuf);
                f.Tables[tag] = outBuf;
            }
            else f.Tables[tag] = b.AsSpan(off, orig).ToArray();
        }
        return f;
    }

    public static byte[] Write(FontData f)
    {
        var tables = f.Tables.Where(t => t.Key != "DSIG").ToList();
        var packed = tables.Select(t =>
        {
            var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) z.Write(t.Value);
            var c = ms.ToArray();
            return c.Length < t.Value.Length ? c : t.Value;
        }).ToList();
        int n = tables.Count;
        int sfntSize = 12 + 16 * n + tables.Sum(t => Be.Pad4(t.Value.Length));
        var outMs = new MemoryStream();
        int offset = 44 + 20 * n;
        var offsets = new List<int>();
        foreach (var p in packed) { offsets.Add(offset); offset += Be.Pad4(p.Length); }
        Be.Tag(outMs, "wOFF");
        Be.W32(outMs, f.Flavor);
        Be.W32(outMs, (uint)offset);
        Be.W16(outMs, n);
        Be.W16(outMs, 0);
        Be.W32(outMs, (uint)sfntSize);
        Be.W16(outMs, 1); Be.W16(outMs, 0);
        for (int i = 0; i < 5; i++) Be.W32(outMs, 0); // no metadata, no private block
        for (int i = 0; i < n; i++)
        {
            Be.Tag(outMs, tables[i].Key);
            Be.W32(outMs, (uint)offsets[i]);
            Be.W32(outMs, (uint)packed[i].Length);
            Be.W32(outMs, (uint)tables[i].Value.Length);
            Be.W32(outMs, Be.Checksum(tables[i].Value));
        }
        foreach (var p in packed) { outMs.Write(p); Be.Pad(outMs); }
        return outMs.ToArray();
    }
}

/// <summary>WOFF 2.0: all tables in one Brotli stream, glyphs optionally in a compact transformed form.</summary>
public static class Woff2
{
    static readonly string[] Known =
    [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG", "EBDT",
        "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH",
        "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar",
        "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
    ];

    static uint Base128(byte[] b, ref int p)
    {
        uint v = 0;
        for (int i = 0; i < 5; i++)
        {
            byte x = b[p++];
            v = (v << 7) | (uint)(x & 0x7F);
            if ((x & 0x80) == 0) return v;
        }
        throw new ConvertException("Broken WOFF2 font.");
    }

    static void Base128(Stream s, uint v)
    {
        Span<byte> tmp = stackalloc byte[5];
        int n = 0;
        do { tmp[n++] = (byte)(v & 0x7F); v >>= 7; } while (v != 0);
        for (int i = n - 1; i >= 0; i--) s.WriteByte((byte)(tmp[i] | (i > 0 ? 0x80 : 0)));
    }

    sealed record Entry(string Tag, int Version, uint OrigLength, uint TransformLength, bool Transformed);

    public static FontData Read(byte[] b)
    {
        if (Be.Tag(b, 0) != "wOF2") throw new ConvertException("Not a WOFF2 font.");
        uint flavor = Be.U32(b, 4);
        if (flavor == 0x74746366) throw new ConvertException("Font collections aren't supported yet.");
        int numTables = Be.U16(b, 12);
        uint compressedSize = Be.U32(b, 20);
        int p = 48;
        var entries = new List<Entry>();
        for (int i = 0; i < numTables; i++)
        {
            byte flags = b[p++];
            int idx = flags & 0x3F, version = flags >> 6;
            string tag = idx == 63 ? Be.Tag(b, p) : Known[idx];
            if (idx == 63) p += 4;
            uint orig = Base128(b, ref p);
            bool transformed = tag is "glyf" or "loca" ? version == 0 : version != 0;
            uint tlen = transformed ? Base128(b, ref p) : orig;
            entries.Add(new Entry(tag, version, orig, tlen, transformed));
        }
        byte[] stream;
        using (var br = new BrotliStream(new MemoryStream(b, p, (int)compressedSize), CompressionMode.Decompress))
        using (var ms = new MemoryStream())
        {
            br.CopyTo(ms);
            stream = ms.ToArray();
        }
        var f = new FontData { Flavor = flavor };
        var raw = new Dictionary<string, byte[]>();
        int off = 0;
        foreach (var e in entries)
        {
            raw[e.Tag] = stream.AsSpan(off, (int)e.TransformLength).ToArray();
            off += (int)e.TransformLength;
        }
        foreach (var e in entries)
        {
            if (e.Tag == "loca" && e.Transformed) continue; // rebuilt with glyf
            if (e.Tag == "glyf" && e.Transformed)
            {
                var (glyf, loca) = Glyf.Rebuild(raw["glyf"], out var xMins, out int numGlyphs);
                f.Tables["glyf"] = glyf;
                f.Tables["loca"] = loca;
                _xMins = xMins; _numGlyphs = numGlyphs;
                continue;
            }
            if (e.Tag == "hmtx" && e.Transformed) continue; // after glyf, below
            f.Tables[e.Tag] = raw[e.Tag];
        }
        var hmtx = entries.FirstOrDefault(e => e.Tag == "hmtx");
        if (hmtx is { Transformed: true }) f.Tables["hmtx"] = Hmtx(raw["hmtx"], f, _xMins ?? [], _numGlyphs);
        // loca's offset format must match head.indexToLocFormat
        if (f.Tables.TryGetValue("head", out var head) && f.Tables.TryGetValue("loca", out var locaTable) && _numGlyphs > 0)
            BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), (short)(locaTable.Length == (_numGlyphs + 1) * 2 ? 0 : 1));
        _xMins = null;
        return f;
    }

    [ThreadStatic] static short[]? _xMins;
    [ThreadStatic] static int _numGlyphs;

    static byte[] Hmtx(byte[] t, FontData f, short[] xMins, int numGlyphs)
    {
        int flags = t[0];
        int numH = Be.U16(f.Tables["hhea"], 34);
        int p = 1;
        var adv = new ushort[numH];
        for (int i = 0; i < numH; i++, p += 2) adv[i] = Be.U16(t, p);
        var lsb = new short[numGlyphs];
        for (int i = 0; i < numH; i++)
            if ((flags & 1) != 0) lsb[i] = i < xMins.Length ? xMins[i] : (short)0;
            else { lsb[i] = Be.I16(t, p); p += 2; }
        for (int i = numH; i < numGlyphs; i++)
            if ((flags & 2) != 0) lsb[i] = i < xMins.Length ? xMins[i] : (short)0;
            else { lsb[i] = Be.I16(t, p); p += 2; }
        var ms = new MemoryStream();
        for (int i = 0; i < numGlyphs; i++)
        {
            if (i < numH) Be.W16(ms, adv[i]);
            Be.W16(ms, lsb[i]);
        }
        return ms.ToArray();
    }

    /// <summary>Writes WOFF2 with the "null" transform: tables are stored as they are, Brotli does the work.</summary>
    public static byte[] Write(FontData f)
    {
        var tables = f.Tables.Where(t => t.Key != "DSIG").Select(t => (t.Key, t.Value)).ToList();
        // loca must follow glyf directly
        int g = tables.FindIndex(t => t.Key == "glyf"), l = tables.FindIndex(t => t.Key == "loca");
        if (g >= 0 && l >= 0)
        {
            var loca = tables[l];
            tables.RemoveAt(l);
            tables.Insert(tables.FindIndex(t => t.Key == "glyf") + 1, loca);
        }
        var dir = new MemoryStream();
        foreach (var (tag, data) in tables)
        {
            int idx = Array.IndexOf(Known, tag);
            int version = tag is "glyf" or "loca" ? 3 : 0;
            dir.WriteByte((byte)((idx >= 0 ? idx : 63) | (version << 6)));
            if (idx < 0) Be.Tag(dir, tag);
            Base128(dir, (uint)data.Length);
        }
        var body = new MemoryStream();
        foreach (var (_, data) in tables) body.Write(data);
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var br = new BrotliEncoderStream(ms)) br.Write(body.ToArray());
            compressed = ms.ToArray();
        }
        int sfntSize = 12 + 16 * tables.Count + tables.Sum(t => Be.Pad4(t.Value.Length));
        int total = Be.Pad4(48 + (int)dir.Length + compressed.Length);
        var outMs = new MemoryStream();
        Be.Tag(outMs, "wOF2");
        Be.W32(outMs, f.Flavor);
        Be.W32(outMs, (uint)total);
        Be.W16(outMs, tables.Count);
        Be.W16(outMs, 0);
        Be.W32(outMs, (uint)sfntSize);
        Be.W32(outMs, (uint)compressed.Length);
        Be.W16(outMs, 1); Be.W16(outMs, 0);
        for (int i = 0; i < 5; i++) Be.W32(outMs, 0);
        outMs.Write(dir.ToArray());
        outMs.Write(compressed);
        Be.Pad(outMs);
        return outMs.ToArray();
    }

    /// <summary>Brotli at its best quality and a window large enough for fonts.</summary>
    sealed class BrotliEncoderStream(Stream inner) : Stream
    {
        readonly MemoryStream _buf = new();
        public override void Write(byte[] buffer, int offset, int count) => _buf.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var src = _buf.ToArray();
                var dst = new byte[BrotliEncoder.GetMaxCompressedLength(src.Length)];
                if (!BrotliEncoder.TryCompress(src, dst, out int written, 11, 22)) throw new IOException("brotli");
                inner.Write(dst, 0, written);
            }
            base.Dispose(disposing);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _buf.Length;
        public override long Position { get => _buf.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>Rebuilds plain 'glyf' and 'loca' tables from WOFF2's transformed glyph streams (spec §5.1).</summary>
    static class Glyf
    {
        public static (byte[] glyf, byte[] loca) Rebuild(byte[] t, out short[] xMins, out int numGlyphs)
        {
            int optionFlags = Be.U16(t, 2);
            numGlyphs = Be.U16(t, 4);
            int indexFormat = Be.U16(t, 6);
            var sizes = new int[7];
            for (int i = 0; i < 7; i++) sizes[i] = (int)Be.U32(t, 8 + i * 4);
            int pos = 36;
            var starts = new int[7];
            for (int i = 0; i < 7; i++) { starts[i] = pos; pos += sizes[i]; }
            int nContourP = starts[0], nPointsP = starts[1], flagP = starts[2], glyphP = starts[3], compositeP = starts[4], bboxP = starts[5], instrP = starts[6];
            int bitmapLen = ((numGlyphs + 31) >> 5) << 2;
            int bboxBitmap = bboxP;
            bboxP += bitmapLen;
            int overlapBitmap = (optionFlags & 1) != 0 ? pos : -1;

            var glyf = new MemoryStream();
            var offsets = new int[numGlyphs + 1];
            xMins = new short[numGlyphs];
            var xs = new List<int>(); var ys = new List<int>(); var on = new List<bool>(); var ends = new List<int>();

            for (int gi = 0; gi < numGlyphs; gi++)
            {
                offsets[gi] = (int)glyf.Length;
                short nContours = Be.I16(t, nContourP); nContourP += 2;
                bool hasBbox = (t[bboxBitmap + (gi >> 3)] & (0x80 >> (gi & 7))) != 0;
                if (nContours == 0) continue;
                var g = new MemoryStream();
                if (nContours == -1)
                {
                    short[] bb = ReadBbox(t, ref bboxP);
                    Be.W16(g, -1); foreach (var v in bb) Be.W16(g, v);
                    xMins[gi] = bb[0];
                    bool instructions = false;
                    while (true)
                    {
                        int flags = Be.U16(t, compositeP);
                        int size = 4 + ((flags & 0x0001) != 0 ? 4 : 2) + ((flags & 0x0008) != 0 ? 2 : (flags & 0x0040) != 0 ? 4 : (flags & 0x0080) != 0 ? 8 : 0);
                        g.Write(t, compositeP, size);
                        compositeP += size;
                        if ((flags & 0x0100) != 0) instructions = true;
                        if ((flags & 0x0020) == 0) break;
                    }
                    if (instructions)
                    {
                        int n = Read255(t, ref glyphP);
                        Be.W16(g, n); g.Write(t, instrP, n); instrP += n;
                    }
                }
                else
                {
                    ends.Clear(); xs.Clear(); ys.Clear(); on.Clear();
                    int total = 0;
                    for (int c = 0; c < nContours; c++) { total += Read255(t, ref nPointsP); ends.Add(total - 1); }
                    int x = 0, y = 0;
                    for (int i = 0; i < total; i++)
                    {
                        int flag = t[flagP + i];
                        on.Add((flag & 0x80) == 0);
                        flag &= 0x7F;
                        int dx, dy;
                        int b0 = glyphP < t.Length ? t[glyphP] : 0;
                        if (flag < 10) { dx = 0; dy = Sign(flag, ((flag & 14) << 7) + b0); glyphP += 1; }
                        else if (flag < 20) { dx = Sign(flag, (((flag - 10) & 14) << 7) + b0); dy = 0; glyphP += 1; }
                        else if (flag < 84)
                        {
                            int f0 = flag - 20;
                            dx = Sign(flag, 1 + (f0 & 0x30) + (b0 >> 4));
                            dy = Sign(flag >> 1, 1 + ((f0 & 0x0C) << 2) + (b0 & 0x0F));
                            glyphP += 1;
                        }
                        else if (flag < 120)
                        {
                            int f0 = flag - 84;
                            dx = Sign(flag, 1 + ((f0 / 12) << 8) + b0);
                            dy = Sign(flag >> 1, 1 + (((f0 % 12) >> 2) << 8) + t[glyphP + 1]);
                            glyphP += 2;
                        }
                        else if (flag < 124)
                        {
                            int b2 = t[glyphP + 1];
                            dx = Sign(flag, (b0 << 4) + (b2 >> 4));
                            dy = Sign(flag >> 1, ((b2 & 0x0F) << 8) + t[glyphP + 2]);
                            glyphP += 3;
                        }
                        else
                        {
                            dx = Sign(flag, (b0 << 8) + t[glyphP + 1]);
                            dy = Sign(flag >> 1, (t[glyphP + 2] << 8) + t[glyphP + 3]);
                            glyphP += 4;
                        }
                        x += dx; y += dy;
                        xs.Add(x); ys.Add(y);
                    }
                    flagP += total;
                    int instrLen = Read255(t, ref glyphP);
                    short[] bb = hasBbox ? ReadBbox(t, ref bboxP)
                        : total > 0 ? [(short)xs.Min(), (short)ys.Min(), (short)xs.Max(), (short)ys.Max()] : [0, 0, 0, 0];
                    xMins[gi] = bb[0];
                    Be.W16(g, nContours); foreach (var v in bb) Be.W16(g, v);
                    foreach (var e in ends) Be.W16(g, e);
                    Be.W16(g, instrLen); g.Write(t, instrP, instrLen); instrP += instrLen;
                    bool overlap = overlapBitmap >= 0 && (t[overlapBitmap + (gi >> 3)] & (0x80 >> (gi & 7))) != 0;
                    for (int i = 0; i < total; i++) g.WriteByte((byte)((on[i] ? 1 : 0) | (i == 0 && overlap ? 0x40 : 0)));
                    int px = 0;
                    foreach (var v in xs) { Be.W16(g, v - px); px = v; }
                    int py = 0;
                    foreach (var v in ys) { Be.W16(g, v - py); py = v; }
                }
                glyf.Write(g.ToArray());
                Be.Pad(glyf);
            }
            offsets[numGlyphs] = (int)glyf.Length;
            var loca = new MemoryStream();
            bool longOffsets = indexFormat != 0 || offsets[numGlyphs] > 0x1FFFE;
            foreach (var o in offsets) { if (longOffsets) Be.W32(loca, (uint)o); else Be.W16(loca, o / 2); }
            return (glyf.ToArray(), loca.ToArray());
        }

        static int Sign(int flag, int v) => (flag & 1) != 0 ? v : -v;

        static short[] ReadBbox(byte[] t, ref int p)
        {
            var bb = new short[4];
            for (int i = 0; i < 4; i++) { bb[i] = Be.I16(t, p); p += 2; }
            return bb;
        }

        /// <summary>255UInt16: one byte for small numbers, more for big ones.</summary>
        static int Read255(byte[] t, ref int p)
        {
            int code = t[p++];
            if (code == 253) { int v = Be.U16(t, p); p += 2; return v; }
            if (code == 255) return t[p++] + 253;
            if (code == 254) return t[p++] + 253 * 2;
            return code;
        }
    }
}
