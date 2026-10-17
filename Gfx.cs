using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Ets2Gps;

/// <summary>Vector maneuver pictograms (turn arrows, roundabout, U-turn, arrival).</summary>
public static class Glyphs
{
    static Pen MakePen(Brush b, double w, PenLineCap cap = PenLineCap.Flat)
    {
        var p = new Pen(b, w) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    static void ArrowHead(DrawingContext dc, Brush fill, double tx, double ty, double dx, double dy, double s)
    {
        double h = s * 0.26, w = s * 0.2, bx = tx - dx * h, by = ty - dy * h;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(tx + dx * h * 0.35, ty + dy * h * 0.35), true, true);
            c.LineTo(new Point(bx - dy * w, by + dx * w), true, false);
            c.LineTo(new Point(bx + dy * w, by - dx * w), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(fill, null, g);
    }

    static StreamGeometry Poly(params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i], true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Draws a maneuver centred at (cx, cy) inside a square of side size.</summary>
    public static void Draw(DrawingContext dc, Turn turn, int exitNo, double angleDeg, double cx, double cy, double size, Color color)
    {
        double s = size;
        var brush = new SolidColorBrush(color); brush.Freeze();
        var dimBrush = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B)); dimBrush.Freeze();
        var stroke = MakePen(brush, s * 0.13);
        var dim = MakePen(dimBrush, s * 0.13);
        void Line(Pen p, double x0, double y0, double x1, double y1) => dc.DrawLine(p, new Point(x0, y0), new Point(x1, y1));
        switch (turn)
        {
            case Turn.Arrive:
                {
                    var p = MakePen(brush, s * 0.1);
                    dc.DrawEllipse(null, p, new Point(cx, cy - s * 0.08), s * 0.26, s * 0.26);
                    dc.DrawEllipse(brush, null, new Point(cx, cy - s * 0.08), s * 0.09, s * 0.09);
                    Line(p, cx, cy + s * 0.18, cx, cy + s * 0.42);
                    break;
                }
            case Turn.UTurn:
                {
                    bool right = angleDeg > 0;
                    if (right) dc.PushTransform(new ScaleTransform(-1, 1, cx, cy));
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(cx + s * 0.16, cy + s * 0.42), false, false);
                        c.LineTo(new Point(cx + s * 0.16, cy - s * 0.14), true, true);
                        c.ArcTo(new Point(cx - s * 0.16, cy - s * 0.14), new Size(s * 0.16, s * 0.16), 0, false, SweepDirection.Counterclockwise, true, true);
                        c.LineTo(new Point(cx - s * 0.16, cy + s * 0.06), true, true);
                    }
                    g.Freeze();
                    dc.DrawGeometry(null, stroke, g);
                    ArrowHead(dc, brush, cx - s * 0.16, cy + s * 0.10, 0, 1, s);
                    if (right) dc.Pop();
                    break;
                }
            case Turn.Roundabout:
                {
                    double ry = cy - s * 0.06, r = s * 0.2;
                    var p = MakePen(brush, s * 0.1); var pd = MakePen(dimBrush, s * 0.1);
                    dc.DrawEllipse(null, pd, new Point(cx, ry), r, r);
                    Line(p, cx, cy + s * 0.44, cx, ry + r);
                    double th = angleDeg * Math.PI / 180, dx = Math.Sin(th), dy = -Math.Cos(th);
                    double ex = cx + dx * (r + s * 0.26), ey = ry + dy * (r + s * 0.26);
                    Line(p, cx + dx * r, ry + dy * r, ex, ey);
                    ArrowHead(dc, brush, ex, ey, dx, dy, s * 0.9);
                    if (exitNo > 0)
                    {
                        var ft = new FormattedText(exitNo.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), s * 0.24, brush, 1.0);
                        dc.DrawText(ft, new Point(cx - ft.Width / 2, ry - ft.Height / 2));
                    }
                    break;
                }
            default:
                {
                    double signed = turn switch { Turn.KeepLeft => -22, Turn.KeepRight => 22, Turn.BearLeft => -48, Turn.BearRight => 48, Turn.TurnLeft => -90, _ => 90 };
                    double th = signed * Math.PI / 180, jx = cx, jy = cy + s * 0.06;
                    Line(dim, jx, jy, jx, cy - s * 0.4);
                    Line(stroke, jx, cy + s * 0.44, jx, jy);
                    double dx = Math.Sin(th), dy = -Math.Cos(th), len = s * 0.34;
                    Line(stroke, jx, jy, jx + dx * len, jy + dy * len);
                    ArrowHead(dc, brush, jx + dx * len, jy + dy * len, dx, dy, s);
                    break;
                }
        }
    }
}

/// <summary>Lane panel: one arrow per lane, green for the lanes the route uses, white for others, a faint second arrow if a lane serves two directions.</summary>
public sealed class LanePanel : FrameworkElement
{
    public const double Cell = 50, Pad = 10, PanelH = 112;
    public IReadOnlyList<Lane>? Lanes { get; set; }
    public string Caption { get; set; } = "";

    public static double DesignWidth(int lanes, string caption) => Math.Max(lanes * Cell + 2 * Pad, caption.Length > 0 ? 80 : 0);

