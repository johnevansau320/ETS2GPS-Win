namespace Ets2Gps;

/// <summary>
/// How routes are chosen. Factors scale travel time by road grade (minor, wide local, expressway, motorway):
/// below 1 attracts the route, above 1 repels it. JunctionExtra adds seconds per junction path.
/// </summary>
public sealed record RouteMode(string Key, string Label, string Short, float[] Factors, float JunctionExtra, string Help)
{
    public static readonly RouteMode Heavy = new("HEAVY", "Heavy transport", "Heavy", new[] { 2.0f, 1.3f, 1.0f, 0.8f }, 4f,
        "Heavy transport (default): motorways and major roads, avoids small roads and awkward junctions");
    public static readonly RouteMode Fastest = new("FASTEST", "Fastest", "Fast", new[] { 1f, 1f, 1f, 1f }, 0f,
        "Fastest: shortest travel time on any road");
    public static readonly RouteMode Tourist = new("TOURIST", "Tourist", "Tour", new[] { 0.8f, 0.8f, 1.6f, 2.8f }, 0f,
        "Tourist: prefers smaller, scenic roads and avoids motorways");
    public static readonly RouteMode[] All = { Heavy, Fastest, Tourist };
    public static RouteMode ByKey(string? k) => All.FirstOrDefault(m => m.Key == k) ?? Heavy;
}

public sealed record Snap(int De, float Dist, float Along, int Seg);
public sealed record RouteResult(int[] Edges, float LengthM, float TimeS);

/// <summary>Directed-edge A* over the network, snapping and the main-network filter (port of Router.kt).</summary>
public sealed class Router
{
    public const float JunctionPenaltyS = 3f, HiddenFactor = 14f;

    public readonly MapData Map;
    readonly int[] outStart, outList;
    public readonly bool[] InMain;
    readonly float[] edgeW;
    readonly bool[] blocked;
    float minFactor = 1f, junctionExtra;
    readonly int[] gStamp, closed, prev;
    readonly float[] gVal;
    int gen;
    RouteMode mode = RouteMode.Heavy;

    /// <summary>Never use ferries or car trains (the map's ferry / train links).</summary>
    public bool AvoidFerries { get; set; }

    public RouteMode Mode { get => mode; set { mode = value; RebuildCosts(); } }

    public Router(MapData map)
    {
        Map = map;
        int n = map.NodeCount, ec = map.EdgeCount;
        outStart = new int[n + 1];
        var cnt = new int[n + 1];
        for (int e = 0; e < ec; e++) { cnt[map.EFrom[e] + 1]++; if (map.IsTwoWay(e)) cnt[map.ETo[e] + 1]++; }
        for (int i = 1; i <= n; i++) cnt[i] += cnt[i - 1];
        Array.Copy(cnt, outStart, n + 1);
        outList = new int[cnt[n]];
        var fill = new int[n];
        for (int e = 0; e < ec; e++)
        {
            int a = map.EFrom[e];
            outList[outStart[a] + fill[a]++] = e * 2;
            if (map.IsTwoWay(e)) { int b = map.ETo[e]; outList[outStart[b] + fill[b]++] = e * 2 + 1; }
        }
        InMain = new bool[n];
        edgeW = new float[ec]; blocked = new bool[ec];
        gStamp = new int[ec * 2]; closed = new int[ec * 2]; prev = new int[ec * 2]; gVal = new float[ec * 2];
        ComputeMainComponent();
        RebuildCosts();
    }

    void RebuildCosts()
    {
        var f = mode.Factors; var m = Map;
        float mn = 1f;
        for (int e = 0; e < m.EdgeCount; e++)
        {
            float w = (m.EJid[e] != 0 || m.IsFerry(e)) ? 1f : f[Math.Min(3, (int)m.EGrade[e])];
            if (m.IsHidden(e)) w *= HiddenFactor; // invisible roads: only when there is no sensible alternative
            edgeW[e] = w;
            if (w < mn) mn = w;
        }
        minFactor = mn;
        junctionExtra = mode.JunctionExtra;
    }

    /// <summary>Marks every road within ~25 m of each point as forbidden for routing.</summary>
    public void SetAvoid(IEnumerable<(float X, float Z)> points)
    {
        var m = Map;
        Array.Clear(blocked);
        foreach (var (x, z) in points)
            m.QueryEdges(x, z, 30f, e =>
            {
                if (m.EJid[e] != 0 || m.IsFerry(e)) return;
                for (int p = m.EPtStart[e]; p < m.EPtStart[e + 1] - 1; p++)
                {
                    float x0 = m.PtX[p], z0 = m.PtZ[p], dx = m.PtX[p + 1] - x0, dz = m.PtZ[p + 1] - z0;
                    float l2 = dx * dx + dz * dz;
                    float t = l2 > 1e-6f ? Math.Clamp(((x - x0) * dx + (z - z0) * dz) / l2, 0f, 1f) : 0f;
                    if (M.Hypot(x - (x0 + dx * t), z - (z0 + dz * t)) < 25f) { blocked[e] = true; break; }
                }
            });
    }

