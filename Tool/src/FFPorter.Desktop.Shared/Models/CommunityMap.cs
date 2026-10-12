using System.Windows.Media;

namespace FFPorter.Desktop.Models;

public static class LocalCopies
{
    public const string None = "none", Current = "current", Outdated = "outdated";
}

public static class CommunityKinds
{
    public const string Zombies = "zombies", Multiplayer = "multiplayer", Campaign = "campaign", Mod = "mod", Console = "console";

    public static string Of(string? stated, string name) => stated?.Trim().ToLowerInvariant() switch
    {
        "zm" or "zombie" or "zombies" => Zombies,
        "mp" or "multiplayer" => Multiplayer,
        "cp" or "campaign" => Campaign,
        "mod" or "mods" => Mod,
        _ => name.StartsWith("zm_", StringComparison.OrdinalIgnoreCase) ? Zombies
            : name.StartsWith("mp_", StringComparison.OrdinalIgnoreCase) ? Multiplayer
            : name.StartsWith("cp_", StringComparison.OrdinalIgnoreCase) ? Campaign
            : Mod,
    };

    public static string Label(string kind) => kind switch
    {
        Zombies => "Zombies",
        Multiplayer => "Multiplayer",
        Campaign => "Campaign",
        Console => "For the console",
        _ => "Mod",
    };
}

public sealed record CommunityFile(string Name, Uri Link, long Size, string Sha256);

public sealed class CommunityMap : Observable
{
    private static readonly string[] ModZones = ["core_mod.ff", "zm_mod.ff", "mp_mod.ff", "cp_mod.ff"];

    private string _state = RunStates.Ready, _stageText = "", _problem = "", _local = LocalCopies.None;
    private double _progress;
    private long _fetched;
    private bool _hasConsole, _sent, _stale, _found, _locked, _canPause, _paused, _light;
    private ImageSource? _picture;

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Folder { get; init; }
    public required IReadOnlyList<Uri> Parts { get; init; }
    public IReadOnlyList<CommunityFile> Extras { get; init; } = [];
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string Version { get; init; } = "";
    public long Size { get; init; }
    public Uri? Thumbnail { get; init; }
    public string Sha256 { get; init; } = "";
    public string Kind { get; init; } = CommunityKinds.Zombies;

    public bool IsMod => Kind == CommunityKinds.Mod;
    public bool IsConsole => Kind == CommunityKinds.Console;

    public IReadOnlyList<string> MainFiles => IsConsole ? [.. Extras.Select(extra => extra.Name)] : IsMod ? ModZones : [Id + ".ff"];

    public IReadOnlyList<string> Required => IsConsole ? [] : MainFiles;

    public string Stamp => Extras.Count == 0 ? Sha256 : string.Join("+", Extras.Select(extra => extra.Sha256).Prepend(Sha256));

    public string SizeText => Size > 0 ? Job.SizeText(Size) : "";
    public string VersionText => Version.Length == 0 ? "" : "   " + (Version.StartsWith('v') || Version.StartsWith('V') ? Version : "v" + Version);
    public bool HasDescription => Description.Length > 0;

    public string Meta
    {
        get
        {
            var parts = new List<string> { CommunityKinds.Label(Kind) };
            if (Size > 0)
                parts.Add(SizeText);
            if (Author.Length > 0)
                parts.Add($"by {Author}");
            return string.Join("  ·  ", parts);
        }
    }

    public string Tip => $"{Title}\n{Id}{(Size > 0 ? " · " + SizeText : "")}{(Parts.Count > 1 ? $" · {Parts.Count} parts" : "")}{(Description.Length > 0 ? "\n\n" + Description : "")}";

    public ImageSource? Picture { get => _picture; set { if (Set(ref _picture, value)) Raise(nameof(HasPicture)); } }
    public bool HasPicture => Picture != null;

