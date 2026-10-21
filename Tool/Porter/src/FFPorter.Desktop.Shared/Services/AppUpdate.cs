using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop.Services;

public enum UpdateStates
{
    Missing,
    Current,
    Ahead,
    Available,
    Doubtful,
}

public sealed record UpdateFile(string Name, string Release, Uri Link, long Size, string Sha256, DateTimeOffset? Uploaded, string Notes = "");

public sealed record ConsolePackage(string Release, UpdateFile Archive, UpdateFile Plugin);

public sealed record UpdateCheck(UpdateStates State, UpdateFile? File);

public static class AppUpdate
{
    public const string Verb = "--apply-update", Failed = "--update-failed";

    private const int Chunk = 1 << 20, Attempts = 3, LargestList = 4 << 20, LongestArguments = 24000, Sweeps = 20, Placements = 150;
    private const int LongestNotes = 1500;
    private const long SpareSpace = 64L << 20, Smallest = 8;
    private const string BuiltKey = "Built", NewSuffix = ".new", OldSuffix = ".old", DigestKind = "sha256:";
    private const string Unreachable = "GitHub could not be reached; check your internet connection";
    private static readonly TimeSpan ListWait = TimeSpan.FromSeconds(15), Stalled = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SweepWait = TimeSpan.FromMilliseconds(500), PlaceWait = TimeSpan.FromMilliseconds(200);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly HttpClient Http = CreateClient();

    private sealed class Release
    {
        [JsonPropertyName("tag_name")]
        public string? Tag { get; set; }
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
        public string? Body { get; set; }
        [JsonPropertyName("published_at")]
        public DateTimeOffset? Published { get; set; }
        public List<Asset?>? Assets { get; set; }
    }