    public float EdgeSeconds(int de)
    {
        int e = de >> 1;
        float v = Math.Max((int)Map.ESpeed[e], 10) / 3.6f;
        return Map.ELen[e] / v + (Map.EJid[e] != 0 ? JunctionPenaltyS : 0f);
    }

    public float EdgeCost(int de)
    {
        int e = de >> 1;
        return EdgeSeconds(de) * edgeW[e] + (Map.EJid[e] != 0 ? junctionExtra : 0f);
    }

    public int OutCount(int node) => outStart[node + 1] - outStart[node];
    public int OutAt(int node, int i) => outList[outStart[node] + i];

    /// <summary>Largest strongly connected component (iterative Kosaraju).</summary>
    void ComputeMainComponent()
    {
        var m = Map; int n = m.NodeCount, ec = m.EdgeCount;
        var rStart = new int[n + 1];
        for (int e = 0; e < ec; e++) { rStart[m.ETo[e] + 1]++; if (m.IsTwoWay(e)) rStart[m.EFrom[e] + 1]++; }
        for (int i = 1; i <= n; i++) rStart[i] += rStart[i - 1];
        var rList = new int[rStart[n]];
        var rFill = new int[n];
        for (int e = 0; e < ec; e++)
        {
            int a = m.EFrom[e], b = m.ETo[e];
            rList[rStart[b] + rFill[b]++] = a;
            if (m.IsTwoWay(e)) rList[rStart[a] + rFill[a]++] = b;
        }
        var seen = new bool[n]; var order = new int[n]; int oc = 0;
        var stackNode = new int[n]; var stackIdx = new int[n];
        for (int s = 0; s < n; s++)
        {
            if (seen[s] || OutCount(s) == 0) continue;
            int sp = 1; stackNode[0] = s; stackIdx[0] = 0; seen[s] = true;
            while (sp > 0)
            {
                int u = stackNode[sp - 1], i = stackIdx[sp - 1];
                if (i < OutCount(u))
                {
                    stackIdx[sp - 1] = i + 1;
                    int v = m.DeTo(OutAt(u, i));
                    if (!seen[v]) { seen[v] = true; stackNode[sp] = v; stackIdx[sp] = 0; sp++; }
                }
                else { order[oc++] = u; sp--; }
            }
        }
        var comp = new int[n]; Array.Fill(comp, -1);
        int best = 0, bestId = -1, cid = 0;
        var stack = new int[n];
        for (int k = oc - 1; k >= 0; k--)
        {
            int s = order[k];
            if (comp[s] != -1) continue;
            comp[s] = cid;
            int sp = 0; stack[sp++] = s; int cntc = 0;
            while (sp > 0)
            {
                int u = stack[--sp]; cntc++;
                for (int j = rStart[u]; j < rStart[u + 1]; j++)
                {
                    int v = rList[j];
                    if (comp[v] == -1) { comp[v] = cid; stack[sp++] = v; }
                }
            }
            if (cntc > best) { best = cntc; bestId = cid; }
            cid++;
        }
        for (int i = 0; i < n; i++) InMain[i] = comp[i] == bestId;
    }

