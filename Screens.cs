using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ets2Gps;

/// <summary>Trip / job screen, elevation chart and settings (screens of the main window).</summary>
public sealed partial class MainWindow
{
    static readonly Brush Red = Ui.Frozen(Color.FromRgb(255, 120, 110)), Amber = Ui.Frozen(Color.FromRgb(255, 190, 90)), GreenText = Ui.Frozen(Color.FromRgb(60, 220, 130));

    static Brush DamageBrush(float v) => v < 5 ? Brushes.White : v < 25 ? Amber : Red;
    static string Fmt(double v, string f = "N0") => v.ToString(f, CultureInfo.InvariantCulture);

    Border Card(string title, params (string K, string V, Brush? C)[] rows)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, FontSize = 13, Foreground = Ui.Frozen(Color.FromRgb(159, 176, 198)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var (k, v, c) in rows)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new TextBlock { Text = k, FontSize = 15, Foreground = Brushes.White });
            var val = new TextBlock { Text = v, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = c ?? Brushes.White, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 0, 0) };
            Grid.SetColumn(val, 1); g.Children.Add(val);
            sp.Children.Add(g);
        }
        return new Border { Child = sp, Background = Ui.Frozen(Color.FromRgb(24, 33, 45)), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(5) };
    }

    void UpdateTrip()
    {
        var s = truck;
        var page = new StackPanel { Margin = new Thickness(204, 12, 18, 12) };
        page.Children.Add(new TextBlock { Text = "TRIP DETAILS", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        if (s == null || !s.Connected)
        {
            page.Children.Add(Card("WAITING", ("Game", "not connected", null)));
            tripPage.Child = new ScrollViewer { Content = page };
            return;
        }
        var j = s.Job; var dt = s.Detail; var n = nav;
        bool routingNow = n?.Active == true && routeRemainingM >= 0f;
        // this screen always counts in game kilometres (planned distance, range and consumption come from the game), whatever the Real / Sim distance setting
        float remGameKm = routingNow ? routeRemainingM * cfg.DistScale / 1000f : dt.NavDistanceKm;
        float realMin = routingNow && routeEtaGameMin >= 0f ? routeEtaGameMin / TimeScale : -1f;
        float simMin = routingNow ? routeEtaGameMin : (s.NavEtaMin >= 0f ? s.NavEtaMin : -1f);
        int planned = j?.PlannedKm ?? 0;
        float progress = planned > 0 && remGameKm > 0f ? Math.Clamp(1f - remGameKm / planned, 0f, 1f) : 0f;
        var now = DateTime.Now;

        // header strip: from -> to, progress, clocks
        var head = new Grid();
        head.RowDefinitions.Add(new RowDefinition()); head.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) }); head.RowDefinitions.Add(new RowDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition());
        TextBlock T(string t, HorizontalAlignment h, int row, int col) { var tb = new TextBlock { Text = t, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = h }; Grid.SetRow(tb, row); Grid.SetColumn(tb, col); head.Children.Add(tb); return tb; }
        T("FROM  " + (j != null ? PlaceName(j.SrcCompany, j.SrcCity) : "-"), HorizontalAlignment.Left, 0, 0);
        T("TO  " + (j != null ? PlaceName(j.DestCompany, j.DestCity) : (hasDest ? destName : "-")), HorizontalAlignment.Right, 0, 1);
        var bar = new Grid { Height = 10, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(new Border { Background = Ui.Frozen(Color.FromRgb(36, 48, 64)), CornerRadius = new CornerRadius(5) });
        var fill = new Border { Background = Ui.Frozen(Color.FromRgb(90, 190, 255)), CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left };
        bar.Children.Add(fill);
        bar.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * progress;
        Grid.SetRow(bar, 1); Grid.SetColumnSpan(bar, 2); head.Children.Add(bar);
        T($"{Fmt(remGameKm)} km left" + (planned > 0 ? $" of {Fmt(planned)} km · {progress * 100:0}%" : ""), HorizontalAlignment.Left, 2, 0);
        T("Local " + now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  ·  ETS2 " + ClockText(s.GameTimeMin), HorizontalAlignment.Right, 2, 1);
        page.Children.Add(new Border { Child = head, Background = Ui.Frozen(Color.FromRgb(24, 33, 45)), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(5, 8, 5, 5) });

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        void Add(Border c) { c.Width = 380; wrap.Children.Add(c); }

        if (j != null)
        {
            bool late = j.DueMin >= 0f && simMin >= 0f && simMin > j.DueMin;
            var rows = new List<(string, string, Brush?)>
            {
                ("Cargo", j.Cargo + (j.MassKg > 0 ? "  " + Fmt(j.MassKg / 1000f, "0.0") + " t" : ""), null), ("Units", j.Units.ToString(CultureInfo.InvariantCulture), null),
            };
            if (j.Income > 0) rows.Add(("Income", "€" + Fmt(j.Income), null));
            if (j.DueMin >= 0f)
            {
                rows.Add(("Deadline in", FormatDuration(j.DueMin) + " (sim)", late ? Red : null));
                if (simMin >= 0f) { float sp = j.DueMin - simMin; rows.Add((late ? "Running late by" : "Time to spare", FormatDuration(MathF.Abs(sp)) + " (sim)", late ? Red : GreenText)); }
            }
            if (planned > 0) rows.Add(("Planned distance", planned + " km", null));
            Add(Card("JOB", rows.ToArray()));
        }
        else Add(Card("JOB", ("Status", "no active job", null)));

        var rr = new List<(string, string, Brush?)> { ("Destination", hasDest ? destName : "none", null) };
        if (routingNow && n != null)
        {
            rr.Add(("Remaining", Fmt(remGameKm) + " km", null)); rr.Add(("Real time left", FormatDuration(realMin), null));
            rr.Add(("Sim time left", FormatDuration(simMin), null)); rr.Add(("Arrive (sim clock)", ClockText(s.GameTimeMin + simMin), null));
            if (lastNextText.Length > 0) rr.Add(("Next", char.ToUpperInvariant(lastNextText[0]) + lastNextText[1..], null));
        }
        else { rr.Add(("Game route left", Fmt(remGameKm) + " km", null)); if (simMin >= 0f) rr.Add(("Game ETA (sim)", FormatDuration(simMin), null)); }
        rr.Add(("Route type", routeMode.Label, null));
        rr.Add(("Ferries", cfg.AvoidFerries ? "avoided" : "allowed", null));
        Add(Card("ROUTE", rr.ToArray()));

        float fr = dt.FuelCapL > 0f ? dt.FuelL / dt.FuelCapL : 0f;
        Brush? fc = fr < 0.12f ? Red : fr < 0.25f ? Amber : null;
        Add(Card("TRUCK", ("Truck", dt.TruckName.Length > 0 ? dt.TruckName : "-", null),
            ("Fuel", $"{dt.FuelL:0} / {dt.FuelCapL:0} L  ({fr * 100f:0}%)", fc), ("Range", Fmt(dt.RangeKm) + " km", null),
            ("Consumption", Fmt(dt.ConsumptionLPerKm * 100f, "0.0") + " L/100 km", null), ("Odometer", Fmt(dt.OdometerKm) + " km", null),
            ("Speed", $"{Math.Round(Math.Max(0, s.SpeedKmh))} km/h" + (s.SpeedLimitKmh > 0 ? $"  (limit {s.SpeedLimitKmh})" : ""), null)));

        if (dt.TrailerAttached)
            Add(Card("TRAILER & CARGO", ("Trailer", dt.TrailerName.Length > 0 ? dt.TrailerName : "-", null), ("Plate", dt.TrailerPlate.Length > 0 ? dt.TrailerPlate : "-", null),
                ("Chassis damage", $"{dt.TrailerChassis:0}%", DamageBrush(dt.TrailerChassis)), ("Wheels damage", $"{dt.TrailerWheels:0}%", DamageBrush(dt.TrailerWheels)),
                ("Body damage", $"{dt.TrailerBody:0}%", DamageBrush(dt.TrailerBody)), ("Cargo damage", $"{dt.CargoDamage:0}%", DamageBrush(dt.CargoDamage))));
        else Add(Card("TRAILER & CARGO", ("Trailer", "none attached", null)));

        float drivenM = MathF.Max(0f, motion.Travelled - tripBase), driven = drivenM * cfg.DistScale / 1000f;
        float avg = tripDriveMs > 1000 ? drivenM / (tripDriveMs / 1000f) * 3.6f : 0f;
        var tc = new List<(string, string, Brush?)>
        {
            ("Distance driven", Fmt(driven) + " km", null), ("Driving time (real)", FormatDuration(tripDriveMs / 60000f), null),
            ("Driving time (sim)", FormatDuration(tripDriveMs / 60000f * TimeScale), null), ("Average speed", Fmt(avg) + " km/h", null), ("Top speed", Fmt(tripMaxKmh) + " km/h", null),
            ("Climbed / descended", $"{elev.Ascent():0} / {elev.Descent():0} m", null), ("Fuel used", Fmt(tripFuelUsed) + " L", null),
        };
        if (driven > 5f && tripFuelUsed > 0f) tc.Add(("Trip consumption", Fmt(tripFuelUsed / driven * 100f, "0.0") + " L/100 km", null));
        Add(Card("THIS TRIP", tc.ToArray()));

        var tr = new List<(string, string, Brush?)> { ("Sim clock", ClockText(s.GameTimeMin), null), ("Real time", now.ToString("HH:mm", CultureInfo.InvariantCulture), null) };
        if (s.NextRestMin >= 0f) tr.Add(("Rest needed in", FormatDuration(s.NextRestMin) + " (sim)", s.NextRestMin <= 30f ? Red : s.NextRestMin <= 90f ? Amber : null));
        if (place.Length > 0) tr.Add(("Location", place.Replace("Near ", ""), null));
        Add(Card("TIME & REST", tr.ToArray()));

        page.Children.Add(wrap);
        var sv = tripPage.Child as ScrollViewer;
        double off = sv?.VerticalOffset ?? 0;
        tripPage.Child = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ((ScrollViewer)tripPage.Child).ScrollToVerticalOffset(off);
    }

    void ShowElevation()
    {
        int n = elev.Alt.Count;
        string summary = n < 2 ? "No data yet" : $"Now {Math.Round(truck?.Y ?? 0)} m  ·  climbed {elev.Ascent():0} m  ·  descended {elev.Descent():0} m  ·  range {elev.Alt.Min():0} to {elev.Alt.Max():0} m";
        var sp = new StackPanel { Margin = new Thickness(18, 6, 18, 0) };
        sp.Children.Add(new TextBlock { Text = summary, FontSize = 14, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(new ElevationChart(elev, distScale));
        Dlg.Custom(this, "Trip elevation", sp, new (string, Action?, bool)[] { ("Reset trip", () => elev.Clear(), false), ("Close", null, true) }, 640);
    }

    void ShowSettings()
    {
        var sp = new StackPanel { Margin = new Thickness(18, 4, 18, 0) };
        TextBlock H(string t) => new() { Text = t, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 12, 0, 4) };
        TextBox Box(string v) => new() { Text = v, FontSize = 14, Padding = new Thickness(5), Background = Ui.Frozen(Color.FromRgb(16, 21, 28)), Foreground = Brushes.White, CaretBrush = Brushes.White, BorderBrush = Brushes.Gray };

        sp.Children.Add(H("Route type"));
        var modeButtons = new List<(RadioButton, RouteMode)>();
        foreach (var md in RouteMode.All)
        {
            var rb = new RadioButton { Content = new TextBlock { Text = md.Help, TextWrapping = TextWrapping.Wrap }, IsChecked = md == routeMode, Foreground = Brushes.White, Margin = new Thickness(0, 3, 0, 3), GroupName = "mode" };
            modeButtons.Add((rb, md)); sp.Children.Add(rb);
        }
        var ferries = new CheckBox { Content = new TextBlock { Text = "Avoid ferries and car trains (routes never cross by ferry; some places may become unreachable)", TextWrapping = TextWrapping.Wrap }, IsChecked = cfg.AvoidFerries, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 4) };
        sp.Children.Add(ferries);
        var clear = Ui.Button($"Clear avoided roads ({cfg.Avoid.Count})", double.NaN, 32); clear.Padding = new Thickness(12, 0, 12, 0); clear.HorizontalAlignment = HorizontalAlignment.Left;
        clear.Click += (_, _) => { ClearAvoid(); clear.Content = "Clear avoided roads (0)"; };
        sp.Children.Add(clear);

        sp.Children.Add(H("Window"));
        var pin = new CheckBox { Content = "Keep this window on top of other apps", IsChecked = Topmost, Foreground = Brushes.White };
        sp.Children.Add(pin);
        var opLabel = new TextBlock { Text = "Opacity " + Math.Round(Opacity * 100) + "%", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 0) };
        var op = new Slider { Minimum = 30, Maximum = 100, Value = Math.Round(Opacity * 100), Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        op.ValueChanged += (_, e) => { opLabel.Text = "Opacity " + (int)e.NewValue + "%"; Opacity = e.NewValue / 100.0; };
        sp.Children.Add(opLabel); sp.Children.Add(op);

        sp.Children.Add(H("Lane guidance"));
        var showLanes = new CheckBox { Content = new TextBlock { Text = "Show lane guidance (the lane panel on the map and in the lane overlay window)", TextWrapping = TextWrapping.Wrap }, IsChecked = cfg.ShowLanes, Foreground = Brushes.White };
        sp.Children.Add(showLanes);
        sp.Children.Add(H("Lane overlay window"));
        var ovAuto = new CheckBox { Content = new TextBlock { Text = "Start the lane overlay when this app starts (if it is not already running)", TextWrapping = TextWrapping.Wrap }, IsChecked = cfg.OverlayAutoStart, Foreground = Brushes.White };
        sp.Children.Add(ovAuto);
        sp.Children.Add(new TextBlock { Text = "Path to Ets2LaneOverlay.exe (empty = look in the usual places)", Foreground = Ui.Dim, Margin = new Thickness(0, 6, 0, 0) });
        var ovPath = Box(cfg.OverlayPath); sp.Children.Add(ovPath);
        var ovNow = Ui.Button("Start overlay now", double.NaN, 32); ovNow.Padding = new Thickness(12, 0, 12, 0); ovNow.HorizontalAlignment = HorizontalAlignment.Left; ovNow.Margin = new Thickness(0, 8, 0, 0);
        ovNow.Click += (_, _) =>
        {
            var keep = cfg.OverlayPath; cfg.OverlayPath = ovPath.Text.Trim();
            Toast(OverlayLauncher.EnsureRunning(cfg) ?? "Lane overlay is already running");
            if (OverlayLauncher.Find(cfg) == null) cfg.OverlayPath = keep;
        };
        sp.Children.Add(ovNow);

        sp.Children.Add(H("Voice (this PC's speakers / headset)"));
        var voiceOn = new CheckBox { Content = "Voice guidance", IsChecked = voice.Enabled, Foreground = Brushes.White };
        sp.Children.Add(voiceOn);
        var voices = voice.Voices().ToList();
        var combo = new ComboBox { Margin = new Thickness(0, 6, 0, 0), MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
        combo.Items.Add("(default voice)");
        foreach (var v in voices) combo.Items.Add(v);
        combo.SelectedIndex = Math.Max(0, voices.IndexOf(cfg.VoiceName) + 1);
        sp.Children.Add(combo);
        var rateLabel = new TextBlock { Text = "Speed " + cfg.VoiceRate, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 0) };
        var rate = new Slider { Minimum = -5, Maximum = 5, Value = cfg.VoiceRate, Width = 260, TickFrequency = 1, IsSnapToTickEnabled = true, HorizontalAlignment = HorizontalAlignment.Left };
        rate.ValueChanged += (_, e) => rateLabel.Text = "Speed " + (int)e.NewValue;
        sp.Children.Add(rateLabel); sp.Children.Add(rate);
        var test = Ui.Button("Test voice", double.NaN, 32); test.Padding = new Thickness(12, 0, 12, 0); test.HorizontalAlignment = HorizontalAlignment.Left; test.Margin = new Thickness(0, 8, 0, 0);
        test.Click += (_, _) =>
        {
            bool was = voice.Enabled; voice.Enabled = true;
            voice.Configure((int)rate.Value, combo.SelectedIndex > 0 ? voices[combo.SelectedIndex - 1] : "");
            voice.Say("Voice guidance is working."); voice.Enabled = was;
        };
        sp.Children.Add(test);

        sp.Children.Add(H("Updates"));
        var updAuto = new CheckBox { Content = new TextBlock { Text = "Look for new versions automatically (about once a day; nothing is downloaded or installed)", TextWrapping = TextWrapping.Wrap }, IsChecked = cfg.CheckUpdates, Foreground = Brushes.White };
        sp.Children.Add(updAuto);
        var updText = new TextBlock { Text = UpdateStatusText(), Foreground = Ui.Dim, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        sp.Children.Add(updText);
        var updRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var updNow = Ui.Button("Check for updates now", double.NaN, 32); updNow.Padding = new Thickness(12, 0, 12, 0);
        var updOpen = Ui.Button("Open the download page", double.NaN, 32); updOpen.Padding = new Thickness(12, 0, 12, 0); updOpen.Margin = new Thickness(8, 0, 0, 0);
        var updSkip = Ui.Button("Skip this version", double.NaN, 32); updSkip.Padding = new Thickness(12, 0, 12, 0); updSkip.Margin = new Thickness(8, 0, 0, 0);
        updRow.Children.Add(updNow); updRow.Children.Add(updOpen); updRow.Children.Add(updSkip);
        sp.Children.Add(updRow);
        void ShowUpdate() { updText.Text = UpdateStatusText(); var vis = update != null ? Visibility.Visible : Visibility.Collapsed; updOpen.Visibility = vis; updSkip.Visibility = vis; }
        ShowUpdate();
        updNow.Click += async (_, _) =>
        {
            updNow.IsEnabled = false; updText.Text = "Checking...";
            try
            {
                var u = await UpdateCheck.CheckAsync();
                cfg.LastUpdateCheckUtc = DateTime.UtcNow.Ticks; cfg.SkippedVersion = ""; cfg.Save();
                SetUpdate(u, announce: false);
                ShowUpdate();
            }
            catch (Exception e) { updText.Text = "Could not reach GitHub: " + e.Message; }
            updNow.IsEnabled = true;
        };
        updOpen.Click += (_, _) => { if (update != null) UpdateCheck.OpenPage(update.PageUrl); };
        updSkip.Click += (_, _) => { if (update != null) { cfg.SkippedVersion = update.Latest.ToString(3); cfg.Save(); SetUpdate(update, announce: false); ShowUpdate(); } };

        sp.Children.Add(H("Connections"));
        sp.Children.Add(new TextBlock { Text = "Telemetry server (TruckSim GPS) address", Foreground = Ui.Dim });
        var tele = Box(cfg.TelemetryHost); sp.Children.Add(tele);
        var srv = new CheckBox { Content = new TextBlock { Text = "Built-in web server (lets the tablet app, a browser and the lane overlay window use this PC)", TextWrapping = TextWrapping.Wrap }, IsChecked = cfg.ServerEnabled, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 0) };
        sp.Children.Add(srv);
        sp.Children.Add(new TextBlock { Text = "Server port (changes apply after restarting the app)", Foreground = Ui.Dim, Margin = new Thickness(0, 6, 0, 0) });
        var port = Box(cfg.ServerPort.ToString(CultureInfo.InvariantCulture)); port.Width = 120; port.HorizontalAlignment = HorizontalAlignment.Left; sp.Children.Add(port);
        sp.Children.Add(H("Distances"));
        var realD = new CheckBox { Content = new TextBlock { Text = "Real distances (turns, lane guidance, route length). Off = Sim distances like the game shows", TextWrapping = TextWrapping.Wrap }, IsChecked = cfg.RealDistances, Foreground = Brushes.White };
        sp.Children.Add(realD);
        sp.Children.Add(new TextBlock { Text = "Sim scale (the game shows map metres x19)", Foreground = Ui.Dim, Margin = new Thickness(0, 6, 0, 0) });
        var scale = Box(cfg.DistScale.ToString(CultureInfo.InvariantCulture)); scale.Width = 120; scale.HorizontalAlignment = HorizontalAlignment.Left; sp.Children.Add(scale);

        void Save(bool reloadMap)
        {
            var md = modeButtons.FirstOrDefault(b => b.Item1.IsChecked == true).Item2;
            bool ferryChanged = (ferries.IsChecked == true) != cfg.AvoidFerries;
            if (ferryChanged) SetAvoidFerries(ferries.IsChecked == true, md == null || md == routeMode);
            if (md != null && md != routeMode) SetRouteMode(md);
            Topmost = pin.IsChecked == true; cfg.Topmost = Topmost; UpdatePinButton();
            cfg.Opacity = Opacity;
            cfg.CheckUpdates = updAuto.IsChecked == true;
            cfg.ShowLanes = showLanes.IsChecked == true; if (!cfg.ShowLanes) SetLaneUi(null);
            cfg.OverlayAutoStart = ovAuto.IsChecked == true; cfg.OverlayPath = ovPath.Text.Trim();
            voice.Enabled = voiceOn.IsChecked == true; cfg.VoiceOn = voice.Enabled; UpdateVoiceButton();
            cfg.VoiceName = combo.SelectedIndex > 0 ? voices[combo.SelectedIndex - 1] : ""; cfg.VoiceRate = (int)rate.Value;
            voice.Configure(cfg.VoiceRate, cfg.VoiceName);
            cfg.TelemetryHost = tele.Text.Trim();
            cfg.ServerEnabled = srv.IsChecked == true;
            if (int.TryParse(port.Text, out var pt) && pt > 1023 && pt < 65536) cfg.ServerPort = pt;
            if (float.TryParse(scale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var sc) && sc > 0) cfg.DistScale = sc;
            cfg.RealDistances = realD.IsChecked == true;
            distScale = cfg.RealDistances ? 1f : cfg.DistScale; if (nav != null) nav.DistScale = distScale;
            cfg.Save();
            if (reloadMap) { try { File.Delete(MapBuilder.MapService.MetaPath); } catch { /* ignore */ } _ = LoadMapAsync(); }
        }
        double opBefore = Opacity;
        Dlg.Custom(this, "Settings", new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(300, ActualHeight - 160) },
            new (string, Action?, bool)[] { ("Cancel", () => Opacity = opBefore, false), ("Save + rebuild map", () => Save(true), false), ("Save", () => Save(false), true) }, 600);
    }
}
