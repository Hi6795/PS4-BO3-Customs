using System.Text.Json;
using System.Text.RegularExpressions;
using FFPorter.Core.T7.Sound;
using FFPorter.Core.T7.Streams;

namespace FFPorter.Core.T7.Port;

public sealed partial class T7Timing
{
    public const string FileName = "timing.json";
    public const int Version = 2;
    public const string ShaderRun = "shaders-per-map";
    public const string CachedSuffix = "-cached";
    public const string SingleCoreSuffix = "/1";

    private const int Kept = 24;
    private const double Recency = 0.8, Prior = 0.35, LeastWeight = 0.05, LeastSeconds = 0.05, LeastPredicted = 0.1, CachedShadersPerMb = 0.02;

    public sealed record Request(string MainFile, string WorkDirectory, string? OutputDirectory = null, bool Sound = true, bool Streams = true, bool Shaders = true,
        bool Movies = true, IReadOnlyCollection<string>? Languages = null, bool Concurrent = false, int Slots = 1, bool SingleCore = false, bool Gpu = false);

    private static readonly Dictionary<string, (double Fixed, double PerWeight)> Usual = new(StringComparer.Ordinal)
    {
        ["walk"] = (0.3, 3.6),
        ["walk" + CachedSuffix] = (0.35, 0.3),
        ["shaders"] = (1, 0.5),
        ["streams"] = (0.3, 0.34),
        ["streams" + CachedSuffix] = (0.5, 0.003),
        ["assets"] = (0.4, 0.3),
        ["link"] = (0.5, 0.15),
        ["bank"] = (0.3, 0.6),
        ["movie"] = (1, 0.75),
        ["movie-gpu"] = (0.5, 0.11),
        [ShaderRun] = (5, 12),
    };

    private readonly string? _path;
    private readonly Dictionary<string, List<(double Weight, double Seconds)>> _samples = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _changed;

    private T7Timing(string? path) => _path = path;

    public static string PathIn(string workDirectory) => Path.Combine(workDirectory, FileName);

    public static string Mode(bool concurrent, int slots, bool singleCore) =>
        (concurrent ? "+" : "") + (slots > 1 ? "x" + Math.Min(slots, 8) : "") + (singleCore ? SingleCoreSuffix : "");

