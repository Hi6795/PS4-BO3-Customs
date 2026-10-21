using System.IO;
using FFPorter.Core.Common.Fidelity;

namespace FFPorter.Desktop.Models;

public sealed class ConvertedItem : Observable
{
    private static readonly string[] ModZones = ["core_mod.ff", "zm_mod.ff", "mp_mod.ff", "cp_mod.ff"];

    private string _problem = "";
    private bool _hasConsole, _locked, _sending, _deleting, _sent;

    public required string Name { get; init; }
    public required string Folder { get; init; }
    public required bool IsMod { get; init; }
    public string? Remote { get; init; }
    public string Place { get; init; } = "";
    public long Size { get; init; }
    public int Files { get; init; }
    public DateTime When { get; init; }
    public bool Downloaded { get; init; }
    public string ScoreText { get; init; } = "";
    public string ScoreGrade { get; init; } = "";
    public IReadOnlyList<string> Scores { get; init; } = [];

    public string Kind => IsMod ? CommunityKinds.Mod
        : Remote == null ? ""
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
        _ => "ZONE",
    };

    private string KindText => Kind switch
    {
        CommunityKinds.Zombies => "Zombies map",
        CommunityKinds.Multiplayer => "Multiplayer map",
        CommunityKinds.Campaign => "Campaign map",
        CommunityKinds.Mod => "Mod",
        _ => "Zone",
    };

    public IReadOnlyList<string> Last => IsMod ? ModZones : [Name + ".ff"];

    public static bool IsModZone(string file) => ModZones.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase);

    public string SizeText => Job.SizeText(Size);
    public string CountText => Files == 1 ? "1 file" : $"{Files} files";
    public bool HasScore => ScoreText.Length > 0;

    public bool HasConsole { get => _hasConsole; set { if (Set(ref _hasConsole, value)) RaiseAll(); } }
    public bool Locked { get => _locked; set { if (Set(ref _locked, value)) RaiseAll(); } }
    public bool IsSending { get => _sending; set { if (Set(ref _sending, value)) RaiseAll(); } }
    public bool IsDeleting { get => _deleting; set { if (Set(ref _deleting, value)) RaiseAll(); } }
    public bool Sent { get => _sent; set { if (Set(ref _sent, value)) RaiseAll(); } }
    public string Problem { get => _problem; set { if (Set(ref _problem, value)) RaiseAll(); } }

    private bool IsBusy => IsSending || IsDeleting;

    public bool ShowSend => HasConsole && Remote != null;
    public bool CanSend => ShowSend && !Locked && !IsBusy;
    public bool CanDelete => !Locked && !IsBusy;

    public string Note => IsDeleting ? "Deleting" : IsSending ? "Sending" : Problem.Length > 0 ? Problem : Sent ? "On the console" : Downloaded ? "Downloaded" : "";
    public bool HasNote => Note.Length > 0;
    public string NoteState => IsBusy ? "" : Problem.Length > 0 ? RunStates.Failed : "";

    public string Tip
    {
        get
        {
            var lines = new List<string> { Name, $"{KindText} · {SizeText} in {CountText}", Folder, $"{(Downloaded ? "Downloaded" : "Converted")} {When:d MMM yyyy, HH:mm}" };
            if (Scores.Count > 0)
            {
                lines.Add("");
                lines.AddRange(Scores);
            }
            return string.Join("\n", lines);
        }
    }

    public static (string Text, string Grade, IReadOnlyList<string> Lines) Score(IEnumerable<(string Zone, FidelitySnapshot Report)> reports)
    {
        (string Zone, int Percent, string Grade)[] done = [.. reports.Where(entry => entry.Report.State == FidelityStates.Done && entry.Report.Percent is not null)
            .Select(entry => (entry.Zone, entry.Report.Percent!.Value, entry.Report.Grade ?? "")).OrderBy(entry => entry.Zone, StringComparer.OrdinalIgnoreCase)];
        if (done.Length == 0)
            return ("", "", []);
        int low = done.Min(entry => entry.Percent), high = done.Max(entry => entry.Percent);
        string grade = done.First(entry => entry.Percent == low).Grade;
        return (low == high ? $"{low}%" : $"{low}–{high}%", grade, [.. done.Select(entry => $"{entry.Zone}: {entry.Percent}% {entry.Grade}".TrimEnd())]);
    }

    private void RaiseAll()
    {
        foreach (string property in (string[])[nameof(ShowSend), nameof(CanSend), nameof(CanDelete), nameof(Note), nameof(HasNote), nameof(NoteState)])
            Raise(property);
    }
}
