using System.IO;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop.Services;

public sealed record Removal(int Files, long Bytes, IReadOnlyList<string> Problems, string? Kept = null);

public static class Cleanup
{
    private static readonly EnumerationOptions Everything = new() { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true };
    private static readonly EnumerationOptions EverythingOrFail = new() { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };

    public static long FolderSize(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("*", Everything).Sum(file => file.Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public static Removal ClearFolders(IEnumerable<string> folders)
    {
        var problems = new List<string>();
        long bytes = 0;
        foreach (string folder in folders)
        {
            long size = FolderSize(folder);
            try
            {
                Directory.Delete(folder, recursive: true);
                bytes += size;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileName(folder)}: {error.Message}");
                bytes += Math.Max(0, size - FolderSize(folder));
            }
        }
        return new Removal(0, bytes, problems);
    }

    public static Removal DeleteOriginals(IReadOnlyList<Job> jobs, Edition edition, string? gameFolder, IReadOnlyList<string> outputFolders)
    {
        string folder = jobs[0].SourceFolder;
        if (edition.IsGameFolder(folder) || (gameFolder != null && Inside(folder, gameFolder)))
            return new Removal(0, 0, [], $"they are part of the {edition.GameFolderName}");

        var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var package = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string root = folder;
        try
        {
            foreach (Job job in jobs)
            {
                if (edition.FindOriginals(job) is not { } found)
                    return new Removal(0, 0, [], "this converter cannot tell which files belong to them");
                own.UnionWith(found.Files);
                package.UnionWith(found.PackageFiles);
                root = found.Root;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new Removal(0, 0, [error.Message]);
        }
        if (outputFolders.Any(output => Inside(folder, output) || Inside(output, root)))
            return new Removal(0, 0, [], "the converted files went into the same folder");

        var problems = new List<string>();
        var emptied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        long bytes = 0;
        void Delete(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                try
                {
                    var file = new FileInfo(path);
                    if (!file.Exists || !Inside(file.FullName, root))
                        continue;
                    long length = file.Length;
                    file.Delete();
                    count++;
                    bytes += length;
                    emptied.Add(file.DirectoryName!);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    problems.Add(error.Message);
                }
            }
        }

        Delete(own);
        if (problems.Count == 0 && HoldsOnly(root, package))
            Delete(package);
        RemoveEmptyFolders(emptied, root);
        return new Removal(count, bytes, problems);
    }

    private static bool HoldsOnly(string root, HashSet<string> files)
    {
        try
        {
            return Directory.Exists(root) && Directory.EnumerateFiles(root, "*", EverythingOrFail).All(files.Contains);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void RemoveEmptyFolders(IEnumerable<string> folders, string root)
    {
        foreach (string start in folders.OrderByDescending(path => path.Length))
        {
            for (string? current = start; current != null && Inside(current, root); current = Path.GetDirectoryName(current))
            {
                try
                {
                    if (!Directory.Exists(current))
                        continue;
                    if (Directory.EnumerateFileSystemEntries(current).Any())
                        break;
                    Directory.Delete(current);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    break;
                }
            }
        }
    }

    private static bool Inside(string path, string parent)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        string prefix = Path.EndsInDirectorySeparator(top) ? top : top + Path.DirectorySeparatorChar;
        return full.Equals(top, StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
