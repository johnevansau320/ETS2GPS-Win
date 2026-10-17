using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Ets2Gps;

public sealed record Poi(int Type, float X, float Z)
{
    public const int Fuel = 1, Service = 2, Parking = 3, Weigh = 4, Dealer = 5, Garage = 6, Recruit = 7, Hotel = 8;

    public static string Label(int t) => t switch
    {
        Fuel => "Fuel station", Service => "Service centre", Parking => "Rest area / parking", Weigh => "Weigh station",
        Dealer => "Truck dealer", Garage => "Garage", Recruit => "Recruitment agency", _ => "Hotel",
    };
}

public sealed record Company(string Id, string CityId, float X, float Z);

public sealed class City
{
    public string Id { get; }
    public string Name { get; }
    public float X { get; }
    public float Z { get; }
    public string Key { get; }

    public City(string id, string name, float x, float z) { Id = id; Name = name; X = x; Z = z; Key = Normalize(name); }

    /// <summary>Lower-case, accent-free name for searching.</summary>
    public static string Normalize(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().ToLowerInvariant();
    }
}

/// <summary>
/// The road network produced by companion/build_map.py (etsmap.bin): edges are polylines between nodes,
/// world units are metres in the game's coordinate system (x east, z south).
/// </summary>
public sealed class MapData
{
    public int Version;
    public float MinX, MinZ, MaxX, MaxZ;
    public int NodeCount, EdgeCount;
    public float[] NodeX = Array.Empty<float>(), NodeZ = Array.Empty<float>();
    public int[] EFrom = Array.Empty<int>(), ETo = Array.Empty<int>();
    public byte[] EFlags = Array.Empty<byte>(), ECls = Array.Empty<byte>(), EGrade = Array.Empty<byte>();
    public ushort[] ESpeed = Array.Empty<ushort>();
    public float[] EWidth = Array.Empty<float>();
    public uint[] EJid = Array.Empty<uint>();
    public int[] EPtStart = Array.Empty<int>();
    public float[] PtX = Array.Empty<float>(), PtZ = Array.Empty<float>();
    public float[] ELen = Array.Empty<float>();
    public HashSet<uint> Roundabouts = new();
    public List<City> Cities = new();
    public List<Company> Companies = new();
    public List<Poi> Pois = new();
    public byte[] LaneN = Array.Empty<byte>();
    public ushort[] LaneMask = Array.Empty<ushort>();

    // spatial grid (CSR layout)
    const float Cell = 400f;
    int cols, rows;
    int[] cellStart = Array.Empty<int>(), cellEdges = Array.Empty<int>(), stamp = Array.Empty<int>();
    int stampGen;

