using System.Globalization;

namespace Ets2Gps;

public enum Turn { KeepLeft, KeepRight, BearLeft, BearRight, TurnLeft, TurnRight, UTurn, Roundabout, Arrive }

/// <summary>One lane of the approach: direction it takes (degrees, + = right; null = unknown), whether the route uses it, an optional second direction.</summary>
public sealed record Lane(double? Angle, bool Preferred, double? Alt);

public sealed class Maneuver
{
    public Turn Turn;
    public float AtM;          // distance along the route
    public int ExitNo;         // roundabout exit number, else 0
    public float AngleDeg;     // signed turn angle, + = right
    public float X, Z;         // position of the junction entry
    public string? Towards;    // next town the route heads for after this maneuver
    public List<Lane>? Lanes;
}

public sealed class LaneHint { public float AtM; public List<Lane> Lanes = new(); }

public sealed class NavState
{
    public float DistToNextM;
    public Maneuver? Next, Following;
    public float RemainingM, RemainingS;
    public bool OffRoute, Arrived;
    public string? Speech;
    public List<Lane>? Lanes;
    public float LanesDistM;
}

/// <summary>Follows a route: maneuvers, lane guidance, progress and spoken prompts (port of Navigator.kt).</summary>
public sealed class Navigator
{
    public const float OffRouteM = 45f;
    public float DistScale = 19f;
    public string ArriveWord = "your destination";

    readonly MapData map;
    readonly Router router;
    int[] route = Array.Empty<int>();
    float[] cum = Array.Empty<float>(), secs = Array.Empty<float>();
    public List<Maneuver> Maneuvers { get; private set; } = new();
    List<LaneHint> laneHints = new();
    int[] stage = Array.Empty<int>();
    int idx, offCount;
    float progress;

    public Navigator(MapData map, Router router) { this.map = map; this.router = router; }

    public bool Active => route.Length > 0;
    public int CurrentIndex => idx;
    public float RouteLengthM => cum.Length > 0 ? cum[^1] : 0f;

    /// <summary>Speed the router assumes for the road the truck is on now (m/s), 0 for junction paths.</summary>
    public float ModelSpeedMps
    {
        get
        {
            if (route.Length == 0) return 0f;
            int e = route[Math.Clamp(idx, 0, route.Length - 1)] >> 1;
            return map.EJid[e] != 0 ? 0f : map.ESpeed[e] / 3.6f;
        }
    }

    public void Clear()
    {
        route = Array.Empty<int>(); cum = Array.Empty<float>(); secs = Array.Empty<float>();
        Maneuvers = new(); laneHints = new(); stage = Array.Empty<int>();
        idx = 0; progress = 0f; offCount = 0;
    }

    /// <summary>remainingM = how much of the first edge was still ahead when the route was computed.</summary>
    public void SetRoute(RouteResult res, float remainingM)
    {
        route = res.Edges;
        int n = route.Length;
        cum = new float[n + 1]; secs = new float[n + 1];
        for (int i = 0; i < n; i++)
        {
            cum[i + 1] = cum[i] + map.ELen[route[i] >> 1];
            secs[i + 1] = secs[i] + router.EdgeSeconds(route[i]);
        }
        idx = 0;
        progress = map.ELen[route[0] >> 1] - remainingM;
        offCount = 0;
        Maneuvers = BuildManeuvers();
        AssignTowards();
        stage = Maneuvers.Select(mv => mv.AtM < progress ? 3 : 0).ToArray(); // nothing behind the start gets announced
    }

    float Tangent(int de, bool atEnd, float win = 6f)
    {
        int n = map.DeCount(de);
        if (n < 2) return 0f;
        if (atEnd)
        {
            float px = map.DeX(de, n - 1), pz = map.DeZ(de, n - 1);
            for (int i = n - 2; i >= 0; i--)
            {
                float qx = map.DeX(de, i), qz = map.DeZ(de, i);
                if (M.Hypot(px - qx, pz - qz) >= win || i == 0) return Router.BearingOf(px - qx, pz - qz);
            }
        }
        else
        {
            float px = map.DeX(de, 0), pz = map.DeZ(de, 0);
            for (int i = 1; i < n; i++)
            {
                float qx = map.DeX(de, i), qz = map.DeZ(de, i);
                if (M.Hypot(qx - px, qz - pz) >= win || i == n - 1) return Router.BearingOf(qx - px, qz - pz);
            }
        }
        return 0f;
    }

