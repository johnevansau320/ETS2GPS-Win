using System.Windows;

namespace Ets2Gps;

public static class Program
{
    // Ets2Gps.exe [--topmost] [--config file.json]   (--topmost forces "stay on top" for this run)
    [STAThread]
    public static int Main(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--config") AppSettings.PathOverride = args[i + 1];
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--build-map") return MapBuilder.MapBuilderCli.Run(args[i + 1]);   // test: build a map file and exit
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--check-map")   // test: load a map file and write a summary next to it
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string res;
                try
                {
                    var mm = MapData.Load(args[i + 1]);
                    int lifted = 0; foreach (var b in mm.PtLift) if (b > 0) lifted++;
                    res = $"v{mm.Version} edges {mm.EdgeCount} lifted points {lifted} of {mm.PtX.Length} liftedEdges {mm.EHasLift.Count(x => x)} in {sw.ElapsedMilliseconds} ms; borders " + string.Join(" ", mm.Borders.Where(x => x.Kind == 1 && (x.Cc == "de" || x.Cc == "nl")).Take(8).Select(x => $"{x.Cc}@{x.X:0},{x.Z:0}"));
                }
                catch (Exception e) { res = "FAILED " + e; }
                File.WriteAllText(args[i + 1] + ".check.txt", res);
                return 0;
            }
        var cfg = AppSettings.Load();
        if (args.Contains("--topmost")) cfg.Topmost = true;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.ToString(), "ETS2 GPS - unexpected error");
            e.Handled = true;
        };
        return app.Run(new MainWindow(cfg));
    }
}
