using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using FFPorter.Core.Common.Tools;

namespace FFPorter.Core.T7.Scripts;

public static class ActsInstall
{
    public const string Release = "https://api.github.com/repos/ate47/atian-cod-tools/releases/latest";
    public const string Repository = "https://github.com/ate47/atian-cod-tools";
    public const string Asset = "acts.zip";

    private static readonly string[] Files =
    [
        "bin/acts.exe", "bin/acts-common.dll", "bin/acts-bo3.dll", "bin/version",
        "bin/data/asset_types.json", "bin/data/games/bo3.json",
    ];

    private static readonly string[] Folders = ["bin/data/keys/", "licenses/"];

    private const string RuntimeFolder = "acts_runtime";

    private static readonly string[] Runtime = ["msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll"];

    private static readonly Dictionary<string, bool> Started = new(StringComparer.OrdinalIgnoreCase);

    public static string Folder => Path.Combine(Workspace.WorkDirectory, "tools", "acts");

    public static string Executable => Path.Combine(Folder, "acts.exe");

    public static string? Installed(Workspace workspace)
    {
        if (Environment.GetEnvironmentVariable(T7Paths.ActsVariable) is { Length: > 0 } variable && File.Exists(variable))
            return variable;
        if (File.Exists(Executable))
            return Executable;
        string shipped = T7Paths.Acts(workspace);
        return File.Exists(shipped) ? shipped : null;
    }

    public static string? Ensure(Workspace workspace, Action<string> log, bool download = true)
    {
        string? acts = Find(workspace, log, download);
        return acts == null ? null : Usable(workspace, acts, log);
    }

    public static string? StartFailure(int exitCode) => unchecked((uint)exitCode) switch
    {
        0xC0000135 => "Windows could not find a DLL it needs (the Microsoft Visual C++ runtime)",
        0xC000007B or 0xC000012F => "a DLL it needs is damaged or built for another processor",
        0xC0000139 => "a DLL it needs is too old",
        0xC0000142 => "a DLL it needs failed to start",
        _ => null,
    };

    private static string? Usable(Workspace workspace, string acts, Action<string> log)
    {
        PlaceRuntime(workspace, acts);
        lock (Started)
        {
            if (Started.TryGetValue(acts, out bool known))
                return known ? acts : null;
        }
        bool starts = Starts(acts, log);
        lock (Started)
            Started[acts] = starts;
        return starts ? acts : null;
    }

    private static void PlaceRuntime(Workspace workspace, string acts)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(acts))!;
        if (!string.Equals(folder.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return;
        foreach (string name in Runtime)
        {
            string source = ToolData.Locate(workspace, RuntimeFolder + "/" + name);
            string target = Path.Combine(folder, name);
            if (!File.Exists(source) || (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length))
                continue;
            try
            {
                File.Copy(source, target, overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool Starts(string acts, Action<string> log)
    {
        ProcessRunner.Result result;
        try
        {
            result = ProcessRunner.Run(acts, ["-t", "--noUpdater"], Path.GetDirectoryName(Path.GetFullPath(acts))!, TimeSpan.FromSeconds(30));
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            log($"acts could not start ({error.Message}); converted scripts will not be checked");
            return false;
        }
        if (StartFailure(result.ExitCode) is { } reason)
        {
            log($"acts could not start: {reason} (exit code 0x{unchecked((uint)result.ExitCode):X8}); converted scripts will not be checked");
            return false;
        }
        return true;
    }

    private static string? Find(Workspace workspace, Action<string> log, bool download)
    {
        if (Installed(workspace) is { } present)
            return present;
        if (!download)
            return null;
        try
        {
            log($"downloading acts from {Repository} (the script check needs it; this happens once)");
            (string tag, string url) = LatestAsset();
            string temporary = Path.Combine(Path.GetTempPath(), "ffport-acts-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                Download(url, temporary);
                Extract(temporary, Folder);
                File.WriteAllText(Path.Combine(Folder, "release.txt"), tag);
                log($"acts {tag} is in {Folder}");
            }
            finally
            {
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return File.Exists(Executable) ? Executable : null;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or TaskCanceledException)
        {
            log($"acts could not be downloaded ({error.Message}); converted scripts will not be checked. Put acts.exe in {Folder} or name it with {T7Paths.ActsVariable}.");
            return null;
        }
    }

    private static (string Tag, string Url) LatestAsset()
    {
        using HttpClient client = Client();
        using JsonDocument document = JsonDocument.Parse(client.GetStringAsync(Release).GetAwaiter().GetResult());
        string tag = document.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        foreach (JsonElement asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (string.Equals(asset.GetProperty("name").GetString(), Asset, StringComparison.OrdinalIgnoreCase))
                return (tag, asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("the release asset has no download url"));
        }
        throw new InvalidDataException($"release {tag} has no {Asset}");
    }

    private static void Download(string url, string target)
    {
        using HttpClient client = Client();
        using Stream source = client.GetStreamAsync(url).GetAwaiter().GetResult();
        using FileStream file = File.Create(target);
        source.CopyTo(file);
    }

    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PS4-FF-Porter");
        return client;
    }

    private static void Extract(string zip, string folder)
    {
        Directory.CreateDirectory(folder);
        using ZipArchive archive = ZipFile.OpenRead(zip);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.Length == 0 && entry.FullName.EndsWith('/'))
                continue;
            string relative = entry.FullName.Replace('\\', '/');
            int slash = relative.IndexOf('/');
            if (slash < 0)
                continue;
            relative = relative[(slash + 1)..];
            if (!Files.Contains(relative, StringComparer.OrdinalIgnoreCase)
                && !Folders.Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            string target = Path.GetFullPath(Path.Combine(folder, relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ? relative[4..] : relative));
            if (!target.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("the acts archive holds a path outside its folder");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
        if (!File.Exists(Path.Combine(folder, "acts.exe")))
            throw new InvalidDataException($"{Asset} did not hold acts.exe");
    }
}
