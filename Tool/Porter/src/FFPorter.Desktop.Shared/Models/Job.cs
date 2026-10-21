using System.IO;

namespace FFPorter.Desktop.Models;

public sealed class Job : Observable
{
    private string _state = RunStates.Ready, _statusText = "Ready", _stageText = "", _remainingText = "";
    private double _progress;
    private FidelityView? _report;
    private bool _originalDeleted, _onConsole, _expanded, _paused;

    public required string Kind { get; init; }
    public required string Name { get; init; }
    public required string MainFile { get; init; }
    public string? Mod { get; init; }
    public JobGroup? Group { get; set; }

    public string Key => Mod == null ? Name : $"{Mod}.{Name}";

    public string Title => Mod == null ? Name : $"{Mod} · {Name}";
    public string RowTitle => Group != null ? Name : Title;
    public IReadOnlyList<string> CompanionZones { get; init; } = [];
    public string CompanionSummary { get; init; } = "";
    public int Streams { get; init; }
    public int SoundBanks { get; init; }
    public long Length { get; init; }
    public bool UsesDll { get; init; }

    public string GameLabel => Edition.Current.GameLabel;
    public string GameName => Edition.Current.GameName;

    public string Codename => Edition.Current.Codename;

    public string Size => SizeText(Length);

    public static string SizeText(long length) =>
        length >= 1073741824 ? $"{length / 1073741824d:0.##} GB" : length >= 1048576 ? $"{length / 1048576d:0.#} MB" : $"{length / 1024d:0.#} KB";

    public string Detail
    {
        get
        {
            var parts = new List<string> { $"{GameName} {Kind.ToLowerInvariant()}" };
            if (CompanionZones.Count > 0 && CompanionSummary.Length > 0)
                parts.Add(CompanionSummary);
            if (Streams > 0)
                parts.Add($"{Streams} xpak");
            if (SoundBanks > 0)
                parts.Add($"{SoundBanks} sound bank{(SoundBanks == 1 ? "" : "s")}");
            if (UsesDll)
                parts.Add("uses a PC DLL");
            return string.Join("  ·  ", parts);
        }
    }

    public string MetaText
    {
        get
        {
            var parts = new List<string>();
            if (Streams > 0)
                parts.Add($"{Streams} xpak");
            if (SoundBanks > 0)
                parts.Add($"{SoundBanks} sound bank{(SoundBanks == 1 ? "" : "s")}");
            return parts.Count == 0 ? "" : "    " + string.Join(" · ", parts);
        }
    }

    public string SourceFolder => Path.GetDirectoryName(MainFile) ?? "";

    public bool OriginalDeleted
    {
        get => _originalDeleted;
        set
        {
            if (!Set(ref _originalDeleted, value))
                return;
            Raise(nameof(Tip));
            RaiseState();
        }
    }

    public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }

    public bool IsPaused
    {
        get => _paused;
        set
        {
            if (!Set(ref _paused, value))
                return;
            Raise(nameof(PauseGlyph));
            Raise(nameof(PauseText));
            Raise(nameof(IconState));
        }
    }

    public string PauseGlyph => IsPaused ? "\uE768" : "\uE769";

    public string PauseText => IsPaused ? "Resume" : "Pause";

    public string IconState => IsPaused && IsRunning ? Paused : State;

    public const string Paused = "paused";

    public bool IsQueued => State == RunStates.Queued;

    public bool OnConsole { get => _onConsole; set { if (Set(ref _onConsole, value)) RaiseState(); } }

    public string Tip => OriginalDeleted ? $"{MainFile}\n{Detail}\nIts PC original was deleted after it converted." : $"{MainFile}\n{Detail}";

    public string State { get => _state; set { if (Set(ref _state, value)) RaiseState(); } }
    public string StatusText { get => _statusText; set { if (Set(ref _statusText, value)) RaiseState(); } }
    public string StageText { get => _stageText; set => Set(ref _stageText, value); }
    public double Progress { get => _progress; set { if (Set(ref _progress, value)) Raise(nameof(ProgressText)); } }
    public string ProgressText => $"{Math.Floor(Progress * 100):0}%";
    public bool IsRunning => State == RunStates.Running;

    public double? Estimate { get; set; }
    public DateTime? StartedAt { get; set; }
    public string RemainingText { get => _remainingText; set { if (Set(ref _remainingText, value)) Raise(nameof(HasRemaining)); } }
    public bool HasRemaining => RemainingText.Length > 0;

    public double? Remaining
    {
        get
        {
            if (!IsRunning)
                return null;
            if (Report?.RemainingSeconds is double live)
                return live;
            if (Estimate is not double whole || StartedAt is not DateTime started)
                return null;
            return Math.Max(whole - (DateTime.UtcNow - started).TotalSeconds, whole * 0.1);
        }
    }

    public FidelityView? Report { get => _report; set { if (Set(ref _report, value)) { Raise(nameof(HasScore)); RaiseState(); } } }
    public bool HasScore => Report != null;

    public bool ShowScore => Report is { HasScore: true } && State is RunStates.Ready or RunStates.Done;

    public bool IsEarlier => State == RunStates.Ready && StatusText == "Converted earlier";

    public bool IsConverted => State == RunStates.Done || IsEarlier;

    public string Note => State switch
    {
        RunStates.Running => "",
        RunStates.Queued => "Waiting",
        RunStates.Done => OnConsole ? "On the console" : OriginalDeleted ? "Original deleted" : ShowScore ? "" : "Converted",
        RunStates.Ready => OnConsole && IsEarlier ? "On the console" : StatusText is "Ready" or "Converted earlier" ? "" : StatusText,
        _ => StatusText,
    };

    public bool HasNote => Note.Length > 0;

    private void RaiseState()
    {
        Raise(nameof(IsRunning));
        Raise(nameof(IsQueued));
        Raise(nameof(IconState));
        Raise(nameof(ShowScore));
        Raise(nameof(IsEarlier));
        Raise(nameof(IsConverted));
        Raise(nameof(Note));
        Raise(nameof(HasNote));
    }
}

