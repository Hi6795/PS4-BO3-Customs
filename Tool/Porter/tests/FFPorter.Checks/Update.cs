using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FFPorter.Desktop;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal sealed class FakeGitHub : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private int _cuts;

    public int Port { get; }
    public volatile string Releases = "[]";
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public ConcurrentQueue<string> Seen { get; } = new();
    public volatile int CutAfter, Status, ListStatus, Delay, Extra;
    public volatile bool IgnoreRange, Redirect;

    public FakeGitHub()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
    }

    public string Address => $"http://127.0.0.1:{Port}";

    public Uri List => new($"{Address}/repos/o/r/releases?per_page=100");

    public void Cut(int times, int after)
    {
        CutAfter = after;
        Interlocked.Exchange(ref _cuts, times);
    }

    public void Reset()
    {
        Cut(0, 0);
        Status = ListStatus = Delay = Extra = 0;
        IgnoreRange = Redirect = false;
        Seen.Clear();
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                return;
            }
            _ = Task.Run(() => Serve(client));
        }
    }

    private static void Head(NetworkStream stream, string status, long length, params string[] headers)
    {
        var text = new StringBuilder($"HTTP/1.1 {status}\r\nContent-Length: {length}\r\nConnection: close\r\n");
        foreach (string header in headers)
            text.Append(header).Append("\r\n");
        stream.Write(Encoding.ASCII.GetBytes(text.Append("\r\n").ToString()));
    }

    private void Serve(TcpClient client)
    {
        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                var request = new StringBuilder();
                var one = new byte[1];
                while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && stream.Read(one, 0, 1) == 1)
                    request.Append((char)one[0]);
                string[] lines = request.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                string path = Uri.UnescapeDataString(lines[0].Split(' ')[1].Split('?')[0]);
                var sent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines.Skip(1))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                        sent[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }
                string Sent(string name) => sent.TryGetValue(name, out string? value) ? value : "";
                Seen.Enqueue($"{path} range={Sent("Range")} agent={Sent("User-Agent")} accept={Sent("Accept")} encoding={Sent("Accept-Encoding")}");
                if (path.StartsWith("/repos/", StringComparison.Ordinal))
                {
                    if (ListStatus != 0)
                    {
                        Head(stream, $"{ListStatus} rate limit exceeded", 0);
                        return;
                    }
                    byte[] list = Encoding.UTF8.GetBytes(Releases);
                    Head(stream, "200 OK", list.Length, "Content-Type: application/json; charset=utf-8");
                    stream.Write(list);
                    return;
                }
                if (path.StartsWith("/download/", StringComparison.Ordinal) && Redirect)
                {
                    Head(stream, "302 Found", 0, $"Location: /cdn/{Uri.EscapeDataString(path["/download/".Length..])}");
                    return;
                }
                string key = path.StartsWith("/cdn/", StringComparison.Ordinal) ? "/download/" + path["/cdn/".Length..] : path;
                if (Status != 0)
                {
                    Head(stream, $"{Status} Refused", 0);
                    return;
                }
                if (!Files.TryGetValue(key, out byte[]? data))
                {
                    Head(stream, "404 Not Found", 0);
                    return;
                }
                long from = 0;
                bool partial = false;
                if (!IgnoreRange && Sent("Range").StartsWith("bytes=", StringComparison.Ordinal))
                {
                    from = long.Parse(Sent("Range")[6..].TrimEnd('-'));
                    partial = true;
                }
                if (partial && from >= data.Length)
                {
                    Head(stream, "416 Range Not Satisfiable", 0, $"Content-Range: bytes */{data.Length}");
                    return;
                }
                long length = data.Length - from + Extra;
                if (partial)
                    Head(stream, "206 Partial Content", length, "Content-Type: application/octet-stream", $"Content-Range: bytes {from}-{data.Length - 1}/{data.Length}");
                else
                    Head(stream, "200 OK", length, "Content-Type: application/octet-stream");
                long limit = Interlocked.Decrement(ref _cuts) >= 0 ? CutAfter : long.MaxValue;
                long written = 0;
                for (long at = from; at < data.Length && written < limit; at += 65536)
                {
                    int count = (int)Math.Min(Math.Min(65536, data.Length - at), limit - written);
                    stream.Write(data, (int)at, count);
                    written += count;
                    if (Delay > 0)
                        Thread.Sleep(Delay);
                }
                if (written < limit && Extra > 0)
                    stream.Write(new byte[Extra]);
            }
        }
        catch (Exception)
        {
        }
    }

    public void Dispose() => _listener.Stop();
}

