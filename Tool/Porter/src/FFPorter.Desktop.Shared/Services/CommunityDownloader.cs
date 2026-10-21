using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop.Services;

public sealed record DownloadProgress(string Stage, string Detail, double Fraction)
{
    public const string Paused = "Paused", Sending = "Sending to the console";
}

public static class CommunityDownloader
{
    private const int Chunk = 1 << 20, Attempts = 3;
    private const long SpareSpace = 256L << 20;
    private const double FetchShare = 0.86, VerifyShare = 0.05;
    private const string SourceFile = "source.txt";
    private static readonly TimeSpan Stalled = TimeSpan.FromSeconds(60), Stale = TimeSpan.FromDays(14);
    private static readonly HashSet<string> MapFiles = new(StringComparer.OrdinalIgnoreCase)
        { ".ff", ".fd", ".xpak", ".sabl", ".sabs", ".json", ".png", ".jpg", ".jpeg", ".mkv" };

    public static string ScratchRoot => Path.Combine(FFPorter.Core.Workspace.WorkDirectory, "downloads");

    internal sealed class Pace(long start = 0)
    {
        private const double Idle = 3;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _marked, _speed;
        private long _markedBytes = start;

        public bool Due(long bytes, out double speed)
        {
            double now = _clock.Elapsed.TotalSeconds;
            speed = _speed;
            if (now - _marked < 0.25)
                return false;
            bool idle = now - _marked > Idle;
            double current = idle ? 0 : Math.Max(0, bytes - _markedBytes) / (now - _marked);
            speed = _speed = idle || _speed <= 0 ? current : _speed * 0.7 + current * 0.3;
            _marked = now;
            _markedBytes = bytes;
            return true;
        }
    }

    private static string Scratch(CommunityMap map) => Path.Combine(ScratchRoot, map.Id);

    private static string Source(CommunityMap map) => $"{map.Stamp}|{map.Size}|{map.Parts.Count}";