public sealed record Originals(IReadOnlyList<string> Files, IReadOnlyList<string> PackageFiles, string Root);

public sealed record ConvertSpeed(string? Output, bool Parallel, int Slots, bool MultiCore, bool GpuVideo);

public sealed record Installed(string Title, string Kind, string Name, string Folder, string? Picture, long Size, string Type = "");

public sealed class JobGroup(string name) : Observable
{
    private bool _expanded = true;

    public string Name { get; } = name;
    public List<Job> Jobs { get; } = [];

    public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }

    public string State
    {
        get
        {
            if (Jobs.Any(job => job.State == RunStates.Running))
                return RunStates.Running;
            if (Jobs.Any(job => job.State == RunStates.Queued))
                return RunStates.Queued;
            if (Jobs.Any(job => job.State == RunStates.Failed))
                return RunStates.Failed;
            if (Jobs.Any(job => job.State == RunStates.Cancelled))
                return RunStates.Cancelled;
            return Jobs.Count > 0 && Jobs.All(job => job.State == RunStates.Done) ? RunStates.Done : RunStates.Ready;
        }
    }

    public bool IsRunning => State == RunStates.Running;
    public bool IsActive => Jobs.Any(job => job.State is RunStates.Running or RunStates.Queued);

    public double Progress => Jobs.Count == 0 ? 0 : Jobs.Sum(job => job.State == RunStates.Done ? 1 : job.State == RunStates.Running ? job.Progress : 0) / Jobs.Count;
    public string ProgressText => $"{Math.Floor(Progress * 100):0}%";

    public string Summary => $"{Jobs.Count} zone{(Jobs.Count == 1 ? "" : "s")}";
    public string SizeText => Job.SizeText(Jobs.Sum(job => job.Length));
    public string DoneText => $"{Jobs.Count(job => job.State == RunStates.Done)} of {Jobs.Count} done";

    private IEnumerable<Job> Scored => Jobs.Where(job => job.ShowScore && job.Report?.Percent != null);

    public string ScoreText
    {
        get
        {
            int[] scores = [.. Scored.Select(job => job.Report!.Percent!.Value)];
            if (scores.Length == 0)
                return "";
            return scores.Min() == scores.Max() ? $"{scores.Min()}%" : $"{scores.Min()}–{scores.Max()}%";
        }
    }

    public string ScoreGrade => Scored.MinBy(job => job.Report!.Percent!.Value)?.Report?.ScoreGrade ?? "";

    public string Note
    {
        get
        {
            int failed = Jobs.Count(job => job.State == RunStates.Failed);
            int cancelled = Jobs.Count(job => job.State == RunStates.Cancelled);
            return failed > 0 ? $"{failed} failed" : cancelled > 0 ? $"{cancelled} cancelled" : "";
        }
    }

    public bool HasNote => Note.Length > 0;
    public string NoteState => Jobs.Any(job => job.State == RunStates.Failed) ? RunStates.Failed : RunStates.Cancelled;

    public void Add(Job job)
    {
        Jobs.Add(job);
        job.PropertyChanged += JobChanged;
        Refresh();
    }

    public void Remove(Job job)
    {
        if (Jobs.Remove(job))
            job.PropertyChanged -= JobChanged;
        Refresh();
    }

    private void JobChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Job.State) or nameof(Job.Progress) or nameof(Job.Report) or nameof(Job.StatusText))
            Refresh();
    }

    private void Refresh()
    {
        foreach (string property in (string[])[nameof(State), nameof(IsRunning), nameof(IsActive), nameof(Progress), nameof(ProgressText), nameof(Summary),
            nameof(SizeText), nameof(DoneText), nameof(ScoreText), nameof(ScoreGrade), nameof(Note), nameof(HasNote), nameof(NoteState)])
            Raise(property);
    }
}

public static class JobScanner
{
    private const int FolderDepth = 3;

    public static List<Job> Scan(IEnumerable<string> paths, Action<string> log)
    {
        Edition edition = Edition.Current;
        var files = new List<string>();
        foreach (string input in paths)
        {
            try
            {
                string path = Path.GetFullPath(input);
                if (Directory.Exists(path))
                    files.AddRange(FastFilesIn(path, FolderDepth));
                else if (File.Exists(path) && path.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
                    files.Add(path);
                else
                    log($"Skipped {input}: not a .ff file or a folder");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                log($"Skipped {input}: {error.Message}");
            }
        }

        var mine = new List<string>();
        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (edition.Accepts(file, out string? why))
                mine.Add(file);
            else
                log($"Skipped {file}: {why}");
        }
        return [.. edition.Jobs(mine)];
    }

    private static IEnumerable<string> FastFilesIn(string folder, int depth)
    {
        foreach (string file in Directory.EnumerateFiles(folder, "*.ff"))
            yield return file;
        if (depth <= 0)
            yield break;
        foreach (string child in Directory.EnumerateDirectories(folder))
        {
            foreach (string file in FastFilesIn(child, depth - 1))
                yield return file;
        }
    }
}
