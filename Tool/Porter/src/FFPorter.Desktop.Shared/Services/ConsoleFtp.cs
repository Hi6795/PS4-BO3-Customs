using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop.Services;

public sealed record ConsoleContents(IReadOnlySet<string> Maps, IReadOnlySet<string> Mods);

public sealed record ConsoleLook(bool Found, IReadOnlyList<string> Drives);

public sealed record ConsoleEntry(string Name, bool IsMod, string? Storage, string Folder);

public sealed record ConsoleFile(string Path, long Size);

public sealed record ConsoleSize(ConsoleEntry Entry, long Bytes, IReadOnlyList<ConsoleFile> Files);

public sealed class ConsoleFtp : IAsyncDisposable
{
    public const int DefaultPort = 2121;
    public const string Root = "/data/BO3-Customs";

    private const string Package = "BO3-Customs", Probe = "ffporter-write-test.tmp";

    private const int Chunk = 256 * 1024, Deepest = 8;
    private const string Staged = ".part";
    private static readonly TimeSpan Reach = TimeSpan.FromSeconds(15), Glance = TimeSpan.FromSeconds(4), Answer = TimeSpan.FromSeconds(30), Settle = TimeSpan.FromMinutes(3),
        Stalled = TimeSpan.FromSeconds(60);
    private static readonly SemaphoreSlim Line = new(1, 1);
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase)
        { ".ff", ".fd", ".xpak", ".sabl", ".sabs", ".json", ".png", ".jpg", ".jpeg", ".mkv", ".lua", ".sprx" };

    public static IReadOnlyList<string> Drives { get; } = [.. Enumerable.Range(0, 8).Select(index => $"usb{index}"), .. Enumerable.Range(0, 8).Select(index => $"ext{index}")];

    private readonly record struct Listed(string Name, bool Folder, long Size);

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;
    private readonly string _host;
    private bool _sizes = true;

    private ConsoleFtp(TcpClient client, string host)
    {
        _client = client;
        _host = host;
        _stream = client.GetStream();
        _reader = new StreamReader(_stream, new UTF8Encoding(false), false, 1024, leaveOpen: true);
    }

    public static bool TryAddress(string? text, out string host, out int port)
    {
        host = "";
        port = DefaultPort;
        string value = text?.Trim() ?? "";
        if (value.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
            value = value[6..];
        value = value.TrimEnd('/');
        int colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
        {
            if (!int.TryParse(value[(colon + 1)..], out port) || port is <= 0 or > 65535)
                return false;
            value = value[..colon];
        }
        if (value.Length == 0 || Uri.CheckHostName(value) == UriHostNameType.Unknown)
            return false;
        host = value;
        return true;
    }

    public static bool IsDrive(string? storage) => storage != null && Drives.Contains(storage);

    public static string RootOf(string? storage) => IsDrive(storage) ? $"/mnt/{storage}/{Package}" : Root;

    public static string StorageName(string? storage) =>
        !IsDrive(storage) ? "Console storage" : storage!.StartsWith("usb", StringComparison.Ordinal) ? $"USB {storage[3..]}" : $"Extended {storage[3..]}";

    public static string Maps(string root) => root + "/usermaps";

    public static string Mods(string root) => root + "/mods";

    public static string Folder(CommunityMap map, string? storage = null) =>
        map.IsConsole ? Root : $"{(map.IsMod ? Mods(RootOf(storage)) : Maps(RootOf(storage)))}/{map.Folder}";

    public static string Target(string host, int port, string folder) => $"ftp://{host}:{port}{folder}";

    public static bool Sendable(string path) =>
        Kinds.Contains(Path.GetExtension(path)) && !path.EndsWith(".fidelity.json", StringComparison.OrdinalIgnoreCase);

    public static async Task<ConsoleLook> TestAsync(string host, int port, string root, CancellationToken token)
    {
        if (!await Line.WaitAsync(0, token))
            throw new IOException("the console is busy with a transfer");
        try
        {
            await using ConsoleFtp ftp = await ConnectAsync(host, port, Reach, token);
            (int code, _) = await ftp.AskAsync("CWD " + root, Answer, token);
            await ftp.RequireAsync("TYPE I", 200, token);
            var drives = new List<string>();
            foreach (string drive in Drives)
            {
                if (await ftp.WritableAsync("/mnt/" + drive))
                    drives.Add(drive);
            }
            return new ConsoleLook(code == 250, drives);
        }
        finally
        {
            Line.Release();
        }
    }

    public static async Task<bool> AnswersAsync(string host, int port)
    {
        try
        {
            await using ConsoleFtp ftp = await ConnectAsync(host, port, Glance, CancellationToken.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static bool Busy => Line.CurrentCount == 0;

    public static async Task IdleAsync()
    {
        await Line.WaitAsync();
        Line.Release();
    }

    public static async Task<IReadOnlyList<string>> UsableAsync(string host, int port, IReadOnlyList<string> drives, bool quick, CancellationToken token)
    {
        if (!await Line.WaitAsync(0, token))
            throw new IOException("the console is busy with a transfer");
        try
        {
            await using ConsoleFtp ftp = await ConnectAsync(host, port, quick ? Glance : Reach, token);
            await ftp.RequireAsync("TYPE I", 200, token);
            var usable = new List<string>();
            foreach (string drive in drives.Where(IsDrive))
            {
                if (await ftp.WritableAsync("/mnt/" + drive))
                    usable.Add(drive);
            }
            return usable;
        }
        finally
        {
            Line.Release();
        }
    }

    public static async Task<ConsoleContents> ContentsAsync(string host, int port, string root, CancellationToken token)
    {
        if (!await Line.WaitAsync(0, token))
            throw new IOException("the console is busy with a transfer");
        try
        {
            await using ConsoleFtp ftp = await ConnectAsync(host, port, Glance, token);
            var maps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string folder, HashSet<string> names) in ((string, HashSet<string>)[])[(Maps(root), maps), (Mods(root), mods)])
            {
                foreach (Listed item in await ftp.ListAsync(folder) ?? [])
                {
                    if (item.Folder)
                        names.Add(item.Name);
                }
            }
            return new ConsoleContents(maps, mods);
        }
        finally
        {
            Line.Release();
        }
    }

    public static async Task InstalledAsync(string host, int port, IProgress<IReadOnlyList<ConsoleEntry>> found, IProgress<ConsoleSize> measured, CancellationToken token)
    {
        await Line.WaitAsync(token);
        try
        {
            await using ConsoleFtp ftp = await ConnectAsync(host, port, Glance, token);
            var entries = new List<ConsoleEntry>();
            foreach (string? storage in Drives.Prepend<string?>(null))
            {
                token.ThrowIfCancellationRequested();
                string root = RootOf(storage);
                foreach ((string parent, bool mod) in ((string, bool)[])[(Maps(root), false), (Mods(root), true)])
                {
                    foreach (Listed item in await ftp.ListAsync(parent) ?? [])
                    {
                        if (item.Folder)
                            entries.Add(new ConsoleEntry(item.Name, mod, storage, $"{parent}/{item.Name}"));
                    }
                }
            }
            found.Report(entries);
            foreach (ConsoleEntry entry in entries)
            {
                token.ThrowIfCancellationRequested();
                var files = new List<ConsoleFile>();
                await ftp.MeasureAsync(entry.Folder, "", files, 0);
                measured.Report(new ConsoleSize(entry, files.Sum(file => file.Size), files));
            }
        }
        finally
        {
            Line.Release();
        }
    }

    public static async Task<int> PresentAsync(string host, int port, string local, string remote, CancellationToken token)
    {
        string root = Path.GetFullPath(local);
        if (!Directory.Exists(root))
            return 0;
        string[] names = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(Sendable).Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))];
        if (names.Length == 0)
            return 0;
        await Line.WaitAsync(token);
        try
        {
            await using ConsoleFtp ftp = await ConnectAsync(host, port, Glance, token);
            int present = 0;
            foreach (IGrouping<string, string> folder in names.GroupBy(name => name.Contains('/') ? name[..name.LastIndexOf('/')] : "", StringComparer.Ordinal))
            {
                if (await ftp.ListAsync(folder.Key.Length > 0 ? $"{remote}/{folder.Key}" : remote) is not { } listed)
                    continue;
                var there = new HashSet<string>(listed.Where(item => !item.Folder).Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
                present += folder.Count(name => there.Contains(name[(name.LastIndexOf('/') + 1)..]));
            }
            return present;
        }
        finally
        {
            Line.Release();
        }
    }

    public static async Task RemoveAsync(string host, int port, string folder, CancellationToken token)
    {
        if (!Removable(folder))
            throw new IOException("only a map or mod folder can be removed from the console");
        await Line.WaitAsync(token);
        try
        {
            await using ConsoleFtp ftp = await ConnectAsync(host, port, Reach, token);
            await ftp.DeleteAsync(folder, 0);
        }
        finally
        {
            Line.Release();
        }
    }

    public static async Task<bool> SendAsync(string host, int port, string local, string remote, IReadOnlyList<string> last, ISet<string> staged,
        IProgress<DownloadProgress> progress, Action<string> log, PauseGate pause, CancellationToken stop)
    {
        string root = Path.GetFullPath(local);
        List<(string Path, string Name, long Length, string Mark)> files = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(Sendable)
            .Select(path => (Path: path, Name: Path.GetRelativePath(root, path).Replace('\\', '/'), Info: new FileInfo(path)))
            .OrderBy(file => last.Contains(file.Name, StringComparer.OrdinalIgnoreCase) ? 1 : 0).ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => (file.Path, file.Name, file.Info.Length, $"{Target(host, port, remote)}/{file.Name}|{file.Info.Length}|{file.Info.LastWriteTimeUtc.Ticks}"))];
        if (files.Count == 0)
            throw new IOException("there is nothing in the folder to send");
        long total = files.Sum(file => file.Length);

        if (Line.CurrentCount == 0)
            progress.Report(new DownloadProgress("Waiting for the console to be free", "", 0));
        await Line.WaitAsync(stop);
        try
        {
            progress.Report(new DownloadProgress("Connecting to the console", "", 0));
            ConsoleFtp ftp = await ConnectAsync(host, port, Reach, stop);
            try
            {
                await ftp.RequireAsync("TYPE I", 200, stop);
                await ftp.RequireDriveAsync(remote);
                foreach (string folder in files.Select(file => file.Name.Contains('/') ? remote + "/" + file.Name[..file.Name.LastIndexOf('/')] : remote)
                    .Distinct(StringComparer.Ordinal).OrderBy(folder => folder.Length))
                {
                    await ftp.EnsureFolderAsync(folder, stop);
                }

                var pace = new CommunityDownloader.Pace();
                long done = 0;
                bool confirmed = true;
                double Fraction() => Math.Clamp((double)done / Math.Max(1, total), 0, 1);
                void Report(string name)
                {
                    if (!pace.Due(done, out double speed))
                        return;
                    string amount = $"{Job.SizeText(done)} of {Job.SizeText(total)}" + (speed > 0 ? $" · {Job.SizeText((long)speed)}/s" : "");
                    progress.Report(new DownloadProgress(DownloadProgress.Sending, $"{Path.GetFileName(name)} · {amount}", Fraction()));
                }

                foreach ((string path, string name, long length, string mark) in files)
                {
                    stop.ThrowIfCancellationRequested();
                    if (pause.IsPaused)
                    {
                        await ftp.DisposeAsync();
                        progress.Report(new DownloadProgress(DownloadProgress.Paused, $"{Job.SizeText(done)} of {Job.SizeText(total)} sent", Fraction()));
                        await pause.WaitAsync(stop);
                        progress.Report(new DownloadProgress("Connecting to the console", "", Fraction()));
                        ftp = await ConnectAsync(host, port, Reach, stop);
                        await ftp.RequireAsync("TYPE I", 200, stop);
                    }
                    string stage = $"{remote}/{name}{Staged}";
                    bool sized = !Encrypted(path);
                    if (sized && staged.Contains(mark) && await ftp.SizeAsync(stage) == length)
                    {
                        done += length;
                        continue;
                    }
                    await ftp.StoreAsync(path, stage, count =>
                    {
                        done += count;
                        Report(name);
                    });
                    long arrived = sized ? await ftp.SizeAsync(stage) : length;
                    if (arrived >= 0 && arrived != length)
                        throw new IOException($"{name} arrived on the console as {arrived:N0} of {length:N0} bytes");
                    confirmed &= arrived >= 0;
                    staged.Add(mark);
                }
                if (!confirmed)
                    log("The console's FTP server does not report file sizes, so the files could not be double-checked after sending.");

                stop.ThrowIfCancellationRequested();
                progress.Report(new DownloadProgress(DownloadProgress.Sending, "putting the files in place", 1));
                foreach ((_, string name, _, string mark) in files)
                {
                    string from = $"{remote}/{name}{Staged}", to = $"{remote}/{name}";
                    if (!await ftp.MoveAsync(from, to, false))
                    {
                        await ftp.DisposeAsync();
                        ftp = await ConnectAsync(host, port, Reach, CancellationToken.None);
                        await ftp.RequireAsync("TYPE I", 200, CancellationToken.None);
                        await ftp.AskAsync("DELE " + to, Answer, CancellationToken.None);
                        await ftp.MoveAsync(from, to, true);
                    }
                    staged.Remove(mark);
                }
                return confirmed;
            }
            finally
            {
                await ftp.DisposeAsync();
            }
        }
        finally
        {
            Line.Release();
        }
    }

    private static bool Encrypted(string path)
    {
        Span<byte> head = stackalloc byte[4];
        using FileStream stream = File.OpenRead(path);
        return stream.Read(head) == 4 && head.SequenceEqual((ReadOnlySpan<byte>)[0x4F, 0x15, 0x3D, 0x1D]);
    }

    private static async Task<ConsoleFtp> ConnectAsync(string host, int port, TimeSpan reach, CancellationToken token)
    {
        TcpClient client = await OpenAsync(host, port, reach, token);
        var ftp = new ConsoleFtp(client, host);
        try
        {
            (int code, string text) = await ftp.ReadAsync(Answer, token);
            if (code != 220)
                throw Unexpected("connecting", code, text);
            (code, text) = await ftp.AskAsync("USER anonymous", Answer, token);
            if (code == 331)
                (code, text) = await ftp.AskAsync("PASS anonymous@", Answer, token);
            if (code is not (230 or 202))
                throw Unexpected("signing in", code, text);
            return ftp;
        }
        catch
        {
            await ftp.DisposeAsync();
            throw;
        }
    }

    private static async Task<TcpClient> OpenAsync(string host, int port, TimeSpan reach, CancellationToken token)
    {
        var client = new TcpClient { NoDelay = true };
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(reach);
        try
        {
            await client.ConnectAsync(host, port, limit.Token);
            return client;
        }
        catch (Exception error) when (error is SocketException || (error is OperationCanceledException && !token.IsCancellationRequested))
        {
            client.Dispose();
            throw new IOException($"the console at {host}:{port} did not answer");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static bool Removable(string folder)
    {
        foreach (string root in Drives.Select(RootOf).Prepend(Root))
        {
            foreach (string parent in (string[])[Maps(root), Mods(root)])
            {
                if (folder.StartsWith(parent + "/", StringComparison.Ordinal) && folder[(parent.Length + 1)..] is { Length: > 0 } name
                    && !name.Contains('/') && !name.Contains('\\') && name is not ("." or ".."))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private async Task RequireDriveAsync(string remote)
    {
        if (Drives.FirstOrDefault(name => remote.StartsWith($"/mnt/{name}/", StringComparison.Ordinal)) is not { } drive)
            return;
        if (!await WritableAsync("/mnt/" + drive))
            throw new IOException($"{StorageName(drive)} is not usable");
    }

    private async Task<bool> WritableAsync(string folder)
    {
        (int code, _) = await AskAsync("CWD " + folder, Answer, CancellationToken.None);
        if (code != 250)
            return false;
        string file = $"{folder}/{Probe}";
        byte[] mark = "PS4 .FF Porter write test"u8.ToArray();
        using (TcpClient data = await PassiveAsync())
        {
            (code, _) = await AskAsync("STOR " + file, Answer, CancellationToken.None);
            if (code is not (125 or 150))
                return false;
            try
            {
                await data.GetStream().WriteAsync(mark);
            }
            catch (IOException)
            {
            }
        }
        (code, _) = await ReadAsync(Answer, CancellationToken.None);
        bool written = code is 226 or 250;
        long size = written ? await SizeAsync(file) : -1;
        await AskAsync("DELE " + file, Answer, CancellationToken.None);
        return written && (size < 0 || size == mark.Length);
    }

    private async Task EnsureFolderAsync(string folder, CancellationToken token)
    {
        string built = "";
        foreach (string part in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            built += "/" + part;
            (int code, _) = await AskAsync("CWD " + built, Answer, token);
            if (code == 250)
                continue;
            await AskAsync("MKD " + built, Answer, token);
            (code, string text) = await AskAsync("CWD " + built, Answer, token);
            if (code != 250)
                throw Unexpected($"making {built}", code, text);
        }
    }

    private async Task MeasureAsync(string folder, string prefix, List<ConsoleFile> files, int depth)
    {
        if (depth > Deepest || await ListAsync(folder) is not { } entries)
            return;
        foreach (Listed item in entries)
        {
            if (item.Folder)
                await MeasureAsync($"{folder}/{item.Name}", $"{prefix}{item.Name}/", files, depth + 1);
            else
                files.Add(new ConsoleFile(prefix + item.Name, item.Size));
        }
    }

    private async Task<List<Listed>?> ListAsync(string folder)
    {
        (int code, string text) = await AskAsync("CWD " + folder, Answer, CancellationToken.None);
        if (code != 250)
            return null;
        var entries = new List<Listed>();
        using (TcpClient data = await PassiveAsync())
        {
            (code, text) = await AskAsync("LIST", Answer, CancellationToken.None);
            if (code is not (125 or 150))
                throw Unexpected($"reading {folder}", code, text);
            using var limit = new CancellationTokenSource(Settle);
            using var reader = new StreamReader(data.GetStream(), new UTF8Encoding(false));
            try
            {
                while (await reader.ReadLineAsync(limit.Token) is { } line)
                {
                    string[] parts = line.Split(' ', 9, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length == 9 && line[0] is 'd' or '-' && parts[8] is not ("." or ".."))
                        entries.Add(new Listed(parts[8], line[0] == 'd', long.TryParse(parts[4], out long size) && size > 0 ? size : 0));
                }
            }
            catch (OperationCanceledException)
            {
                throw new IOException("the console stopped answering");
            }
        }
        (code, text) = await ReadAsync(Settle, CancellationToken.None);
        if (code is not (226 or 250))
            throw Unexpected($"reading {folder}", code, text);
        return entries;
    }

    private async Task DeleteAsync(string folder, int depth)
    {
        if (depth > Deepest)
            throw new IOException($"{folder} is nested too deeply to remove");
        if (await ListAsync(folder) is not { } entries)
            return;
        foreach (Listed item in entries)
        {
            if (item.Folder)
            {
                await DeleteAsync($"{folder}/{item.Name}", depth + 1);
                continue;
            }
            (int code, string text) = await AskAsync($"DELE {folder}/{item.Name}", Answer, CancellationToken.None);
            if (code / 100 != 2)
                throw Unexpected($"removing {item.Name}", code, text);
        }
        await AskAsync("CWD /", Answer, CancellationToken.None);
        (int done, string answer) = await AskAsync("RMD " + folder, Answer, CancellationToken.None);
        if (done / 100 != 2)
            throw Unexpected($"removing {folder}", done, answer);
    }

    private async Task StoreAsync(string local, string remote, Action<int> sent)
    {
        using TcpClient data = await PassiveAsync();
        (int code, string text) = await AskAsync("STOR " + remote, Answer, CancellationToken.None);
        if (code is not (125 or 150))
            throw Unexpected($"sending {Path.GetFileName(local)}", code, text);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            using var stall = new CancellationTokenSource();
            await using NetworkStream target = data.GetStream();
            await using var source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, Chunk, useAsync: true);
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, Chunk))) > 0)
            {
                stall.CancelAfter(Stalled);
                await target.WriteAsync(buffer.AsMemory(0, read), stall.Token);
                sent(read);
            }
        }
        catch (OperationCanceledException)
        {
            throw new IOException("the console stopped taking data");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        data.Close();
        (code, text) = await ReadAsync(Settle, CancellationToken.None);
        if (code is not (226 or 250))
            throw Unexpected($"sending {Path.GetFileName(local)}", code, text);
    }

    private async Task<TcpClient> PassiveAsync()
    {
        (int code, string text) = await AskAsync("PASV", Answer, CancellationToken.None);
        Match numbers = Regex.Match(text, @"(\d+),(\d+),(\d+),(\d+),(\d+),(\d+)");
        if (code != 227 || !numbers.Success)
            throw Unexpected("opening a data connection", code, text);
        return await OpenAsync(_host, int.Parse(numbers.Groups[5].Value) * 256 + int.Parse(numbers.Groups[6].Value), Reach, CancellationToken.None);
    }

    private async Task<long> SizeAsync(string remote)
    {
        if (!_sizes)
            return -1;
        (int code, string text) = await AskAsync("SIZE " + remote, Answer, CancellationToken.None);
        if (code == 213 && long.TryParse(text.Trim(), out long size))
            return size;
        if (code is 500 or 502 or 504)
            _sizes = false;
        return -1;
    }

    private async Task<bool> MoveAsync(string from, string to, bool required)
    {
        (int code, string text) = await AskAsync("RNFR " + from, Answer, CancellationToken.None);
        if (code != 350)
            throw Unexpected($"putting {Path.GetFileName(to)} in place", code, text);
        (code, text) = await AskAsync("RNTO " + to, Answer, CancellationToken.None);
        if (code / 100 == 2)
            return true;
        if (required)
            throw Unexpected($"putting {Path.GetFileName(to)} in place", code, text);
        return false;
    }

    private async Task RequireAsync(string command, int expected, CancellationToken token)
    {
        (int code, string text) = await AskAsync(command, Answer, token);
        if (code != expected)
            throw Unexpected(command, code, text);
    }

    private async Task<(int Code, string Text)> AskAsync(string command, TimeSpan wait, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(wait);
        try
        {
            await _stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\r\n"), limit.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("the console stopped answering");
        }
        return await ReadAsync(wait, token);
    }

    private async Task<(int Code, string Text)> ReadAsync(TimeSpan wait, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(wait);
        try
        {
            string line = await _reader.ReadLineAsync(limit.Token) ?? throw new IOException("the console closed the connection");
            if (line.Length < 3 || !int.TryParse(line.AsSpan(0, 3), out int code))
                throw new IOException($"the console answered with something that is not FTP: {line}");
            if (line.Length > 3 && line[3] == '-')
            {
                string end = line[..3] + " ";
                do
                    line = await _reader.ReadLineAsync(limit.Token) ?? throw new IOException("the console closed the connection");
                while (!line.StartsWith(end, StringComparison.Ordinal));
            }
            return (code, line.Length > 4 ? line[4..].Trim() : "");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("the console stopped answering");
        }
    }

    private static IOException Unexpected(string doing, int code, string text) => new($"the console answered {code} {text}".TrimEnd() + $" while {doing}");

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _stream.WriteAsync("QUIT\r\n"u8.ToArray(), limit.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
        _reader.Dispose();
        _client.Dispose();
    }
}
