using System.Globalization;
using System.Speech.Recognition;

namespace Ets2Gps;

/// <summary>
/// Listens on the PC's default microphone for spoken commands ("GPS, nearest fuel station") with the speech recognizer that comes with Windows.
/// Everything is recognised on this PC: no audio is recorded, stored or sent anywhere. Every command starts with the wake word, so ordinary speech and the
/// game's sound do not trigger anything, and recognition pauses while the app itself is speaking.
/// </summary>
public sealed class VoiceControl : IDisposable
{
    /// <summary>Recognitions below this confidence are ignored.</summary>
    public const float MinConfidence = 0.72f;
    /// <summary>The wake word itself must be heard at least this well (a recognizer-invented one scores 0.00).</summary>
    public const float MinWakeConfidence = 0.55f;

    SpeechRecognitionEngine? engine;
    readonly Func<bool> muted;

    /// <summary>Raised on a background thread: command key (for example "poi:1", "job", "far"), the words heard, the confidence.</summary>
    public event Action<string, string, float>? Command;

    public string? Problem { get; private set; }
    public bool Active => engine != null;
    public string Wake { get; private set; } = "GPS";

    public VoiceControl(Func<bool> muted) { this.muted = muted; }

    /// <summary>Points of interest that can be asked for: key, the words that name it.</summary>
    public static readonly (string Key, string[] Words)[] PoiWords =
    {
        ("poi:1", new[] { "fuel station", "petrol station", "gas station", "filling station", "fuel" }),
        ("poi:3", new[] { "rest area", "rest stop", "parking area", "parking", "place to rest" }),
        ("poi:2", new[] { "service centre", "service center", "service station" }),
        ("poi:4", new[] { "weigh station", "weighing station", "weight station", "weighbridge" }),
        ("poi:6", new[] { "garage" }),
        ("poi:5", new[] { "truck dealer", "dealer", "dealership" }),
        ("poi:9", new[] { "ferry", "ferry terminal" }),
    };

    /// <summary>Everything that can be said after the wake word (key, phrases), apart from the "find the nearest ..." family.</summary>
    public static readonly (string Key, string[] Phrases)[] Commands =
    {
        ("job", new[] { "take the job", "take this job", "start the job", "navigate to the job", "route to the job", "go to the job", "take me to the delivery",
            "accept the job", "yes navigate", "navigate", "yes please" }),
        ("gamegps", new[] { "follow the game route", "follow the game navigation", "go to the game destination", "use the game destination", "use the game route" }),
        ("dismiss", new[] { "not now", "no thanks", "no thank you", "ignore the job", "skip the job", "don't navigate", "cancel", "close", "close the dialog" }),
        ("jobinfo", new[] { "show job info", "show the job info", "show job information", "open job info", "open the job info", "job info" }),
        ("map", new[] { "show the map", "back to the map", "return to the map", "show navigation", "back to navigation", "show the navigation screen", "close job info" }),
        ("cancel", new[] { "cancel the route", "cancel route", "cancel navigation", "stop navigation", "clear the route" }),
        ("resume", new[] { "back to my route", "back to the route", "resume my route", "resume the route", "return to my route" }),
        ("far", new[] { "how far is it", "how far to go", "how far is the destination", "distance remaining" }),
        ("eta", new[] { "when do I arrive", "what time do I arrive", "what is the estimated arrival time", "estimated arrival time" }),
        ("next", new[] { "what is next", "what's next", "what is the next turn", "repeat that", "repeat the instruction", "say that again" }),
        ("limit", new[] { "what is the speed limit", "what's the speed limit", "speed limit" }),
        ("fuel", new[] { "how much fuel do I have", "how much fuel is left", "what is my fuel level", "fuel level", "what is my range" }),
        ("lanes:on", new[] { "turn lane guidance on", "lane guidance on", "show lane guidance" }),
        ("lanes:off", new[] { "turn lane guidance off", "lane guidance off", "hide lane guidance" }),
        ("cruise:on", new[] { "turn cruise assist on", "cruise assist on", "automatic cruise on" }),
        ("cruise:off", new[] { "turn cruise assist off", "cruise assist off", "automatic cruise off" }),
        ("voice:on", new[] { "turn voice guidance on", "voice guidance on", "unmute voice guidance" }),
        ("voice:off", new[] { "turn voice guidance off", "voice guidance off", "mute voice guidance" }),
        ("help", new[] { "help", "what can I say", "voice commands", "list the voice commands" }),
    };