    public static MapData Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var z = new ZLibStream(fs, CompressionMode.Decompress);
        using var ms = new MemoryStream(40_000_000);
        z.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    static float F32(byte[] b, int o) => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(o));
    static short I16(byte[] b, int o) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(o));
    static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));

    static MapData Parse(byte[] d)
    {
        if (d[0] != 'E' || d[1] != 'T' || d[2] != 'S' || d[3] != 'M') throw new InvalidDataException("not an ETS2 GPS map file");
        var m = new MapData { Version = U16(d, 4) };
        if (m.Version < 2 || m.Version > 8) throw new InvalidDataException($"unsupported map version {m.Version} (rebuild the map or update the app)");
        m.MinX = F32(d, 8); m.MinZ = F32(d, 12); m.MaxX = F32(d, 16); m.MaxZ = F32(d, 20);
        int o = 24;
        int nn = (int)U32(d, o); o += 4;
        m.NodeCount = nn; m.NodeX = new float[nn]; m.NodeZ = new float[nn];
        for (int i = 0; i < nn; i++) { m.NodeX[i] = F32(d, o); m.NodeZ[i] = F32(d, o + 4); o += 8; }
        int jn = (int)U32(d, o); o += 4;
        for (int i = 0; i < jn; i++) { if (d[o + 5] != 0) m.Roundabouts.Add(U32(d, o)); o += 6; }
        int en = (int)U32(d, o); o += 4;
        int total = 0;
        for (int i = 0, p = o; i < en; i++) { int np = d[p + 16]; total += np; p += 17 + 4 * np; }
        m.EdgeCount = en;
        m.EFrom = new int[en]; m.ETo = new int[en]; m.EFlags = new byte[en]; m.ECls = new byte[en]; m.EGrade = new byte[en];
        m.ESpeed = new ushort[en]; m.EWidth = new float[en]; m.EJid = new uint[en]; m.EPtStart = new int[en + 1];
        m.PtX = new float[total]; m.PtZ = new float[total]; m.ELen = new float[en];
        int pp = 0;
        for (int i = 0; i < en; i++)
        {
            m.EFrom[i] = (int)U32(d, o); m.ETo[i] = (int)U32(d, o + 4);
            byte fl = d[o + 8]; m.EFlags[i] = fl; m.ECls[i] = d[o + 9];
            m.EGrade[i] = m.Version >= 6 ? (byte)((fl >> 1) & 3) : (byte)(m.ECls[i] == 2 ? 3 : m.ECls[i] == 1 ? 2 : 0);
            m.ESpeed[i] = (ushort)(d[o + 10] * 5); m.EWidth[i] = d[o + 11] / 4f;
            m.EJid[i] = U32(d, o + 12);
            int npts = d[o + 16];
            o += 17;
            m.EPtStart[i] = pp;
            int qx = (int)MathF.Floor(m.NodeX[m.EFrom[i]] * 4f + 0.5f), qz = (int)MathF.Floor(m.NodeZ[m.EFrom[i]] * 4f + 0.5f);
            float len = 0;
            for (int k = 0; k < npts; k++)
            {
                qx += I16(d, o); qz += I16(d, o + 2); o += 4;
                m.PtX[pp] = qx / 4f; m.PtZ[pp] = qz / 4f;
                if (k > 0) { float dx = m.PtX[pp] - m.PtX[pp - 1], dz = m.PtZ[pp] - m.PtZ[pp - 1]; len += MathF.Sqrt(dx * dx + dz * dz); }
                pp++;
            }
            m.ELen[i] = MathF.Max(len, 0.5f);
        }
        m.EPtStart[en] = pp;
        string Str() { int n = d[o]; var s = Encoding.UTF8.GetString(d, o + 1, n); o += 1 + n; return s; }
        int cn = (int)U32(d, o); o += 4;
        for (int i = 0; i < cn; i++) { var id = Str(); var name = Str(); float x = F32(d, o), z = F32(d, o + 4); o += 8; m.Cities.Add(new City(id, name, x, z)); }
        m.Cities.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        if (m.Version >= 3 && o + 4 <= d.Length)
        {
            int kn = (int)U32(d, o); o += 4;
            for (int i = 0; i < kn; i++) { var id = Str(); var cityId = Str(); m.Companies.Add(new Company(id, cityId, F32(d, o), F32(d, o + 4))); o += 8; }
        }
        if (m.Version >= 5 && o + 4 <= d.Length)
        {
            int pn = (int)U32(d, o); o += 4;
            for (int i = 0; i < pn; i++) { m.Pois.Add(new Poi(d[o], F32(d, o + 1), F32(d, o + 5))); o += 9; }
        }
        if (m.Version >= 8 && o + 4 <= d.Length)
        {
            m.LaneN = new byte[en]; m.LaneMask = new ushort[en];
            int ln = (int)U32(d, o); o += 4;
            for (int i = 0; i < ln; i++)
            {
                uint e = U32(d, o);
                if (e < en) { m.LaneN[e] = d[o + 4]; m.LaneMask[e] = U16(d, o + 5); }
                o += 7;
            }
        }
        m.BuildGrid();
        return m;
    }

    void BuildGrid()
    {
        cols = (int)((MaxX - MinX) / Cell) + 2;
        rows = (int)((MaxZ - MinZ) / Cell) + 2;
        var counts = new int[cols * rows + 1];
        void Each(Action<int, int> f)
        {
            for (int e = 0; e < EdgeCount; e++)
            {
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                for (int p = EPtStart[e]; p < EPtStart[e + 1]; p++)
                {
                    float x = PtX[p], z = PtZ[p];
                    if (x < x0) x0 = x; if (x > x1) x1 = x; if (z < z0) z0 = z; if (z > z1) z1 = z;
                }
                if (x0 > x1) continue;
                int cx0 = CellX(x0), cx1 = CellX(x1), cz0 = CellZ(z0), cz1 = CellZ(z1);
                for (int cz = cz0; cz <= cz1; cz++) for (int cx = cx0; cx <= cx1; cx++) f(e, cz * cols + cx);
            }
        }
        Each((_, c) => counts[c + 1]++);
        for (int i = 1; i < counts.Length; i++) counts[i] += counts[i - 1];
        cellStart = counts;
        cellEdges = new int[counts[^1]];
        var fill = new int[cols * rows];
        Each((e, c) => { cellEdges[cellStart[c] + fill[c]] = e; fill[c]++; });
        stamp = new int[EdgeCount];
    }

    int CellX(float x) => Math.Clamp((int)((x - MinX) / Cell), 0, cols - 1);
    int CellZ(float z) => Math.Clamp((int)((z - MinZ) / Cell), 0, rows - 1);

    /// <summary>Calls f once for every edge whose grid cells intersect the square [x±r, z±r].</summary>
    public void QueryEdges(float x, float z, float r, Action<int> f)
    {
        int gen = ++stampGen;
        int cx0 = CellX(x - r), cx1 = CellX(x + r), cz0 = CellZ(z - r), cz1 = CellZ(z + r);
        for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                int c = cz * cols + cx;
                for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
                {
                    int e = cellEdges[k];
                    if (stamp[e] != gen) { stamp[e] = gen; f(e); }
                }
            }
    }

    public bool IsTwoWay(int e) => (EFlags[e] & 1) != 0;
    public bool IsFerry(int e) => (EFlags[e] & 8) != 0;
    public bool IsHidden(int e) => (EFlags[e] & 16) != 0;
    public int LanesAtEntry(int e) => e < LaneN.Length ? LaneN[e] : 0;
    public int LaneMaskOf(int e) => e < LaneMask.Length ? LaneMask[e] : 0;

    public int DeFrom(int de) => (de & 1) == 0 ? EFrom[de >> 1] : ETo[de >> 1];
    public int DeTo(int de) => (de & 1) == 0 ? ETo[de >> 1] : EFrom[de >> 1];
    public int DeCount(int de) { int e = de >> 1; return EPtStart[e + 1] - EPtStart[e]; }
    public float DeX(int de, int i) { int e = de >> 1; return (de & 1) == 0 ? PtX[EPtStart[e] + i] : PtX[EPtStart[e + 1] - 1 - i]; }
    public float DeZ(int de, int i) { int e = de >> 1; return (de & 1) == 0 ? PtZ[EPtStart[e] + i] : PtZ[EPtStart[e + 1] - 1 - i]; }

    public City? FindCity(string id) => Cities.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    public Company? FindCompany(string cityId, string companyId) =>
        Companies.FirstOrDefault(c => string.Equals(c.Id, companyId, StringComparison.OrdinalIgnoreCase) && string.Equals(c.CityId, cityId, StringComparison.OrdinalIgnoreCase));
}
