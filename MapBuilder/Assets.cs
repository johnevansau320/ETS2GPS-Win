using System.IO.Compression;
using System.Text;

namespace Ets2Gps.MapBuilder;

/// <summary>
/// Company logos and country flags, taken from the player's own game files and packed as small PNGs (etsassets.bin) for the
/// map. They are generated on this PC when the map is built and are never part of the source or a release.
/// </summary>
public static class AssetPack
{
    public const int Version = 1;

    /// <summary>Country id used by the game (def/country) -> ISO 3166 alpha-2, for the flag files.</summary>
    public static readonly Dictionary<string, string> Iso3To2 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alb"] = "al", ["aut"] = "at", ["bel"] = "be", ["bih"] = "ba", ["bgr"] = "bg", ["hrv"] = "hr", ["cze"] = "cz", ["dnk"] = "dk",
        ["est"] = "ee", ["fin"] = "fi", ["fra"] = "fr", ["deu"] = "de", ["grc"] = "gr", ["hun"] = "hu", ["ita"] = "it", ["xkx"] = "xk",
        ["kos"] = "xk", ["lva"] = "lv", ["ltu"] = "lt", ["lux"] = "lu", ["mkd"] = "mk", ["mne"] = "me", ["nld"] = "nl", ["nor"] = "no",
        ["pol"] = "pl", ["prt"] = "pt", ["rou"] = "ro", ["srb"] = "rs", ["svk"] = "sk", ["svn"] = "si", ["esp"] = "es", ["swe"] = "se",
        ["che"] = "ch", ["gbr"] = "gb", ["ala"] = "ax", ["and"] = "ad", ["arm"] = "am", ["aze"] = "az", ["cyp"] = "cy", ["fro"] = "fo",
        ["geo"] = "ge", ["gib"] = "gi", ["grl"] = "gl", ["ggy"] = "gg", ["irl"] = "ie", ["imn"] = "im", ["jey"] = "je", ["mlt"] = "mt",
        ["mco"] = "mc", ["mda"] = "md", ["sjm"] = "sj", ["ukr"] = "ua", ["rus"] = "ru", ["blr"] = "by", ["kaz"] = "kz", ["tur"] = "tr",
        ["isr"] = "il", ["egy"] = "eg", ["jor"] = "jo", ["lbn"] = "lb", ["syr"] = "sy", ["irq"] = "iq", ["sau"] = "sa", ["dza"] = "dz",
        ["lby"] = "ly", ["mar"] = "ma", ["mrt"] = "mr", ["tun"] = "tn", ["tkm"] = "tm", ["uzb"] = "uz", ["isl"] = "is", ["lie"] = "li",
        ["pse"] = "ps", ["kwt"] = "kw", ["bhr"] = "bh", ["qat"] = "qa", ["are"] = "ae", ["omn"] = "om", ["yem"] = "ye", ["irn"] = "ir",
    };

    // ------------------------------------------------------------------------------------ DDS --

    static int C565(int v, int shift, int bits) => (v >> shift) & ((1 << bits) - 1);

    static void Bc1Colors(byte[] d, int o, byte[] pal /* 16 = 4 x rgba */, out bool fourColor)
    {
        int c0 = d[o] | d[o + 1] << 8, c1 = d[o + 2] | d[o + 3] << 8;
        void Set(int i, int v, int a)
        {
            int r = C565(v, 11, 5), g = C565(v, 5, 6), b = C565(v, 0, 5);
            pal[i * 4] = (byte)(r << 3 | r >> 2); pal[i * 4 + 1] = (byte)(g << 2 | g >> 4); pal[i * 4 + 2] = (byte)(b << 3 | b >> 2); pal[i * 4 + 3] = (byte)a;
        }
        Set(0, c0, 255); Set(1, c1, 255);
        fourColor = c0 > c1;
        for (int k = 0; k < 3; k++)
        {
            if (fourColor) { pal[8 + k] = (byte)((2 * pal[k] + pal[4 + k]) / 3); pal[12 + k] = (byte)((pal[k] + 2 * pal[4 + k]) / 3); }
            else { pal[8 + k] = (byte)((pal[k] + pal[4 + k]) / 2); pal[12 + k] = 0; }
        }
        pal[11] = 255; pal[15] = (byte)(fourColor ? 255 : 0);
    }

    /// <summary>Decodes the first image of a DDS file to RGBA, or returns null for formats we do not know.</summary>
    public static (int W, int H, byte[] Rgba)? DecodeDds(byte[] d)
    {
        if (d.Length < 128 || d[0] != 'D' || d[1] != 'D' || d[2] != 'S') return null;
        int h = Bin.I32(d, 12), w = Bin.I32(d, 16);
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return null;
        uint pfFlags = Bin.U32(d, 80);
        string fourcc = Encoding.ASCII.GetString(d, 84, 4);
        int bits = Bin.I32(d, 88);
        uint rm = Bin.U32(d, 92), gm = Bin.U32(d, 96), bm = Bin.U32(d, 100), am = Bin.U32(d, 104);
        int data = 128, dxgi = 0;
        if (fourcc == "DX10") { dxgi = Bin.I32(d, 128); data = 148; }
        var rgba = new byte[w * h * 4];
        string kind = fourcc switch { "DXT1" => "bc1", "DXT3" => "bc2", "DXT5" => "bc3", _ => "" };
        if (dxgi is 71 or 72) kind = "bc1"; else if (dxgi is 74 or 75) kind = "bc2"; else if (dxgi is 77 or 78) kind = "bc3";
        if (kind != "")
        {
            int block = kind == "bc1" ? 8 : 16, bx = (w + 3) / 4, by = (h + 3) / 4;
            if (data + bx * by * block > d.Length) return null;
            var pal = new byte[16];
            for (int y = 0; y < by; y++)
                for (int x = 0; x < bx; x++)
                {
                    int o = data + (y * bx + x) * block;
                    var alpha = new byte[16];
                    for (int i = 0; i < 16; i++) alpha[i] = 255;
                    if (kind == "bc2") for (int i = 0; i < 16; i++) { int nib = (d[o + i / 2] >> (4 * (i & 1))) & 15; alpha[i] = (byte)(nib * 17); }
                    if (kind == "bc3")
                    {
                        int a0 = d[o], a1 = d[o + 1];
                        var ap = new int[8]; ap[0] = a0; ap[1] = a1;
                        if (a0 > a1) for (int i = 1; i < 7; i++) ap[1 + i] = ((7 - i) * a0 + i * a1) / 7;
                        else { for (int i = 1; i < 5; i++) ap[1 + i] = ((5 - i) * a0 + i * a1) / 5; ap[6] = 0; ap[7] = 255; }
                        ulong bitsA = 0; for (int i = 0; i < 6; i++) bitsA |= (ulong)d[o + 2 + i] << (8 * i);
                        for (int i = 0; i < 16; i++) alpha[i] = (byte)ap[(int)((bitsA >> (3 * i)) & 7)];
                    }
                    int co = kind == "bc1" ? o : o + 8;
                    Bc1Colors(d, co, pal, out bool four);
                    uint idx = (uint)(d[co + 4] | d[co + 5] << 8 | d[co + 6] << 16 | d[co + 7] << 24);
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int X = x * 4 + px, Y = y * 4 + py;
                            if (X >= w || Y >= h) continue;
                            int ci = (int)((idx >> (2 * (py * 4 + px))) & 3), di = (Y * w + X) * 4;
                            rgba[di] = pal[ci * 4]; rgba[di + 1] = pal[ci * 4 + 1]; rgba[di + 2] = pal[ci * 4 + 2];
                            rgba[di + 3] = kind == "bc1" ? pal[ci * 4 + 3] : alpha[py * 4 + px];
                        }
                }
            return (w, h, rgba);
        }
        // uncompressed
        int bpp = dxgi != 0 ? 32 : bits;
        if (bpp != 32 && bpp != 24) return null;
        int bytes = bpp / 8;
        if (data + w * h * bytes > d.Length) return null;
        bool bgra = true, hasAlpha = (pfFlags & 1) != 0 || am != 0;
        if (dxgi is 28 or 29) bgra = false; else if (dxgi is 87 or 88 or 91 or 93) bgra = true; else if (dxgi != 0) return null;
        else if (rm == 0xFF && bm == 0xFF0000) bgra = false;
        if (dxgi != 0) hasAlpha = dxgi is 28 or 29 or 87 or 91;
        for (int i = 0; i < w * h; i++)
        {
            int s = data + i * bytes;
            byte b0 = d[s], b1 = d[s + 1], b2 = d[s + 2], b3 = bytes == 4 ? d[s + 3] : (byte)255;
            rgba[i * 4] = bgra ? b2 : b0; rgba[i * 4 + 1] = b1; rgba[i * 4 + 2] = bgra ? b0 : b2; rgba[i * 4 + 3] = hasAlpha ? b3 : (byte)255;
        }
        return (w, h, rgba);
    }

    // ------------------------------------------------------------------------------------ PNG --

    static readonly uint[] CrcTable = MakeCrc();
    static uint[] MakeCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
        return t;
    }
    static uint Crc(byte[] b, int o, int n) { uint c = 0xFFFFFFFFu; for (int i = 0; i < n; i++) c = CrcTable[(c ^ b[o + i]) & 255] ^ (c >> 8); return c ^ 0xFFFFFFFFu; }

    public static byte[] Png(int w, int h, byte[] rgba)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.SmallestSize, true))
            for (int y = 0; y < h; y++) { z.WriteByte(0); z.Write(rgba, y * w * 4, w * 4); }
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        void Chunk(string type, byte[] body)
        {
            var len = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, (uint)body.Length); ms.Write(len);
            var tb = new byte[4 + body.Length]; Encoding.ASCII.GetBytes(type).CopyTo(tb, 0); body.CopyTo(tb, 4);
            ms.Write(tb);
            var crc = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(tb, 0, tb.Length)); ms.Write(crc);
        }
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)w);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)h);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk("IHDR", ihdr); Chunk("IDAT", raw.ToArray()); Chunk("IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    // ------------------------------------------------------------------------------------ pack --

    /// <summary>etsassets.bin: "ETSA", u16 version, u16 0, u32 count, then per image: u8 kind (0 logo, 1 flag), str key, u32 length, PNG bytes.</summary>
    public static int Build(FileSystem fs, string outPath, Action<string> log)
    {
        var items = new List<(byte Kind, string Key, byte[] Png)>();
        void Add(byte kind, string dir, Func<string, string?> key)
        {
            foreach (var f in fs.ListDir(dir).Files.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!f.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) continue;
                string? k = key(f[..^4]);
                if (k == null) continue;
                var data = fs.Read(dir + "/" + f);
                if (data == null) continue;
                try
                {
                    var img = DecodeDds(data);
                    if (img == null) continue;
                    items.RemoveAll(i => i.Kind == kind && i.Key == k);
                    items.Add((kind, k, Png(img.Value.W, img.Value.H, img.Value.Rgba)));
                }
                catch (Exception e) { log($"  ! image {dir}/{f}: {e.Message}"); }
            }
        }
        Add(0, "material/ui/company/small", n => n.EndsWith("_in", StringComparison.OrdinalIgnoreCase) ? null : n.ToLowerInvariant());
        foreach (var dir in new[] { "material/ui/hud/flags", "material/ui/hud/me_flags", "material/ui/hud/maghreb_flags" })
            Add(1, dir, n => n.StartsWith("flag_", StringComparison.OrdinalIgnoreCase) ? n[5..].ToLowerInvariant() : null);
        if (!items.Any(i => i.Kind == 1 && i.Key == "gb") && items.FirstOrDefault(i => i.Kind == 1 && i.Key == "uk") is { Png: not null } uk) items.Add((1, "gb", uk.Png));
        string tmp = outPath + ".part";
        using (var f = File.Create(tmp))
        using (var w = new BinaryWriter(f))
        {
            w.Write(Encoding.ASCII.GetBytes("ETSA")); w.Write((ushort)Version); w.Write((ushort)0); w.Write((uint)items.Count);
            foreach (var (kind, key, png) in items)
            {
                w.Write(kind); var kb = Encoding.UTF8.GetBytes(key); w.Write((byte)kb.Length); w.Write(kb); w.Write((uint)png.Length); w.Write(png);
            }
        }
        File.Move(tmp, outPath, true);
        log($"assets: {items.Count(i => i.Kind == 0)} company logos, {items.Count(i => i.Kind == 1)} flags, {new FileInfo(outPath).Length / 1024} KB");
        return items.Count;
    }
}