internal static class UpdateChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string AppName = "PS4 .FF Porter", ExeName = "PS4 .FF Porter.exe", AssetName = "PS4.FF.Porter.exe";

    private sealed class Reports : IProgress<DownloadProgress>
    {
        public ConcurrentQueue<DownloadProgress> Seen { get; } = new();
        public Action<DownloadProgress>? Each;

        public void Report(DownloadProgress value)
        {
            Seen.Enqueue(value);
            Each?.Invoke(value);
        }
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] Noise(int bytes, int seed)
    {
        var data = new byte[bytes];
        new Random(seed).NextBytes(data);
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        return data;
    }

    internal static Dictionary<string, object?> Asset(string name, byte[] data, string link, DateTimeOffset updated, string state = "uploaded", string? digest = "auto") => new()
    {
        ["id"] = 7, ["name"] = name, ["state"] = state, ["size"] = data.Length, ["content_type"] = "application/x-msdownload",
        ["digest"] = digest == "auto" ? "sha256:" + Sha(data) : digest, ["created_at"] = updated.AddSeconds(-20), ["updated_at"] = updated, ["browser_download_url"] = link,
        ["uploader"] = new Dictionary<string, object?> { ["login"] = "someone", ["id"] = 3 },
    };

    internal static string Notes { get; set; } = "notes with \"quotes\"\nand lines";

    internal static Dictionary<string, object?> Release(string tag, DateTimeOffset? published, bool draft, bool early, params Dictionary<string, object?>[] assets) => new()
    {
        ["id"] = 11, ["tag_name"] = tag, ["name"] = tag + " RELEASE", ["draft"] = draft, ["prerelease"] = early, ["published_at"] = published,
        ["body"] = Notes, ["assets"] = assets,
    };

    internal static string Json(params Dictionary<string, object?>[] releases) => JsonSerializer.Serialize(releases);

    private static T Now<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();

    private static Exception? Fails(Func<Task> work)
    {
        try
        {
            Task.Run(work).GetAwaiter().GetResult();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private static bool WaitFor(Func<bool> done, double seconds)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until)
            Thread.Sleep(50);
        return done();
    }

    private static string Text(string file)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                return File.Exists(file) ? File.ReadAllText(file) : "";
            }
            catch (IOException)
            {
                Thread.Sleep(25);
            }
        }
        return "";
    }

    public static void Run(string scratch, string probes)
    {
        Console.WriteLine("== updates: finding the release file");
        string one = Path.Combine(probes, "probe_one", ExeName), two = Path.Combine(probes, "probe_two", ExeName);
        if (!File.Exists(one) || !File.Exists(two))
        {
            Program.Check(false, $"the probe apps are missing ({one})");
            return;
        }
        using var hub = new FakeGitHub();
        string root = Path.Combine(scratch, "update");
        Directory.CreateDirectory(root);
        DateTimeOffset t1 = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), t2 = t1.AddDays(10), t3 = t1.AddDays(20), t4 = t1.AddDays(30);
        byte[] oldApp = Noise(400_000, 1), newApp = Noise(1_200_000, 2), other = Noise(50_000, 3);
        string Link(string tag, string name) => $"{hub.Address}/download/{tag}/{name}";

        hub.Releases = Json(
            Release("v2.00", t1, false, false, Asset(AssetName, oldApp, Link("v2.00", AssetName), t1)),
            Release("v9.00-draft", null, true, false, Asset(AssetName, other, Link("draft", AssetName), t4)),
            Release("v3.00", t3, false, false, Asset("BO3-Customs.sprx", other, Link("v3.00", "BO3-Customs.sprx"), t3), Asset("Console.zip", other, Link("v3.00", "Console.zip"), t3)),
            Release("v2.10", t2, false, false, Asset("Other.Tool.exe", other, Link("v2.10", "Other.Tool.exe"), t2), Asset(AssetName, newApp, Link("v2.10", AssetName), t2.AddHours(1)),
                Asset("Console.zip", other, Link("v2.10", "Console.zip"), t2)),
            Release("v4.00-beta", t4, false, true, Asset(AssetName, other, Link("beta", AssetName), t4)));
        UpdateFile? latest = Now(() => AppUpdate.LatestAsync(hub.List, AppName, CancellationToken.None));
        Console.WriteLine($"   found: {latest}");
        Program.Check(latest is { Name: AssetName, Release: "v2.10" } && latest.Sha256 == Sha(newApp) && latest.Size == newApp.Length && latest.Uploaded == t2.AddHours(1)
            && latest.Link.ToString() == Link("v2.10", AssetName),
            "the newest published release that has the app's exe is used: drafts, pre-releases and releases without an exe are passed over, and the exe is picked by name");
        string seen = hub.Seen.First();
        Console.WriteLine($"   request: {seen}");
        Program.Check(seen.Contains("agent=PS4-FF-Porter") && seen.Contains("accept=application/vnd.github+json") && seen.StartsWith("/repos/o/r/releases "), "GitHub is asked the way its API wants");
        Program.Check(latest!.Notes == "notes with \"quotes\"\nand lines", "the release's notes come along with the file");

        Notes = "## What's New\r\n\r\n- **Community** tab\r\n* `Pause` and resume\r\n---\r\n### Fixes\r\n\r\n\r\n- one-frame loops\r\n" + new string('x', 3000);
        hub.Releases = Json(Release("v2.10", t2, false, false, Asset(AssetName, newApp, Link("v2.10", AssetName), t2)));
        latest = Now(() => AppUpdate.LatestAsync(hub.List, AppName, CancellationToken.None));
        Notes = "notes with \"quotes\"\nand lines";
        Console.WriteLine("   notes: " + latest?.Notes[..90].Replace("\n", " / "));
        Program.Check(latest != null && latest.Notes.StartsWith("• Community tab\n• Pause and resume\nFixes\n\n• one-frame loops\nxxx") && latest.Notes.Length == 1501 && latest.Notes.EndsWith("x…"),
            "notes are shown as plain text: their own \"What's new\" heading, markdown marks and rules are dropped, lists become bullets, and long notes are cut");

        hub.Releases = Json(Release("v5", t3, false, false, Asset("Converter.exe", newApp, Link("v5", "Converter.exe"), t3), Asset("notes.txt", other, Link("v5", "notes.txt"), t3)));
        latest = Now(() => AppUpdate.LatestAsync(hub.List, AppName, CancellationToken.None));
        Program.Check(latest is { Name: "Converter.exe", Release: "v5" }, "a release with a single exe under another name is still used");

        hub.Releases = Json(
            Release("v6", t3, false, false, Asset("A.exe", other, Link("v6", "A.exe"), t3), Asset("B.exe", other, Link("v6", "B.exe"), t3)),
            Release("v5", t2, false, false, Asset(AssetName, oldApp, Link("v5", AssetName), t2)));
        latest = Now(() => AppUpdate.LatestAsync(hub.List, AppName, CancellationToken.None));
        Program.Check(latest is { Release: "v5" }, "a release with several unrelated exes is passed over");

        hub.Releases = Json(Release("v6", t3, false, false, Asset(AssetName, newApp, Link("v6", AssetName), t3, state: "open")), Release("v5", t2, false, false, Asset(AssetName, oldApp, Link("v5", AssetName), t2)));
        latest = Now(() => AppUpdate.LatestAsync(hub.List, AppName, CancellationToken.None));
        Program.Check(latest is { Release: "v5" }, "an exe that is still uploading is not used");

        hub.Releases = Json(Release("v6", t3, false, false, Asset(AssetName, newApp, Link("v6", AssetName), t3, digest: null)), Release("v5", t2, false, false, Asset(AssetName, oldApp, Link("v5", AssetName), t2)));
        latest = Now(() => AppUpdate.LatestAsync(hub.List, AppName, CancellationToken.None));
        Program.Check(latest == null, "an exe without a SHA-256 from GitHub is not installed, and an older release is not used in its place");

        hub.Releases = Json(Release("v6", t3, false, false, Asset(AssetName, newApp, $"https://example.com/{AssetName}", t3)));
        latest = Now(() => AppUpdate.LatestAsync(hub.List, AppName, CancellationToken.None));
        Program.Check(latest == null, "an exe that is not on the release's own site is not installed");

        byte[] zip = Noise(70_000, 4), plugin = Noise(30_000, 5), mended = Noise(31_000, 6);
        hub.Releases = Json(
            Release("v3.00", t1, false, false, Asset(AssetName, oldApp, Link("v3.00", AssetName), t1), Asset("Console.zip", zip, Link("v3.00", "Console.zip"), t1),
                Asset("BO3-Customs.sprx", plugin, Link("v3.00", "BO3-Customs.sprx"), t1)),
            Release("v3.01", t2, false, false, Asset("BO3-Customs.sprx", mended, Link("v3.01", "BO3-Customs.sprx"), t2)),
            Release("v3.02", t3, false, false, Asset(AssetName, newApp, Link("v3.02", AssetName), t3)));
        latest = Now(() => AppUpdate.LatestAsync(hub.List, AppName, CancellationToken.None));
        ConsolePackage? pieces = Now(() => AppUpdate.ConsoleAsync(hub.List, "Console.zip", "BO3-Customs.sprx", CancellationToken.None));
        Console.WriteLine($"   one file per release: exe from {latest?.Release}, plugin from {pieces?.Plugin.Release}, console files from {pieces?.Archive.Release}");
        Program.Check(latest is { Release: "v3.02" } && latest.Sha256 == Sha(newApp) && pieces is { Plugin.Release: "v3.01", Archive.Release: "v3.00" }
            && pieces.Plugin.Sha256 == Sha(mended) && pieces.Archive.Sha256 == Sha(zip),
            "each file comes from the newest release that has it: a release may carry only the exe, only the plugin or only the console files");

        hub.Releases = Json(Release("v3.00", t3, false, false, Asset("BO3-Customs.sprx", other, Link("v3.00", "BO3-Customs.sprx"), t3)));
        string exe = Path.Combine(root, "state", ExeName);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllBytes(exe, oldApp);
        UpdateCheck check = Now(() => AppUpdate.CheckAsync(hub.List, exe, AppName, null, CancellationToken.None));
        Program.Check(check is { State: UpdateStates.Missing, File: null }, "no release with an exe: nothing to do");

        hub.ListStatus = 403;
        Exception? refused = Fails(() => AppUpdate.CheckAsync(hub.List, exe, AppName, null, CancellationToken.None));
        hub.ListStatus = 0;
        hub.Releases = "<html>not json</html>";
        Exception? garbled = Fails(() => AppUpdate.CheckAsync(hub.List, exe, AppName, null, CancellationToken.None));
        Exception? down = Fails(() => AppUpdate.CheckAsync(new Uri("http://127.0.0.1:9/repos/o/r/releases"), exe, AppName, null, CancellationToken.None));
        Console.WriteLine($"   refused: {refused?.Message}\n   garbled: {garbled?.Message}\n   down: {down?.Message}");
        Program.Check(refused is IOException && refused.Message.Contains("403") && garbled is IOException && down is IOException && down.Message.Contains("could not be reached"),
            "a refused, unreadable or unreachable check fails with a plain reason");

        Console.WriteLine("== updates: comparing hashes");
        hub.Releases = Json(Release("v2.00", t1, false, false, Asset(AssetName, oldApp, Link("v2.00", AssetName), t1)), Release("v2.10", t2, false, false, Asset(AssetName, newApp, Link("v2.10", AssetName), t2)));
        hub.Files[$"/download/v2.10/{AssetName}"] = newApp;
        check = Now(() => AppUpdate.CheckAsync(hub.List, exe, AppName, null, CancellationToken.None));
        Program.Check(check is { State: UpdateStates.Available, File.Release: "v2.10" }, "a different hash and no build time: the release replaces the exe");
        check = Now(() => AppUpdate.CheckAsync(hub.List, exe, AppName, t2.AddMinutes(-1), CancellationToken.None));
        Program.Check(check.State == UpdateStates.Available, "a different hash, built before the release file was uploaded: the release replaces the exe");
        check = Now(() => AppUpdate.CheckAsync(hub.List, exe, AppName, t2.AddMinutes(1), CancellationToken.None));
        Program.Check(check.State == UpdateStates.Ahead, "a different hash, built after the release file was uploaded: an unreleased build is left alone");
        File.WriteAllBytes(exe, newApp);
        check = Now(() => AppUpdate.CheckAsync(hub.List, exe, AppName, t1, CancellationToken.None));
        Program.Check(check.State == UpdateStates.Current, "the same hash as the release file: up to date, whatever the build time");
        File.WriteAllBytes(exe, oldApp);
        byte[] stub = Noise(40_000, 4);
        hub.Releases = Json(Release("v2.20", t3, false, false, Asset(AssetName, stub, Link("v2.20", AssetName), t3)), Release("v2.10", t2, false, false, Asset(AssetName, newApp, Link("v2.10", AssetName), t2)));
        check = Now(() => AppUpdate.CheckAsync(hub.List, exe, AppName, null, CancellationToken.None));
        Program.Check(check is { State: UpdateStates.Doubtful, File.Release: "v2.20" },
            "a release exe far smaller than the app (a wrong file uploaded by mistake) is never installed");
        hub.Releases = Json(Release("v2.00", t1, false, false, Asset(AssetName, oldApp, Link("v2.00", AssetName), t1)), Release("v2.10", t2, false, false, Asset(AssetName, newApp, Link("v2.10", AssetName), t2)));
        UpdateFile update = Now(() => AppUpdate.LatestAsync(hub.List, AppName, CancellationToken.None))!;

        DateTimeOffset? built = AppUpdate.BuiltOf(typeof(T7Edition).Assembly);
        Console.WriteLine($"   this build's stamp: {built:u}");
        Program.Check(built is { } stamp && stamp <= DateTimeOffset.UtcNow.AddMinutes(1) && stamp > DateTimeOffset.UtcNow.AddHours(-6), "the app's program file carries the time it was built, set by the build itself");
        Program.Check(AppUpdate.Location == null && AppUpdate.BuiltOf(typeof(Program).Assembly) == null, "anything that is not the packed single-file app never updates itself");
        Program.Check(AppUpdate.Releases("ItsJokerZz/PS4-BO3-Customs").ToString() == "https://api.github.com/repos/ItsJokerZz/PS4-BO3-Customs/releases?per_page=100"
            && Edition.Current.UpdateSource == "ItsJokerZz/PS4-BO3-Customs", "the app asks its own repository on GitHub for its last 100 releases");

        Console.WriteLine("== updates: downloading");
        string incoming = exe + ".new";
        var reports = new Reports();
        hub.Reset();
        hub.Redirect = true;
        Exception? failure = Fails(() => AppUpdate.DownloadAsync(update, exe, reports, CancellationToken.None));
        string[] asked = [.. hub.Seen];
        Console.WriteLine($"   requests: {string.Join(" || ", asked)}");
        Program.Check(failure == null && File.Exists(incoming) && File.ReadAllBytes(incoming).AsSpan().SequenceEqual(newApp) && File.ReadAllBytes(exe).AsSpan().SequenceEqual(oldApp),
            "the new exe is downloaded beside the app, through GitHub's redirect, and the app's own file is not touched");
        Program.Check(asked.Length == 2 && asked[0].StartsWith("/download/v2.10/") && asked[1].StartsWith("/cdn/v2.10/") && asked.All(line => line.EndsWith("encoding=")),
            "the download asks for the file as it is, never compressed");
        Program.Check(reports.Seen.First().Stage == "Downloading" && reports.Seen.Last() is { Stage: "Checking", Fraction: 1 }, "progress is reported and ends with the check");

        hub.Reset();
        hub.Cut(1, 300_000);
        reports = new Reports();
        failure = Fails(() => AppUpdate.DownloadAsync(update, exe, reports, CancellationToken.None));
        asked = [.. hub.Seen];
        Console.WriteLine($"   after a cut: {string.Join(" || ", asked.Select(line => line.Split(" agent=")[0]))}");
        Program.Check(failure == null && File.ReadAllBytes(incoming).AsSpan().SequenceEqual(newApp) && asked.Length == 2 && asked[1].Contains("range=bytes=300000-"),
            "a download that is cut off carries on from where it stopped");

        hub.Reset();
        hub.Cut(1, 300_000);
        hub.IgnoreRange = true;
        failure = Fails(() => AppUpdate.DownloadAsync(update, exe, new Reports(), CancellationToken.None));
        Program.Check(failure == null && File.ReadAllBytes(incoming).AsSpan().SequenceEqual(newApp) && hub.Seen.Count == 2, "a server that cannot continue a download is given the whole file to send again");

        hub.Reset();
        hub.Files[$"/download/v2.10/{AssetName}"] = Noise(newApp.Length, 9);
        failure = Fails(() => AppUpdate.DownloadAsync(update, exe, new Reports(), CancellationToken.None));
        Console.WriteLine($"   wrong file: {failure?.GetType().Name}: {failure?.Message}");
        Program.Check(failure is InvalidDataException && !File.Exists(incoming) && hub.Seen.Count == 1, "a file that does not match GitHub's SHA-256 is thrown away and not asked for again");

        hub.Reset();
        hub.Files[$"/download/v2.10/{AssetName}"] = newApp;
        hub.Extra = 10;
        failure = Fails(() => AppUpdate.DownloadAsync(update, exe, new Reports(), CancellationToken.None));
        Program.Check(failure is InvalidDataException && !File.Exists(incoming), $"a download larger than the release file is refused ({failure?.Message})");

        hub.Reset();
        hub.Status = 404;
        var clock = Stopwatch.StartNew();
        failure = Fails(() => AppUpdate.DownloadAsync(update, exe, new Reports(), CancellationToken.None));
        Console.WriteLine($"   refused download: {failure?.Message} after {hub.Seen.Count} tries, {clock.Elapsed.TotalSeconds:0.0}s");
        Program.Check(failure is IOException && failure.Message.Contains("404") && hub.Seen.Count == 3 && !File.Exists(incoming), "a download GitHub refuses is tried three times, then given up with the reason");

        hub.Reset();
        hub.Delay = 60;
        using (var stop = new CancellationTokenSource())
        {
            reports = new Reports { Each = report => { if (report.Fraction > 0.1) stop.Cancel(); } };
            failure = Fails(() => AppUpdate.DownloadAsync(update, exe, reports, stop.Token));
        }
        Program.Check(failure is OperationCanceledException && !File.Exists(incoming), "a cancelled download leaves nothing behind");

        hub.Reset();
        string spare = exe + ".config", older = exe + ".older";
        File.WriteAllBytes(incoming, other);
        File.WriteAllBytes(exe + ".old", other);
        File.WriteAllBytes(spare, other);
        File.WriteAllBytes(older, other);
        File.SetAttributes(exe + ".old", FileAttributes.ReadOnly);
        Now(async () => { await AppUpdate.CleanUpAsync(exe); return 0; });
        Program.Check(!File.Exists(incoming) && !File.Exists(exe + ".old") && File.Exists(spare) && File.Exists(older) && File.Exists(exe),
            "starting up clears a leftover download and helper copy, even a read-only one, and nothing else");
        File.WriteAllBytes(exe + ".old", other);
        clock.Restart();
        using (var held = new FileStream(exe + ".old", FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Task cleaning = Task.Run(() => AppUpdate.CleanUpAsync(exe));
            Thread.Sleep(1300);
            bool waiting = !cleaning.IsCompleted;
            held.Dispose();
            bool finished = cleaning.Wait(TimeSpan.FromSeconds(5));
            Program.Check(waiting && finished && !File.Exists(exe + ".old"), $"a helper copy that is still closing is waited for, then cleared ({clock.Elapsed.TotalSeconds:0.0}s)");
        }

        Probes(root, hub, one, two);
        Windows(scratch, root, hub, one, two);
    }

    private static void Probes(string root, FakeGitHub hub, string one, string two)
    {
        Console.WriteLine("== updates: the whole cycle in a real single-file app");
        byte[] oldApp = File.ReadAllBytes(one), newApp = File.ReadAllBytes(two);
        hub.Reset();
        hub.Redirect = true;
        hub.Files[$"/download/v9/{AssetName}"] = newApp;
        hub.Releases = Json(Release("v9", DateTimeOffset.UtcNow, false, false, Asset(AssetName, newApp, $"{hub.Address}/download/v9/{AssetName}", DateTimeOffset.UtcNow)));

        string folder = Path.Combine(root, "cycle with spaces");
        string exe = Path.Combine(folder, ExeName), note = exe + ".log";
        Directory.CreateDirectory(folder);
        File.Copy(one, exe);
        string map = @"D:\some maps\zm_town & co\zone\zm_town.ff";
        using (Process first = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { "update", hub.List.ToString(), map, "100% \"odd\"" } })!)
            first.WaitForExit(60000);
        bool done = WaitFor(() => Text(note).Contains("[two PS4 .FF Porter.exe] exit"), 60);
        string log = Text(note);
        Console.WriteLine("     " + log.TrimEnd().Replace("\n", "\n     "));
        Program.Check(done && log.Contains("[one PS4 .FF Porter.exe] started failed=False packed=True") && log.Contains("check=Available") && log.Contains("others=0")
            && log.Contains($"downloaded {newApp.Length}") && log.Contains("handed over"), "the old app finds the release, downloads it and hands over to a helper copy of itself");
        Program.Check(log.Contains("[one PS4 .FF Porter.exe.old] helper started packed=True") && log.Contains("helper done code=0"), "the helper copy puts the new exe in place once the app has closed");
        Program.Check(log.Contains($"[two PS4 .FF Porter.exe] started failed=False packed=True") && log.Contains($"args=check|{hub.List}|{map}|100% \"odd\""),
            "the new version starts by itself and is handed what was open, spaces and quotes intact");
        Program.Check(log.Contains("[two PS4 .FF Porter.exe] swept new=False old=False") && log.Contains("[two PS4 .FF Porter.exe] check=Current")
            && File.ReadAllBytes(exe).AsSpan().SequenceEqual(newApp) && Directory.GetFiles(folder).Length == 2,
            $"the new version clears the leftovers and finds itself up to date; the folder holds: {string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName))}");

        Console.WriteLine("   -- another copy of the app is still running");
        folder = Path.Combine(root, "busy");
        exe = Path.Combine(folder, ExeName);
        note = exe + ".log";
        string release = Path.Combine(folder, "let go");
        Directory.CreateDirectory(folder);
        File.Copy(one, exe);
        using (Process holder = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { "hold", release } })!)
        {
            WaitFor(() => Text(note).Contains("holding"), 20);
            int others = AppUpdate.Others(exe);
            using (Process first = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { "update", hub.List.ToString(), map } })!)
                first.WaitForExit(60000);
            WaitFor(() => Text(note).Contains("helper started"), 20);
            Thread.Sleep(4000);
            bool untouched = File.ReadAllBytes(exe).AsSpan().SequenceEqual(oldApp) && !Text(note).Contains("helper done");
            File.WriteAllText(release, "");
            holder.WaitForExit(20000);
            done = WaitFor(() => Text(note).Contains("[two PS4 .FF Porter.exe] exit"), 40);
            log = Text(note);
            Console.WriteLine("     " + log.TrimEnd().Replace("\n", "\n     "));
            Program.Check(others == 1 && log.Contains("others=1"), "another running copy of the app is counted");
            Program.Check(untouched, "while another copy is still running, the exe is not replaced: the helper waits");
            Program.Check(done && log.Contains("helper done code=0") && File.ReadAllBytes(exe).AsSpan().SequenceEqual(newApp) && log.Contains("[two PS4 .FF Porter.exe] check=Current"),
                "as soon as the other copy closes, the new exe goes in and starts");
        }

        Console.WriteLine("   -- the other copy never closes (30 s)");
        folder = Path.Combine(root, "stuck");
        exe = Path.Combine(folder, ExeName);
        note = exe + ".log";
        release = Path.Combine(folder, "let go");
        Directory.CreateDirectory(folder);
        File.Copy(one, exe);
        using (Process holder = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { "hold", release } })!)
        {
            WaitFor(() => Text(note).Contains("holding"), 20);
            using (Process first = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { "update", hub.List.ToString(), map } })!)
                first.WaitForExit(60000);
            done = WaitFor(() => Text(note).Contains("started failed=True") && Text(note).Split("started failed=True")[1].Contains("exit"), 60);
            log = Text(note);
            Console.WriteLine("     " + log.TrimEnd().Replace("\n", "\n     "));
            Program.Check(done && log.Contains("helper done code=1") && log.Contains($"[one PS4 .FF Porter.exe] started failed=True packed=True") && log.Contains($"args=check|{hub.List}|{map}")
                && File.ReadAllBytes(exe).AsSpan().SequenceEqual(oldApp),
                "if the exe never comes free, it is left as it was and the old version is started again, told that the update did not go in");
            Program.Check(log.Split("started failed=True")[1].Contains("swept new=False old=False") && log.Split("started failed=True")[1].Contains("check=Available"),
                "that restart clears the unused download and still sees the update");
            File.WriteAllText(release, "");
            holder.WaitForExit(20000);
        }
    }

    private static void Touch(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[bytes];
        if (path.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
            "TAff"u8.CopyTo(data);
        File.WriteAllBytes(path, data);
    }

    private sealed class Stage
    {
        public required MainWindow Window;
        public required Border Host;
        public required string Exe;
        public bool Closed;

        public T Named<T>(string name) where T : class => (T)Window.FindName(name);
        public object? Field(string name) => typeof(MainWindow).GetField(name, Private)!.GetValue(Window);
        public void Set(string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(Window, value);
        public object? Call(string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(Window, arguments);
        public Visibility Overlay => Named<Grid>("UpdateOverlay").Visibility;
        public Visibility Button => Named<Button>("UpdateButton").Visibility;
        public string Status => ((Run)Window.FindName("StatusText")).Text + " |" + ((Run)Window.FindName("DetailText")).Text;
        public bool Busy => Field("_updating") != null;
    }

    private static Stage Open(string root, string name, string source)
    {
        string exe = Path.Combine(root, name, ExeName);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.Copy(source, exe);
        var window = new MainWindow();
        var content = (UIElement)window.Content;
        window.Content = null;
        var stage = new Stage { Window = window, Host = Program.Host(window, content, 1364, 841), Exe = exe };
        window.Closed += (_, _) => stage.Closed = true;
        stage.Set("_exe", exe);
        return stage;
    }

    private static void Windows(string scratch, string root, FakeGitHub hub, string one, string two)
    {
        Console.WriteLine("== updates: the window");
        byte[] oldApp = File.ReadAllBytes(one), newApp = File.ReadAllBytes(two);
        hub.Reset();
        hub.Redirect = true;
        hub.Files[$"/download/v9/{AssetName}"] = newApp;
        var update = new UpdateFile(AssetName, "v2.10", new Uri($"{hub.Address}/download/v9/{AssetName}"), newApp.Length, Sha(newApp), DateTimeOffset.UtcNow, "• Faster\n• Fewer bugs");

        Stage stage = Open(root, "window", one);
        string usermaps = Path.Combine(root, "queue", "usermaps");
        foreach (string file in (string[])["zone/zm_one.ff", "zone/zm_one.xpak", "workshop.json"])
            Touch(Path.Combine(usermaps, "zm one", file), 4000);
        foreach (string file in (string[])["zone/zm_two.ff", "workshop.json"])
            Touch(Path.Combine(usermaps, "zm_two", file), 4000);
        stage.Window.AddPaths([usermaps]);
        Program.Check(stage.Overlay == Visibility.Collapsed && stage.Button == Visibility.Collapsed, "with no update, the window shows nothing about updating");

        hub.Delay = 45;
        var offer = (Task)stage.Call("OfferUpdate", update)!;
        var bar = stage.Named<ProgressBar>("UpdateProgress");
        CommunityChecks.Pump(() => bar.Value > 0.35, 30);
        Console.WriteLine($"   overlay: '{stage.Named<TextBlock>("UpdateHeading").Text}' / '{stage.Named<TextBlock>("UpdateBody").Text}' / '{stage.Named<TextBlock>("UpdateDetail").Text}' / button '{stage.Named<Button>("UpdateCancel").Content}'");
        Program.Check(stage.Overlay == Visibility.Visible && stage.Busy && bar.Value is > 0.35 and < 1 && stage.Named<TextBlock>("UpdateDetail").Text.Contains(" of ")
            && stage.Named<TextBlock>("UpdateBody").Text == "v2.10 is downloading. The app restarts by itself." && (string)stage.Named<Button>("UpdateCancel").Content == "Not now",
            "an update found while the app is idle starts by itself and shows its progress over the window");
        Program.Check(stage.Named<Border>("UpdateNotesBox").Visibility == Visibility.Visible && stage.Named<TextBlock>("UpdateNotes").Text == "• Faster\n• Fewer bugs",
            "the panel shows what is new in the release");
        Program.Render(stage.Host, Path.Combine(scratch, "update_overlay.png"));
        hub.Delay = 0;
        CommunityChecks.Pump(() => offer.IsCompleted, 30);
        string note = stage.Exe + ".log";
        bool started = WaitFor(() => Text(note).Contains("[two PS4 .FF Porter.exe] exit"), 40);
        string log = Text(note);
        Console.WriteLine("     " + log.TrimEnd().Replace("\n", "\n     "));
        string first = Path.Combine(usermaps, "zm one", "zone", "zm_one.ff"), second = Path.Combine(usermaps, "zm_two", "zone", "zm_two.ff");
        Program.Check(offer.IsCompletedSuccessfully && stage.Closed && !stage.Busy, "when the download is in and checked, the window closes by itself");
        Program.Check(started && log.Contains("helper done code=0") && File.ReadAllBytes(stage.Exe).AsSpan().SequenceEqual(newApp) && log.Contains($"args={first}|{second}"),
            "the new version is put in place and started with the queue the window had");

        stage = Open(root, "cancel", one);
        hub.Seen.Clear();
        hub.Delay = 45;
        offer = (Task)stage.Call("OfferUpdate", update)!;
        bar = stage.Named<ProgressBar>("UpdateProgress");
        CommunityChecks.Pump(() => bar.Value > 0.2, 30);
        stage.Call("UpdateCancelClick", stage.Window, new RoutedEventArgs());
        CommunityChecks.Pump(() => offer.IsCompleted, 30);
        Console.WriteLine($"   after Not now: {stage.Status}");
        Program.Check(offer.IsCompletedSuccessfully && stage.Overlay == Visibility.Collapsed && stage.Button == Visibility.Visible && !stage.Closed && !File.Exists(stage.Exe + ".new")
            && !File.Exists(stage.Exe + ".old") && File.ReadAllBytes(stage.Exe).AsSpan().SequenceEqual(oldApp) && stage.Status.StartsWith("Update postponed"),
            "Not now stops the download, leaves the app as it is and puts an Update button in the footer");
        Program.Render(stage.Host, Path.Combine(scratch, "update_button.png"));
        hub.Delay = 0;
        stage.Set("_running", true);
        stage.Call("SetBusy", true);
        Program.Check(!stage.Named<Button>("UpdateButton").IsEnabled, "the Update button is off while something converts");
        stage.Set("_running", false);
        stage.Call("SetBusy", false);

        hub.Files[$"/download/v9/{AssetName}"] = Noise(newApp.Length, 5);
        stage.Call("UpdateClick", stage.Window, new RoutedEventArgs());
        var heading = stage.Named<TextBlock>("UpdateHeading");
        CommunityChecks.Pump(() => !stage.Busy, 30);
        Console.WriteLine($"   failed: '{heading.Text}' / '{stage.Named<TextBlock>("UpdateBody").Text.Replace("\n", " ")}' / button '{stage.Named<Button>("UpdateCancel").Content}'");
        Program.Check(stage.Overlay == Visibility.Visible && heading.Text == "The update was not installed" && (string)stage.Named<Button>("UpdateCancel").Content == "Close"
            && stage.Named<ProgressBar>("UpdateProgress").Visibility == Visibility.Collapsed && stage.Named<Border>("UpdateNotesBox").Visibility == Visibility.Collapsed
            && stage.Named<TextBlock>("UpdateDetail").Text == "The Update button tries again." && !stage.Named<TextBlock>("UpdateBody").Text.Contains('\n')
            && !File.Exists(stage.Exe + ".new") && File.ReadAllBytes(stage.Exe).AsSpan().SequenceEqual(oldApp),
            "pressing Update tries again; a download that does not match the release is not installed, and the window says so");
        Program.Render(stage.Host, Path.Combine(scratch, "update_failed.png"));
        stage.Call("UpdateCancelClick", stage.Window, new RoutedEventArgs());
        Program.Check(stage.Overlay == Visibility.Collapsed && stage.Button == Visibility.Visible && !stage.Closed, "Close puts the window back, with the Update button still there");
        hub.Files[$"/download/v9/{AssetName}"] = newApp;

        stage = Open(root, "busy window", one);
        stage.Set("_running", true);
        int asked = hub.Seen.Count;
        offer = (Task)stage.Call("OfferUpdate", update)!;
        CommunityChecks.Pump(() => offer.IsCompleted, 10);
        Program.Check(stage.Overlay == Visibility.Collapsed && stage.Button == Visibility.Visible && hub.Seen.Count == asked && !File.Exists(stage.Exe + ".new"),
            "an update found while something is converting does not interrupt: only the Update button appears");
        stage.Set("_running", false);

        stage = Open(root, "locked", one);
        asked = hub.Seen.Count;
        using (new FileStream(stage.Exe + ".new", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            offer = (Task)stage.Call("OfferUpdate", update)!;
            CommunityChecks.Pump(() => offer.IsCompleted, 10);
        }
        File.Delete(stage.Exe + ".new");
        Program.Check(stage.Overlay == Visibility.Collapsed && stage.Button == Visibility.Visible && hub.Seen.Count == asked,
            "where the app's folder cannot be written, the update does not start by itself at every launch: only the Update button appears");
        Program.Check(AppUpdate.Writable(stage.Exe) && !File.Exists(stage.Exe + ".new"), "checking that the folder can be written leaves nothing in it");

        stage = Open(root, "told", one);
        stage.Window.UpdateFailed();
        offer = (Task)stage.Call("OfferUpdate", update)!;
        CommunityChecks.Pump(() => offer.IsCompleted, 10);
        Console.WriteLine($"   after a failed install: {stage.Status}");
        Program.Check(stage.Overlay == Visibility.Collapsed && stage.Button == Visibility.Visible && stage.Status.StartsWith("The update was not installed"),
            "started again after an install that did not go in, the app says so and waits for the Update button instead of trying in a loop");

        stage = Open(root, "second copy", one);
        string release = Path.Combine(root, "second copy", "let go");
        using (Process holder = Process.Start(new ProcessStartInfo(stage.Exe) { UseShellExecute = false, ArgumentList = { "hold", release } })!)
        {
            WaitFor(() => Text(stage.Exe + ".log").Contains("holding"), 20);
            offer = (Task)stage.Call("OfferUpdate", update)!;
            CommunityChecks.Pump(() => offer.IsCompleted, 10);
            Console.WriteLine($"   with a second copy open: {stage.Status}");
            Program.Check(stage.Overlay == Visibility.Collapsed && stage.Button == Visibility.Visible && stage.Status.StartsWith("The update waits for the other windows") && !File.Exists(stage.Exe + ".new"),
                "with a second copy of the app open, nothing is downloaded and the window asks for it to be closed");
            File.WriteAllText(release, "");
            holder.WaitForExit(20000);
        }

        stage = Open(root, "closing", one);
        hub.Delay = 45;
        offer = (Task)stage.Call("OfferUpdate", update)!;
        bar = stage.Named<ProgressBar>("UpdateProgress");
        CommunityChecks.Pump(() => bar.Value > 0.2, 30);
        stage.Window.Close();
        bool held = !stage.Closed;
        CommunityChecks.Pump(() => offer.IsCompleted && stage.Closed, 30);
        hub.Delay = 0;
        Program.Check(held && stage.Closed && !File.Exists(stage.Exe + ".new") && !File.Exists(stage.Exe + ".old") && File.ReadAllBytes(stage.Exe).AsSpan().SequenceEqual(oldApp),
            "closing the window during the download stops it cleanly first, then closes; nothing is left behind and the app is unchanged");

        var view = stage.Named<FFPorter.Desktop.Controls.LogView>("Log");
        string noted = ((TextBox)view.FindName("Text")).Text + typeof(FFPorter.Desktop.Controls.LogView).GetField("_pending", Private)!.GetValue(view);
        Console.WriteLine("   log: " + noted.Trim().Replace("\r", "").Replace("\n", " / "));
        Program.Check(noted.Contains($"A newer version is out (v2.10): {AssetName}, {Job.SizeText(newApp.Length)}."), "the log notes the update");
    }
}
