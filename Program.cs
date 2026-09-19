using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenRGB.NET;

namespace OpenRgbChromaBridge;

internal sealed class BridgeConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6742;
    public string SourceDevice { get; set; } = "Chroma Bridge";
    public int PollMilliseconds { get; set; } = 500;
    public bool EnableDeviceBreathing { get; set; } = true;
    public string ProfileName { get; set; } = "White to Red - Speed 30";
    public bool FollowSourceColor { get; set; } = false;
    public string BreathingColor { get; set; } = "#FFFFFF";
    public string BreathingSecondColor { get; set; } = "#FF0000";
    public int? BreathingSpeed { get; set; } = 30;
    public int BreathingPeriodMilliseconds { get; set; } = 4000;
    public int BreathingFrameMilliseconds { get; set; } = 100;
    public double BreathingMinimumBrightness { get; set; } = 0.06;
    public bool MaintainBrightness { get; set; } = true;
    public double EndpointHoldPercent { get; set; } = 10.0;
    public int HeadsetUpdateMilliseconds { get; set; } = 300;
    public int HeadsetPhaseLeadMilliseconds { get; set; } = 700;
    public double HeadsetRedThreshold { get; set; } = 0.65;
    public int ProfileCheckMilliseconds { get; set; } = 1000;
    public bool EnableLogitechLighting { get; set; } = true;
}

internal enum HeadsetEffect
{
    None = 0,
    Static = 1,
    Breathing = 2,
    SpectrumCycling = 3
}

internal readonly record struct DesiredEffect(HeadsetEffect Effect, byte R, byte G, byte B)
{
    public override string ToString() => Effect == HeadsetEffect.None
        ? "Off"
        : $"{Effect} #{R:X2}{G:X2}{B:X2}";
}

internal readonly record struct ClientColorCommand(Color Color, bool ResumeSavedProfile);
internal readonly record struct SourceModeState(
    string Name, Color FirstColor, Color SecondColor, uint Speed, double Brightness, OpenRGB.NET.Direction Direction);