    /// <summary>What the wake word may sound like to the recognizer ("GPS" is also tried spelled out).</summary>
    static string[] WakeVariants(string wake)
    {
        wake = wake.Trim();
        var list = new List<string> { wake, "hey " + wake, "ok " + wake };
        if (wake.Length is >= 2 and <= 4 && wake.All(char.IsLetter)) { string spelled = string.Join(" ", wake.ToUpperInvariant().ToCharArray()); list.Add(spelled); list.Add("hey " + spelled); }
        return list.Distinct().ToArray();
    }

    public static Grammar BuildGrammar(CultureInfo culture, string wake)
    {
        GrammarBuilder G(params string[] words) { var gb = new GrammarBuilder(new Choices(words)) { Culture = culture }; return gb; }
        var all = new Choices();
        var lead = new GrammarBuilder(G("where is", "find", "find me", "take me to", "navigate to", "go to", "show me"), 0, 1) { Culture = culture };
        var the = new GrammarBuilder(G("the"), 0, 1) { Culture = culture };
        foreach (var (key, words) in PoiWords)
        {
            var gb = new GrammarBuilder { Culture = culture };
            gb.Append(lead); gb.Append(the); gb.Append(G("nearest", "closest")); gb.Append(G(words));
            all.Add(new SemanticResultValue(gb, key));
        }
        foreach (var (key, phrases) in Commands) all.Add(new SemanticResultValue(G(phrases), key));
        var root = new GrammarBuilder { Culture = culture };
        root.Append(G(WakeVariants(wake)));
        root.Append(new SemanticResultKey("cmd", all));
        return new Grammar(root) { Name = "ets2gps-commands" };
    }

    public static RecognizerInfo? FindRecognizer() =>
        SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Culture.Name.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
        ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault();

    /// <summary>Starts listening. Returns false (with <see cref="Problem"/> set) when there is no recognizer or microphone.</summary>
    public bool Start(string wake)
    {
        Stop();
        try
        {
            var rec = FindRecognizer();
            if (rec == null) { Problem = "No speech recognizer is installed in Windows (Settings > Time & language > Speech)."; return false; }
            Wake = string.IsNullOrWhiteSpace(wake) ? "GPS" : wake.Trim();
            var e = new SpeechRecognitionEngine(rec);
            e.LoadGrammar(BuildGrammar(rec.Culture, Wake));
            e.SetInputToDefaultAudioDevice();
            e.SpeechRecognized += OnRecognized;
            e.RecognizeAsync(RecognizeMode.Multiple);
            engine = e; Problem = null;
            return true;
        }
        catch (Exception ex) { Problem = "Voice control could not start: " + ex.Message; Stop(); return false; }
    }

    void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        try
        {
            if (muted() || e.Result.Confidence < MinConfidence || e.Result.Words.Count == 0 || e.Result.Words[0].Confidence < MinWakeConfidence) return;
            if (e.Result.Semantics.ContainsKey("cmd") && e.Result.Semantics["cmd"].Value is string key) Command?.Invoke(key, e.Result.Text, e.Result.Confidence);
        }
        catch { /* a bad result must never stop the listener */ }
    }

    public void Stop()
    {
        var e = engine; engine = null;
        if (e == null) return;
        try { e.SpeechRecognized -= OnRecognized; e.RecognizeAsyncCancel(); } catch { /* already stopped */ }
        try { e.Dispose(); } catch { /* ignore */ }
    }

    public void Dispose() => Stop();
}