    /// <summary>Closest drivable position to (x, z); with a heading (radians) the direction of travel is matched.</summary>
    public Snap? SnapTo(float x, float z, float? headingRad, float radius = 60f)
    {
        var m = Map;
        Snap? best = null; float bestScore = float.MaxValue;
        m.QueryEdges(x, z, radius, e =>
        {
            if (m.IsFerry(e)) return;   // a truck is never on a ferry link: at a port it would otherwise snap onto the crossing and route back over the sea
            int a = m.EPtStart[e], b = m.EPtStart[e + 1];
            float cum = 0f;
            for (int p = a; p < b - 1; p++)
            {
                float x0 = m.PtX[p], z0 = m.PtZ[p], x1 = m.PtX[p + 1], z1 = m.PtZ[p + 1];
                float dx = x1 - x0, dz = z1 - z0, l2 = dx * dx + dz * dz, segLen = MathF.Sqrt(l2);
                float t = l2 > 1e-6f ? ((x - x0) * dx + (z - z0) * dz) / l2 : 0f;
                t = Math.Clamp(t, 0f, 1f);
                float d = M.Hypot(x - (x0 + dx * t), z - (z0 + dz * t));
                if (d <= radius)
                {
                    float bearing = MathF.Atan2(dx, -dz);
                    for (int dir = 0; dir < 2; dir++)
                    {
                        if (dir == 1 && !m.IsTwoWay(e)) continue;
                        float penalty = 0f;
                        if (headingRad is float h)
                        {
                            float br = dir == 0 ? bearing : bearing + MathF.PI;
                            float diff = MathF.Abs(AngleDiff(br, h));
                            penalty = diff > 1.9f ? 1000f : diff * 12f;
                        }
                        float score = d + penalty;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            float alongFwd = cum + segLen * t, total = m.ELen[e];
                            best = dir == 0 ? new Snap(e * 2, d, alongFwd, p - a)
                                : new Snap(e * 2 + 1, d, MathF.Max(0f, total - alongFwd), Math.Max(0, b - p - 2));
                        }
                    }
                }
                cum += segLen;
            }
        });
        return best;
    }

    /// <summary>Nearest node in the main network, searching outwards (prefers visible, non-avoided roads).</summary>
    public int NearestMainNode(float x, float z)
    {
        var m = Map;
        float r = 300f;
        while (r <= 20000f)
        {
            int best = -1; float bestD = float.MaxValue;
            m.QueryEdges(x, z, r, e =>
            {
                foreach (int n in new[] { m.EFrom[e], m.ETo[e] })
                {
                    if (!InMain[n]) continue;
                    float pen = blocked[e] ? 600f : m.IsHidden(e) ? 150f : 0f;
                    float d = M.Hypot(m.NodeX[n] - x, m.NodeZ[n] - z) + pen;
                    if (d < bestD) { bestD = d; best = n; }
                }
            });
            if (best >= 0 && bestD <= r) return best;
            r *= 3f;
        }
        return -1;
    }

    /// <summary>Cheapest route that finishes directed edge startDe (remainingM left of it) and ends at destNode.</summary>
    public RouteResult? Route(int startDe, float remainingM, int destNode)
    {
        var m = Map;
        int g = ++gen;
        float dx = m.NodeX[destNode], dz = m.NodeZ[destNode];
        float vmax = 110f / 3.6f / minFactor;
        var heap = new Heap();
        int startE = startDe >> 1;
        float startCost = EdgeCost(startDe) * Math.Clamp(remainingM / m.ELen[startE], 0f, 1f);
        float H(int node) { float ax = m.NodeX[node] - dx, az = m.NodeZ[node] - dz; return MathF.Sqrt(ax * ax + az * az) / vmax; }
        gStamp[startDe] = g; gVal[startDe] = startCost; prev[startDe] = -1;
        heap.Push(startCost + H(m.DeTo(startDe)), startDe);
        int goal = -1, pops = 0;
        while (heap.Size > 0)
        {
            int cur = heap.Pop();
            if (closed[cur] == g) continue;
            closed[cur] = g;
            if (++pops > 6_000_000) break;
            int node = m.DeTo(cur);
            if (node == destNode) { goal = cur; break; }
            float gc = gVal[cur]; int curE = cur >> 1;
            for (int i = outStart[node]; i < outStart[node + 1]; i++)
            {
                int nx = outList[i];
                if ((nx >> 1) == curE) continue; // no U-turn on the same road
                if (blocked[nx >> 1]) continue;
                if (AvoidFerries && m.IsFerry(nx >> 1)) continue;
                if (closed[nx] == g) continue;
                float ng = gc + EdgeCost(nx);
                if (gStamp[nx] != g || ng < gVal[nx])
                {
                    gStamp[nx] = g; gVal[nx] = ng; prev[nx] = cur;
                    heap.Push(ng + H(m.DeTo(nx)), nx);
                }
            }
        }
        if (goal < 0) return null;
        var list = new List<int>();
        for (int c = goal; c != -1; c = prev[c]) list.Add(c);
        list.Reverse();
        float len = remainingM;
        for (int k = 1; k < list.Count; k++) len += m.ELen[list[k] >> 1];
        return new RouteResult(list.ToArray(), len, gVal[goal]);
    }

    /// <summary>Smallest signed difference a - b in radians, in (-pi, pi].</summary>
    public static float AngleDiff(float a, float b)
    {
        float d = (a - b) % (2 * MathF.PI);
        if (d > MathF.PI) d -= 2 * MathF.PI;
        if (d <= -MathF.PI) d += 2 * MathF.PI;
        return d;
    }

    public static float BearingOf(float dx, float dz) => MathF.Atan2(dx, -dz);

    sealed class Heap
    {
        float[] pri = new float[1024];
        int[] vals = new int[1024];
        public int Size;

        public void Push(float p, int v)
        {
            if (Size == pri.Length) { Array.Resize(ref pri, Size * 2); Array.Resize(ref vals, Size * 2); }
            int i = Size++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (pri[parent] <= p) break;
                pri[i] = pri[parent]; vals[i] = vals[parent]; i = parent;
            }
            pri[i] = p; vals[i] = v;
        }

        public int Pop()
        {
            int top = vals[0];
            Size--;
            if (Size > 0)
            {
                float p = pri[Size]; int v = vals[Size];
                int i = 0;
                for (;;)
                {
                    int c = i * 2 + 1;
                    if (c >= Size) break;
                    if (c + 1 < Size && pri[c + 1] < pri[c]) c++;
                    if (pri[c] >= p) break;
                    pri[i] = pri[c]; vals[i] = vals[c]; i = c;
                }
                pri[i] = p; vals[i] = v;
            }
            return top;
        }
    }
}
