namespace Ets2Gps.MapBuilder;

/// <summary>Command line test: Ets2Gps.exe --build-map out.bin   (writes out.bin and out.bin.log)</summary>
public static class MapBuilderCli
{
    public static int Run(string outPath)
    {
        using var logFile = new StreamWriter(outPath + ".log") { AutoFlush = true };
        void Log(string s) => logFile.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + s);
        try
        {
            string gd = GameFiles.FindGameDir();
            Log("game: " + gd);
            var (fs, _) = GameFiles.MountEverything(gd, Log);
            BuildMap.Build(fs, outPath, Log, null, outPath + ".assets", outPath + ".drops.json");
            return 0;
        }
        catch (Exception e) { Log("FAILED: " + e); return 1; }
    }
}