    static float DegDiff(float a, float b) => Router.AngleDiff(a, b) * 180f / MathF.PI;

    List<Maneuver> BuildManeuvers()
    {
        var outList = new List<Maneuver>();
        var hints = new List<LaneHint>();
        int n = route.Length;
        for (int k = 1; k < n - 1; k++)
        {
            int de = route[k], e = de >> 1;
            uint jid = map.EJid[e];
            if (jid == 0) continue;
            int entry = map.DeFrom(de), cameFrom = map.DeFrom(route[k - 1]);
            var alts = new List<(int De, float Angle)>();
            for (int i = 0; i < router.OutCount(entry); i++)
            {
                int j = router.OutAt(entry, i);
                if (map.EJid[j >> 1] != jid || map.DeTo(j) == cameFrom) continue;
                alts.Add((j, DegDiff(Tangent(j, true), Tangent(j, false))));
            }
            if (alts.Count < 2) continue;
            int ci = alts.FindIndex(a => a.De == de);
            if (ci < 0) continue;
            float angle = alts[ci].Angle;
            var straightest = alts[0];
            foreach (var a in alts) if (MathF.Abs(a.Angle) < MathF.Abs(straightest.Angle)) straightest = a;
            float x = map.DeX(de, 0), z = map.DeZ(de, 0);
            Maneuver Mk(Turn t, int exitNo) => new() { Turn = t, AtM = cum[k], ExitNo = exitNo, AngleDeg = angle, X = x, Z = z };
            if (map.Roundabouts.Contains(jid))
            {
                var sorted = alts.OrderByDescending(a => a.Angle).ToList();
                int no = sorted.FindIndex(a => a.De == de) + 1;
                outList.Add(Mk(Turn.Roundabout, no));
                continue;
            }
            float ab = MathF.Abs(angle); bool right = angle > 0;
            int before = outList.Count;
            var laneList = LanesFor(alts, de);
            if (ab < 22f) { if (straightest.De != de) outList.Add(Mk(right ? Turn.KeepRight : Turn.KeepLeft, 0)); }
            else if (ab < 55f) outList.Add(Mk(right ? Turn.BearRight : Turn.BearLeft, 0));
            else if (ab < 135f) outList.Add(Mk(right ? Turn.TurnRight : Turn.TurnLeft, 0));
            else outList.Add(Mk(Turn.UTurn, 0));
            if (outList.Count > before) outList[^1].Lanes = laneList;
            // also hint junctions where the route just carries straight on but some lanes would leave it
            if (laneList != null && (outList.Count > before || laneList.Any(l => !l.Preferred)))
                hints.Add(new LaneHint { AtM = cum[k], Lanes = laneList });
        }
        int last = route[n - 1], cnt = map.DeCount(last);
        outList.Add(new Maneuver { Turn = Turn.Arrive, AtM = cum[n], X = map.DeX(last, cnt - 1), Z = map.DeZ(last, cnt - 1) });
        laneHints = hints;
        return outList;
    }

    /// <summary>Lane arrows for the approach to junction path de, from the map's lane table (null when unknown).</summary>
    List<Lane>? LanesFor(List<(int De, float Angle)> alts, int de)
    {
        int n = map.LanesAtEntry(de >> 1), chosen = map.LaneMaskOf(de >> 1);
        if (n < 2 || chosen == 0) return null;
        float mine = alts.First(a => a.De == de).Angle;
        var res = new List<Lane>();
        for (int i = 0; i < n; i++)
        {
            var dirs = alts.Where(a => ((map.LaneMaskOf(a.De >> 1) >> i) & 1) != 0).ToList();
            if (dirs.Count == 0) { res.Add(new Lane(null, false, null)); continue; }
            bool pref = ((chosen >> i) & 1) != 0;
            float main = mine;
            if (!pref) { var b = dirs[0]; foreach (var d in dirs) if (MathF.Abs(d.Angle) < MathF.Abs(b.Angle)) b = d; main = b.Angle; }
            float? other = null;
            foreach (var d in dirs) if (MathF.Abs(d.Angle - main) > 20f && (other == null || MathF.Abs(d.Angle) < MathF.Abs(other.Value))) other = d.Angle;
            res.Add(new Lane(main, pref, other));
        }
        return res;
    }

