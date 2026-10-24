using System.Diagnostics;

namespace Ets2Gps;

/// <summary>Finds and starts the stand-alone lane overlay window (Ets2LaneOverlay.exe) when it is not already running.</summary>
public static class OverlayLauncher
{
    const string ProcessName = "Ets2LaneOverlay";

    public static bool IsRunning() => Process.GetProcessesByName(ProcessName).Length > 0;

    /// <summary>The configured path, else the usual places: its install folder, next to this app, the project's own build folder.</summary>
    public static string? Find(AppSettings cfg)
    {
        var tries = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.OverlayPath)) tries.Add(cfg.OverlayPath.Trim().Trim('"'));
        tries.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ETS2 Lane Overlay", "Ets2LaneOverlay.exe"));
        string here = AppContext.BaseDirectory;
        tries.Add(Path.Combine(here, "Ets2LaneOverlay.exe"));
        tries.Add(Path.GetFullPath(Path.Combine(here, "..", "..", "overlay-dotnet", "publish", "Ets2LaneOverlay.exe")));
        tries.Add(Path.GetFullPath(Path.Combine(here, "..", "overlay-dotnet", "publish", "Ets2LaneOverlay.exe")));
        foreach (var t in tries) { try { if (File.Exists(t) && LooksLikeProgram(t)) return t; } catch { /* bad path */ } }
        return null;
    }

    /// <summary>A program file starts with "MZ"; a half-written or damaged copy does not (so a good copy elsewhere can be used instead).</summary>
    static bool LooksLikeProgram(string path)
    {
        using var f = File.OpenRead(path);
        return f.Length > 1_000_000 && f.ReadByte() == 'M' && f.ReadByte() == 'Z';
    }

    /// <summary>Starts the overlay unless it is running. Returns a short message for the user, or null when it was already running.</summary>
    public static string? EnsureRunning(AppSettings cfg)
    {
        if (IsRunning()) return null;
        var exe = Find(cfg);
        if (exe == null) return "Lane overlay not found or damaged - reinstall the app, or set its path in Settings";
        try
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
            return "Lane overlay started";
        }
        catch (Exception e) { return "Could not start the lane overlay: " + e.Message; }
    }
}
