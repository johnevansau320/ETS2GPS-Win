using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Ets2Gps;

/// <summary>
/// Heading-up road map: roads, route, waypoints, POIs, next-maneuver marker and the truck.
/// Drag to look around (Recentre returns), wheel to zoom, right-click a point for the point menu.
/// </summary>
public sealed class MapControl : FrameworkElement
{
    public MapData? Map;
    public float TruckX, TruckZ, Bearing;       // Bearing: radians, clockwise from north
    public bool HasFix;
    public float[] RoutePts = Array.Empty<float>();   // x,z pairs
    public int[] RouteEdgePt = Array.Empty<int>();    // pair index where each route edge starts
    public int RouteIdx;
    public List<(float X, float Z)> Stops = new();
    public Maneuver? NextManeuver;
    public HashSet<int> ShowPoiTypes = new() { Poi.Fuel, Poi.Parking, Poi.Service, Poi.Weigh };
    public float UserZoom = 1f;
    public bool Free { get; private set; }
    public event Action<bool>? FreeChanged;
    public event Action<float, float>? PointRequested;

    float freeCx, freeCz, freeBearing;
    public float ViewBearing => Free ? freeBearing : Bearing;

    static readonly float[] WidthsM = { 7f, 11f, 18f, 8f };
    static readonly Brush[] ClassBrush =
    {
        F(Color.FromRgb(70, 82, 98)), F(Color.FromRgb(104, 118, 138)), F(Color.FromRgb(222, 178, 84)), F(Color.FromRgb(82, 94, 112)),
    };
    static readonly Brush Bg = F(Color.FromRgb(16, 21, 28));
    static readonly Brush RouteBrush = F(Color.FromRgb(36, 132, 255)), CasingBrush = F(Color.FromArgb(235, 255, 255, 255));
    static readonly Brush CityBrush = F(Color.FromArgb(230, 220, 226, 236));
    static readonly Brush MsgBrush = F(Color.FromArgb(220, 220, 226, 236));
    static readonly Dictionary<int, Color> PoiColors = new()
    {
        [Poi.Fuel] = Color.FromRgb(230, 120, 20), [Poi.Parking] = Color.FromRgb(40, 110, 220), [Poi.Service] = Color.FromRgb(120, 130, 145),
        [Poi.Weigh] = Color.FromRgb(150, 90, 190), [Poi.Dealer] = Color.FromRgb(190, 60, 60), [Poi.Garage] = Color.FromRgb(60, 140, 120),
        [Poi.Recruit] = Color.FromRgb(160, 140, 40), [Poi.Hotel] = Color.FromRgb(90, 90, 170),
    };
    static readonly Dictionary<int, string> PoiLetters = new()
    {
        [Poi.Fuel] = "F", [Poi.Parking] = "P", [Poi.Service] = "S", [Poi.Weigh] = "W", [Poi.Dealer] = "D", [Poi.Garage] = "G", [Poi.Recruit] = "R", [Poi.Hotel] = "H",
    };
    static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static readonly Typeface FaceNormal = new("Segoe UI");
    static Brush F(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    readonly List<int>[] buckets = { new(), new(), new(), new() };
    readonly List<Point> pts = new();
    readonly Dictionary<string, FormattedText> textCache = new();

    public MapControl() { ClipToBounds = true; Focusable = false; }

    // ------------------------------------------------------------------ projection / view --

    double PxPerM => UserZoom * 0.55;
    double PivotX => ActualWidth * 0.5;
    double PivotY => ActualWidth > ActualHeight ? ActualHeight * 0.68 : ActualHeight * 0.72;

    public void ZoomBy(float f) { UserZoom = Math.Clamp(UserZoom * f, 0.35f, 4f); InvalidateVisual(); }

    void EnterFree()
    {
        double s = PxPerM, ahead = -(ActualHeight / 2 - PivotY) / s;
        freeBearing = Bearing;
        freeCx = TruckX + (float)(ahead * Math.Sin(Bearing));
        freeCz = TruckZ - (float)(ahead * Math.Cos(Bearing));
        Free = true;
        FreeChanged?.Invoke(true);
    }

    public void Recentre()
    {
        if (!Free) return;
        Free = false;
        FreeChanged?.Invoke(false);
        InvalidateVisual();
    }

    (float X, float Z)? ToWorld(Point p)
    {
        if (!HasFix) return null;
        double s = PxPerM, px = Free ? ActualWidth / 2 : PivotX, py = Free ? ActualHeight / 2 : PivotY;
        double b = ViewBearing, u = (p.X - px) / s, v = -(p.Y - py) / s;
        float cx = Free ? freeCx : TruckX, cz = Free ? freeCz : TruckZ;
        return ((float)(cx + u * Math.Cos(b) + v * Math.Sin(b)), (float)(cz + u * Math.Sin(b) - v * Math.Cos(b)));
    }

    // ------------------------------------------------------------------------------ mouse --

    bool dragging; Point dragLast, downPos; bool moved;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2) { UserZoom = 1f; InvalidateVisual(); return; }
        dragging = true; moved = false; downPos = dragLast = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        var p = e.GetPosition(this);
        if (!moved && (p - downPos).Length < 6) return;
        moved = true;
        if (!HasFix || Map == null) return;
        if (!Free) EnterFree();
        double s = PxPerM, b = freeBearing, u = -(p.X - dragLast.X) / s, v = (p.Y - dragLast.Y) / s;
        freeCx += (float)(u * Math.Cos(b) + v * Math.Sin(b));
        freeCz += (float)(u * Math.Sin(b) - v * Math.Cos(b));
        dragLast = p;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        dragging = false; ReleaseMouseCapture();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var w = ToWorld(e.GetPosition(this));
        if (w != null) PointRequested?.Invoke(w.Value.X, w.Value.Z);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomBy(e.Delta > 0 ? 1.15f : 1f / 1.15f);
    }

    // ---------------------------------------------------------------------------- drawing --

    FormattedText Text(string t, double size, Brush b, bool bold)
    {
        string key = t + "|" + size.ToString("0.#", CultureInfo.InvariantCulture) + "|" + (bold ? "b" : "n") + "|" + b.GetHashCode();
        if (!textCache.TryGetValue(key, out var ft))
        {
            if (textCache.Count > 4000) textCache.Clear();
            ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? Face : FaceNormal, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            textCache[key] = ft;
        }
        return ft;
    }

    void Center(DrawingContext dc, string t, double x, double y, double size, Brush b, bool bold)
    {
        var ft = Text(t, size, b, bold);
        dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        dc.DrawRectangle(Bg, null, new Rect(0, 0, W, H));
        var m = Map;
        if (m == null) { Center(dc, "Loading the map…", W / 2, H / 2, 16, MsgBrush, false); return; }
        if (!HasFix) { Center(dc, "Waiting for the game…", W / 2, H / 2, 16, MsgBrush, false); return; }
        double s = PxPerM; bool isFree = Free; double vb = ViewBearing;
        double px = isFree ? W / 2 : PivotX, py = isFree ? H / 2 : PivotY;
        double cs = Math.Cos(vb), sn = Math.Sin(vb);
        float cx = isFree ? freeCx : TruckX, cz = isFree ? freeCz : TruckZ;
        Point Proj(float wx, float wz) { double a = wx - cx, b = wz - cz; return new Point(px + (a * cs + b * sn) * s, py - (a * sn - b * cs) * s); }
        float radius = (float)(Math.Max(Math.Max(px, W - px), Math.Max(py, H - py)) * 1.42 / s + 20);
        double zoom = UserZoom;
        bool showLocal = zoom > 0.3, showJunction = zoom > 0.45;
        foreach (var b in buckets) b.Clear();
        m.QueryEdges(cx, cz, radius, e =>
        {
            int cls = Math.Min(3, (int)m.ECls[e]);
            if ((cls == 0 && !showLocal) || (cls == 3 && !showJunction)) return;
            if (m.EPtStart[e + 1] - m.EPtStart[e] < 2) return;
            buckets[cls].Add(e);
        });

        var mat = Matrix.Identity;
        mat.Translate(-cx, -cz); mat.Scale(s, s); mat.Rotate(-vb * 180 / Math.PI); mat.Translate(px, py);
        dc.PushTransform(new MatrixTransform(mat));
        foreach (int cls in new[] { 3, 0, 1, 2 })
        {
            var list = buckets[cls];
            if (list.Count == 0) continue;
            var g = new StreamGeometry();
            using (var c = g.Open())
                foreach (int e in list)
                {
                    int a = m.EPtStart[e], b = m.EPtStart[e + 1];
                    pts.Clear();
                    for (int p = a + 1; p < b; p++) pts.Add(new Point(m.PtX[p], m.PtZ[p]));
                    c.BeginFigure(new Point(m.PtX[a], m.PtZ[a]), false, false);
                    c.PolyLineTo(pts, true, true);
                }
            g.Freeze();
            var pen = new Pen(ClassBrush[cls], Math.Max(WidthsM[cls] * s, cls == 2 ? 3.2 : 2.2) / s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, g);
        }
        if (RoutePts.Length > 0 && RouteEdgePt.Length > 0)
        {
            int total = RoutePts.Length / 2;
            int first = RouteEdgePt[Math.Min(Math.Max(0, RouteIdx - 1), RouteEdgePt.Length - 1)];
            int last = Math.Min(total, first + 6000);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                pts.Clear();
                for (int p = first + 1; p < last; p++) pts.Add(new Point(RoutePts[p * 2], RoutePts[p * 2 + 1]));
                if (last - first >= 2)
                {
                    c.BeginFigure(new Point(RoutePts[first * 2], RoutePts[first * 2 + 1]), false, false);
                    c.PolyLineTo(pts, true, true);
                }
            }
            g.Freeze();
            double w = Math.Max(10 * s, 6);
            dc.DrawGeometry(null, new Pen(CasingBrush, (w + 5) / s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, g);
            dc.DrawGeometry(null, new Pen(RouteBrush, w / s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, g);
        }
        dc.Pop();

        if (zoom < 0.9)
        {
            foreach (var city in m.Cities)
            {
                float dx = city.X - cx, dz = city.Z - cz;
                if (dx * dx + dz * dz > radius * radius) continue;
                var p = Proj(city.X, city.Z);
                if (p.X < -50 || p.X > W + 50 || p.Y < -20 || p.Y > H + 20) continue;
                var ft = Text(city.Name, 13, CityBrush, false);
                dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
            }
        }
        if (zoom > 0.3)
        {
            double r = zoom > 0.8 ? 24 : 18;
            foreach (var poi in m.Pois)
            {
                if (!ShowPoiTypes.Contains(poi.Type)) continue;
                float dx = poi.X - cx, dz = poi.Z - cz;
                if (dx > radius || dx < -radius || dz > radius || dz < -radius) continue;
                var p = Proj(poi.X, poi.Z);
                if (p.X < -r || p.X > W + r || p.Y < -r || p.Y > H + r) continue;
                dc.DrawEllipse(new SolidColorBrush(PoiColors.GetValueOrDefault(poi.Type, Colors.Gray)), null, p, r, r);
                Center(dc, PoiLetters.GetValueOrDefault(poi.Type, "?"), p.X, p.Y, r * 1.1, Brushes.White, true);
            }
        }
        for (int i = 0; i < Stops.Count; i++)
        {
            var p = Proj(Stops[i].X, Stops[i].Z);
            if (p.X < -60 || p.X > W + 60 || p.Y < -60 || p.Y > H + 60) continue;
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(25, 118, 210)), new Pen(new SolidColorBrush(Color.FromRgb(18, 92, 58)), 3), p, 17, 17);
            Center(dc, (i + 1).ToString(CultureInfo.InvariantCulture), p.X, p.Y, 20, Brushes.White, true);
        }
        var nm = NextManeuver;
        if (nm != null)
        {
            var p = Proj(nm.X, nm.Z);
            if (p.X > -80 && p.X < W + 80 && p.Y > -80 && p.Y < H + 80)
            {
                double r = 26;
                var edge = Color.FromRgb(18, 92, 58);
                dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(edge), 3), p, r, r);
                if (!isFree) Glyphs.Draw(dc, nm.Turn, nm.ExitNo, nm.AngleDeg, p.X, p.Y, r * 1.55, edge);
                else dc.DrawEllipse(new SolidColorBrush(edge), null, p, r * 0.35, r * 0.35);
            }
        }
        var tp = Proj(TruckX, TruckZ);
        double t = 15;
        dc.PushTransform(new TranslateTransform(tp.X, tp.Y));
        dc.PushTransform(new RotateTransform((Bearing - vb) * 180 / Math.PI));
        var tg = new StreamGeometry();
        using (var c = tg.Open())
        {
            c.BeginFigure(new Point(0, -t * 1.2), true, true);
            c.LineTo(new Point(t * 0.85, t * 0.85), true, false); c.LineTo(new Point(0, t * 0.4), true, false); c.LineTo(new Point(-t * 0.85, t * 0.85), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0, 214, 120)), new Pen(Brushes.White, 2.5), tg);
        dc.Pop(); dc.Pop();
    }
}
