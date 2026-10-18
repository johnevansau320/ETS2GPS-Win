using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ets2Gps;

/// <summary>Small control that draws one maneuver pictogram (white on transparent).</summary>
public sealed class GlyphControl : FrameworkElement
{
    public Turn? Turn; public int ExitNo; public double Angle; public Color Color = Colors.White;
    protected override void OnRender(DrawingContext dc)
    {
        if (Turn is Turn t) Glyphs.Draw(dc, t, ExitNo, Angle, ActualWidth / 2, ActualHeight / 2, Math.Min(ActualWidth, ActualHeight), Color);
    }
}

/// <summary>ETS2 GPS for Windows: the map, route guidance, lane panel and trip screen in one window (UI part).</summary>
public sealed partial class MainWindow : Window
{
    const float TimeScale = 19f;
    const float LaneShowM = 50_000f;   // the lane panel appears this far (game metres) before the junction

    readonly AppSettings cfg;
    readonly MapControl mapView = new();
    readonly Voice voice = new();
    readonly TelemetryClient telemetry = new();
    readonly MotionFilter motion = new();
    readonly ElevationLog elev = new();
    readonly Stopwatch clock = Stopwatch.StartNew();

    MapData? map; Router? router; Navigator? nav; TruckState? truck;
    RouteMode routeMode;
    float distScale;

    // UI parts
    readonly Border banner = new(), bottomBar = new(), laneHost = new(), altBox = new(), tripPage = new(), toastBox = new();
    readonly GlyphControl bGlyph = new() { Width = 140, Height = 140 }, bThen = new() { Width = 92, Height = 92 };
    readonly TextBlock bDist = Ui.Text("", 68, null, FontWeights.Bold), bWhat = Ui.Text("", 34), bMsg = Ui.Text("", 34);
    readonly TextBlock bJob1 = Ui.Text("", 30, null, FontWeights.Bold), bJob2 = Ui.Text("", 28);
    readonly Border bThenBox = new();
    readonly LanePanel lanePanel = new();
    readonly CompassControl compass = new() { Width = 169, Height = 169 };
    readonly TextBlock speedText = Ui.Text("0", 68, null, FontWeights.Bold), speedUnit = Ui.Text("km/h", 22, Ui.Dim);
    readonly Border limitBox = new();
    readonly TextBlock limitText = Ui.Text("--", 38, Brushes.Black, FontWeights.Bold);
    readonly TextBlock infoText = Ui.Text("No route", 30), statusText = Ui.Text("", 13, new SolidColorBrush(Color.FromArgb(220, 255, 200, 120)));
    readonly TextBlock altText = Ui.Text("-- m", 44, null, FontWeights.Bold);
    readonly TextBlock toastText = Ui.Text("", 15);
    readonly Button tripBtn = Ui.Button("Trip", 210, 62, Ui.Accent, 36), modeBtn = Ui.Button("Heavy", 210, 62, null, 36), voiceBtn = Ui.Button("Voice on", 210, 62, null, 36),
        pinBtn = Ui.Button("Pin on top", 210, 62, null, 36), recentreBtn = Ui.Button("◎  Recentre", 150, 48, new SolidColorBrush(Color.FromRgb(30, 120, 70)), 17);
    readonly StackPanel menu = new();
    readonly UIElement[] hideOnTrip;
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    readonly DispatcherTimer tripTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    bool tripOpen, laneHiddenByTrip;
    string place = "", bannerMsg = "";
    long lastFrameMs;