    public string State { get => _state; set { if (Set(ref _state, value)) RaiseState(); } }
    public string Local { get => _local; set { if (Set(ref _local, value)) RaiseState(); } }
    public string Problem { get => _problem; set { if (Set(ref _problem, value)) RaiseState(); } }
    public bool HasConsole { get => _hasConsole; set { if (Set(ref _hasConsole, value)) RaiseState(); } }
    public bool Sent { get => _sent; set { if (Set(ref _sent, value)) RaiseState(); } }
    public bool Stale { get => _stale; set { if (Set(ref _stale, value)) RaiseState(); } }
    public bool Found { get => _found; set { if (Set(ref _found, value)) RaiseState(); } }
    public bool Locked { get => _locked; set { if (Set(ref _locked, value)) RaiseState(); } }
    public bool Light { get => _light; set { if (Set(ref _light, value)) RaiseState(); } }
    public long Fetched { get => _fetched; set { if (Set(ref _fetched, value)) RaiseState(); } }
    public string StageText { get => _stageText; set => Set(ref _stageText, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    public bool CanPause { get => _canPause; set => Set(ref _canPause, value); }

    public bool IsPaused
    {
        get => _paused;
        set
        {
            if (!Set(ref _paused, value))
                return;
            Raise(nameof(PauseText));
            Raise(nameof(PauseGlyph));
        }
    }

    public string PauseText => IsPaused ? "Resume" : "Pause";
    public string PauseGlyph => IsPaused ? "" : "";

    public bool IsRunning => State == RunStates.Running;
    public bool IsBusy => State is RunStates.Running or RunStates.Queued;
    public bool OnConsole => HasConsole && (Sent || (Found && !Stale));
    public bool CanStart => !IsBusy && (Local != LocalCopies.Current || (HasConsole && !Sent));
    public bool CanOpen => !IsBusy && Local != LocalCopies.None;
    public bool CanDelete => !IsBusy && (Local != LocalCopies.None || Fetched > 0);
    public bool CanResend => !IsBusy && Local == LocalCopies.Current && HasConsole && Sent;
    public bool CanRemove => !IsBusy && !IsConsole && HasConsole && (Sent || Stale || Found);
    public bool CanCancel => IsBusy && !Locked;
    public bool HasMenu => CanOpen || CanDelete || CanResend || CanRemove;

    public string ActionText => Local == LocalCopies.Current ? "Send to console" : State == RunStates.Failed ? "Retry" : Fetched > 0 ? "Resume"
        : Local == LocalCopies.Outdated || (HasConsole && Stale) ? "Update" : "Download";

    public string Note => State switch
    {
        RunStates.Queued => "Waiting",
        RunStates.Running => "",
        RunStates.Failed => Problem.Length > 0 ? Problem : "The download failed",
        RunStates.Cancelled => "Cancelled",
        _ when Problem.Length > 0 => Problem,
        _ => Local switch
        {
            LocalCopies.Current => OnConsole ? "Downloaded · on the console" : "Downloaded",
            LocalCopies.Outdated => $"A newer {Newer} is available",
            _ => Fetched > 0 ? Kept : OnConsole ? "On the console" : HasConsole && Stale ? $"On the console · a newer {Newer} is available" : "",
        },
    };

    private string Newer => Light ? "plugin" : "version";

    private string Kept => Size > 0 && Fetched >= Size ? "Downloaded, not unpacked yet" : $"Partly downloaded · {Job.SizeText(Fetched)}{(Size > 0 ? " of " + SizeText : "")}";

    public bool HasNote => Note.Length > 0;

    public string NoteState => State is RunStates.Failed or RunStates.Cancelled ? State : Problem.Length > 0 ? RunStates.Failed
        : Local == LocalCopies.Current || (Local == LocalCopies.None && Fetched == 0 && OnConsole) ? RunStates.Done : "";

    private void RaiseState()
    {
        foreach (string property in (string[])[nameof(IsRunning), nameof(IsBusy), nameof(OnConsole), nameof(CanStart), nameof(CanOpen), nameof(CanDelete), nameof(CanResend), nameof(CanRemove),
            nameof(CanCancel), nameof(HasMenu), nameof(ActionText), nameof(Note), nameof(HasNote), nameof(NoteState)])
            Raise(property);
    }
}