    private sealed class Asset
    {
        public string? Name { get; set; }
        public string? State { get; set; }
        public long Size { get; set; }
        public string? Digest { get; set; }
        [JsonPropertyName("updated_at")]
        public DateTimeOffset? Updated { get; set; }
        [JsonPropertyName("browser_download_url")]
        public string? Link { get; set; }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20) }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PS4-FF-Porter");
        return client;
    }

    public static string? Location => Packed && Environment.ProcessPath is { Length: > 0 } path ? path : null;

    public static string Name => Assembly.GetEntryAssembly()?.GetName().Name ?? "";

    public static string Summary { get; set; } = "";

    public static DateTimeOffset? Built => BuiltOf(Assembly.GetEntryAssembly());

    [UnconditionalSuppressMessage("SingleFile", "IL3000")]
    private static bool Packed => typeof(AppUpdate).Assembly.Location.Length == 0;

    public static DateTimeOffset? BuiltOf(Assembly? assembly) =>
        assembly?.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == BuiltKey)?.Value is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset built) ? built : null;

    public static Uri Releases(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out Uri? direct) && (direct.Scheme == Uri.UriSchemeHttps || (direct.Scheme == Uri.UriSchemeHttp && direct.IsLoopback))
            ? direct
            : new Uri($"https://api.github.com/repos/{source}/releases?per_page=100");

    public static async Task<UpdateCheck> CheckAsync(Uri releases, string exe, string name, DateTimeOffset? built, CancellationToken token)
    {
        if (await LatestAsync(releases, name, token) is not { } latest)
            return new UpdateCheck(UpdateStates.Missing, null);
        if (latest.Size < new FileInfo(exe).Length / Smallest)
            return new UpdateCheck(UpdateStates.Doubtful, latest);
        if (await HashAsync(exe, token) == latest.Sha256)
            return new UpdateCheck(UpdateStates.Current, latest);
        return new UpdateCheck(built is { } time && latest.Uploaded is { } uploaded && time >= uploaded ? UpdateStates.Ahead : UpdateStates.Available, latest);
    }

    public static async Task<UpdateFile?> LatestAsync(Uri releases, string name, CancellationToken token)
    {
        foreach (Release release in await ReleasesAsync(releases, token))
        {
            if (Executable(release, name) is { } asset)
                return Describe(release, asset, releases);
        }
        return null;
    }

    public static async Task<ConsolePackage?> ConsoleAsync(Uri releases, string archive, string plugin, CancellationToken token)
    {
        (Release Release, Asset Asset)? files = null, loader = null;
        foreach (Release release in await ReleasesAsync(releases, token))
        {
            Asset? Named(string name) => (release.Assets ?? []).OfType<Asset>()
                .FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase) && asset.State is null or "uploaded");
            if (files == null && Named(archive) is { } first)
                files = (release, first);
            if (loader == null && Named(plugin) is { } second)
                loader = (release, second);
            if (files != null && loader != null)
                break;
        }
        if (files is not { } zip || loader is not { } sprx || Describe(zip.Release, zip.Asset, releases) is not { } package || Describe(sprx.Release, sprx.Asset, releases) is not { } latest)
            return null;
        bool newer = (sprx.Release.Published ?? DateTimeOffset.MinValue) > (zip.Release.Published ?? DateTimeOffset.MinValue);
        return new ConsolePackage(newer ? latest.Release : package.Release, package, latest);
    }

    private static async Task<List<Release>> ReleasesAsync(Uri releases, CancellationToken token)
    {
        List<Release?>? list;
        try
        {
            list = JsonSerializer.Deserialize<List<Release?>>(await ReadAsync(releases, token), Json);
        }
        catch (JsonException error)
        {
            throw new IOException($"GitHub's list of releases could not be read: {error.Message}", error);
        }
        return [.. (list ?? []).OfType<Release>().Where(release => !release.Draft && !release.Prerelease).OrderByDescending(release => release.Published ?? DateTimeOffset.MinValue)];
    }

    private static readonly HashSet<string> Headings = ["whatsnew", "whatisnew", "changelog", "changes", "releasenotes", "notes"];

    private static string Readable(string? notes)
    {
        var lines = new List<string>();
        foreach (string raw in (notes ?? "").Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim().TrimStart('#').Trim().Replace("**", "").Replace("`", "");
            if (line is "---" or "***")
                continue;
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                line = "• " + line[2..];
            if (line.Length > 0 || (lines.Count > 0 && lines[^1].Length > 0))
                lines.Add(line);
        }
        if (lines.Count > 0 && Headings.Contains(string.Concat(lines[0].Where(char.IsLetter)).ToLowerInvariant()))
            lines.RemoveAt(0);
        string text = string.Join("\n", lines).Trim();
        return text.Length > LongestNotes ? text[..LongestNotes].TrimEnd() + "…" : text;
    }

    private static Asset? Executable(Release release, string name)
    {
        List<Asset> programs = [.. (release.Assets ?? []).OfType<Asset>()
            .Where(asset => asset.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true && asset.State is null or "uploaded")];
        return programs.FirstOrDefault(asset => Plain(asset.Name!) == Plain(name + ".exe")) ?? (programs.Count == 1 ? programs[0] : null);
    }

    private static UpdateFile? Describe(Release release, Asset asset, Uri releases)
    {
        string digest = asset.Digest?.Trim().ToLowerInvariant() ?? "";
        if (asset.Size <= 0 || !digest.StartsWith(DigestKind, StringComparison.Ordinal) || digest.Length != DigestKind.Length + 64 || !digest[DigestKind.Length..].All(Uri.IsHexDigit))
            return null;
        if (!Uri.TryCreate(releases, asset.Link?.Trim(), out Uri? link) || !Trusted(link, releases))
            return null;
        return new UpdateFile(asset.Name!, release.Tag?.Trim() ?? "", link, asset.Size, digest[DigestKind.Length..], asset.Updated ?? release.Published, Readable(release.Body));
    }

    private static bool Trusted(Uri link, Uri releases) =>
        (link.Scheme == Uri.UriSchemeHttps || (link.Scheme == Uri.UriSchemeHttp && link.IsLoopback))
        && (link.Host.Equals(releases.Host, StringComparison.OrdinalIgnoreCase)
            || (releases.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) && link.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)));

    private static string Plain(string name) => string.Concat(name.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();

    public static async Task<string> HashAsync(string file, CancellationToken token)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Chunk, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
    }

    public static async Task DownloadAsync(UpdateFile update, string exe, IProgress<DownloadProgress> progress, CancellationToken token)
    {
        string file = exe + NewSuffix;
        TryDelete(file);
        try
        {
            CheckSpace(file, update.Size + new FileInfo(exe).Length);
            progress.Report(new DownloadProgress("Downloading", Job.SizeText(update.Size), 0));
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await FetchAsync(update, file, progress, token);
                    break;
                }
                catch (IOException) when (attempt < Attempts && !token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), token);
                }
            }
            progress.Report(new DownloadProgress("Checking", "", 1));
            if (await HashAsync(file, token) != update.Sha256)
                throw new InvalidDataException("the download does not match the file in the release");
        }
        catch
        {
            TryDelete(file);
            throw;
        }
    }

    private static async Task FetchAsync(UpdateFile update, string file, IProgress<DownloadProgress> progress, CancellationToken token)
    {
        long have = File.Exists(file) ? new FileInfo(file).Length : 0;
        if (have == update.Size)
            return;
        if (have > update.Size)
            have = 0;
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            stall.CancelAfter(Stalled);
            using var request = new HttpRequestMessage(HttpMethod.Get, update.Link);
            if (have > 0)
                request.Headers.Range = new RangeHeaderValue(have, null);
            using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            bool resumed = have > 0 && response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == have;
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable || (response.StatusCode == HttpStatusCode.PartialContent && !resumed))
            {
                TryDelete(file);
                throw new IOException("the download could not be continued");
            }
            if (!response.IsSuccessStatusCode)
                throw new IOException(Refused(response));
            long total = resumed ? have : 0;
            await using (Stream source = await response.Content.ReadAsStreamAsync(stall.Token))
            await using (var target = new FileStream(file, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, Chunk, useAsync: true))
            {
                var pace = new CommunityDownloader.Pace(total);
                while (true)
                {
                    stall.CancelAfter(Stalled);
                    int read = await source.ReadAsync(buffer.AsMemory(0, Chunk), stall.Token);
                    if (read == 0)
                        break;
                    if (total + read > update.Size)
                        throw new InvalidDataException("the download is larger than the file in the release");
                    await target.WriteAsync(buffer.AsMemory(0, read), stall.Token);
                    total += read;
                    if (pace.Due(total, out double speed))
                    {
                        string amount = $"{Job.SizeText(total)} of {Job.SizeText(update.Size)}";
                        progress.Report(new DownloadProgress("Downloading", speed > 0 ? $"{amount} · {Job.SizeText((long)speed)}/s" : amount, (double)total / update.Size));
                    }
                }
            }
            if (total != update.Size)
                throw new IOException($"the download stopped at {Job.SizeText(total)} of {Job.SizeText(update.Size)}");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("the download stopped arriving");
        }
        catch (HttpRequestException error)
        {
            throw new IOException(Unreachable, error);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static bool Writable(string exe)
    {
        try
        {
            using (new FileStream(exe + NewSuffix, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static int Others(string exe)
    {
        int others = 0;
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId && string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase))
                        others++;
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException)
                {
                }
            }
        }
        return others;
    }

    public static void HandOver(string exe, IEnumerable<string> arguments)
    {
        string helper = exe + OldSuffix;
        File.Copy(exe, helper, overwrite: true);
        try
        {
            Start(helper, [Verb, exe, .. arguments]);
        }
        catch
        {
            TryDelete(helper);
            throw;
        }
    }

    public static int Apply(string[] arguments, Action<string> stranded)
    {
        if (arguments.Length == 0)
            return 1;
        string exe = arguments[0], file = exe + NewSuffix;
        bool placed = false;
        for (int attempt = 0; attempt < Placements && !placed && File.Exists(file); attempt++)
        {
            try
            {
                if (File.Exists(exe))
                    File.SetAttributes(exe, FileAttributes.Normal);
                File.Move(file, exe, overwrite: true);
                placed = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(PlaceWait);
            }
        }
        try
        {
            Start(exe, placed ? arguments[1..] : [Failed, .. arguments[1..]]);
            return placed ? 0 : 1;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            stranded(error.Message.TrimEnd('.'));
            return 1;
        }
    }

    private static void Start(string exe, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        int length = 0;
        foreach (string argument in arguments)
        {
            length += argument.Length + 3;
            if (length > LongestArguments)
                break;
            start.ArgumentList.Add(argument);
        }
        using Process? process = Process.Start(start);
    }

    public static async Task CleanUpAsync(string exe)
    {
        for (int attempt = 0; attempt < Sweeps; attempt++)
        {
            if (await Task.Run(() => TryDelete(exe + NewSuffix) & TryDelete(exe + OldSuffix)))
                return;
            await Task.Delay(SweepWait);
        }
    }

    private static bool TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            return !File.Exists(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CheckSpace(string file, long needed)
    {
        DriveInfo drive;
        try
        {
            if (Path.GetPathRoot(Path.GetFullPath(file)) is not { Length: > 0 } root)
                return;
            drive = new DriveInfo(root);
        }
        catch (ArgumentException)
        {
            return;
        }
        long free = drive.AvailableFreeSpace;
        if (free < needed + SpareSpace)
            throw new IOException($"not enough free space on {drive.Name.TrimEnd(Path.DirectorySeparatorChar)}: the new version needs {Job.SizeText(needed)} and {Job.SizeText(free)} is free");
    }

    private static string Refused(HttpResponseMessage response) => $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();

    private static async Task<byte[]> ReadAsync(Uri address, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(ListWait);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (!response.IsSuccessStatusCode)
                throw new IOException(Refused(response));
            await using Stream stream = await response.Content.ReadAsStreamAsync(limit.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, limit.Token)) > 0)
            {
                if (buffer.Length + read > LargestList)
                    throw new IOException("GitHub sent more than a list of releases should be");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("GitHub did not answer in time");
        }
        catch (HttpRequestException error)
        {
            throw new IOException(Unreachable, error);
        }
    }
}
