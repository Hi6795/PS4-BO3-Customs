using System.IO;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop;

public abstract class Edition
{
    private static Edition? _current;

    public static Edition Current => _current ?? throw new InvalidOperationException("the application did not set its edition");

    public static void Use(Edition edition) => _current = edition;

    public abstract string Codename { get; }

    public abstract string Title { get; }

    public virtual string Product => Title.Split(" — ", 2)[0];

    public abstract string Subtitle { get; }

    public virtual string? Author => null;

    public abstract string GameName { get; }

    public abstract string GameLabel { get; }

    public abstract string SettingsFile { get; }

    public abstract string GameFolderName { get; }

    public abstract string GameFolderHint { get; }

    public abstract string GameFolderOption { get; }

    public abstract bool TryGameFolder(string chosen, out string? folder);

    public virtual void PrepareTools(Action<string> log)
    {
    }

    public virtual string? CommunityList => null;

    public virtual string? UpdateSource => null;

    public virtual string? ConsoleArchive => null;

    public virtual string? ConsolePlugin => null;

    public bool HasConsoleFiles => UpdateSource != null && ConsoleArchive != null && ConsolePlugin != null;

    public virtual bool FindsInstalled => false;

    public virtual IReadOnlyList<Installed> FindInstalled(string? gameFolder) => [];

    public virtual string? ConsoleFolder(Job job) => null;

    public virtual string? ConsoleFolder(string name, bool mod) => null;

    public virtual bool IsConverted(string zone) => true;

    public virtual IReadOnlyList<string> CacheFolders() => [];

    public virtual string? CacheFolder(Job job) => null;

    public virtual bool IsGameFolder(string folder) => false;

    public virtual Originals? FindOriginals(Job job) => null;

    public virtual double? EstimateSeconds(Job job, bool sideBySide) => null;

    public abstract bool Accepts(string file, out string? why);

    public abstract IEnumerable<Job> Jobs(IReadOnlyList<string> files);

    public abstract int RunBackend(string[] arguments, TextWriter output, TextWriter error);
}
