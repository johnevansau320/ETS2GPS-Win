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