    public static T7Timing Load(string? path)
    {
        var timing = new T7Timing(path);
        try
        {
            if (path != null && File.Exists(path))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("version", out JsonElement version) && version.ValueKind == JsonValueKind.Number && version.GetInt32() == Version
                    && root.TryGetProperty("samples", out JsonElement samples) && samples.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty kind in samples.EnumerateObject())
                    {
                        if (kind.Value.ValueKind != JsonValueKind.Array)
                            continue;
                        var list = new List<(double, double)>();
                        foreach (JsonElement pair in kind.Value.EnumerateArray())
                        {
                            if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() == 2 && pair[0].TryGetDouble(out double weight) && pair[1].TryGetDouble(out double seconds)
                                && double.IsFinite(weight) && double.IsFinite(seconds) && weight >= 0 && seconds >= 0)
                                list.Add((weight, seconds));
                        }
                        if (list.Count > 0)
                            timing._samples[kind.Name] = list.TakeLast(Kept).ToList();
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
        }
        return timing;
    }

    public bool Knows(string kind, string mode)
    {
        lock (_gate)
            return Samples(kind, mode) != null;
    }

    public double Seconds(string kind, double weight, string mode)
    {
        if (weight <= 0)
            return 0;
        (double fixedPart, double perWeight) = Fit(kind, mode);
        return Math.Max(LeastPredicted, fixedPart + perWeight * Size(weight));
    }

    public double ShaderSeconds(double megabytes, string mode) => Seconds(ShaderRun, T7MapPort.ShaderGuess(megabytes), mode);

    public void Learn(string kind, double weight, double seconds, string mode)
    {
        if (kind.Length == 0 || weight < LeastWeight || seconds < LeastSeconds || !double.IsFinite(seconds) || !double.IsFinite(weight))
            return;
        lock (_gate)
        {
            string key = kind + mode;
            if (!_samples.TryGetValue(key, out List<(double, double)>? list))
                _samples[key] = list = [];
            list.Add((weight, seconds));
            if (list.Count > Kept)
            {
                double[] trust = Trust(list);
                list.RemoveAt(Array.IndexOf(trust, trust.Min()));
            }
            _changed = true;
        }
    }

    public void LearnShaders(double guess, double seconds, string mode) => Learn(ShaderRun, guess, seconds, mode);

    private static double Size(double weight) => Math.Max(0, weight - 1);

    private static double[] Trust(List<(double Weight, double Seconds)> samples)
    {
        var trust = new double[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            int newer = 0;
            for (int j = i + 1; j < samples.Count; j++)
            {
                if (Alike(samples[i].Weight, samples[j].Weight))
                    newer++;
            }
            trust[i] = Math.Pow(Recency, newer);
        }
        return trust;
    }

    private static bool Alike(double first, double second)
    {
        double a = Size(first), b = Size(second);
        return (a < 1 && b < 1) || Math.Max(a, b) <= 2 * Math.Min(a, b);
    }

    private (List<(double Weight, double Seconds)> List, double Scale)? Samples(string kind, string mode)
    {
        if (_samples.TryGetValue(kind + mode, out List<(double, double)>? own) && own.Count > 0)
            return (own, 1);
        double wanted = Slowdown(mode), gap = double.MaxValue;
        (List<(double, double)>, double)? nearest = null;
        foreach ((string key, List<(double, double)> list) in _samples)
        {
            if (list.Count == 0 || !key.StartsWith(kind, StringComparison.Ordinal) || !ModePattern().IsMatch(key[kind.Length..]))
                continue;
            double other = Slowdown(key[kind.Length..]), distance = Math.Abs(Math.Log(wanted / other));
            if (distance < gap)
            {
                gap = distance;
                nearest = (list, wanted / other);
            }
        }
        return nearest;
    }

    [GeneratedRegex(@"^\+?(x[2-8])?(/1)?$")]
    private static partial Regex ModePattern();

    private (double Fixed, double PerWeight) Fit(string kind, string mode)
    {
        (double fixedPart, double perWeight) usual = Usual.GetValueOrDefault(kind, (1, 1));
        double slowdown = Slowdown(mode);
        usual = (usual.fixedPart * slowdown, usual.perWeight * slowdown);
        List<(double Weight, double Seconds)>? samples;
        double scale;
        lock (_gate)
        {
            var found = Samples(kind, mode);
            samples = found?.List.ToList();
            scale = found?.Scale ?? 1;
        }
        if (samples == null || samples.Count == 0)
            return usual;
        double reference = Math.Max(1, samples.Max(sample => Size(sample.Weight)));
        var points = new List<(double Size, double Seconds, double Trust)>
        {
            (0, usual.fixedPart, Prior),
            (reference, usual.fixedPart + usual.perWeight * reference, Prior * 0.5),
        };
        double[] trust = Trust(samples);
        for (int i = 0; i < samples.Count; i++)
            points.Add((Size(samples[i].Weight), samples[i].Seconds * scale, trust[i]));
        double sw = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach ((double x, double y, double w) in points)
        {
            sw += w;
            sx += w * x;
            sy += w * y;
            sxx += w * x * x;
            sxy += w * x * y;
        }
        double spread = sw * sxx - sx * sx;
        double slope = spread > 1e-9 ? (sw * sxy - sx * sy) / spread : 0;
        double intercept = (sy - slope * sx) / sw;
        if (slope < 0)
        {
            slope = 0;
            intercept = sy / sw;
        }
        if (intercept < 0)
        {
            intercept = 0;
            slope = sxx > 1e-9 ? sxy / sxx : 0;
        }
        return (intercept, slope);
    }

    private static double Slowdown(string mode)
    {
        double factor = 1;
        int x = mode.IndexOf('x');
        if (x >= 0 && int.TryParse(new string(mode[(x + 1)..].TakeWhile(char.IsDigit).ToArray()), out int slots) && slots > 1)
            factor *= 1 + 0.35 * (slots - 1);
        if (mode.StartsWith('+'))
            factor *= 1.3;
        if (mode.EndsWith(SingleCoreSuffix, StringComparison.Ordinal))
            factor *= 2.5;
        return factor;
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
                File.WriteAllText(partial, JsonSerializer.Serialize(new
                {
                    version = Version,
                    samples = _samples.OrderBy(sample => sample.Key, StringComparer.Ordinal)
                        .ToDictionary(sample => sample.Key, sample => sample.Value.Select(pair => new[] { Math.Round(pair.Weight, 3), Math.Round(pair.Seconds, 2) }).ToArray()),
                }, new JsonSerializerOptions { WriteIndented = true }));
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
        string mode = Mode(request.Concurrent, request.Slots, request.SingleCore);
        double Step(string kind, double weight) => Seconds(kind, weight, mode);
        double Walk(string zone) => WalkCached(request.WorkDirectory, zone)
            ? Step("walk" + CachedSuffix, T7MapPort.WalkCost(SizeMb(zone)))
            : Step("walk", T7MapPort.WalkCost(SizeMb(zone)));
        double Stream(string zone)
        {
            string xpak = Path.ChangeExtension(zone, ".xpak");
            if (!request.Streams || !File.Exists(xpak))
                return 0;
            double weight = T7MapPort.StreamCost(SizeMb(xpak));
            return StreamsCached(request.WorkDirectory, request.OutputDirectory, zone) ? Step("streams" + CachedSuffix, weight) : Step("streams", weight);
        }
        double After(string zone) => Step("assets", T7MapPort.AssetCost(SizeMb(zone))) + Step("link", T7MapPort.LinkCost(SizeMb(zone)));
        string main = companions.Zones[0];
        double walk = Walk(main);
        double mainMb = SizeMb(main);
        double shaders = !request.Shaders ? 0
            : WalkCached(request.WorkDirectory, main) ? Step("shaders", 1 + mainMb * CachedShadersPerMb)
            : ShaderSeconds(mainMb, mode);
        double streams = Stream(main);
        double[] banks = request.Sound
            ? [.. companions.SoundBanks.Select(bank => T7SoundConvert.IsCached(bank, Path.Combine(request.WorkDirectory, Path.GetRelativePath(folder, bank)))
                ? LeastPredicted : Step("bank", T7MapPort.BankCost(SizeMb(bank), false)))]
            : [];
        double[] movies = request.Movies && request.OutputDirectory != null
            ? [.. T7MapPort.MovieWeights(request.MainFile, request.OutputDirectory).Select(weight => weight <= 1 ? LeastPredicted : Step(request.Gpu ? "movie-gpu" : "movie", weight))]
            : [];
        double languages = companions.Zones.Skip(1).Sum(zone => Walk(zone) + Stream(zone) + After(zone));
        if (!request.Concurrent)
            return walk + shaders + streams + banks.Sum() + After(main) + languages + movies.Sum();
        int width = Math.Max(1, request.SingleCore ? 1 : T7MapPort.SoundLaneWidth);
        double sound = banks.Length == 0 ? 0 : Math.Max(banks.Max(), banks.Sum() / width);
        double chain = Math.Max(walk + Math.Max(shaders, streams), sound) + After(main) + languages;
        return Math.Max(chain, movies.Sum());
    }

    private static bool WalkCached(string workDirectory, string zone)
    {
        try
        {
            return File.Exists(Path.Combine(workDirectory, $"{Path.GetFileNameWithoutExtension(zone)}.{T7FastFileDelta.CacheKey(zone)}.pc.t7walk"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool StreamsCached(string workDirectory, string? outputDirectory, string zone) =>
        outputDirectory != null && T7StreamConvert.LooksCached(workDirectory, Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(zone) + ".xpak"));

    private static double SizeMb(string path) => File.Exists(path) ? new FileInfo(path).Length / 1048576.0 : 0;
}