    /// <summary>For each maneuver, the first town the route passes after it (within 2 km of the road).</summary>
    void AssignTowards()
    {
        int nc = map.Cities.Count;
        var passAt = new float[nc]; var bestD = new float[nc];
        Array.Fill(passAt, float.MaxValue); Array.Fill(bestD, float.MaxValue);
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (int de in route)
            for (int i = 0; i < map.DeCount(de); i++)
            {
                float x = map.DeX(de, i), z = map.DeZ(de, i);
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
        var cand = new List<int>();
        for (int ci = 0; ci < nc; ci++)
        {
            var c = map.Cities[ci];
            if (c.X > minX - 2000f && c.X < maxX + 2000f && c.Z > minZ - 2000f && c.Z < maxZ + 2000f) cand.Add(ci);
        }
        for (int k = 0; k < route.Length; k++)
        {
            int de = route[k], n = map.DeCount(de);
            float len = map.ELen[de >> 1];
            for (int i = 0; i < n; i += 2)
            {
                float x = map.DeX(de, i), z = map.DeZ(de, i);
                foreach (int ci in cand)
                {
                    var c = map.Cities[ci];
                    float dx = c.X - x, dz = c.Z - z;
                    if (dx > 2000f || dx < -2000f || dz > 2000f || dz < -2000f) continue;
                    float d = M.Hypot(dx, dz);
                    if (d < 2000f && d < bestD[ci]) { bestD[ci] = d; passAt[ci] = cum[k] + len * i / Math.Max(1, n - 1); }
                }
            }
        }
        foreach (var mv in Maneuvers)
        {
            if (mv.Turn == Turn.Arrive) continue;
            int best = -1;
            foreach (int ci in cand) if (passAt[ci] > mv.AtM + 250f && (best < 0 || passAt[ci] < passAt[best])) best = ci;
            if (best >= 0) mv.Towards = map.Cities[best].Name;
        }
    }

    (float Dist, float Along) Project(int de, float x, float z)
    {
        float best = float.MaxValue, bestAlong = 0f, cumLen = 0f;
        int n = map.DeCount(de);
        for (int i = 0; i < n - 1; i++)
        {
            float x0 = map.DeX(de, i), z0 = map.DeZ(de, i), x1 = map.DeX(de, i + 1), z1 = map.DeZ(de, i + 1);
            float dx = x1 - x0, dz = z1 - z0, l2 = dx * dx + dz * dz, seg = MathF.Sqrt(l2);
            float t = l2 > 1e-6f ? ((x - x0) * dx + (z - z0) * dz) / l2 : 0f;
            t = Math.Clamp(t, 0f, 1f);
            float d = M.Hypot(x - (x0 + dx * t), z - (z0 + dz * t));
            if (d < best) { best = d; bestAlong = cumLen + seg * t; }
            cumLen += seg;
        }
        return (best, bestAlong);
    }

    /// <summary>Feed the latest truck position; returns guidance for this instant.</summary>
    public NavState Update(float x, float z, float speedMps)
    {
        if (route.Length == 0) return new NavState();
        int bestK = idx; float bestD = float.MaxValue, bestAlong = 0f;
        for (int k = Math.Max(0, idx - 1); k <= Math.Min(route.Length - 1, idx + 10); k++)
        {
            var p = Project(route[k], x, z);
            float score = p.Dist + Math.Abs(k - idx) * 0.5f;
            if (score < bestD) { bestD = score; bestK = k; bestAlong = p.Along; }
        }
        float rawDist = Project(route[bestK], x, z).Dist;
        if (rawDist > OffRouteM) offCount++; else offCount = 0;
        bool offRoute = offCount >= 8;
        if (rawDist <= OffRouteM)
        {
            idx = bestK;
            float p = cum[idx] + bestAlong;
            progress = p >= progress - 5f ? MathF.Max(progress, p) : p; // ignore tiny backward jitter
        }
        float remaining = MathF.Max(0f, cum[route.Length] - progress);
        bool arrived = remaining < 25f;
        float remS = MathF.Max(0f, secs[route.Length] - (secs[idx] + (progress - cum[idx]) / map.ELen[route[idx] >> 1] * (secs[idx + 1] - secs[idx])));
        int ni = -1;
        for (int i = 0; i < Maneuvers.Count; i++) if (Maneuvers[i].AtM > progress - 12f) { ni = i; break; }
        var next = ni >= 0 ? Maneuvers[ni] : null;
        var following = ni >= 0 && ni + 1 < Maneuvers.Count ? Maneuvers[ni + 1] : null;
        float toNext = next != null ? MathF.Max(0f, next.AtM - progress) : 0f;
        string? speech = null;
        if (next != null && !offRoute)
        {
            float tau = MathF.Max(speedMps, 4f);
            float d1 = MathF.Max(tau * 26f, 300f), d2 = MathF.Max(tau * 10f, 110f), d3 = MathF.Max(tau * 3.5f, 35f);
            int want = toNext <= d3 ? 3 : toNext <= d2 ? 2 : toNext <= d1 ? 1 : 0;
            if (want > stage[ni]) { stage[ni] = want; speech = Phrase(next, following, toNext, want); }
        }
        var hint = laneHints.FirstOrDefault(h => h.AtM > progress - 12f);
        return new NavState
        {
            DistToNextM = toNext, Next = next, Following = following, RemainingM = remaining, RemainingS = remS,
            OffRoute = offRoute, Arrived = arrived, Speech = speech,
            Lanes = hint?.Lanes, LanesDistM = hint != null ? MathF.Max(0f, hint.AtM - progress) : 0f,
        };
    }

    string Phrase(Maneuver mv, Maneuver? then, float dist, int stg)
    {
        string baseText = Instruction(mv);
        string tail = (then != null && then.Turn != Turn.Arrive && then.AtM - mv.AtM < 320f && mv.Turn != Turn.Arrive) ? ", then " + Instruction(then) : "";
        if (mv.Turn == Turn.Arrive)
            return stg >= 3 ? "You have arrived at " + ArriveWord : "In " + SpokenDistance(dist) + ", you will arrive at " + ArriveWord;
        if (stg >= 3) return char.ToUpperInvariant(baseText[0]) + baseText[1..] + tail;
        return "In " + SpokenDistance(dist) + ", " + baseText + (mv.Towards != null ? " towards " + mv.Towards : "") + tail;
    }

    public string Instruction(Maneuver mv) => mv.Turn switch
    {
        Turn.KeepLeft => "keep left",
        Turn.KeepRight => "keep right",
        Turn.BearLeft => "bear left",
        Turn.BearRight => "bear right",
        Turn.TurnLeft => "turn left",
        Turn.TurnRight => "turn right",
        Turn.UTurn => "make a U-turn",
        Turn.Roundabout => "at the roundabout, take the " + Ordinal(mv.ExitNo) + " exit",
        _ => "arrive at " + ArriveWord,
    };

    static string Ordinal(int n) => n switch
    {
        1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth", 6 => "sixth", 7 => "seventh", 8 => "eighth",
        _ => n + "th",
    };

    public string SpokenDistance(float worldM)
    {
        float m = worldM * DistScale;
        if (m < 950f) return Math.Max(50, (int)MathF.Round(m / 50f) * 50) + " meters";
        if (m < 10000f)
        {
            float half = MathF.Round(m / 500f) / 2f;
            return half.ToString("0.#", CultureInfo.InvariantCulture) + " kilometers";
        }
        return (int)MathF.Round(m / 1000f) + " kilometers";
    }

    public string ShortDistance(float worldM)
    {
        float m = worldM * DistScale;
        if (m < 1000f) return Math.Max(10, (int)MathF.Round(m / 10f) * 10) + " m";
        if (m < 10000f) return (m / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
        return (int)MathF.Round(m / 1000f) + " km";
    }
}