    public MainWindow(AppSettings settings)
    {
        cfg = settings;
        routeMode = RouteMode.ByKey(cfg.RouteMode);
        distScale = cfg.DistScale;
        Title = "ETS2 GPS";
        transparentWindow = cfg.Opacity < 0.999;
        if (transparentWindow) AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(16, 21, 28));
        Foreground = Brushes.White;
        MinWidth = 320; MinHeight = 200;
        Width = Math.Max(MinWidth, cfg.Width); Height = Math.Max(MinHeight, cfg.Height);
        if (!double.IsNaN(cfg.Left) && !double.IsNaN(cfg.Top) && Fits(cfg.Left, cfg.Top)) { WindowStartupLocation = WindowStartupLocation.Manual; Left = cfg.Left; Top = cfg.Top; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (cfg.Maximized) WindowState = WindowState.Maximized;
        Topmost = cfg.Topmost;
        Opacity = cfg.Opacity;
        if (float.TryParse(ArgAfter("--zoom"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var zoomArg)) mapView.UserZoom = Math.Clamp(zoomArg, 0.35f, 10f);   // test only
        voice.Enabled = cfg.VoiceOn;

        Content = BuildUi();
        hideOnTrip = new UIElement[] { banner, bottomBar, compass, altBox, zoomBox, statusText, laneHost };
        UpdateModeButton(); UpdateVoiceButton(); UpdatePinButton();
        SetBannerMessage(null);

        mapView.FreeChanged += f => recentreBtn.Visibility = f && !tripOpen ? Visibility.Visible : Visibility.Collapsed;
        mapView.PointRequested += OnPointRequested;
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); toastBox.Visibility = Visibility.Collapsed; };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); PersistWindow(); };
        tripTimer.Tick += (_, _) => { if (tripOpen) UpdateTrip(); };
        LocationChanged += (_, _) => { saveTimer.Stop(); saveTimer.Start(); };
        SizeChanged += (_, _) => { saveTimer.Stop(); saveTimer.Start(); };
        StateChanged += (_, _) => { saveTimer.Stop(); saveTimer.Start(); };
        PreviewKeyDown += OnKey;
        SizeChanged += (_, _) => PlaceCompass();
        Loaded += (_, _) => OnLoaded();
        ContentRendered += (_, _) => ApplyAlpha();   // WPF resets the layered style while the window is being shown
        Activated += (_, _) => ApplyAlpha();
        StateChanged += (_, _) => ApplyAlpha();
        Closed += (_, _) => { PersistWindow(); telemetry.Dispose(); server?.Dispose(); voice.Dispose(); };
    }

    static bool Fits(double l, double t) =>
        l > SystemParameters.VirtualScreenLeft - 200 && t > SystemParameters.VirtualScreenTop - 50 &&
        l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 200 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

    void PersistWindow()
    {
        if (WindowState == WindowState.Normal) { cfg.Left = Left; cfg.Top = Top; cfg.Width = Width; cfg.Height = Height; }
        cfg.Maximized = WindowState == WindowState.Maximized;
        cfg.Save();
    }

    StackPanel zoomBox = new();
    Grid root = new();

    UIElement BuildUi()
    {
        root = new Grid();
        root.Children.Add(mapView);

        // ---- green banner
        var bg = new Grid { Margin = new Thickness(12, 0, 12, 0) };
        bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(bGlyph, 0); bg.Children.Add(bGlyph);
        var main = new StackPanel { Margin = new Thickness(12, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        bWhat.MaxWidth = 900; bWhat.TextTrimming = TextTrimming.CharacterEllipsis;
        main.Children.Add(bDist); main.Children.Add(bWhat);
        Grid.SetColumn(main, 1); bg.Children.Add(main);
        bMsg.Margin = new Thickness(12, 0, 0, 0); bMsg.Visibility = Visibility.Collapsed;
        Grid.SetColumn(bMsg, 1); bg.Children.Add(bMsg);
        var job = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        bJob1.TextAlignment = TextAlignment.Right; bJob2.TextAlignment = TextAlignment.Right;
        bJob1.TextTrimming = TextTrimming.CharacterEllipsis; bJob2.TextTrimming = TextTrimming.CharacterEllipsis;
        job.Children.Add(bJob1); job.Children.Add(bJob2);
        Grid.SetColumn(job, 2); bg.Children.Add(job);
        var thenGrid = new Grid();
        thenGrid.Children.Add(bThen);
        thenGrid.Children.Add(new TextBlock { Text = "then", FontSize = 20, Foreground = Ui.Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, 0, 0) });
        bThenBox.Child = thenGrid; bThenBox.Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)); bThenBox.CornerRadius = new CornerRadius(10);
        bThenBox.Width = 112; bThenBox.Height = 112; bThenBox.Padding = new Thickness(8); bThenBox.Visibility = Visibility.Collapsed;
        bThenBox.VerticalAlignment = VerticalAlignment.Bottom; bThenBox.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetColumn(bThenBox, 3); bg.Children.Add(bThenBox);
        // window controls (there is no title bar): minimise, maximise, close
        var ctl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 8, 0) };
        foreach (var (glyph, act) in new (string, Action)[] { ("—", () => WindowState = WindowState.Minimized), ("☐", ToggleMaximize), ("✕", Close) })
        {
            var cb = Ui.Button(glyph, 52, 36, new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), 20); cb.Margin = new Thickness(3, 0, 0, 0);
            cb.Click += (_, _) => act();
            ctl.Children.Add(cb);
        }
        Panel.SetZIndex(ctl, 3);
        var bannerHost = new Grid(); bannerHost.Children.Add(bg); bannerHost.Children.Add(ctl);
        banner.Child = bannerHost;
        banner.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { ToggleMaximize(); return; }
            if (WindowState == WindowState.Normal) { try { DragMove(); } catch (InvalidOperationException) { /* released */ } }
        }; banner.Background = new SolidColorBrush(Color.FromArgb(240, 18, 92, 58)); banner.CornerRadius = new CornerRadius(14);
        banner.Height = 170; banner.Margin = new Thickness(8, 6, 8, 0); banner.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(banner);

        // ---- lane panel under the banner
        lanePanel.Height = LanePanel.PanelH; lanePanel.Width = 200;
        laneHost.Child = lanePanel; laneHost.HorizontalAlignment = HorizontalAlignment.Center; laneHost.VerticalAlignment = VerticalAlignment.Top;
        laneHost.Margin = new Thickness(0, 182, 0, 0); laneHost.Visibility = Visibility.Collapsed;
        root.Children.Add(laneHost);

        // ---- left button column
        menu.Orientation = Orientation.Vertical; menu.VerticalAlignment = VerticalAlignment.Center;
        void Add(Button b, Action click) { b.Margin = new Thickness(0, 2, 0, 2); b.Click += (_, _) => click(); menu.Children.Add(b); }
        Add(tripBtn, ToggleTrip);
        menu.Children.Add(new Border { Height = 8 });
        Add(Ui.Button("Search", 210, 62, null, 36), () => ShowSearch(false));
        Add(Ui.Button("Stops", 210, 62, null, 36), ShowStops);
        Add(Ui.Button("Trips", 210, 62, null, 36), ShowTrips);
        Add(modeBtn, CycleMode);
        Add(voiceBtn, ToggleVoice);
        Add(Ui.Button("POI", 210, 62, null, 36), ShowPoiPicker);
        Add(Ui.Button("Job", 210, 62, null, 36), RouteToJob);
        Add(Ui.Button("Stop", 210, 62, null, 36), CancelRoute);
        Add(pinBtn, TogglePin);
        Add(Ui.Button("⚙", 210, 62, null, 36), ShowSettings);
        var menuScroll = new ScrollViewer { Content = menu, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(10, 184, 0, 152) };
        Panel.SetZIndex(menuScroll, 5);
        root.Children.Add(menuScroll);
        menuHost = menuScroll;

        // ---- right side: compass, altitude, zoom, recentre, status
        compass.HorizontalAlignment = HorizontalAlignment.Right; compass.VerticalAlignment = VerticalAlignment.Top; compass.Margin = new Thickness(0, 230, 4, 0);
        root.Children.Add(compass);
        altText.TextAlignment = TextAlignment.Center;
        altBox.Child = altText; altBox.Background = new SolidColorBrush(Color.FromArgb(190, 14, 18, 24)); altBox.CornerRadius = new CornerRadius(10);
        altBox.Padding = new Thickness(16, 5, 16, 5); altBox.HorizontalAlignment = HorizontalAlignment.Right; altBox.VerticalAlignment = VerticalAlignment.Top;
        altBox.Margin = new Thickness(0, 230, 112, 0); altBox.Height = 169; altBox.Cursor = Cursors.Hand;
        altBox.MouseLeftButtonUp += (_, _) => ShowElevation();
        root.Children.Add(altBox);
        zoomBox = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        var zin = Ui.Button("+", 88, 88, null, 44); zin.Margin = new Thickness(0, 0, 0, 13); zin.Click += (_, _) => mapView.ZoomBy(1.3f);
        var zout = Ui.Button("−", 88, 88, null, 44); zout.Click += (_, _) => mapView.ZoomBy(1f / 1.3f);
        zoomBox.Children.Add(zin); zoomBox.Children.Add(zout);
        root.Children.Add(zoomBox);
        recentreBtn.HorizontalAlignment = HorizontalAlignment.Right; recentreBtn.VerticalAlignment = VerticalAlignment.Bottom; recentreBtn.Margin = new Thickness(0, 0, 24, 160);
        recentreBtn.Visibility = Visibility.Collapsed; recentreBtn.Click += (_, _) => mapView.Recentre();
        root.Children.Add(recentreBtn);
        statusText.HorizontalAlignment = HorizontalAlignment.Right; statusText.VerticalAlignment = VerticalAlignment.Top; statusText.Margin = new Thickness(0, 184, 14, 0);
        statusText.TextAlignment = TextAlignment.Right;
        root.Children.Add(statusText);

        // ---- bottom bar
        var bb = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        bb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        bb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        bb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        speedText.HorizontalAlignment = HorizontalAlignment.Center; speedUnit.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(speedText); sp.Children.Add(speedUnit);
        bb.Children.Add(sp);
        limitBox.Child = limitText; limitBox.Background = Brushes.White; limitBox.Width = 92; limitBox.Height = 92; limitBox.Visibility = Visibility.Hidden;
        limitText.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(limitBox, 1); bb.Children.Add(limitBox);
        infoText.TextWrapping = TextWrapping.Wrap; infoText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(infoText, 2); bb.Children.Add(infoText);
        bottomBar.Child = bb; bottomBar.Background = new SolidColorBrush(Color.FromArgb(230, 14, 18, 24)); bottomBar.VerticalAlignment = VerticalAlignment.Bottom;
        bottomBar.MinHeight = 140;
        root.Children.Add(bottomBar);

        // ---- trip screen, toast
        tripPage.Background = new SolidColorBrush(Color.FromRgb(16, 21, 28)); tripPage.Visibility = Visibility.Collapsed;
        root.Children.Add(tripPage);
        toastText.TextWrapping = TextWrapping.Wrap; toastText.MaxWidth = 700;
        toastBox.Child = toastText; toastBox.Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)); toastBox.CornerRadius = new CornerRadius(20);
        toastBox.Padding = new Thickness(18, 10, 18, 10); toastBox.HorizontalAlignment = HorizontalAlignment.Center; toastBox.VerticalAlignment = VerticalAlignment.Bottom;
        toastBox.Margin = new Thickness(0, 0, 0, 160); toastBox.Visibility = Visibility.Collapsed; toastBox.IsHitTestVisible = false;
        Panel.SetZIndex(toastBox, 10);
        root.Children.Add(toastBox);

        // the whole UI (map, banner, buttons, text) scales with the window: laid out as if the window were the
        // design size and then magnified, so it always keeps its proportions and fills the window
        scaleTransform = new ScaleTransform(1, 1);
        root.LayoutTransform = scaleTransform;
        var host = new Grid { ClipToBounds = true };
        host.Children.Add(root);
        host.SizeChanged += (_, e) => ApplyScale(e.NewSize);
        return host;
    }

    void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ---- borderless window: edge resizing and a maximise that respects the taskbar
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var src = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        src?.AddHook(WndProc);
        ApplyAlpha();
    }

    // A transparent window (AllowsTransparency) is what lets the game / desktop underneath show through; WPF's plain
    // Window.Opacity on an ordinary window only fades towards black. It costs some rendering speed, so it is only switched on
    // when the saved opacity is below 100 % (changing between 100 % and less takes effect after a restart).
    double alpha = 1.0;
    readonly bool transparentWindow;
    public new double Opacity
    {
        get => alpha;
        set { alpha = Math.Clamp(value, 0.3, 1.0); ApplyAlpha(); }
    }

    void ApplyAlpha()
    {
        if (transparentWindow) base.Opacity = alpha;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int flags; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct MINMAXINFO { public System.Drawing.Point ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x24, WM_NCHITTEST = 0x84;
        if (msg == WM_GETMINMAXINFO)
        {
            // maximise to the work area of the monitor the window is on (not over the taskbar)
            var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var mon = MonitorFromWindow(hwnd, 2);
            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
            {
                mmi.ptMaxPosition = new System.Drawing.Point(mi.rcWork.L - mi.rcMonitor.L, mi.rcWork.T - mi.rcMonitor.T);
                mmi.ptMaxSize = new System.Drawing.Point(mi.rcWork.R - mi.rcWork.L, mi.rcWork.B - mi.rcWork.T);
                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
            }
            handled = false;
        }
        else if (msg == WM_NCHITTEST && WindowState == WindowState.Normal)
        {
            // grab zone at the window edges for resizing
            int x = (short)(lParam.ToInt64() & 0xFFFF), y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            if (GetWindowRect(hwnd, out var r))
            {
                int b = (int)(8 * VisualTreeHelper.GetDpi(this).DpiScaleX);
                bool l = x < r.L + b, rt = x >= r.R - b, t = y < r.T + b, bt = y >= r.B - b;
                int hit = t && l ? 13 : t && rt ? 14 : bt && l ? 16 : bt && rt ? 17 : l ? 10 : rt ? 11 : t ? 12 : bt ? 15 : 0;
                if (hit != 0) { handled = true; return (IntPtr)hit; }
            }
        }
        return IntPtr.Zero;
    }

    const double DesignW = 1920, DesignH = 1080;
    ScaleTransform scaleTransform = new(1, 1);

    void ApplyScale(Size size)
    {
        if (size.Width < 1 || size.Height < 1) return;
        double s = Math.Clamp(Math.Min(size.Width / DesignW, size.Height / DesignH), 0.2, 1.0);
        if (Math.Abs(s - scaleTransform.ScaleX) < 0.001) return;
        scaleTransform.ScaleX = s; scaleTransform.ScaleY = s;
        Ui.Scale = s;
        PlaceCompass();
    }

    ScrollViewer? menuHost;

    /// <summary>Compass and altitude box sit on the right edge, halfway between the banner and the zoom buttons.</summary>
    void PlaceCompass()
    {
        double h = ActualHeight / Math.Max(0.01, scaleTransform.ScaleY), barBottom = 180, zoomTop = h / 2 - 98;
        double top = Math.Max(barBottom + 6, (barBottom + zoomTop) / 2 - 85);
        compass.Margin = new Thickness(0, top, 4, 0);
        altBox.Margin = new Thickness(0, top, 185, 0);
    }

    // test hooks: --goto <city> routes there as soon as the truck is known, --trip opens the trip screen
    string? autoGoto = ArgAfter("--goto");
    bool autoTrip = Environment.GetCommandLineArgs().Contains("--trip");
    static string? ArgAfter(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    void OnLoaded()
    {
        voice.Configure(cfg.VoiceRate, cfg.VoiceName);
        telemetry.Host = () => cfg.TelemetryHost;
        telemetry.State += s => Dispatcher.BeginInvoke(new Action(() => OnTruck(s)));
        telemetry.Status += t => Dispatcher.BeginInvoke(new Action(() => { if (truck == null || !truck.Connected) statusText.Text = t; }));
        telemetry.Start();
        if (cfg.ServerEnabled)
        {
            server = new BuiltInServer(cfg, voice);
            if (!server.Start()) { Toast(server.Problem ?? "The built-in server could not start"); server = new BuiltInServer(cfg, voice); }
        }
        CompositionTarget.Rendering += OnFrame;
        tripTimer.Start();
        _ = LoadMapAsync();
        if (cfg.OverlayAutoStart) { var om = OverlayLauncher.EnsureRunning(cfg); if (om != null) Toast(om); }
    }

    void OnFrame(object? sender, EventArgs e)
    {
        long now = clock.ElapsedMilliseconds;
        if (now - lastFrameMs < 25) return; // ~40 fps is plenty
        lastFrameMs = now;
        if (motion.HaveFix)
        {
            var (x, z) = motion.Predicted(now);
            mapView.TruckX = x; mapView.TruckZ = z; mapView.Bearing = motion.Bearing;
        }
        if (!tripOpen) { mapView.InvalidateVisual(); compass.HeadingRad = mapView.ViewBearing; compass.Valid = mapView.HasFix; compass.InvalidateVisual(); }
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        switch (e.Key)
        {
            case Key.OemPlus: case Key.Add: mapView.ZoomBy(1.3f); break;
            case Key.OemMinus: case Key.Subtract: mapView.ZoomBy(1f / 1.3f); break;
            case Key.Escape: if (tripOpen) ToggleTrip(); else mapView.Recentre(); break;
            case Key.T: ToggleTrip(); break;
            case Key.P: TogglePin(); break;
            case Key.F11: WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; break;
            default: return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------------- small UI --

    public void Toast(string text)
    {
        toastText.Text = text; toastBox.Visibility = Visibility.Visible;
        toastTimer.Stop(); toastTimer.Start();
    }

    void UpdateModeButton() => modeBtn.Content = routeMode.Short;
    void UpdateVoiceButton() => voiceBtn.Content = voice.Enabled ? "Voice on" : "Voice off";
    void UpdatePinButton() { pinBtn.Content = Topmost ? "On top ✓" : "Pin on top"; pinBtn.Background = Topmost ? Ui.Accent : Ui.Btn; }

    /// <summary>Keeps the window above other apps (also the dialogs it opens).</summary>
    void TogglePin()
    {
        Topmost = !Topmost;
        cfg.Topmost = Topmost; cfg.Save();
        UpdatePinButton();
        Toast(Topmost ? "Staying on top of other apps" : "No longer on top");
    }

    void ToggleVoice()
    {
        voice.Enabled = !voice.Enabled; cfg.VoiceOn = voice.Enabled; cfg.Save();
        UpdateVoiceButton();
        if (voice.Enabled) voice.Say("Voice on");
    }

    void CycleMode()
    {
        var all = RouteMode.All;
        SetRouteMode(all[(Array.IndexOf(all, routeMode) + 1) % all.Length]);
        Toast("Route mode: " + routeMode.Label); voice.Say(routeMode.Label + " routing");
    }

    void SetRouteMode(RouteMode m, bool reroute = true)
    {
        routeMode = m; cfg.RouteMode = m.Key; cfg.Save();
        if (router != null) router.Mode = m;
        UpdateModeButton();
        if (reroute) RerouteForModeChange();
    }

    void SetBannerMessage(string? t)
    {
        bannerMsg = t ?? "";
        bMsg.Text = bannerMsg; bMsg.Visibility = t != null ? Visibility.Visible : Visibility.Collapsed;
        bWhat.Visibility = t != null ? Visibility.Collapsed : Visibility.Visible;
        if (t != null) bDist.Visibility = Visibility.Collapsed;
        if (t != null) { bGlyph.Turn = null; bGlyph.InvalidateVisual(); bThenBox.Visibility = Visibility.Collapsed; }
    }

    void SetManeuver(Maneuver next, string dist, string what, Maneuver? then)
    {
        if (bannerMsg.Length > 0) SetBannerMessage(null);
        bDist.Text = dist; bDist.Visibility = Visibility.Visible; bWhat.Text = what;
        bGlyph.Turn = next.Turn; bGlyph.ExitNo = next.ExitNo; bGlyph.Angle = next.AngleDeg; bGlyph.InvalidateVisual();
        var t = then != null && then.Turn != Turn.Arrive ? then : null;
        bThenBox.Visibility = t != null ? Visibility.Visible : Visibility.Collapsed;
        if (t != null) { bThen.Turn = t.Turn; bThen.ExitNo = t.ExitNo; bThen.Angle = t.AngleDeg; bThen.InvalidateVisual(); }
    }

    void ClearManeuver()
    {
        bDist.Text = ""; bDist.Visibility = Visibility.Collapsed; bWhat.Text = "No route - click Search or Job, or right-click the map";
        bGlyph.Turn = null; bGlyph.InvalidateVisual(); bThenBox.Visibility = Visibility.Collapsed;
    }

    void UpdateBannerJob()
    {
        var j = truck?.Job;
        if (j == null) { bJob1.Text = ""; bJob2.Text = ""; return; }
        bJob1.Text = PlaceName(j.SrcCompany, j.SrcCity) + "  →  " + PlaceName(j.DestCompany, j.DestCity);
        var parts = new List<string>();
        if (j.Cargo.Length > 0) parts.Add(j.Cargo + (j.MassKg > 0 ? " " + (j.MassKg / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " t" : ""));
        if (j.Income > 0) parts.Add("€" + j.Income.ToString("N0", CultureInfo.InvariantCulture));
        float etaMin = nav?.Active == true && routeEtaGameMin >= 0 ? routeEtaGameMin : truck!.NavEtaMin;
        bool late = false;
        if (j.DueMin >= 0)
        {
            string due = "Due " + FormatDuration(j.DueMin);
            if (etaMin >= 0) { due += " (ETA " + FormatDuration(etaMin) + ")"; late = etaMin > j.DueMin; }
            parts.Add(due);
        }
        bJob2.Text = string.Join("  ·  ", parts);
        bJob2.Foreground = late ? new SolidColorBrush(Color.FromRgb(255, 160, 150)) : Brushes.White;
    }

    void SetLaneUi(List<Lane>? lanes, string caption = "")
    {
        if (tripOpen || lanes == null)
        {
            laneHost.Visibility = Visibility.Collapsed;
            PushLanes(null, "");
            return;
        }
        lanePanel.Lanes = lanes; lanePanel.Caption = caption;
        lanePanel.Width = LanePanel.DesignWidth(lanes.Count, caption);
        laneHost.Visibility = Visibility.Visible;
        lanePanel.InvalidateVisual();
        PushLanes(lanes, caption);
    }

    // ---- toggle the trip screen
    void ToggleTrip()
    {
        tripOpen = !tripOpen;
        tripPage.Visibility = tripOpen ? Visibility.Visible : Visibility.Collapsed;
        foreach (var el in hideOnTrip) el.Visibility = tripOpen ? Visibility.Collapsed : (el == statusText ? Visibility.Visible : Visibility.Visible);
        if (!tripOpen && lanePanel.Lanes == null) laneHost.Visibility = Visibility.Collapsed;
        recentreBtn.Visibility = !tripOpen && mapView.Free ? Visibility.Visible : Visibility.Collapsed;
        tripBtn.Content = tripOpen ? "Map" : "Trip";
        if (tripOpen) { SetLaneUi(null); UpdateTrip(); }
    }

    static string PlaceName(string company, string city) => company.Length > 0 ? company + ", " + city : city;

    static string FormatDuration(float min)
    {
        int m = (int)Math.Round(min);
        if (m >= 1440) return $"{m / 1440}d {m % 1440 / 60}h {m % 60:00}m";
        if (m >= 60) return $"{m / 60}h {m % 60:00}m";
        return m + "m";
    }

    static string ClockText(float absMin)
    {
        if (absMin < 0) return "-";
        int day = (int)(absMin / 1440f); float inDay = absMin - day * 1440f;
        string[] names = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        return $"{names[day % 7]} {(int)(inDay / 60f):00}:{(int)(inDay % 60f):00}";
    }

    string EtaClock(float gameMin)
    {
        if (truck == null || truck.GameTimeMin < 0) return "";
        float abs = truck.GameTimeMin + gameMin; int day = (int)(abs / 1440f), today = (int)(truck.GameTimeMin / 1440f);
        return ClockText(abs) + (day > today + 6 ? $" (+{day - today}d)" : "");
    }
}
