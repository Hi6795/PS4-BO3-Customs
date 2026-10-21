using FFPorter.Desktop.Services;

namespace FFPorter.Desktop.Models;

public sealed class ConsoleDrive(string? storage) : Observable
{
    private string _summary = "";

    public string? Storage { get; } = storage;
    public string Title { get; } = ConsoleFtp.StorageName(storage);
    public string Root { get; } = ConsoleFtp.RootOf(storage);
    public int Order { get; } = storage == null ? -1 : ConsoleFtp.Drives.TakeWhile(drive => drive != storage).Count();

    public string Summary { get => _summary; set => Set(ref _summary, value); }
}

public sealed class ConsoleItem : Observable
{
    private const int Listed = 6;
    private static readonly string[] ModZones = ["core_mod.ff", "zm_mod.ff", "mp_mod.ff", "cp_mod.ff"];

    private IReadOnlyList<ConsoleFile> _files = [];
    private long _size = -1;
    private string _problem = "";
    private bool _removing;

    public required string Name { get; init; }
    public required string Folder { get; init; }
    public required bool IsMod { get; init; }
    public required ConsoleDrive Drive { get; init; }

    public string Kind => IsMod ? CommunityKinds.Mod
        : Name.StartsWith("zm_", StringComparison.OrdinalIgnoreCase) ? CommunityKinds.Zombies
        : Name.StartsWith("mp_", StringComparison.OrdinalIgnoreCase) ? CommunityKinds.Multiplayer
        : Name.StartsWith("cp_", StringComparison.OrdinalIgnoreCase) ? CommunityKinds.Campaign
        : "";

    public string Tag => Kind switch
    {
        CommunityKinds.Zombies => "ZM",
        CommunityKinds.Multiplayer => "MP",
        CommunityKinds.Campaign => "CP",
        CommunityKinds.Mod => "MOD",
        _ => "MAP",
    };

    private string KindText => Kind switch
    {
        CommunityKinds.Zombies => "Zombies map",
        CommunityKinds.Multiplayer => "Multiplayer map",
        CommunityKinds.Campaign => "Campaign map",
        CommunityKinds.Mod => "Mod",
        _ => "Map",
    };

    public bool IsMeasured => _size >= 0;
    public long Size => Math.Max(0, _size);
    public int Files => _files.Count;
    public string SizeText => IsMeasured ? Job.SizeText(_size) : "…";
    public string CountText => !IsMeasured ? "" : _files.Count == 1 ? "1 file" : $"{_files.Count} files";

    public void Measure(ConsoleSize size)
    {
        _files = size.Files;
        _size = size.Bytes;
        RaiseAll();
    }

    public string Problem
    {
        get => _problem;
        set
        {
            if (Set(ref _problem, value))
                RaiseAll();
        }
    }

    public bool IsRemoving
    {
        get => _removing;
        set
        {
            if (Set(ref _removing, value))
                RaiseAll();
        }
    }

    public bool CanRemove => !IsRemoving;

    private string Flaw => !IsMeasured ? ""
        : _files.Count == 0 ? "Empty folder"
        : _files.Any(file => file.Path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) ? "Send not finished"
        : Holds(IsMod ? ModZones : [Name + ".ff"]) ? ""
        : IsMod ? "No mod zone inside" : $"No {Name}.ff inside";

    public string Note => IsRemoving ? "Removing" : Problem.Length > 0 ? Problem : Flaw;
    public bool HasNote => Note.Length > 0;
    public string NoteState => IsRemoving ? "" : Problem.Length > 0 ? RunStates.Failed : Flaw.Length > 0 ? RunStates.Cancelled : "";

    public string Tip
    {
        get
        {
            var lines = new List<string> { Name, IsMeasured ? $"{KindText} · {SizeText} in {CountText}" : KindText, $"{Drive.Title} · {Folder}" };
            if (Flaw.Length > 0)
                lines.Add(Flaw == "Send not finished" ? "A send to this folder did not finish. Send it again to complete it." : $"{Flaw}: the game does not list it.");
            if (_files.Count > 0)
            {
                lines.Add("");
                lines.AddRange(_files.OrderByDescending(file => file.Size).Take(Listed).Select(file => $"{file.Path}   {Job.SizeText(file.Size)}"));
                if (_files.Count > Listed)
                    lines.Add($"and {_files.Count - Listed} more");
            }
            return string.Join("\n", lines);
        }
    }

    private bool Holds(IReadOnlyList<string> zones) =>
        _files.Any(file => zones.Any(zone => file.Path.Equals(zone, StringComparison.OrdinalIgnoreCase) || file.Path.Equals("zone/" + zone, StringComparison.OrdinalIgnoreCase)));

    private void RaiseAll()
    {
        foreach (string property in (string[])[nameof(IsMeasured), nameof(Size), nameof(Files), nameof(SizeText), nameof(CountText), nameof(CanRemove), nameof(Note), nameof(HasNote),
            nameof(NoteState), nameof(Tip)])
            Raise(property);
    }
}