    public static long Fetched(CommunityMap map)
    {
        try
        {
            string scratch = Scratch(map), marker = Path.Combine(scratch, SourceFile);
            if (!File.Exists(marker) || File.ReadAllText(marker) != Source(map))
                return 0;
            return new DirectoryInfo(scratch).EnumerateFiles().Where(file => file.Name != SourceFile).Sum(file => file.Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public static void Discard(CommunityMap map) => DeleteFolder(Scratch(map));

    public static void Drop(string folder) => DeleteFolder(folder);

    public static async Task<string> ExtrasAsync(CommunityMap map, IProgress<DownloadProgress> progress, CancellationToken token)
    {
        string folder = Scratch(map) + ".extras";
        DeleteFolder(folder);
        Directory.CreateDirectory(folder);
        try
        {
            await ExtrasAsync(map, folder, progress, token);
        }
        catch
        {
            DeleteFolder(folder);
            throw;
        }
        return folder;
    }

    public static string? CopyExtras(CommunityMap map, string source)
    {
        string folder = Scratch(map) + ".extras";
        DeleteFolder(folder);
        try
        {
            Directory.CreateDirectory(folder);
            foreach (CommunityFile extra in map.Extras)
                File.Copy(Path.Combine(source, extra.Name), Path.Combine(folder, extra.Name), overwrite: true);
            return folder;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DeleteFolder(folder);
            return null;
        }
    }

    public static async Task DownloadAsync(CommunityMap map, string destination, IProgress<DownloadProgress> progress, Action<string> log, PauseGate pause,
        CancellationToken token)
    {
        string scratch = Scratch(map);
        string incoming = destination + ".incoming";
        try
        {
            Directory.CreateDirectory(scratch);
            List<string> parts = await FetchAsync(map, scratch, progress, log, pause, token);
            await Task.Run(() => Verify(map, parts, progress, pause, token), token);
            await Task.Run(() => Unpack(map, parts, incoming, progress, log, pause, token), token);
            await ExtrasAsync(map, incoming, progress, token);
            Place(incoming, destination);
            DeleteFolder(scratch);
        }
        catch (InvalidDataException)
        {
            DeleteFolder(scratch);
            DeleteFolder(incoming);
            throw;
        }
        catch
        {
            DeleteFolder(incoming);
            throw;
        }
    }

    public static void ClearStale()
    {
        try
        {
            if (!Directory.Exists(ScratchRoot))
                return;
            foreach (string folder in Directory.GetDirectories(ScratchRoot))
            {
                DateTime newest = Directory.EnumerateFiles(folder).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(Directory.GetLastWriteTimeUtc(folder)).Max();
                if (DateTime.UtcNow - newest > Stale)
                    DeleteFolder(folder);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task<List<string>> FetchAsync(CommunityMap map, string scratch, IProgress<DownloadProgress> progress, Action<string> log, PauseGate pause,
        CancellationToken token)
    {
        string marker = Path.Combine(scratch, SourceFile), source = Source(map);
        if (!File.Exists(marker) || File.ReadAllText(marker) != source)
        {
            foreach (string leftover in Directory.GetFiles(scratch))
                File.Delete(leftover);
            File.WriteAllText(marker, source);
        }
        var files = new List<string>();
        for (int index = 0; index < map.Parts.Count; index++)
            files.Add(Path.Combine(scratch, $"{index + 1:000}.part"));
        long done = files.Where(File.Exists).Sum(file => new FileInfo(file).Length);
        CheckSpace(scratch, map.Size - done, $"download {map.Title}");

        var pace = new Pace();
        long current = 0;
        DownloadProgress Shown(string stage, double speed)
        {
            string amount = map.Size > 0 ? $"{Job.SizeText(current)} of {map.SizeText}" : Job.SizeText(current);
            double fraction = map.Size > 0 ? Math.Clamp((double)current / map.Size, 0, 1) : 0;
            return new DownloadProgress(stage, speed > 0 ? $"{amount} · {Job.SizeText((long)speed)}/s" : amount, fraction * FetchShare);
        }
        void Report(long bytes)
        {
            current = bytes;
            if (pace.Due(bytes, out double speed))
                progress.Report(Shown("Downloading", speed));
        }

        progress.Report(new DownloadProgress("Downloading", map.SizeText, 0));
        long before = 0;
        for (int index = 0; index < files.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(files[index]))
            {
                before += new FileInfo(files[index]).Length;
                continue;
            }
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    long start = current = before;
                    before += await FetchPartAsync(map.Parts[index], files[index], written => Report(start + written),
                        () => progress.Report(Shown(DownloadProgress.Paused, 0)), pause, token);
                    break;
                }
                catch (IOException error) when (attempt < Attempts && !token.IsCancellationRequested)
                {
                    log($"{map.Title}: part {index + 1} of {files.Count} stopped ({error.Message}); trying again.");
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), token);
                }
            }
        }
        return files;
    }

    private static async Task<long> FetchPartAsync(Uri link, string file, Action<long> written, Action held, PauseGate pause, CancellationToken token)
    {
        while (true)
        {
            if (pause.IsPaused)
            {
                held();
                await pause.WaitAsync(token);
            }
            using var halt = CancellationTokenSource.CreateLinkedTokenSource(token, pause.Asked);
            try
            {
                return await ReceiveAsync(link, file, written, halt.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task<long> ReceiveAsync(Uri link, string file, Action<long> written, CancellationToken token)
    {
        string partial = file + ".partial";
        long have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            stall.CancelAfter(Stalled);
            using var request = new HttpRequestMessage(HttpMethod.Get, link);
            if (have > 0)
                request.Headers.Range = new RangeHeaderValue(have, null);
            using HttpResponseMessage response = await CommunityCatalog.Files.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            bool resumed = have > 0 && response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == have;
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && response.Content.Headers.ContentRange?.Length == have)
            {
                File.Move(partial, file, overwrite: true);
                return have;
            }
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable || (response.StatusCode == HttpStatusCode.PartialContent && !resumed))
            {
                File.Delete(partial);
                throw new IOException("the map server could not carry on from where the download stopped");
            }
            if (!response.IsSuccessStatusCode)
                throw new IOException(CommunityCatalog.Refused(response));
            long? expected = resumed ? response.Content.Headers.ContentRange?.Length : response.Content.Headers.ContentLength;
            long total = resumed ? have : 0;
            await using (Stream source = await response.Content.ReadAsStreamAsync(stall.Token))
            await using (var target = new FileStream(partial, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, Chunk, useAsync: true))
            {
                while (true)
                {
                    stall.CancelAfter(Stalled);
                    int read = await source.ReadAsync(buffer.AsMemory(0, Chunk), stall.Token);
                    if (read == 0)
                        break;
                    await target.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None);
                    total += read;
                    written(total);
                    if (total == expected)
                        break;
                }
            }
            if (expected is long length && total != length)
                throw new IOException($"the map server sent {total:N0} of {length:N0} bytes");
            File.Move(partial, file, overwrite: true);
            return total;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("the map server stopped sending");
        }
        catch (HttpRequestException error)
        {
            throw new IOException(CommunityCatalog.Unreachable(error), error);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void Hold(PauseGate pause, IProgress<DownloadProgress> progress, double fraction, CancellationToken token)
    {
        if (!pause.IsPaused)
            return;
        progress.Report(new DownloadProgress(DownloadProgress.Paused, "", fraction));
        pause.Wait(token);
    }

    private static void Verify(CommunityMap map, IReadOnlyList<string> parts, IProgress<DownloadProgress> progress, PauseGate pause, CancellationToken token)
    {
        if (map.Sha256.Length == 0)
            return;
        progress.Report(new DownloadProgress("Checking", "", FetchShare));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new PartsStream(parts);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            var pace = new Pace();
            long done = 0;
            int read;
            while ((read = stream.Read(buffer, 0, Chunk)) > 0)
            {
                Hold(pause, progress, FetchShare + VerifyShare * done / Math.Max(1, stream.Length), token);
                token.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
                done += read;
                if (pace.Due(done, out _))
                    progress.Report(new DownloadProgress("Checking", "", FetchShare + VerifyShare * done / Math.Max(1, stream.Length)));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(map.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("the download does not match the checksum in the list, so it was thrown away");
    }

    private static void Unpack(CommunityMap map, IReadOnlyList<string> parts, string incoming, IProgress<DownloadProgress> progress, Action<string> log, PauseGate pause,
        CancellationToken token)
    {
        double start = FetchShare + VerifyShare;
        progress.Report(new DownloadProgress("Unpacking", "", start));
        DeleteFolder(incoming);
        using var stream = new PartsStream(parts);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var files = new List<(ZipArchiveEntry Entry, string[] Path)>();
        int skipped = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue;
            string[] path = entry.FullName.Split('/', '\\');
            if (path.Any(part => part.Length == 0 || part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                || !Wanted(map, path[^1]))
            {
                skipped++;
                continue;
            }
            files.Add((entry, path));
        }
        while (files.Count > 0 && files.All(file => file.Path.Length > 1 && file.Path[0].Equals(files[0].Path[0], StringComparison.OrdinalIgnoreCase)))
            files = [.. files.Select(file => (file.Entry, file.Path[1..]))];
        if (map.Required.Count > 0 && !files.Any(file => file.Path.Length == 1 && map.Required.Contains(file.Path[0], StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(map.IsMod
                ? $"the download holds none of a mod's zones ({string.Join(", ", map.MainFiles)}), so it is not a converted mod"
                : $"the download holds no {map.Id}.ff, so it is not a converted {map.Id}");
        }

        long total = files.Sum(file => file.Entry.Length);
        if (total > (map.Size > 0 ? Math.Max(map.Size * 20, 4L << 30) : 64L << 30))
            throw new InvalidDataException($"the download unpacks to {Job.SizeText(total)}, far more than a map of that size should");
        CheckSpace(Path.GetDirectoryName(incoming)!, total, $"unpack {map.Title}");

        Directory.CreateDirectory(incoming);
        string root = Path.GetFullPath(incoming) + Path.DirectorySeparatorChar;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            var pace = new Pace();
            long done = 0;
            foreach ((ZipArchiveEntry entry, string[] path) in files)
            {
                string target = Path.GetFullPath(Path.Combine(incoming, Path.Combine(path)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using Stream source = entry.Open();
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, Chunk);
                long written = 0;
                int read;
                while ((read = source.Read(buffer, 0, Chunk)) > 0)
                {
                    Hold(pause, progress, start + (1 - start) * done / Math.Max(1, total), token);
                    token.ThrowIfCancellationRequested();
                    written += read;
                    if (written > entry.Length)
                        throw new InvalidDataException($"{entry.FullName} is larger than the download says it is");
                    output.Write(buffer, 0, read);
                    done += read;
                    if (pace.Due(done, out _))
                        progress.Report(new DownloadProgress("Unpacking", path[^1], start + (1 - start) * done / Math.Max(1, total)));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        if (skipped > 0)
            log($"{map.Title}: left out {skipped} file{(skipped == 1 ? "" : "s")} that {(skipped == 1 ? "is" : "are")} not part of a map.");
    }

    private static bool Wanted(CommunityMap map, string name) =>
        MapFiles.Contains(Path.GetExtension(name)) || (map.IsConsole && Path.GetExtension(name).Equals(".lua", StringComparison.OrdinalIgnoreCase));

    private static async Task ExtrasAsync(CommunityMap map, string incoming, IProgress<DownloadProgress> progress, CancellationToken token)
    {
        foreach (CommunityFile extra in map.Extras)
        {
            progress.Report(new DownloadProgress("Downloading", extra.Name, 1));
            string file = Path.Combine(incoming, extra.Name);
            await ReceiveAsync(extra.Link, file, _ => { }, token);
            if (extra.Sha256.Length > 0 && !(await AppUpdate.HashAsync(file, token)).Equals(extra.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{extra.Name} does not match its checksum, so the download was thrown away");
        }
    }

    private static void Place(string incoming, string destination)
    {
        string old = destination + ".old";
        DeleteFolder(old);
        if (Directory.Exists(destination))
            Directory.Move(destination, old);
        try
        {
            Directory.Move(incoming, destination);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (Directory.Exists(old) && !Directory.Exists(destination))
                Directory.Move(old, destination);
            throw;
        }
        DeleteFolder(old);
    }

    private static void CheckSpace(string folder, long needed, string what)
    {
        if (needed <= 0)
            return;
        DriveInfo drive;
        try
        {
            if (Path.GetPathRoot(Path.GetFullPath(folder)) is not { Length: > 0 } root)
                return;
            drive = new DriveInfo(root);
        }
        catch (ArgumentException)
        {
            return;
        }
        long free = drive.AvailableFreeSpace;
        if (free < needed + SpareSpace)
            throw new IOException($"not enough free space on {drive.Name.TrimEnd(Path.DirectorySeparatorChar)} to {what}: it needs {Job.SizeText(needed)} and {Job.SizeText(free)} is free");
    }

    private static void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
