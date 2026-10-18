using System.Windows.Media.Imaging;

namespace Ets2Gps;

/// <summary>Company logos and country flags from the game, packed by the map builder (etsassets.bin) and decoded when first needed.</summary>
public sealed class MapAssets
{
    readonly Dictionary<string, byte[]> logos = new(StringComparer.OrdinalIgnoreCase), flags = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, BitmapSource?> cache = new(StringComparer.OrdinalIgnoreCase);

    public int LogoCount => logos.Count;
    public int FlagCount => flags.Count;

    public static MapAssets? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var d = File.ReadAllBytes(path);
            if (d.Length < 12 || d[0] != 'E' || d[1] != 'T' || d[2] != 'S' || d[3] != 'A') return null;
            var a = new MapAssets();
            int n = (int)BitConverter.ToUInt32(d, 8), o = 12;
            for (int i = 0; i < n; i++)
            {
                int kind = d[o], kl = d[o + 1];
                string key = System.Text.Encoding.UTF8.GetString(d, o + 2, kl); o += 2 + kl;
                int len = (int)BitConverter.ToUInt32(d, o); o += 4;
                var png = new byte[len]; Array.Copy(d, o, png, 0, len); o += len;
                (kind == 0 ? a.logos : a.flags)[key] = png;
            }
            return a;
        }
        catch { return null; }
    }

    BitmapSource? Decode(string cacheKey, byte[] png)
    {
        if (cache.TryGetValue(cacheKey, out var hit)) return hit;
        BitmapSource? bmp = null;
        try
        {
            var f = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            f.Freeze(); bmp = f;
        }
        catch { /* bad image: ignore */ }
        cache[cacheKey] = bmp;
        return bmp;
    }

    public BitmapSource? Logo(string id) => logos.TryGetValue(id, out var png) ? Decode("l:" + id, png) : null;

    public BitmapSource? Flag(string cc)
    {
        if (flags.TryGetValue(cc, out var png)) return Decode("f:" + cc, png);
        string alt = cc.Equals("gb", StringComparison.OrdinalIgnoreCase) ? "uk" : cc.Equals("uk", StringComparison.OrdinalIgnoreCase) ? "gb" : "";
        return alt.Length > 0 && flags.TryGetValue(alt, out var p2) ? Decode("f:" + alt, p2) : null;
    }
}
