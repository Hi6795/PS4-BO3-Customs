using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Core.Common.Tools;
using FFPorter.Core.T7.Formats;
using FFPorter.Core.T7.Sound;
using FFPorter.Core.T7.Streams;

namespace FFPorter.Core.T7.Port;

public sealed class T7MapPortOptions
{
    public required string PcMapFastFile { get; init; }
    public required string OutputFolder { get; init; }
    public required string WorkDirectory { get; init; }
    public required string Ps4LoaderDirectory { get; init; }
    public required string PcImage { get; init; }
    public required T7DonorLibrary Donors { get; init; }
    public required Workspace Workspace { get; init; }
    public T7Ps4StreamIndex? Ps4StreamIndex { get; init; }
    public IReadOnlyList<string>? PcStreamXPaks { get; init; }
    public T7PcShaderLibrary? ShaderLibrary { get; init; }
    public string? Acts { get; init; }
    public bool GscRecompile { get; init; }
    public bool Force { get; init; }
    public bool DonorFallbackForAllTypes { get; init; }
    public bool ConvertStreams { get; init; } = true;
    public bool ConvertSound { get; init; } = true;
    public bool ConvertMovies { get; init; } = true;
    public bool ApplyDelta { get; init; } = true;
    public bool Concurrent { get; init; }
    public int Slots { get; init; } = 1;
    public string ReportPrefix { get; init; } = "";
    public IReadOnlyCollection<string>? Languages { get; init; }
    public Shaders.T7ShaderCompiler? ShaderCompiler { get; init; }
    public Action<string> Log { get; init; } = _ => { };

    public T7Fidelity? Fidelity { get; init; }
}

public sealed record T7MapPortResult(bool Success, IReadOnlyList<string> Outputs, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings)
{
    public FidelitySnapshot? Fidelity { get; init; }
}

public static class T7MapPort
{
    public sealed record Companions(IReadOnlyList<string> Zones, IReadOnlyList<string> SoundBanks);

    public sealed record Originals(IReadOnlyList<string> Files, IReadOnlyList<string> PackageFiles, string Root);

    private const int MostRows = 6;
    private const string SoundLane = "sound", MovieLane = "movie";
    private static readonly string[] UsermapFiles = ["workshop.json", "previewimage.png", "loadingimage.png"];

    private sealed class Unit
    {
        public required string Key { get; init; }
        public string? Row { get; init; }
        public required string Title { get; init; }
        public required List<(string Phase, double Weight)> Phases { get; init; }
        public required Action Run { get; init; }
        public Func<double?>? Estimate { get; init; }
        public List<Unit> After { get; } = [];
        public string? Lane { get; init; }
        public long Memory { get; init; }
        public string Step => Row ?? Key;
        public double Cost => Estimate?.Invoke() ?? Phases.Sum(p => p.Weight);
    }

    private sealed class Collected
    {
        private readonly object _gate = new();

        public List<string> Outputs { get; } = [];
        public List<string> Problems { get; } = [];
        public List<string> Warnings { get; } = [];

        public void AddOutput(string path)
        {
            lock (_gate)
                Outputs.Add(path);
        }

        public void AddProblem(string text)
        {
            lock (_gate)
                Problems.Add(text);
        }

        public void AddProblems(IEnumerable<string> texts)
        {
            lock (_gate)
                Problems.AddRange(texts);
        }

        public void AddWarning(string text)
        {
            lock (_gate)
                Warnings.Add(text);
        }
    }