    static readonly Brush Back = F(new SolidColorBrush(Color.FromArgb(235, 38, 46, 62)));
    static readonly Brush Green = F(new SolidColorBrush(Color.FromRgb(60, 235, 130)));
    static readonly Brush GreenGlow = F(new SolidColorBrush(Color.FromArgb(80, 60, 235, 130)));
    static readonly Brush White = F(new SolidColorBrush(Color.FromRgb(245, 247, 250)));
    static readonly Brush Faint = F(new SolidColorBrush(Color.FromArgb(95, 255, 255, 255)));
    static readonly Pen Divider = MakeDivider();
    static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static Brush F(Brush b) { b.Freeze(); return b; }
    static Pen MakeDivider() { var p = new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1.5) { DashStyle = new DashStyle(new double[] { 3.3, 3.3 }, 0) }; p.Freeze(); return p; }

    protected override void OnRender(DrawingContext dc)
    {
        var lanes = Lanes;
        if (lanes == null || lanes.Count == 0) return;
        double w = DesignWidth(lanes.Count, Caption), h = PanelH;
        double s = Math.Min(ActualWidth / w, ActualHeight / h);
        dc.PushTransform(new TranslateTransform((ActualWidth - w * s) / 2, (ActualHeight - h * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawRoundedRectangle(Back, null, new Rect(0, 0, w, h), 14, 14);
        if (Caption.Length > 0)
        {
            var ft = new FormattedText(Caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 14,
                new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point((w - ft.Width) / 2, h - 27 + (20 - ft.Height) / 2));
        }
        double x0 = (w - lanes.Count * Cell) / 2;
        for (int i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            double cx = x0 + Cell * (i + 0.5);
            if (i > 0) dc.DrawLine(Divider, new Point(x0 + Cell * i, 10), new Point(x0 + Cell * i, h - 30));
            if (lane.Alt is double alt) Arrow(dc, cx, h, alt, Faint, false);
            if (lane.Angle is not double a) continue;
            if (lane.Preferred) Arrow(dc, cx, h, a, Green, true); else Arrow(dc, cx, h, a, White, false);
        }
        dc.Pop(); dc.Pop();
    }

    static Pen Stroke(Brush b, double width) { var p = new Pen(b, width) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Round }; p.Freeze(); return p; }

    /// <summary>Arrow rising from the bottom of the lane cell: a stem, then a bend of deg (+ = right) ending in a head.</summary>
    static void Arrow(DrawingContext dc, double cx, double h, double deg, Brush color, bool bright)
    {
        const double w = 7, top = 14, hl = 13, hw = 11;
        double bottom = h - 34;
        double a = Math.Clamp(deg, -100, 100), th = a * Math.PI / 180, dx = Math.Sin(th), dy = -Math.Cos(th);
        bool straight = Math.Abs(a) < 12;
        double bendY = straight ? top + 8 : (top + bottom) / 2, len = straight ? 0 : 24;
        double ex = cx + dx * len, ey = bendY + dy * len;
        double tipX = straight ? cx : ex, tipY = straight ? top : ey, fx = straight ? 0 : dx, fy = straight ? -1 : dy;
        var shaft = new StreamGeometry();
        using (var g = shaft.Open())
        {
            g.BeginFigure(new Point(cx, bottom), false, false);
            if (straight) g.LineTo(new Point(cx, tipY + hl * 0.97), true, true);
            else { g.LineTo(new Point(cx, bendY), true, true); g.LineTo(new Point(ex - fx * hl * 0.97, ey - fy * hl * 0.97), true, true); }
        }
        shaft.Freeze();
        if (bright) dc.DrawGeometry(null, Stroke(GreenGlow, w + 7), shaft);
        dc.DrawGeometry(null, Stroke(color, w), shaft);
        double bx = tipX - fx * hl, by = tipY - fy * hl;
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(new Point(tipX, tipY), true, true);
            g.LineTo(new Point(bx - fy * hw, by + fx * hw), true, false);
            g.LineTo(new Point(bx + fy * hw, by - fx * hw), true, false);
        }
        head.Freeze();
        dc.DrawGeometry(color, null, head);
    }
}

/// <summary>Rotating compass rose (N points to true north on the heading-up map).</summary>
public sealed class CompassControl : FrameworkElement
{
    public double HeadingRad { get; set; }
    public bool Valid { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        double S = Math.Min(ActualWidth, ActualHeight);
        double k = S / 100.0;
        dc.PushTransform(new ScaleTransform(k, k));
        double cx = 50, cy = 50, r = 46.5;
        var ring = new Pen(Brushes.White, 3);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(190, 14, 18, 24)), ring, new Point(cx, cy), r, r);
        double deg = HeadingRad * 180 / Math.PI;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        void Txt(string t, double x, double y, double size, Brush b)
        {
            var ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, b, dpi);
            dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
        }
        dc.PushTransform(new RotateTransform(-deg, cx, cy));
        double off = r * 0.72, sz = r * 0.34;
        var white = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
        Txt("N", cx, cy - off, sz, new SolidColorBrush(Color.FromRgb(255, 120, 120)));
        Txt("S", cx, cy + off, sz, white); Txt("E", cx + off, cy, sz, white); Txt("W", cx - off, cy, sz, white);
        dc.Pop();
        if (Valid)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int idx = (int)(((deg % 360) + 360 + 22.5) / 45) % 8;
            Txt(names[idx], cx, cy, r * 0.42, Brushes.White);
        }
        var tri = new StreamGeometry();
        using (var c = tri.Open())
        {
            c.BeginFigure(new Point(cx, cy - r - 4), true, true);
            c.LineTo(new Point(cx + 6, cy - r + 6), true, false); c.LineTo(new Point(cx - 6, cy - r + 6), true, false);
        }
        dc.DrawGeometry(Brushes.White, null, tri);
        dc.Pop();
    }
}
