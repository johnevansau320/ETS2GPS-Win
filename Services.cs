using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ets2Gps;

public sealed class StopPoint { public float X { get; set; } public float Z { get; set; } public string Name { get; set; } = ""; }

public sealed class SavedTrip
{
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "HEAVY";
    public StopPoint? Start { get; set; }          // where a planned trip begins (null = wherever the truck is)
    public StopPoint Dest { get; set; } = new();
    public List<StopPoint> Stops { get; set; } = new();
    public float LengthM { get; set; }
    public bool AvoidFerries { get; set; }
}

/// <summary>Everything remembered between runs (%APPDATA%\ETS2-GPS\windows_app.json).</summary>
public sealed class AppSettings
{
    public string TelemetryHost { get; set; } = "127.0.0.1";
    public bool ServerEnabled { get; set; } = true;        // built-in web server for the tablet, browsers and the lane overlay
    public int ServerPort { get; set; } = 31378;
    public string RouteMode { get; set; } = "HEAVY";
    public bool AvoidFerries { get; set; } = false;        // route planning: never use ferries / car trains
    public float DistScale { get; set; } = 19f;
    /// <summary>true: distances in real metres (map units); false: game (Sim) distances, map metres x DistScale.</summary>
    public bool RealDistances { get; set; } = false;
    /// <summary>While cruise control is on, keep it on the current speed limit (taps the game's cruise faster / slower keys).</summary>
    public bool CruiseAutoLimit { get; set; } = true;
    /// <summary>Show the lane guidance panel on the map (and in the lane overlay window).</summary>
    public bool ShowLanes { get; set; } = true;
    public bool FollowGameGps { get; set; } = true;   // offer to route to the game's GPS destination when there is no job
    public bool AutoQuickSave { get; set; } = true;   // press the game's quick-save key when its GPS gets a new destination, so it is read straight away
    public bool VoiceOn { get; set; } = true;
    /// <summary>Listen on the microphone for spoken commands ("GPS, nearest fuel station"). Off by default; recognised on this PC, nothing is recorded or sent.</summary>
    public bool VoiceControl { get; set; } = false;
    public string VoiceWake { get; set; } = "GPS";
    /// <summary>Look for a newer release on GitHub about once a day (nothing is downloaded; the notice opens the release page).</summary>
    public bool CheckUpdates { get; set; } = true;
    public long LastUpdateCheckUtc { get; set; }
    public string SkippedVersion { get; set; } = "";
    public int VoiceRate { get; set; } = 0;                // -10 .. 10
    public string VoiceName { get; set; } = "";
    public bool Topmost { get; set; } = false;             // keep the window above other apps
    public double Opacity { get; set; } = 1.0;
    public bool OverlayAutoStart { get; set; } = true;     // start the lane overlay window with the app when it is not already running
    public string OverlayPath { get; set; } = "";           // Ets2LaneOverlay.exe (empty = look in the usual places)
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 760;
    public bool Maximized { get; set; } = false;
    public List<StopPoint> Avoid { get; set; } = new();
    public List<SavedTrip> Trips { get; set; } = new();

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETS2-GPS");
    public static string? PathOverride;   // --config <file> (used for testing)
    public static string FilePath => PathOverride ?? Path.Combine(Dir, "windows_app.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings(); }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* read-only profile: not fatal */ }
    }
}

/// <summary>Spoken guidance through the PC's default audio device (headset) with Windows speech (SAPI).</summary>
public sealed class Voice : IDisposable
{
    SpeechSynthesizer? tts;
    public bool Enabled { get; set; } = true;
    public string? Problem { get; private set; }

    int pending; long endedAt;
    /// <summary>True while a prompt is being spoken (and for a moment after): voice control must not listen to the app's own voice.</summary>
    public bool Busy => Volatile.Read(ref pending) > 0 || Environment.TickCount64 - Interlocked.Read(ref endedAt) < 700;

    public IEnumerable<string> Voices()
    {
        try { using var s = new SpeechSynthesizer(); return s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    public void Configure(int rate, string voiceName)
    {
        try
        {
            if (tts == null)
            {
                tts = new SpeechSynthesizer();
                tts.SpeakCompleted += (_, _) => { if (Interlocked.Decrement(ref pending) < 0) Interlocked.Exchange(ref pending, 0); Interlocked.Exchange(ref endedAt, Environment.TickCount64); };
            }
            tts.SetOutputToDefaultAudioDevice();
            tts.Rate = Math.Clamp(rate, -10, 10);
            if (!string.IsNullOrEmpty(voiceName)) { try { tts.SelectVoice(voiceName); } catch { /* voice not installed */ } }
            Problem = null;
        }
        catch (Exception e) { Problem = "Speech is not available: " + e.Message; }
    }

    /// <summary>Speaks even when voice guidance is switched off here (prompts the tablet sends to this PC).</summary>
    public void SayAlways(string text) => Speak(text);

    /// <summary>Speaks the text; newer prompts replace anything still being spoken (stale directions are worse than none).</summary>
    public void Say(string text)
    {
        if (!Enabled) return;
        Speak(text);
    }

    void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            if (tts == null) Configure(0, "");
            if (tts == null) return;
            tts.SpeakAsyncCancelAll();
            Interlocked.Increment(ref pending);
            tts.SpeakAsync(text);
        }
        catch (Exception e) { Problem = "Speech failed: " + e.Message; }
    }

    public void Dispose() { tts?.Dispose(); }
}