    public static Companions Find(string pcMapFastFile, IReadOnlyCollection<string>? languages = null)
    {
        string stem = Path.GetFileNameWithoutExtension(pcMapFastFile);
        string folder = Path.GetDirectoryName(Path.GetFullPath(pcMapFastFile))!;
        bool Wanted(string language) => languages == null || language.Equals("all", StringComparison.OrdinalIgnoreCase)
            || languages.Contains(language, StringComparer.OrdinalIgnoreCase);
        var zones = new List<string> { Path.GetFullPath(pcMapFastFile) };
        zones.AddRange(Directory.EnumerateFiles(folder, "*_" + stem + ".ff")
            .Where(f => Path.GetFileName(f).Length == stem.Length + 6 && Wanted(Path.GetFileName(f)[..2]))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        var banks = new List<string>();
        string sound = Path.Combine(folder, "snd");
        if (Directory.Exists(sound))
        {
            banks.AddRange(Directory.EnumerateFiles(sound, stem + ".*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".sabl", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".sabs", StringComparison.OrdinalIgnoreCase))
                .Where(f => Wanted(Path.GetExtension(Path.GetFileNameWithoutExtension(f)).TrimStart('.')))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        return new Companions(zones, banks);
    }

    public static Originals FindOriginals(string pcMapFastFile)
    {
        string input = Path.GetFullPath(pcMapFastFile);
        string folder = Path.GetDirectoryName(input)!;
        Companions companions = Find(input);
        var files = new List<string>();
        foreach (string zone in companions.Zones)
        {
            string stem = Path.GetFileNameWithoutExtension(zone);
            files.Add(zone);
            files.AddRange(((string[])[Path.Combine(folder, stem + ".xpak"), Path.Combine(folder, stem + "_d.xpak")]).Where(File.Exists));
        }
        files.AddRange(companions.SoundBanks);
        string[] roots = UsermapRoots(folder);
        var package = new List<string>();
        foreach (string relative in UsermapFiles)
            package.AddRange(roots.Select(root => Path.Combine(root, relative)).Where(File.Exists));
        if (IsUsermap(folder, roots) && VideoFolder(roots) is { } videos)
            package.AddRange(Directory.EnumerateFiles(videos).Where(file => T7MovieTranscoder.Extensions.Contains(Path.GetExtension(file))));
        return new Originals(files, package, roots[^1]);
    }

    public static T7MapPortResult Run(T7MapPortOptions options)
    {
        Action<string> log = options.Log;
        string input = Path.GetFullPath(options.PcMapFastFile);
        string folder = Path.GetDirectoryName(input)!;
        string stem = Path.GetFileNameWithoutExtension(input);
        string report = options.ReportPrefix + stem;
        Companions companions = Find(input, options.Languages);
        var collected = new Collected();
        T7Fidelity fidelity = options.Fidelity ?? new T7Fidelity(stem, null);
        fidelity.Concurrent = options.Concurrent;
        Directory.CreateDirectory(options.OutputFolder);
        Directory.CreateDirectory(options.WorkDirectory);

        foreach (string zone in companions.Zones)
        {
            string target = Path.Combine(options.OutputFolder, Path.GetFileName(zone));
            if (File.Exists(target) && !options.Force)
            {
                string problem = $"{target} exists; replace is off";
                fidelity.Tracker.Problem(problem);
                FidelitySnapshot stopped = fidelity.Tracker.Finish(FidelityStates.Failed, $"Nothing was converted: {Path.GetFileName(target)} is already in the output folder and replacing is off.");
                return new T7MapPortResult(false, collected.Outputs, [problem], collected.Warnings) { Fidelity = stopped };
            }
        }

        string[] roots = UsermapRoots(folder);
        IReadOnlyList<string> movies = options.ConvertMovies && IsUsermap(folder, roots) ? MovieSources(roots) : [];
        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var streamedSounds = new HashSet<uint>();
        var switchedSounds = new HashSet<uint>();
        var sharedStreams = new T7StreamMap();
        using T7PcStreamSource? pcStreams = options.ConvertStreams && options.PcStreamXPaks != null
            ? new T7PcStreamSource(Directory.EnumerateFiles(folder, "*.xpak").Order(StringComparer.OrdinalIgnoreCase).Concat(options.PcStreamXPaks), log)
            : null;

        T7StockImages? stockImages = options.PcStreamXPaks is { Count: > 0 } gameXPaks
            ? new T7StockImages(gameXPaks, Path.Combine(T7Paths.Work(options.Workspace), "game_textures"), log)
            : null;

        string XPakOf(string zone) => Path.Combine(folder, Path.GetFileNameWithoutExtension(zone) + ".xpak");

        bool HasStreams(string zone) => options.ConvertStreams && File.Exists(XPakOf(zone));

        T7ZonePortOptions ZoneOptions(string zone, T7StreamMap shared)
        {
            string zoneStem = Path.GetFileNameWithoutExtension(zone);
            string indexXPak = Path.Combine(folder, zoneStem + "_d.xpak");
            bool streams = HasStreams(zone);
            return new T7ZonePortOptions
            {
                PcFastFile = zone,
                OutputFastFile = Path.Combine(options.OutputFolder, Path.GetFileName(zone)),
                WorkDirectory = options.WorkDirectory,
                Ps4LoaderDirectory = options.Ps4LoaderDirectory,
                PcImage = options.PcImage,
                Donors = options.Donors,
                Log = log,
                DonorFallbackForAllTypes = options.DonorFallbackForAllTypes,
                PcXPak = streams ? XPakOf(zone) : null,
                OutputXPak = streams ? Path.Combine(options.OutputFolder, zoneStem + ".xpak") : null,
                PcIndexXPak = streams && File.Exists(indexXPak) ? indexXPak : null,
                OutputIndexXPak = streams && File.Exists(indexXPak) ? Path.Combine(options.OutputFolder, zoneStem + "_d.xpak") : null,
                Ps4StreamIndex = options.Ps4StreamIndex,
                PcStreamSource = pcStreams,
                StockImages = stockImages,
                SoundRenames = renames,
                StreamedSounds = streamedSounds,
                SwitchedSounds = switchedSounds,
                ShaderLibrary = options.ShaderLibrary,
                SharedStreams = shared,
                Acts = options.Acts,
                GscRecompile = options.GscRecompile,
                ApplyDelta = options.ApplyDelta,
                ShaderCompiler = options.ShaderCompiler,
                Fidelity = fidelity,
                ReportName = options.ReportPrefix + zoneStem,
            };
        }

        List<(string Phase, double Weight)> ZonePhases(string zone)
        {
            double megabytes = SizeMb(zone);
            var phases = new List<(string Phase, double Weight)> { (T7Fidelity.ZonePhase(zone, "walk"), WalkCost(megabytes)) };
            if (options.ShaderCompiler != null)
                phases.Add((T7Fidelity.ZonePhase(zone, "shaders"), ShaderGuess(megabytes)));
            if (HasStreams(zone))
                phases.Add((T7Fidelity.ZonePhase(zone, "streams"), StreamCost(SizeMb(XPakOf(zone)))));
            phases.Add((T7Fidelity.ZonePhase(zone, "assets"), AssetCost(megabytes)));
            phases.Add((T7Fidelity.ZonePhase(zone, "link"), LinkCost(megabytes)));
            return phases;
        }

        void Collect(string zone, T7ZonePortResult result)
        {
            string zoneStem = Path.GetFileNameWithoutExtension(zone);
            log($"{Path.GetFileName(zone)}: {(result.Success ? "converted" : "FAILED")}");
            foreach ((string key, int value) in result.Strategies.OrderBy(p => p.Key))
                log($"  {key} x{value}");
            if (result.Success)
            {
                collected.AddOutput(result.OutputFastFile);
                if (HasStreams(zone))
                    collected.AddOutput(Path.Combine(options.OutputFolder, zoneStem + ".xpak"));
            }
            collected.AddProblems(result.Problems.Select(p => $"{zoneStem}: {p}"));
        }

        var units = new List<Unit>();
        using SoundPass? sound = options.ConvertSound ? SoundPass.Start(options, folder, companions, renames, streamedSounds, collected, fidelity) : null;
        if (!options.ConvertSound && companions.SoundBanks.Count > 0)
            fidelity.SoundOff(companions.SoundBanks.Count);
        List<Unit> soundUnits = sound?.Units(stem) ?? [];
        units.AddRange(soundUnits);

        string main = companions.Zones[0];
        var job = new T7ZonePort.Job(ZoneOptions(main, sharedStreams));
        Dictionary<string, double> mainPhases = ZonePhases(main).ToDictionary(p => p.Phase, p => p.Weight, StringComparer.OrdinalIgnoreCase);
        Unit? Stage(string step, string title, Action run, Func<double?>? estimate = null)
        {
            string phase = T7Fidelity.ZonePhase(main, step);
            if (!mainPhases.TryGetValue(phase, out double weight))
                return null;
            var unit = new Unit
            {
                Key = phase,
                Title = title,
                Phases = [(phase, weight)],
                Estimate = estimate,
                Run = () =>
                {
                    if (job.Ended)
                        fidelity.Skip(phase);
                    else
                        run();
                    if (step == "link" && job.Result is { } result)
                        Collect(main, result);
                },
            };
            units.Add(unit);
            return unit;
        }
        Unit walk = Stage("walk", "Read the PC zone", job.Read)!;
        Unit? shaders = Stage("shaders", "Compile shaders", job.Shaders, job.ShaderCost);
        Unit? streams = Stage("streams", "Convert streamed data", job.Streams);
        Unit assets = Stage("assets", "Convert assets", job.Assets)!;
        Unit link = Stage("link", "Link and check", job.Link)!;
        shaders?.After.Add(walk);
        streams?.After.Add(walk);
        assets.After.Add(walk);
        if (shaders != null)
            assets.After.Add(shaders);
        if (streams != null)
            assets.After.Add(streams);
        assets.After.AddRange(soundUnits);
        link.After.Add(assets);

        List<string> languages = companions.Zones.Skip(1).ToList();
        if (languages.Count > 0)
        {
            var languageUnit = new Unit
            {
                Key = "languages",
                Title = languages.Count == 1 ? $"Convert language zone ({Path.GetFileName(languages[0])[..2]})" : $"Convert {languages.Count} language zones",
                Phases = languages.SelectMany(ZonePhases).ToList(),
                Run = () =>
                {
                    foreach (string zone in languages)
                    {
                        T7ZonePortResult result = T7ZonePort.Run(ZoneOptions(zone, sharedStreams.Copy()));
                        fidelity.EndZone(zone);
                        Collect(zone, result);
                    }
                },
            };
            languageUnit.After.Add(link);
            units.Add(languageUnit);
        }

        units.AddRange(MovieUnits(options, movies, collected, fidelity));

        List<Unit> pending = Schedule(units);
        fidelity.Timing ??= T7Timing.Load(T7Timing.PathIn(T7Paths.Work(options.Workspace)));
        fidelity.GpuMovies = T7MovieTranscoder.UseGpu;
        List<Unit> planned = [.. pending];
        fidelity.Plan([.. planned.Select(unit => new T7Fidelity.PlanUnit([.. unit.Phases.Select(phase => phase.Phase)], [.. unit.After.Select(before => planned.IndexOf(before))],
            unit.Lane, unit.Lane == null ? 1 : LaneWidth(unit.Lane)))], options.Concurrent, (shaders ?? walk).Key,
            T7Timing.Mode(options.Concurrent, options.Slots, FFPorter.Core.Common.Tools.Cores.Single));
        try
        {
            if (options.Concurrent)
            {
                foreach (IGrouping<string, Unit> row in pending.GroupBy(unit => unit.Step, StringComparer.Ordinal))
                    fidelity.AddStep(row.Key, row.First().Title, row.SelectMany(unit => unit.Phases));
                RunTogether(pending, fidelity, log);
            }
            else
            {
                foreach (Unit unit in pending)
                    fidelity.AddStep(unit.Key, unit.Title, unit.Phases);
                RunInTurn(pending, fidelity);
            }
        }
        finally
        {
            fidelity.Timing?.Save();
        }

        int unswitched = streamedSounds.Count(id => !switchedSounds.Contains(id));
        if (unswitched > 0)
        {
            string text = $"{unswitched} sounds moved to a stream bank have no alias in the map's zones that could be switched to streaming; they will not play";
            log(text);
            collected.AddWarning(text);
        }
        CopyUsermapFiles(options, roots, collected);

        File.WriteAllText(Workspace.ReportPath(report, ".map-port.json"), JsonSerializer.Serialize(new
        {
            source = input,
            zones = companions.Zones,
            sound_banks = companions.SoundBanks,
            outputs = collected.Outputs,
            renamed_sounds = renames.Count,
            streamed_sounds = streamedSounds.Count,
            problems = collected.Problems,
            warnings = collected.Warnings,
        }, new JsonSerializerOptions { WriteIndented = true }));
        FidelitySnapshot snapshot = fidelity.Tracker.Finish(collected.Problems.Count == 0 ? FidelityStates.Done : FidelityStates.Failed,
            fidelity.Tracker.Headline(built: fidelity.ZonesNotWritten == 0, collected.Problems.Count));
        try
        {
            FidelityProtocol.Save(Workspace.ReportPath(report, ".fidelity.json"), snapshot);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log($"could not write {report}.fidelity.json: {error.Message}");
        }
        return new T7MapPortResult(collected.Problems.Count == 0, collected.Outputs, collected.Problems, collected.Warnings) { Fidelity = snapshot };
    }

    private static List<Unit> Schedule(List<Unit> units)
    {
        var order = new List<Unit>();
        var pending = new List<Unit>(units);
        while (pending.Count > 0)
        {
            Unit next = Next(pending, order);
            order.Add(next);
            pending.Remove(next);
        }
        return order;
    }

    private static Unit Next(List<Unit> pending, List<Unit> finished) =>
        pending.Where(unit => unit.After.All(finished.Contains)).MinBy(unit => unit.Cost)
        ?? throw new InvalidOperationException("the conversion steps wait on each other");

    private static void RunInTurn(List<Unit> pending, T7Fidelity fidelity)
    {
        var finished = new List<Unit>();
        while (pending.Count > 0)
        {
            foreach (Unit unit in pending)
            {
                if (unit.Phases.Count == 1 && unit.Estimate?.Invoke() is double estimate)
                    fidelity.Reweigh(unit.Phases[0].Phase, estimate);
            }
            Unit next = Next(pending, finished);
            fidelity.PlaceNext(next.Key);
            next.Run();
            pending.Remove(next);
            finished.Add(next);
        }
    }

    private static void RunTogether(List<Unit> pending, T7Fidelity fidelity, Action<string> log)
    {
        var finished = new HashSet<Unit>();
        var running = new Dictionary<Task, Unit>();
        var left = pending.GroupBy(unit => unit.Step, StringComparer.Ordinal).ToDictionary(row => row.Key, row => row.Count(), StringComparer.Ordinal);
        var lanes = new Dictionary<string, int>(StringComparer.Ordinal);
        long budget = Math.Clamp(Common.Native.SystemMemory.Total / 8, 1L << 30, 4L << 30), held = 0;
        ExceptionDispatchInfo? failure = null;
        int widest = 0;
        fidelity.EndCurrent();
        while (true)
        {
            List<Unit> ready = pending.Where(unit => unit.After.All(finished.Contains)).ToList();
            foreach (Unit unit in ready)
            {
                if (unit.Phases.Count == 1 && unit.Estimate?.Invoke() is double estimate)
                    fidelity.Reweigh(unit.Phases[0].Phase, estimate);
            }
            if (failure == null)
            {
                foreach (Unit unit in ready.OrderBy(unit => unit.Cost))
                {
                    if (unit.Lane != null && lanes.GetValueOrDefault(unit.Lane) >= LaneWidth(unit.Lane))
                        continue;
                    if (unit.Memory > 0 && held > 0 && held + unit.Memory > budget)
                        continue;
                    pending.Remove(unit);
                    held += unit.Memory;
                    if (unit.Lane != null)
                        lanes[unit.Lane] = lanes.GetValueOrDefault(unit.Lane) + 1;
                    fidelity.PlaceNext(unit.Step);
                    running[Task.Factory.StartNew(unit.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)] = unit;
                }
            }
            widest = Math.Max(widest, running.Count);
            if (running.Count == 0)
                break;
            Task done = Task.WhenAny(running.Keys).GetAwaiter().GetResult();
            Unit ended = running[done];
            running.Remove(done);
            held -= ended.Memory;
            if (ended.Lane != null)
                lanes[ended.Lane]--;
            if (done.Exception is { } error)
                failure ??= ExceptionDispatchInfo.Capture(error.InnerExceptions.Count == 1 ? error.InnerExceptions[0] : error);
            finished.Add(ended);
            if (--left[ended.Step] == 0)
                fidelity.EndStep(ended.Step);
        }
        log($"steps ran side by side, up to {widest} at once");
        failure?.Throw();
        if (pending.Count > 0)
            throw new InvalidOperationException("the conversion steps wait on each other");
    }

    private static int LaneWidth(string lane) => lane == SoundLane ? SoundLaneWidth : 1;

    internal static int SoundLaneWidth => FFPorter.Core.Common.Tools.Cores.Half;

    internal static double WalkCost(double megabytes) => 1 + megabytes * 0.03;

    internal static double ShaderGuess(double megabytes) => 1 + megabytes * 0.05;

    internal static double StreamCost(double megabytes) => 1 + megabytes * 0.05;

    internal static double AssetCost(double megabytes) => 1 + megabytes * 0.25;

    internal static double LinkCost(double megabytes) => 1 + megabytes * 0.2;

    internal static double BankCost(double megabytes, bool cached) => cached ? 0.2 : 1 + megabytes * 0.3;

    private static double SizeMb(string path) => File.Exists(path) ? new FileInfo(path).Length / 1048576.0 : 0;

    private static long SizeOf(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static string ShortName(string stem, string path)
    {
        string name = Path.GetFileName(path);
        return name.Length > stem.Length + 1 && name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase) ? name[(stem.Length + 1)..] : name;
    }

    private static string[] UsermapRoots(string folder) =>
        Path.GetFileName(folder).Equals("zone", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(folder) is { } parent
            ? [folder, parent]
            : [folder];

    private static bool IsUsermap(string folder, string[] roots) =>
        roots.Any(root => File.Exists(Path.Combine(root, "workshop.json")))
        || Path.GetFullPath(folder).Split(Path.DirectorySeparatorChar).Contains("usermaps", StringComparer.OrdinalIgnoreCase);

    private static void CopyUsermapFiles(T7MapPortOptions options, string[] roots, Collected collected)
    {
        foreach (string relative in UsermapFiles)
        {
            string? source = roots.Select(root => Path.Combine(root, relative)).FirstOrDefault(File.Exists);
            if (source == null)
                continue;
            string target = Path.Combine(options.OutputFolder, relative);
            CacheFiles.Copy(source, target);
            collected.AddOutput(target);
        }
    }

    internal static IReadOnlyList<double> MovieWeights(string mainFile, string outputFolder)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(mainFile))!;
        string[] roots = UsermapRoots(folder);
        if (!IsUsermap(folder, roots))
            return [];
        var weights = new List<double>();
        foreach (string source in MovieSources(roots))
        {
            string target = Path.Combine(outputFolder, "video", Path.GetFileNameWithoutExtension(source) + ".mkv");
            if (!Path.GetFullPath(target).Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                weights.Add(MovieCost(source, target));
        }
        return weights;
    }

    private static string? VideoFolder(string[] roots) => roots.Select(root => Path.Combine(root, "video")).FirstOrDefault(Directory.Exists);

    private static IReadOnlyList<string> MovieSources(string[] roots)
    {
        string? videos = VideoFolder(roots);
        if (videos == null)
            return [];
        return Directory.EnumerateFiles(videos)
            .Where(file => T7MovieTranscoder.Extensions.Contains(Path.GetExtension(file)))
            .GroupBy(file => Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(file => Path.GetExtension(file).Equals(".mkv", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(file => file, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<Unit> MovieUnits(T7MapPortOptions options, IReadOnlyList<string> sources, Collected collected, T7Fidelity fidelity)
    {
        var movies = new List<(string Source, string Relative, string Target, double Cost)>();
        foreach (string source in sources)
        {
            string relative = Path.Combine("video", Path.GetFileNameWithoutExtension(source) + ".mkv");
            string target = Path.Combine(options.OutputFolder, relative);
            if (Path.GetFullPath(target).Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                continue;
            movies.Add((source, relative, target, MovieCost(source, target)));
        }
        if (options.Concurrent)
        {
            bool grouped = movies.Count > MostRows;
            return movies.Select(movie => new Unit
            {
                Key = T7Fidelity.MoviePhase(movie.Source),
                Row = grouped ? "movies" : null,
                Title = grouped ? $"Prepare {movies.Count} movies" : "Prepare movie · " + Path.GetFileNameWithoutExtension(movie.Source),
                Phases = [(T7Fidelity.MoviePhase(movie.Source), movie.Cost)],
                Run = () => ConvertMovie(options, movie.Source, movie.Relative, movie.Target, collected, fidelity),
                Lane = MovieLane,
            }).ToList();
        }
        if (movies.Count <= MostRows)
        {
            return movies.Select(movie => new Unit
            {
                Key = T7Fidelity.MoviePhase(movie.Source),
                Title = "Prepare movie · " + Path.GetFileNameWithoutExtension(movie.Source),
                Phases = [(T7Fidelity.MoviePhase(movie.Source), movie.Cost)],
                Run = () => ConvertMovie(options, movie.Source, movie.Relative, movie.Target, collected, fidelity),
            }).ToList();
        }
        var ordered = movies.OrderBy(movie => movie.Cost).ToList();
        return
        [
            new Unit
            {
                Key = "movies",
                Title = $"Prepare {ordered.Count} movies",
                Phases = ordered.Select(movie => (T7Fidelity.MoviePhase(movie.Source), movie.Cost)).ToList(),
                Run = () => ordered.ForEach(movie => ConvertMovie(options, movie.Source, movie.Relative, movie.Target, collected, fidelity)),
            },
        ];
    }

    private static double MovieCost(string source, string target)
    {
        double megabytes = SizeMb(source);
        try
        {
            var converted = new FileInfo(target);
            if (converted.Exists && converted.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(source) && T7Movie.Describe(target)?.WritingApp == T7MovieTranscoder.WritingApp)
                return 0.5;
            if (T7Movie.Describe(source) is { Codec: T7Movie.AvcCodec, Width: T7Movie.Width, Height: T7Movie.Height, HasAudio: false } video
                && Math.Abs(video.Fps - T7Movie.FramesPerSecond) < 0.01)
                return 0.5 + megabytes * 0.01;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
        }
        return 10 + megabytes;
    }

    private static void ConvertMovie(T7MapPortOptions options, string source, string relative, string target, Collected collected, T7Fidelity fidelity)
    {
        fidelity.Enter(T7Fidelity.MoviePhase(source), "Preparing movies", relative);
        try
        {
            T7MovieTranscoder.Outcome outcome;
            using (CacheFiles.Lock(target))
            {
                outcome = T7MovieTranscoder.Prepare(source, target, options.Log, (frames, expected) => fidelity.Step(
                    expected > 0 ? (double)frames / expected : 0,
                    expected > 0 ? $"{relative} · {T7Fidelity.Of(frames, expected)} frames" : $"{relative} · {frames.ToString("N0", CultureInfo.InvariantCulture)} frames"));
            }
            collected.AddOutput(target);
            fidelity.Movie(Path.GetFileName(source), outcome);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            collected.AddWarning($"{relative} was not converted: {error.Message}");
            fidelity.MovieFailed(Path.GetFileName(source), error.Message);
            fidelity.Fail();
        }
        fidelity.Step(1);
    }

    private static Dictionary<uint, string> SoundNamesInZones(IReadOnlyList<string> zones, Action<string> log)
    {
        var names = new Dictionary<uint, string>();
        ReadOnlySpan<byte> suffix = ".snd\0"u8;
        foreach (string zone in zones)
        {
            byte[] data;
            try
            {
                data = T7FastFile.Load(zone).Zone;
            }
            catch (Exception error) when (error is IOException or InvalidDataException)
            {
                log($"  could not read {Path.GetFileName(zone)} for sound names: {error.Message}");
                continue;
            }
            ReadOnlySpan<byte> span = data;
            int at = 0;
            while (at < span.Length)
            {
                int hit = span[at..].IndexOf(suffix);
                if (hit < 0)
                    break;
                int end = at + hit + 4;
                int start = end - 4;
                while (start > 0 && span[start - 1] >= 0x20 && span[start - 1] < 0x7F)
                    start--;
                if (end - start > 4)
                {
                    string name = Encoding.Latin1.GetString(span[start..end]);
                    names.TryAdd(T7SoundBank.HashName(name), name);
                }
                at = end + 1;
            }
        }
        return names;
    }

    private sealed class SoundPass(T7MapPortOptions options, string folder, IReadOnlyList<string> banks, IReadOnlyList<string> zones, Dictionary<string, string> renames,
        ISet<uint> streamed, Collected collected, T7Fidelity fidelity, Lame lame) : IDisposable
    {
        private readonly Dictionary<string, T7SoundBank> _converted = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();
        private Dictionary<uint, string>? _zoneSoundNames;
        private int _remaining = banks.Count;
        private bool _disposed;

        public static SoundPass? Start(T7MapPortOptions options, string folder, Companions companions, Dictionary<string, string> renames, ISet<uint> streamed,
            Collected collected, T7Fidelity fidelity)
        {
            IReadOnlyList<string> banks = companions.SoundBanks;
            if (banks.Count == 0)
                return null;
            Lame lame;
            try
            {
                lame = Lame.Load(options.Workspace);
            }
            catch (FileNotFoundException error)
            {
                collected.AddProblem($"sound banks not converted: {error.Message}");
                fidelity.Tracker.Problem($"sound banks not converted: {error.Message}");
                foreach (string bank in banks)
                    fidelity.SoundBankFailed(Path.GetRelativePath(folder, bank), null, error.Message);
                return null;
            }
            return new SoundPass(options, folder, banks, companions.Zones, renames, streamed, collected, fidelity, lame);
        }

        public List<Unit> Units(string stem)
        {
            Dictionary<string, double> costs = banks.ToDictionary(bank => bank, Cost, StringComparer.OrdinalIgnoreCase);
            if (options.Concurrent)
            {
                bool grouped = banks.Count > MostRows;
                return banks.Select(bank => new Unit
                {
                    Key = T7Fidelity.BankPhase(bank),
                    Row = grouped ? "sound" : null,
                    Title = grouped ? $"Convert sound · {banks.Count} banks" : "Convert sound · " + ShortName(stem, bank),
                    Phases = [(T7Fidelity.BankPhase(bank), costs[bank])],
                    Run = () => Convert(bank),
                    Lane = SoundLane,
                    Memory = 2 * SizeOf(bank),
                }).ToList();
            }
            if (banks.Count <= MostRows)
            {
                return banks.Select(bank => new Unit
                {
                    Key = T7Fidelity.BankPhase(bank),
                    Title = "Convert sound · " + ShortName(stem, bank),
                    Phases = [(T7Fidelity.BankPhase(bank), costs[bank])],
                    Run = () => Convert(bank),
                }).ToList();
            }
            List<string> ordered = banks.OrderBy(bank => costs[bank]).ToList();
            return
            [
                new Unit
                {
                    Key = "sound",
                    Title = $"Convert sound · {banks.Count} banks",
                    Phases = ordered.Select(bank => (T7Fidelity.BankPhase(bank), costs[bank])).ToList(),
                    Run = () => ordered.ForEach(Convert),
                },
            ];
        }

        private double Cost(string bank) =>
            BankCost(SizeMb(bank), T7SoundConvert.IsCached(bank, Path.Combine(options.WorkDirectory, Path.GetRelativePath(folder, bank))));

        private Dictionary<uint, string> ZoneSoundNames()
        {
            lock (_gate)
                return _zoneSoundNames ??= SoundNamesInZones(zones, options.Log);
        }

        private void Convert(string bank)
        {
            string relative = Path.GetRelativePath(folder, bank);
            options.Log($"sound bank {relative}");
            fidelity.Enter(T7Fidelity.BankPhase(bank), "Converting sound", relative);
            int? entries = null;
            try
            {
                string cache = Path.Combine(options.WorkDirectory, relative);
                T7SoundConvert.Result? result = T7SoundConvert.Reuse(bank, cache, options.Log);
                if (result == null)
                {
                    T7SoundBank pc = T7SoundBank.Parse(File.ReadAllBytes(bank));
                    entries = pc.Entries.Count;
                    if (pc.NamesMissing)
                    {
                        Dictionary<uint, string> names = ZoneSoundNames();
                        int found = 0;
                        foreach (T7SoundBank.Entry entry in pc.Entries)
                        {
                            if (names.TryGetValue(entry.Id, out string? name))
                            {
                                entry.Name = name;
                                found++;
                            }
                        }
                        string text = $"sound bank {relative} has no name table; {found} of {pc.Entries.Count} names came back from the map's sound aliases, the rest keep their PC ids";
                        options.Log($"  {text}");
                        collected.AddWarning(text);
                    }
                    result = T7SoundConvert.Convert(pc, options.Workspace, lame, options.Log,
                        (done, total) => fidelity.Step((double)done / total, $"{relative} · {T7Fidelity.Of(done, total)} sounds"));
                    T7SoundConvert.Remember(bank, cache, result, options.Log);
                }
                lock (renames)
                {
                    foreach ((string from, string to) in result.RenamedAssets)
                        renames[from] = to;
                }
                lock (_gate)
                    _converted[relative] = result.Bank;
                foreach (string note in result.Notes.Take(10))
                    options.Log($"  {note}");
                fidelity.SoundBank(result.Bank.Entries.Count, result.Reencoded, result.Resampled, result.Retimed, result.Silenced);
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
            {
                collected.AddProblem($"sound bank {relative}: {error.Message}");
                fidelity.SoundBankFailed(relative, entries, error.Message);
                fidelity.Tracker.Problem($"sound bank {relative}: {error.Message}");
                fidelity.Fail();
            }
            fidelity.Step(1);
            if (Interlocked.Decrement(ref _remaining) == 0)
                Finish();
        }

        private void Finish()
        {
            Dispose();
            var converted = new Dictionary<string, T7SoundBank>(StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                foreach (string bank in banks)
                {
                    string relative = Path.GetRelativePath(folder, bank);
                    if (_converted.TryGetValue(relative, out T7SoundBank? sound))
                        converted[relative] = sound;
                }
                _converted.Clear();
                _zoneSoundNames = null;
            }

            T7SoundBudget.Result budget = T7SoundBudget.Fit(converted, streamed);
            foreach (T7SoundBudget.Move move in budget.Moves)
            {
                string text = $"sound bank {move.LoadedBank}: the {move.Count} longest sounds ({Megabytes(move.Bytes)}) now stream from {Path.GetFileName(move.StreamBank)}, "
                    + $"leaving {Megabytes(move.After)} of {Megabytes(move.Before)} loaded - the PS4 keeps about {Megabytes(T7SoundBudget.LoadedBytes)} of a map's loaded sound in memory";
                options.Log(text);
                collected.AddWarning(text);
                fidelity.SoundStreamed(move.Count, move.Longest);
            }
            if (!budget.Fits)
            {
                string text = $"the map's loaded sound ({Megabytes(budget.Loaded)}) is still more than the PS4 keeps in memory "
                    + $"({Megabytes(T7SoundBudget.LoadedBytes)}); the map may stop loading with \"SOUND: Allocation failed\"";
                options.Log(text);
                collected.AddWarning(text);
            }

            foreach ((string relative, T7SoundBank sound) in converted)
            {
                string target = Path.Combine(options.OutputFolder, relative);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, sound.Build());
                    collected.AddOutput(target);
                }
                catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    collected.AddProblem($"sound bank {relative}: {error.Message}");
                    fidelity.Tracker.Problem($"sound bank {relative}: {error.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            lame.Dispose();
        }
    }

    private static string Megabytes(long bytes) => $"{bytes / 1048576.0:F1} MB";
}