internal static class ChromaNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AppInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Title;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string Description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AuthorName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AuthorContact;
        public uint SupportedDevice;
        public uint Category;
    }

    [DllImport("CChromaEditorLibrary64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int PluginCoreInitSDK(ref AppInfo appInfo);

    [DllImport("CChromaEditorLibrary64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int PluginCoreInit();

    [DllImport("CChromaEditorLibrary64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int PluginCoreCreateHeadsetEffect(int effect, IntPtr param, out Guid effectId);

    [DllImport("CChromaEditorLibrary64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int PluginCoreSetEffect(Guid effectId);

    [DllImport("CChromaEditorLibrary64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int PluginCoreDeleteEffect(Guid effectId);

    [DllImport("CChromaEditorLibrary64.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int PluginCoreUnInit();

    private static Guid? _activeEffect;

    public static void Initialize()
    {
        var info = new AppInfo
        {
            Title = "OpenRGB Chroma Bridge",
            Description = "Mirrors an OpenRGB color profile to legacy Razer Chroma headsets.",
            AuthorName = "OpenRGB Chroma Bridge",
            AuthorContact = "local",
            SupportedDevice = 0x04,
            Category = 1
        };

        int result = PluginCoreInitSDK(ref info);
        if (result != 0)
            result = PluginCoreInit();
        if (result != 0)
            throw new InvalidOperationException($"Razer Chroma initialization failed ({result}).");
    }

    public static void Apply(DesiredEffect desired)
    {
        IntPtr param = IntPtr.Zero;
        try
        {
            if (desired.Effect is HeadsetEffect.Static or HeadsetEffect.Breathing)
            {
                param = Marshal.AllocHGlobal(sizeof(int));
                int colorRef = desired.R | (desired.G << 8) | (desired.B << 16);
                Marshal.WriteInt32(param, colorRef);
            }

            int create = PluginCoreCreateHeadsetEffect((int)desired.Effect, param, out Guid next);
            if (create != 0)
                throw new InvalidOperationException($"Razer rejected the headset effect ({create}).");

            int set = PluginCoreSetEffect(next);
            if (set != 0)
            {
                PluginCoreDeleteEffect(next);
                throw new InvalidOperationException($"Razer rejected activation of the headset effect ({set}).");
            }

            Guid? previous = _activeEffect;
            _activeEffect = next;
            if (previous.HasValue)
                PluginCoreDeleteEffect(previous.Value);
        }
        finally
        {
            if (param != IntPtr.Zero)
                Marshal.FreeHGlobal(param);
        }
    }

    public static void Shutdown()
    {
        if (_activeEffect.HasValue)
            PluginCoreDeleteEffect(_activeEffect.Value);
        _activeEffect = null;
        PluginCoreUnInit();
    }
}

internal static class Program
{
    private static readonly string BaseDirectory = AppContext.BaseDirectory;
    private static readonly string ConfigPath = Path.Combine(BaseDirectory, "bridge-config.json");
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenRGB Chroma Bridge");
    private static readonly string LogPath = Path.Combine(LogDirectory, "bridge.log");
    private static bool _console;
    private static bool _traceFirstAnimationFrame = true;
    private static readonly object LogLock = new();

    private static void Log(string message)
    {
        Directory.CreateDirectory(LogDirectory);
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        lock (LogLock) File.AppendAllText(LogPath, line + Environment.NewLine);
        if (_console)
            Console.WriteLine(line);
    }

    private static BridgeConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaults = new BridgeConfig();
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true }));
            return defaults;
        }

        return JsonSerializer.Deserialize<BridgeConfig>(File.ReadAllText(ConfigPath)) ?? new BridgeConfig();
    }

    private static DesiredEffect ReadDesiredEffect(OpenRgbClient client, BridgeConfig config)
    {
        Device? source = null;
        Device? fallback = null;
        int count = client.GetControllerCount();
        for (int index = 0; index < count; index++)
        {
            Device device = client.GetControllerData(index);
            if (device.Name.Equals(config.SourceDevice, StringComparison.OrdinalIgnoreCase))
            {
                source = device;
                break;
            }

            if (fallback is null &&
                !string.Equals(device.Vendor, "Razer", StringComparison.OrdinalIgnoreCase) &&
                device.Colors.Length > 0)
                fallback = device;
        }

        source ??= fallback;
        if (source is null)
            throw new InvalidOperationException($"OpenRGB source device '{config.SourceDevice}' was not found.");

        string modeName = source.ActiveMode.Name ?? string.Empty;
        if (modeName.Contains("off", StringComparison.OrdinalIgnoreCase))
            return new DesiredEffect(HeadsetEffect.None, 0, 0, 0);
        if (modeName.Contains("spectrum", StringComparison.OrdinalIgnoreCase))
            return new DesiredEffect(HeadsetEffect.SpectrumCycling, 0, 0, 0);

        var colors = source.ActiveMode.Colors.Length > 0 ? source.ActiveMode.Colors : source.Colors;
        var visible = colors.Where(c => c.R != 0 || c.G != 0 || c.B != 0).ToArray();
        if (visible.Length == 0)
            visible = source.Colors.Where(c => c.R != 0 || c.G != 0 || c.B != 0).ToArray();
        if (visible.Length == 0)
            return new DesiredEffect(HeadsetEffect.None, 0, 0, 0);

        byte r = (byte)Math.Clamp((int)visible.Average(c => c.R), 0, 255);
        byte g = (byte)Math.Clamp((int)visible.Average(c => c.G), 0, 255);
        byte b = (byte)Math.Clamp((int)visible.Average(c => c.B), 0, 255);
        if (source.ActiveMode.SupportsBrightness)
        {
            uint max = Math.Max(1, source.ActiveMode.BrightnessMax);
            double scale = Math.Clamp(source.ActiveMode.Brightness / (double)max, 0, 1);
            r = (byte)Math.Round(r * scale);
            g = (byte)Math.Round(g * scale);
            b = (byte)Math.Round(b * scale);
        }

        HeadsetEffect effect = modeName.Contains("breath", StringComparison.OrdinalIgnoreCase)
            ? HeadsetEffect.Breathing
            : HeadsetEffect.Static;
        return new DesiredEffect(effect, r, g, b);
    }

    private static SourceModeState ReadSourceMode(OpenRgbClient client, BridgeConfig config)
    {
        int count = client.GetControllerCount();
        for (int index = 0; index < count; index++)
        {
            Device device = client.GetControllerData(index);
            if (!device.Name.Equals(config.SourceDevice, StringComparison.OrdinalIgnoreCase))
                continue;
            return SourceModeFromDevice(device, config);
        }

        throw new InvalidOperationException($"OpenRGB source device '{config.SourceDevice}' was not found.");
    }

    private static int FindSourceId(OpenRgbClient client, BridgeConfig config)
    {
        int count = client.GetControllerCount();
        for (int index = 0; index < count; index++)
            if (client.GetControllerData(index).Name.Equals(config.SourceDevice, StringComparison.OrdinalIgnoreCase))
                return index;
        throw new InvalidOperationException($"OpenRGB source device '{config.SourceDevice}' was not found.");
    }

    private static SourceModeState SourceModeFromDevice(Device device, BridgeConfig config)
    {
        Mode mode = device.ActiveMode;
        Color fallback = device.Colors.Length > 0 ? device.Colors[0] : ParseColor(config.BreathingColor);
        Color first = mode.Colors.Length > 0 ? mode.Colors[0] : fallback;
        Color second = mode.Colors.Length > 1 ? mode.Colors[1] : ParseColor(config.BreathingSecondColor);
        double brightness = mode.SupportsBrightness
            ? Math.Clamp(mode.Brightness / (double)Math.Max(1u, mode.BrightnessMax), 0.0, 1.0)
            : 1.0;
        return new SourceModeState(mode.Name ?? "Direct", first, second, mode.Speed, brightness, mode.Direction);
    }

    private static SourceModeState InspectSourceMode(BridgeConfig config)
    {
        using var watcher = new OpenRgbClient(config.Host, config.Port,
            "OpenRGB Chroma Bridge Mode Check", false, 1000);
        watcher.Connect();
        return ReadSourceMode(watcher, config);
    }

    private static Color ParseColor(string value)
    {
        string hex = value.Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int rgb))
            throw new InvalidOperationException($"BreathingColor '{value}' must be a six-digit RGB color.");

        return new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    private static List<(int Id, int LedCount, string Name)> FindSynchronizedTargets(
        OpenRgbClient client, BridgeConfig config)
    {
        var targets = new List<(int, int, string)>();
        int count = client.GetControllerCount();

        for (int id = 0; id < count; id++)
        {
            Device device = client.GetControllerData(id);
            bool synchronize = device.Name.StartsWith("TT LEDFanBox", StringComparison.OrdinalIgnoreCase) ||
                               device.Name.Equals("Razer Firefly", StringComparison.OrdinalIgnoreCase) ||
                               device.Name.Equals(config.SourceDevice, StringComparison.OrdinalIgnoreCase) ||
                               device.Name.Equals("EVGA Z590 DARK USB", StringComparison.OrdinalIgnoreCase) ||
                               device.Name.StartsWith("NVIDIA GeForce", StringComparison.OrdinalIgnoreCase);
            if (synchronize && device.Colors.Length > 0)
                targets.Add((id, device.Colors.Length, device.Name));
        }

        return targets;
    }

    private static string ExpectedOpenRgbMode(string deviceName, BridgeConfig config)
    {
        if (deviceName.Equals(config.SourceDevice, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        if (deviceName.Equals("EVGA Z590 DARK USB", StringComparison.OrdinalIgnoreCase))
            return "Static";
        if (deviceName.StartsWith("TT LEDFanBox", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("Razer Firefly", StringComparison.OrdinalIgnoreCase) ||
            deviceName.StartsWith("NVIDIA GeForce", StringComparison.OrdinalIgnoreCase))
            return "Direct";
        return string.Empty;
    }

    private static ClientColorCommand? DetectClientColorChange(
        BridgeConfig config,
        int sourceId,
        Color? lastSentColor,
        Color? lastSourceColor,
        out SourceModeState? observedSourceMode)
    {
        observedSourceMode = null;
        if (!lastSentColor.HasValue)
            return null;

        using var watcher = new OpenRgbClient(config.Host, config.Port,
            "OpenRGB Chroma Bridge Master Watcher", false, 1000);
        watcher.Connect();
        Device source = watcher.GetControllerData(sourceId);
        observedSourceMode = SourceModeFromDevice(source, config);
        if (source.ActiveMode.Name.Equals("Direct", StringComparison.OrdinalIgnoreCase) &&
            source.Colors.Length > 0 && lastSourceColor.HasValue &&
            source.Colors[0] != lastSourceColor.Value)
            return new ClientColorCommand(source.Colors[0], false);
        return null;
    }

    private static void SetOpenRgbMode(BridgeConfig config, string deviceName, string mode, Color color)
    {
        string openRgb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "OpenRGB", "OpenRGB.exe");
        if (!File.Exists(openRgb))
            throw new FileNotFoundException("The installed OpenRGB client was not found.", openRgb);

        var startInfo = new ProcessStartInfo(openRgb)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--client");
        startInfo.ArgumentList.Add($"{config.Host}:{config.Port}");
        startInfo.ArgumentList.Add("--device");
        startInfo.ArgumentList.Add(deviceName);
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add("--color");
        startInfo.ArgumentList.Add($"{color.R:X2}{color.G:X2}{color.B:X2}");

        using Process? process = Process.Start(startInfo);
        if (process is null || !process.WaitForExit(5000) || process.ExitCode != 0)
            throw new InvalidOperationException($"OpenRGB could not set {deviceName} to {mode} mode.");
    }

    private static void EnsureOpenRgbModes(OpenRgbClient client, BridgeConfig config, Color color)
    {
        var changes = new List<(string Name, string Mode)>();
        int count = client.GetControllerCount();
        for (int index = 0; index < count; index++)
        {
            Device device = client.GetControllerData(index);
            string expected = ExpectedOpenRgbMode(device.Name, config);
            if (expected.Length > 0 &&
                !device.ActiveMode.Name.Equals(expected, StringComparison.OrdinalIgnoreCase))
                changes.Add((device.Name, expected));
        }

        foreach (var change in changes)
            SetOpenRgbMode(config, change.Name, change.Mode.ToLowerInvariant(), color);
    }

    private static double GetBreathingPeriod(BridgeConfig config)
    {
        if (!config.BreathingSpeed.HasValue)
            return Math.Clamp(config.BreathingPeriodMilliseconds, 1000, 30000);

        return GetPeriodForSpeed(config.BreathingSpeed.Value);
    }

    private static double GetPeriodForSpeed(double requestedSpeed)
    {
        double speed = Math.Clamp(requestedSpeed, 0, 100);
        return speed <= 50
            ? 20000.0 + ((5000.0 - 20000.0) * (speed / 50.0))
            : 5000.0 + ((1000.0 - 5000.0) * ((speed - 50.0) / 50.0));
    }

    private static Color HueColor(double hue, double brightness)
    {
        hue = ((hue % 1.0) + 1.0) % 1.0;
        double scaled = hue * 6.0;
        int sector = (int)Math.Floor(scaled);
        double fraction = scaled - sector;
        (double r, double g, double b) = sector switch
        {
            0 => (1.0, fraction, 0.0),
            1 => (1.0 - fraction, 1.0, 0.0),
            2 => (0.0, 1.0, fraction),
            3 => (0.0, 1.0 - fraction, 1.0),
            4 => (fraction, 0.0, 1.0),
            _ => (1.0, 0.0, 1.0 - fraction)
        };
        double level = Math.Clamp(brightness, 0.0, 1.0);
        return new Color((byte)Math.Round(r * 255 * level),
            (byte)Math.Round(g * 255 * level), (byte)Math.Round(b * 255 * level));
    }

    private static double GetColorMix(double phase, double endpointHoldPercent)
    {
        double halfHold = Math.Clamp(endpointHoldPercent / 200.0, 0.0, 0.20);
        if (phase < halfHold || phase >= 1.0 - halfHold)
            return 0.0;
        if (phase >= 0.5 - halfHold && phase < 0.5 + halfHold)
            return 1.0;

        double transitionLength = 0.5 - (2.0 * halfHold);
        if (phase < 0.5)
        {
            double progress = (phase - halfHold) / transitionLength;
            return 0.5 - (0.5 * Math.Cos(progress * Math.PI));
        }

        double reverseProgress = (phase - (0.5 + halfHold)) / transitionLength;
        return 0.5 + (0.5 * Math.Cos(reverseProgress * Math.PI));
    }

    private static Color UpdateSynchronizedBreathing(
        OpenRgbClient client,
        BridgeConfig config,
        IEnumerable<(int Id, int LedCount, string Name)> targets,
        Color firstColor,
        Color secondColor,
        double colorMix,
        double level,
        double? wavePhase = null,
        bool reverseWave = false)
    {
        Color color = MixColor(firstColor, secondColor, colorMix, level);

        foreach (var target in targets)
        {
            if (target.Name.Equals(config.SourceDevice, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_traceFirstAnimationFrame)
                Log($"Testing animation write to {target.Name}.");
            Color[] colors = wavePhase.HasValue
                ? Enumerable.Range(0, target.LedCount)
                    .Select(index => HueColor(wavePhase.Value +
                        ((reverseWave ? -1.0 : 1.0) * index / Math.Max(1.0, target.LedCount)), level))
                    .ToArray()
                : Enumerable.Repeat(color, target.LedCount).ToArray();
            client.UpdateLeds(target.Id, colors);
            if (_traceFirstAnimationFrame)
                Log($"Animation write completed for {target.Name}.");
        }

        return color;
    }

    private static Color MixColor(Color firstColor, Color secondColor, double mix, double level = 1.0)
    {
        double r = firstColor.R + ((secondColor.R - firstColor.R) * mix);
        double g = firstColor.G + ((secondColor.G - firstColor.G) * mix);
        double b = firstColor.B + ((secondColor.B - firstColor.B) * mix);
        return new Color(
            (byte)Math.Round(r * level),
            (byte)Math.Round(g * level),
            (byte)Math.Round(b * level));
    }

    [STAThread]
    private static int Main(string[] args)
    {
        _console = args.Contains("--console", StringComparer.OrdinalIgnoreCase);
        bool once = args.Contains("--once", StringComparer.OrdinalIgnoreCase);
        using var mutex = new Mutex(true, "Local\\OpenRGB-Chroma-Bridge", out bool ownsMutex);
        if (!ownsMutex)
            return 0;

        BridgeConfig config;
        try { config = LoadConfig(); }
        catch (Exception ex) { Log("Configuration error: " + ex.Message); return 2; }

        using var logitech = config.EnableLogitechLighting ? new LogitechLighting(Log) : null;
        bool chromaReady = false;
        Task chromaInitialization = Task.Run(ChromaNative.Initialize);
        try
        {
            try
            {
                if (chromaInitialization.Wait(TimeSpan.FromSeconds(5)))
                {
                    chromaInitialization.GetAwaiter().GetResult();
                    chromaReady = true;
                    Log("Razer Chroma connected.");
                }
                else
                {
                    Log("Razer Chroma initialization timed out; OpenRGB animation will continue while Chroma connects.");
                }
            }
            catch (Exception ex)
            {
                Log("Razer Chroma unavailable; OpenRGB animation will continue: " + ex.GetBaseException().Message);
            }

            DesiredEffect? profileSeen = null;
            DesiredEffect? headsetApplied = null;
            Task? chromaApplyTask = null;
            long chromaApplyStartedAt = 0;
            Color? clientOverride = null;
            while (true)
            {
                try
                {
                    Color breathingColor = ParseColor(config.BreathingColor);
                    Color breathingSecondColor = ParseColor(config.BreathingSecondColor);
                    if (clientOverride.HasValue)
                        breathingColor = breathingSecondColor = clientOverride.Value;
                    DesiredEffect initial;
                    SourceModeState sourceMode;
                    int sourceId;
                    List<(int Id, int LedCount, string Name)> setupTargets;
                    using (var setupClient = new OpenRgbClient(config.Host, config.Port,
                               "OpenRGB Chroma Bridge Setup", false, 1000))
                    {
                        setupClient.Connect();
                        initial = ReadDesiredEffect(setupClient, config);
                        sourceMode = ReadSourceMode(setupClient, config);
                        sourceId = FindSourceId(setupClient, config);
                        setupTargets = FindSynchronizedTargets(setupClient, config);
                        if (config.EnableDeviceBreathing)
                            EnsureOpenRgbModes(setupClient, config, breathingColor);
                    }

                    if (config.FollowSourceColor &&
                        initial.Effect is HeadsetEffect.Static or HeadsetEffect.Breathing &&
                        (initial.R != 0 || initial.G != 0 || initial.B != 0))
                        breathingColor = new Color(initial.R, initial.G, initial.B);
                    profileSeen = initial;

                    using var client = new OpenRgbClient(config.Host, config.Port,
                        "OpenRGB Chroma Bridge Animation", false, 1000);
                    client.Connect();
                    var targets = FindSynchronizedTargets(client, config);
                    Log($"Profile '{config.ProfileName}' enabled: {string.Join(", ", targets.Select(t => t.Name))}, and ManO'War.");

                    var sessionTimer = Stopwatch.StartNew();
                    long nextProfilePoll = 0;
                    long nextSourceModePoll = 0;
                    long nextFrameLog = 0;
                    long nextHeadsetFrame = 0;
                    long nextClientPoll = 0;
                    Color? lastSentFrame = null;
                    Color? lastSourceColor = sourceMode.FirstColor;
                    do
                    {
                        long elapsed = sessionTimer.ElapsedMilliseconds;
                        if (!config.EnableDeviceBreathing)
                        {
                            var current = ReadDesiredEffect(client, config);
                            logitech?.Update(current.Effect == HeadsetEffect.None
                                ? new Color(0, 0, 0) : new Color(current.R, current.G, current.B));
                        }
                        if (!chromaReady && chromaInitialization.IsCompletedSuccessfully)
                        {
                            chromaReady = true;
                            Log("Razer Chroma connected after delayed initialization; ManO'War synchronization resumed.");
                        }
                        if (chromaApplyTask is { IsCompleted: false } &&
                            Environment.TickCount64 - chromaApplyStartedAt > 5000)
                        {
                            Log("Razer Chroma update stalled; exiting so the watchdog can restart the bridge.");
                            chromaReady = false;
                            return 4;
                        }
                        if (chromaApplyTask is { IsFaulted: true })
                        {
                            Log("Razer Chroma update failed: " + chromaApplyTask.Exception?.GetBaseException().Message);
                            chromaReady = false;
                            chromaApplyTask = null;
                        }
                        ClientColorCommand? clientColorChange = null;
                        SourceModeState? observedSourceMode = null;
                        if (elapsed >= nextClientPoll)
                        {
                            clientColorChange = DetectClientColorChange(
                                config, sourceId, lastSentFrame, lastSourceColor,
                                out observedSourceMode);
                            nextClientPoll = elapsed + 250;
                        }
                        if (clientColorChange.HasValue)
                        {
                            lastSourceColor = clientColorChange.Value.Color;
                            if (clientColorChange.Value.ResumeSavedProfile)
                            {
                                SetOpenRgbMode(config, config.SourceDevice, "direct", ParseColor(config.BreathingColor));
                                sourceMode = InspectSourceMode(config);
                                lastSourceColor = sourceMode.FirstColor;
                                clientOverride = null;
                                breathingColor = ParseColor(config.BreathingColor);
                                breathingSecondColor = ParseColor(config.BreathingSecondColor);
                                sessionTimer.Restart();
                                elapsed = 0;
                                nextClientPoll = 0;
                                nextSourceModePoll = 0;
                                nextProfilePoll = 0;
                                nextFrameLog = 0;
                                nextHeadsetFrame = 0;
                                headsetApplied = null;
                                Log($"Saved profile '{config.ProfileName}' selected; synchronized cycle restarted.");
                            }
                            else
                            {
                                clientOverride = clientColorChange.Value.Color;
                                breathingColor = breathingSecondColor = clientOverride.Value;
                                Log($"OpenRGB client color accepted immediately: #{clientOverride.Value.R:X2}{clientOverride.Value.G:X2}{clientOverride.Value.B:X2}.");
                            }
                        }

                        if (elapsed >= nextSourceModePoll)
                        {
                            SourceModeState currentSourceMode = observedSourceMode ?? sourceMode;
                            if (currentSourceMode != sourceMode)
                            {
                                bool effectChanged = !currentSourceMode.Name.Equals(
                                    sourceMode.Name, StringComparison.OrdinalIgnoreCase) ||
                                    !currentSourceMode.Name.Equals("Direct", StringComparison.OrdinalIgnoreCase);
                                sourceMode = currentSourceMode;
                                if (currentSourceMode.Name.Equals("Direct", StringComparison.OrdinalIgnoreCase))
                                    lastSourceColor = currentSourceMode.FirstColor;
                                if (effectChanged)
                                {
                                    clientOverride = null;
                                    sessionTimer.Restart();
                                    elapsed = 0;
                                    nextClientPoll = 0;
                                    nextSourceModePoll = 0;
                                    nextProfilePoll = 0;
                                    nextFrameLog = 0;
                                    nextHeadsetFrame = 0;
                                    headsetApplied = null;
                                    Log($"Shared mode <- {sourceMode.Name}, speed {sourceMode.Speed}, brightness {sourceMode.Brightness:P0} from {config.SourceDevice}.");
                                }
                            }
                            nextSourceModePoll = elapsed + Math.Clamp(config.ProfileCheckMilliseconds, 500, 10000);
                        }

                        if ((config.FollowSourceColor || !config.EnableDeviceBreathing) &&
                            elapsed >= nextProfilePoll)
                        {
                            DesiredEffect desired = ReadDesiredEffect(client, config);
                            if (desired != profileSeen)
                            {
                                profileSeen = desired;

                                if (config.FollowSourceColor &&
                                    desired.Effect is HeadsetEffect.Static or HeadsetEffect.Breathing &&
                                    (desired.R != 0 || desired.G != 0 || desired.B != 0))
                                {
                                    breathingColor = new Color(desired.R, desired.G, desired.B);
                                    Log($"Breathing color <- #{desired.R:X2}{desired.G:X2}{desired.B:X2} from {config.SourceDevice}.");
                                }

                                if (!config.EnableDeviceBreathing)
                                {
                                    if (chromaReady)
                                    {
                                        ChromaNative.Apply(desired);
                                        headsetApplied = desired;
                                        Log($"ManO'War <- {desired} from {config.SourceDevice}.");
                                    }
                                }
                            }
                            nextProfilePoll = elapsed + Math.Clamp(config.PollMilliseconds, 250, 10000);
                        }

                        if (config.EnableDeviceBreathing && targets.Count > 0)
                        {
                            string sharedMode = clientOverride.HasValue ? "Static" : sourceMode.Name;
                            bool configuredProfile = sharedMode.Equals("Direct", StringComparison.OrdinalIgnoreCase);
                            bool spectrum = sharedMode.Contains("Spectrum", StringComparison.OrdinalIgnoreCase);
                            bool wave = sharedMode.Equals("Wave", StringComparison.OrdinalIgnoreCase);
                            bool off = sharedMode.Equals("Off", StringComparison.OrdinalIgnoreCase);
                            bool stationary = sharedMode.Equals("Static", StringComparison.OrdinalIgnoreCase) || off;
                            Color firstColor = clientOverride ?? (configuredProfile
                                ? ParseColor(config.BreathingColor)
                                : sourceMode.FirstColor);
                            Color secondColor = clientOverride ?? (configuredProfile
                                ? ParseColor(config.BreathingSecondColor)
                                : sourceMode.SecondColor);
                            if (off)
                                firstColor = secondColor = new Color(0, 0, 0);
                            double period = configuredProfile
                                ? GetBreathingPeriod(config)
                                : GetPeriodForSpeed(sourceMode.Speed);
                            double phase = (elapsed % period) / period;
                            double colorMix = stationary || spectrum || wave
                                ? 0.0
                                : GetColorMix(phase, config.EndpointHoldPercent);
                            double minimum = Math.Clamp(config.BreathingMinimumBrightness, 0.0, 0.95);
                            double modeBrightness = configuredProfile ? 1.0 : sourceMode.Brightness;
                            double level = config.MaintainBrightness || stationary || spectrum || wave
                                ? modeBrightness
                                : minimum + ((1.0 - minimum) * colorMix);
                            if (spectrum || wave)
                                firstColor = secondColor = HueColor(phase, 1.0);
                            Color frame = UpdateSynchronizedBreathing(client, config, targets, firstColor,
                                secondColor, colorMix, level, wave ? phase : null,
                                sourceMode.Direction == OpenRGB.NET.Direction.Left);
                            lastSentFrame = frame;
                            logitech?.Update(frame);
                            if (chromaReady && elapsed >= nextHeadsetFrame)
                            {
                                double headsetElapsed = elapsed + Math.Clamp(config.HeadsetPhaseLeadMilliseconds, 0, 5000);
                                double headsetPhase = (headsetElapsed % period) / period;
                                double headsetMix = stationary || spectrum || wave
                                    ? 0.0
                                    : GetColorMix(headsetPhase, config.EndpointHoldPercent);
                                if (!stationary && !spectrum && !wave)
                                    headsetMix = Math.Clamp(headsetMix /
                                        Math.Clamp(config.HeadsetRedThreshold, 0.1, 1.0), 0.0, 1.0);
                                Color headsetColor = spectrum || wave
                                    ? HueColor(headsetPhase, modeBrightness)
                                    : MixColor(firstColor, secondColor, headsetMix, level);
                                var headsetFrame = new DesiredEffect(HeadsetEffect.Static,
                                    headsetColor.R, headsetColor.G, headsetColor.B);
                                if (headsetFrame != headsetApplied)
                                {
                                    if (chromaApplyTask is null || chromaApplyTask.IsCompleted)
                                    {
                                        if (_traceFirstAnimationFrame)
                                            Log("Testing animation write to ManO'War.");
                                        DesiredEffect effectToApply = headsetFrame;
                                        chromaApplyStartedAt = Environment.TickCount64;
                                        chromaApplyTask = Task.Run(() => ChromaNative.Apply(effectToApply));
                                        headsetApplied = headsetFrame;
                                        if (_traceFirstAnimationFrame)
                                            Log("Animation write queued for ManO'War.");
                                    }
                                }
                                nextHeadsetFrame = elapsed + Math.Clamp(config.HeadsetUpdateMilliseconds, 200, 1000);
                            }

                            _traceFirstAnimationFrame = false;

                            if (elapsed >= nextFrameLog)
                            {
                                Log($"Animation frame #{frame.R:X2}{frame.G:X2}{frame.B:X2}, phase {phase:F2}, targets {targets.Count}; ManO'War {headsetApplied}.");
                                nextFrameLog = elapsed + 2000;
                            }
                        }

                        Thread.Sleep(Math.Clamp(config.BreathingFrameMilliseconds, 50, 1000));
                    }
                    while (!once);

                    Thread.Sleep(3000);
                    return 0;
                }
                catch (Exception ex)
                {
                    Log("Waiting to reconnect: " + ex.Message);
                    if (once)
                        return 3;
                }

                Thread.Sleep(Math.Clamp(config.PollMilliseconds, 250, 10000));
            }
        }
        catch (Exception ex)
        {
            Log("Bridge stopped: " + ex);
            return 1;
        }
        finally
        {
            if (chromaReady)
                ChromaNative.Shutdown();
        }
    }
}
