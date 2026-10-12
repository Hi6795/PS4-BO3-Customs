using System.Text.Json;
using FFPorter.Core.T7.Sound;

namespace FFPorter.Core.T7.Port;

public sealed class T7Timing
{
    public const string FileName = "timing.json";

    private const string ShaderRun = "shaders-per-map";
    private const double Fresh = 0.4, LeastWeight = 2, LeastSeconds = 0.5, SlowestRate = 50, FastestRate = 0.02, ColdShaderSeconds = 5, UsualShaderSeconds = 180;

    public sealed record Request(string MainFile, string WorkDirectory, bool Sound = true, bool Streams = true, bool Shaders = true,
        IReadOnlyCollection<string>? Languages = null, bool Concurrent = false, bool ConvertedBefore = false);

    private static readonly Dictionary<string, double> Usual = new(StringComparer.Ordinal) { ["walk"] = 1.5, ["shaders"] = 0.4, ["assets"] = 0.7, ["link"] = 0.55 };

    private readonly string? _path;
    private readonly Dictionary<string, double> _rates = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _changed;

    private T7Timing(string? path) => _path = path;

    public static string PathIn(string workDirectory) => Path.Combine(workDirectory, FileName);

    public static T7Timing Load(string? path)
    {
        var timing = new T7Timing(path);
        try
        {
            if (path != null && File.Exists(path))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("rates", out JsonElement rates) && rates.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty rate in rates.EnumerateObject())
                    {
                        if (rate.Value.ValueKind == JsonValueKind.Number && rate.Value.GetDouble() is double value && double.IsFinite(value) && value > 0)
                            timing._rates[rate.Name] = value;
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return timing;
    }

    public static string Key(string kind, bool sideBySide) => sideBySide ? kind + "+" : kind;

    public double Rate(string kind, bool sideBySide = false)
    {
        lock (_gate)
            return _rates.TryGetValue(Key(kind, sideBySide), out double own) ? own : _rates.TryGetValue(kind, out double known) ? known : Usual.GetValueOrDefault(kind, 1);
    }

    public bool Knows(string kind, bool sideBySide = false)
    {
        lock (_gate)
            return _rates.ContainsKey(Key(kind, sideBySide)) || _rates.ContainsKey(kind);
    }

    public double ShaderSeconds
    {
        get
        {
            lock (_gate)
                return _rates.GetValueOrDefault(ShaderRun, UsualShaderSeconds);
        }
    }

    public void Learn(string kind, double weight, double seconds, bool sideBySide = false)
    {
        if (kind.Length == 0 || weight < LeastWeight || seconds < LeastSeconds)
            return;
        Blend(Key(kind, sideBySide), Math.Clamp(seconds / weight, FastestRate, SlowestRate));
    }

    public void LearnShaders(double seconds)
    {
        if (seconds >= ColdShaderSeconds)
            Blend(ShaderRun, seconds);
    }

    private void Blend(string key, double value)
    {
        lock (_gate)
        {
            _rates[key] = _rates.TryGetValue(key, out double known) ? known * (1 - Fresh) + value * Fresh : value;
            _changed = true;
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            if (_path == null || !_changed)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string partial = _path + "." + Environment.ProcessId + ".part";
                File.WriteAllText(partial, JsonSerializer.Serialize(new { version = 1, rates = _rates.OrderBy(rate => rate.Key, StringComparer.Ordinal).ToDictionary(rate => rate.Key, rate => Math.Round(rate.Value, 4)) },
                    new JsonSerializerOptions { WriteIndented = true }));
                File.Move(partial, _path, true);
                _changed = false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public double Before(Request request)
    {
        T7MapPort.Companions companions = T7MapPort.Find(request.MainFile, request.Languages);
        string folder = Path.GetDirectoryName(Path.GetFullPath(request.MainFile))!;
        double Speed(string kind) => Rate(kind, request.Concurrent);
        double Stream(string zone) => request.Streams && File.Exists(Path.ChangeExtension(zone, ".xpak"))
            ? Speed("streams") * T7MapPort.StreamCost(SizeMb(Path.ChangeExtension(zone, ".xpak"))) : 0;
        double After(string zone) => Speed("assets") * T7MapPort.AssetCost(SizeMb(zone)) + Speed("link") * T7MapPort.LinkCost(SizeMb(zone));
        string main = companions.Zones[0];
        double walk = Speed("walk") * T7MapPort.WalkCost(SizeMb(main));
        double shaders = !request.Shaders || (request.ConvertedBefore && HasShaderCache(request.WorkDirectory)) ? 0 : ShaderSeconds;
        double streams = Stream(main);
        double[] banks = request.Sound
            ? [.. companions.SoundBanks.Select(bank => Speed("bank") * T7MapPort.BankCost(SizeMb(bank),
                T7SoundConvert.IsCached(bank, Path.Combine(request.WorkDirectory, Path.GetRelativePath(folder, bank)))))]
            : [];
        double languages = companions.Zones.Skip(1).Sum(zone => Speed("walk") * T7MapPort.WalkCost(SizeMb(zone)) + Stream(zone) + After(zone));
        if (!request.Concurrent)
            return walk + shaders + streams + banks.Sum() + After(main) + languages;
        double sound = banks.Length == 0 ? 0 : Math.Max(banks.Max(), banks.Sum() / T7MapPort.SoundLaneWidth);
        return Math.Max(walk + Math.Max(shaders, streams), sound) + After(main) + languages;
    }

    private static bool HasShaderCache(string workDirectory)
    {
        try
        {
            string cache = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workDirectory)) ?? workDirectory, "shader_cache");
            return Directory.Exists(cache) && Directory.EnumerateDirectories(cache).Any();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static double SizeMb(string path) => File.Exists(path) ? new FileInfo(path).Length / 1048576.0 : 0;
}
