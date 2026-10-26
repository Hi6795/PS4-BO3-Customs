using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FFPorter.Desktop;
using FFPorter.Desktop.Controls;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal sealed class MiniServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _root;

    public ConcurrentDictionary<string, int> Hits { get; } = new();
    public ConcurrentDictionary<string, bool> Missing { get; } = new();
    public ConcurrentQueue<string> Asked { get; } = new();
    public volatile string? Slow, Cut;
    public volatile int CutAfter;
    public volatile bool NoRange;
    public int Port { get; }

    public MiniServer(string root)
    {
        _root = root;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
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
                string path = Uri.UnescapeDataString(request.ToString().Split(' ')[1].Split('?')[0]);
                string range = request.ToString().Split("\r\n").FirstOrDefault(line => line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))?[6..].Trim() ?? "";
                Hits.AddOrUpdate(path, 1, (_, count) => count + 1);
                Asked.Enqueue($"{path} {range}".TrimEnd());
                string file = Path.Combine(_root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (Missing.ContainsKey(path) || !File.Exists(file))
                {
                    stream.Write(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                    return;
                }
                byte[] data = File.ReadAllBytes(file);
                int from = 0;
                bool partial = !NoRange && range.StartsWith("bytes=", StringComparison.Ordinal);
                if (partial)
                    from = (int)Math.Min(int.MaxValue, long.Parse(range[6..].TrimEnd('-')));
                if (partial && from >= data.Length)
                {
                    stream.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{data.Length}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                    return;
                }
                string status = partial ? $"206 Partial Content\r\nContent-Range: bytes {from}-{data.Length - 1}/{data.Length}" : "200 OK";
                stream.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {data.Length - from}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n"));
                int limit = int.MaxValue;
                if (path == Cut)
                {
                    Cut = null;
                    limit = CutAfter;
                }
                for (int at = from, sent = 0; at < data.Length && sent < limit; )
                {
                    int count = Math.Min(Math.Min(65536, data.Length - at), limit - sent);
                    stream.Write(data, at, count);
                    at += count;
                    sent += count;
                    if (path == Slow)
                        Thread.Sleep(120);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    public void Dispose() => _listener.Stop();
}

internal static class CommunityChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Random Dice = new(7);

    internal static byte[] Noise(int bytes, string? head = null)
    {
        var data = new byte[bytes];
        Dice.NextBytes(data);
        if (head != null)
            Encoding.ASCII.GetBytes(head).CopyTo(data, 0);
        return data;
    }

    internal static byte[] Zip(Dictionary<string, byte[]> entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] data) in entries)
            {
                using Stream entry = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
                entry.Write(data);
            }
        }
        return buffer.ToArray();
    }

    internal static List<string> Split(byte[] zip, string folder, string name, int size)
    {
        Directory.CreateDirectory(folder);
        var parts = new List<string>();
        for (int at = 0, index = 1; at < zip.Length; at += size, index++)
        {
            string part = $"{name}.zip.{index:000}";
            File.WriteAllBytes(Path.Combine(folder, part), zip.AsSpan(at, Math.Min(size, zip.Length - at)).ToArray());
            parts.Add(part);
        }
        return parts;
    }

    internal static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    internal static byte[] Picture(Color colour)
    {
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(new LinearGradientBrush(colour, Color.FromRgb(20, 22, 26), 35), null, new Rect(0, 0, 320, 180));
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), null, new Point(230, 60), 46, 46);
        }
        var bitmap = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    internal static Dictionary<string, byte[]> MapFiles(string id, string prefix, int scale = 1) => new()
    {
        [$"{prefix}{id}.ff"] = Noise(3_000_000 * scale, "TAff"),
        [$"{prefix}en_{id}.ff"] = Noise(200_000),
        [$"{prefix}{id}.xpak"] = Noise(2_500_000 * scale),
        [$"{prefix}snd/all/{id}.all.sabl"] = Noise(400_000),
        [$"{prefix}workshop.json"] = Encoding.UTF8.GetBytes("{\"Title\":\"Demo\"}"),
        [$"{prefix}previewimage.png"] = Picture(Colors.SteelBlue),
    };

    internal static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;

    internal static void Pump(Func<bool> done, double seconds = 20)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Background, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private static Exception? Fails(Func<Task> action)
    {
        Task task = Task.Run(action);
        Pump(() => task.IsCompleted, 60);
        return task.Exception?.InnerException ?? (task.IsCanceled ? new OperationCanceledException() : null);
    }

    public static void Run(string scratch)
    {
        Console.WriteLine("== community maps");
        string site = Path.Combine(scratch, "site");
        string maps = Path.Combine(site, "t7", "maps");
        Directory.CreateDirectory(Path.Combine(site, "t7", "thumbnails"));

        Dictionary<string, byte[]> demo = MapFiles("zm_demo", "zm_demo/");
        var demoZip = new Dictionary<string, byte[]>(demo)
        {
            ["zm_demo/evil.exe"] = Noise(5000, "MZ"),
            ["zm_demo/../../escape.ff"] = Noise(5000, "TAff"),
            ["zm_demo/notes.txt"] = Noise(100),
        };
        byte[] zip = Zip(demoZip);
        List<string> demoParts = Split(zip, Path.Combine(maps, "zm_demo"), "zm_demo", 1_500_000);

        byte[] missingZip = Zip(MapFiles("zm_missing", "zm_missing/"));
        List<string> missingParts = Split(missingZip, Path.Combine(maps, "zm_missing"), "zm_missing", 2_500_000);
        byte[] notMapZip = Zip(new Dictionary<string, byte[]> { ["readme.json"] = Noise(2000) });
        Split(notMapZip, Path.Combine(maps, "zm_notmap"), "zm_notmap", 1 << 20);
        byte[] flatZip = Zip(MapFiles("mp_flat", ""));
        Directory.CreateDirectory(Path.Combine(maps, "mp_flat"));
        File.WriteAllBytes(Path.Combine(maps, "mp_flat", "mp_flat.zip"), flatZip);
        byte[] slowZip = Zip(MapFiles("zm_slow", "zm_slow/", 2));
        Directory.CreateDirectory(Path.Combine(maps, "zm_slow"));
        File.WriteAllBytes(Path.Combine(maps, "zm_slow", "zm_slow.zip"), slowZip);
        var modFiles = new Dictionary<string, byte[]>
        {
            ["coolmod/core_mod.ff"] = Noise(900_000, "TAff"),
            ["coolmod/en_core_mod.ff"] = Noise(60_000, "TAff"),
            ["coolmod/zm_mod.ff"] = Noise(700_000, "TAff"),
            ["coolmod/core_mod.xpak"] = Noise(800_000),
            ["coolmod/snd/all/zm_mod.all.sabl"] = Noise(100_000),
            ["coolmod/workshop.json"] = Encoding.UTF8.GetBytes("{\"Title\":\"Cool Mod\"}"),
        };
        byte[] modZip = Zip(modFiles);
        Directory.CreateDirectory(Path.Combine(maps, "coolmod"));
        File.WriteAllBytes(Path.Combine(maps, "coolmod", "coolmod.zip"), modZip);
        foreach ((string name, Color colour) in new[] { ("zm_demo", Colors.SteelBlue), ("zm_missing", Colors.IndianRed), ("mp_flat", Colors.SeaGreen), ("zm_slow", Colors.Goldenrod), ("coolmod", Colors.MediumPurple) })
            File.WriteAllBytes(Path.Combine(site, "t7", "thumbnails", name + ".png"), Picture(colour));

        using var server = new MiniServer(site);
        string address = $"http://127.0.0.1:{server.Port}/t7/catalog.json";
        object[] entries =
        [
            new { name = "zm_demo", title = "The Demo Challenge", author = "someone", version = "1.2", description = "A small test map with a description that says what the map is about and runs long enough to be cut off at the end of its second line in the list.",
                size = zip.Length, folder = "zm_demo", parts = demoParts.Select(part => "maps/zm_demo/" + part), thumbnail = "thumbnails/zm_demo.png", sha256 = Sha(zip) },
            new { name = "zm_badsum", title = "Wrong Checksum", size = zip.Length, folder = "zm_badsum", parts = demoParts.Select(part => "maps/zm_demo/" + part), sha256 = new string('0', 64) },
            new { name = "zm_missing", title = "WEEPING PINES", size = missingZip.Length, folder = "zm_missing", parts = missingParts.Select(part => "maps/zm_missing/" + part), thumbnail = "thumbnails/zm_missing.png", sha256 = Sha(missingZip) },
            new { name = "zm_notmap", title = "Not A Map", size = notMapZip.Length, parts = new[] { "maps/zm_notmap/zm_notmap.zip.001" }, sha256 = Sha(notMapZip) },
            new { name = "mp_flat", title = "[MP] Flat Town '68", file = $"http://127.0.0.1:{server.Port}/t7/maps/mp_flat/mp_flat.zip", thumbnail = "thumbnails/mp_flat.png" },
            new { name = "zm_slow", title = "Kyassuru", size = slowZip.Length, url = "maps/zm_slow/zm_slow.zip", thumbnail = "thumbnails/zm_slow.png", sha256 = Sha(slowZip) },
            new { name = "../evil", title = "Bad Folder", parts = new[] { "maps/zm_demo/zm_demo.zip.001" } },
            new { name = "zm_plainhttp", title = "Plain Http", parts = new[] { "http://example.com/zm_plainhttp.zip" } },
            new { name = "zm_demo", title = "Duplicate", parts = new[] { "maps/zm_demo/zm_demo.zip.001" } },
            new { name = "zm_badsha", title = "Bad Sha", parts = new[] { "maps/zm_demo/zm_demo.zip.001" }, sha256 = "abc" },
            new { name = "zm_nofile", title = "No File" },
            new { name = "cp_story", title = "[mp]Tight Lowercase Tag", parts = new[] { "maps/zm_demo/zm_demo.zip.001" } },
            new { name = "coolmod", title = "Cool Mod", type = "mod", size = modZip.Length, file = "maps/coolmod/coolmod.zip", thumbnail = "thumbnails/coolmod.png", sha256 = Sha(modZip) },
            new { name = "zm_looks_like_a_map", title = "[MP]", category = "Mods", parts = demoParts.Select(part => "maps/zm_demo/" + part) },
        ];
        object[] modEntries =
        [
            new { name = "weaponpack", title = "Weapon Pack", author = "someone else", size = modZip.Length, file = "maps/coolmod/coolmod.zip", sha256 = Sha(modZip) },
        ];
        File.WriteAllText(Path.Combine(site, "t7", "catalog.json"), JsonSerializer.Serialize(new { name = "Test maps", maps = entries, mods = modEntries }), new UTF8Encoding(true));

        var log = new List<string>();
        void Log(string line)
        {
            lock (log)
                log.Add(line);
        }

        Program.Check(CommunityCatalog.TryAddress("https://host/t7/catalog.json", out _) && CommunityCatalog.TryAddress(address, out Uri list)
            && !CommunityCatalog.TryAddress("http://example.com/catalog.json", out _) && !CommunityCatalog.TryAddress("ftp://host/x", out _) && !CommunityCatalog.TryAddress("catalog.json", out _),
            "a list address must be https (plain http only on this PC)");
        CommunityCatalog.TryAddress(address, out list);
        Task<IReadOnlyList<CommunityMap>> loading = Task.Run(() => CommunityCatalog.LoadAsync(list, Log, CancellationToken.None));
        Pump(() => loading.IsCompleted);
        IReadOnlyList<CommunityMap> found = loading.Result;
        Console.WriteLine("   listed: " + string.Join(", ", found.Select(map => $"{map.Id} ({map.Parts.Count} part{(map.Parts.Count == 1 ? "" : "s")})")));
        foreach (string line in log)
            Console.WriteLine("   log: " + line);
        Program.Check(found.Select(map => map.Id).SequenceEqual(["zm_demo", "zm_badsum", "zm_missing", "zm_notmap", "mp_flat", "zm_slow", "cp_story", "coolmod", "zm_looks_like_a_map", "weaponpack"]) && log.Count == 5,
            "the list keeps ten entries and skips the bad folder name, the plain-http file, the duplicate, the bad checksum and the entry with no file");
        Console.WriteLine("   kinds: " + string.Join(", ", found.Select(map => $"{map.Id}={map.Kind}")));
        Program.Check(found.First(map => map.Id == "zm_demo").Kind == CommunityKinds.Zombies && found.First(map => map.Id == "mp_flat").Kind == CommunityKinds.Multiplayer
            && found.First(map => map.Id == "cp_story").Kind == CommunityKinds.Campaign && found.First(map => map.Id == "coolmod").Kind == CommunityKinds.Mod
            && found.First(map => map.Id == "zm_looks_like_a_map").Kind == CommunityKinds.Mod && found.First(map => map.Id == "weaponpack").Kind == CommunityKinds.Mod,
            "a map's type comes from its name (zm_, mp_, cp_); a mod is anything typed as one, listed under mods, or with no map prefix");
        Console.WriteLine("   titles: " + string.Join(" | ", found.Select(map => map.Title)));
        Program.Check(found.First(map => map.Id == "mp_flat").Title == "Flat Town '68" && found.First(map => map.Id == "cp_story").Title == "Tight Lowercase Tag"
            && found.First(map => map.Id == "zm_looks_like_a_map").Title == "[MP]" && found.First(map => map.Id == "zm_missing").Title == "WEEPING PINES",
            "a leading [MP] is dropped from a title, whatever its case or spacing; a title that is only the tag is kept");
        CommunityMap Map(string id) => found.First(map => map.Id == id);
        Program.Check(Map("zm_demo").Parts.Count == demoParts.Count && Map("zm_demo").Parts[0].AbsoluteUri == $"http://127.0.0.1:{server.Port}/t7/maps/zm_demo/zm_demo.zip.001"
            && Map("zm_demo").Thumbnail != null && Map("zm_demo").Meta == $"Zombies  ·  {Job.SizeText(zip.Length)}  ·  by someone" && Map("mp_flat").Folder == "mp_flat",
            "parts and pictures resolve against the list's address");

        string output = Path.Combine(scratch, "out", "usermaps");
        var progress = new List<DownloadProgress>();
        var reporter = new Progress<DownloadProgress>(report => progress.Add(report));
        Exception? Download(string id, CancellationToken token = default) =>
            Fails(() => CommunityDownloader.DownloadAsync(Map(id), Path.Combine(output, Map(id).Folder), reporter, Log, new PauseGate(), token));

        Directory.CreateDirectory(Path.Combine(output, "zm_demo"));
        File.WriteAllText(Path.Combine(output, "zm_demo", "old_conversion.ff"), "old");
        Exception? error = Download("zm_demo");
        string[] got = Directory.Exists(Path.Combine(output, "zm_demo"))
            ? [.. Directory.EnumerateFiles(Path.Combine(output, "zm_demo"), "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(Path.Combine(output, "zm_demo"), file).Replace('\\', '/')).Order()]
            : [];
        Console.WriteLine($"   zm_demo: {error?.Message ?? "ok"}; files: {string.Join(" | ", got)}");
        Program.Check(error == null && got.SequenceEqual(demo.Keys.Select(name => name["zm_demo/".Length..]).Order())
            && demo.All(pair => File.ReadAllBytes(Path.Combine(output, pair.Key.Replace('/', '\\'))).AsSpan().SequenceEqual(pair.Value)),
            "a split download unpacks to exactly the map's files, byte for byte, and replaces what was in the folder");
        Program.Check(!File.Exists(Path.Combine(scratch, "out", "escape.ff")) && !File.Exists(Path.Combine(output, "escape.ff")) && !got.Contains("evil.exe") && !got.Contains("notes.txt")
            && log.Any(line => line.Contains("left out 3 files")), "the .exe, the text file and the path that climbs out of the folder are left out");
        Program.Check(!Directory.Exists(Path.Combine(CommunityDownloader.ScratchRoot, "zm_demo")) && !Directory.Exists(Path.Combine(output, "zm_demo.incoming"))
            && !Directory.Exists(Path.Combine(output, "zm_demo.old")), "nothing temporary is left behind");
        Pump(() => false, 0.3);
        Program.Check(progress.Count >= 3 && progress.Any(report => report.Stage == "Downloading") && progress.Any(report => report.Stage == "Unpacking")
            && progress.Zip(progress.Skip(1)).All(pair => pair.Second.Fraction >= pair.First.Fraction - 1e-9) && progress[^1].Fraction <= 1,
            $"progress only moves forward ({progress.Count} reports, last: {progress[^1].Stage} {progress[^1].Fraction:0.00})");

        error = Download("zm_badsum");
        Program.Check(error is InvalidDataException && !Directory.Exists(Path.Combine(output, "zm_badsum")) && !Directory.Exists(Path.Combine(CommunityDownloader.ScratchRoot, "zm_badsum")),
            $"a download that fails its checksum is thrown away ({error?.Message})");

        error = Download("zm_notmap");
        Program.Check(error is InvalidDataException && !Directory.Exists(Path.Combine(output, "zm_notmap")), $"a zip with no <map>.ff is refused ({error?.Message})");

        string second = "/t7/maps/zm_missing/" + missingParts[1];
        server.Missing[second] = true;
        error = Download("zm_missing");
        string kept = Path.Combine(CommunityDownloader.ScratchRoot, "zm_missing", "001.part");
        Program.Check(error is IOException && server.Hits[second] == 3 && File.Exists(kept) && !Directory.Exists(Path.Combine(output, "zm_missing")),
            $"a part the server will not give is tried three times, then the parts already fetched are kept ({error?.Message})");
        server.Missing.Clear();
        error = Download("zm_missing");
        Program.Check(error == null && server.Hits["/t7/maps/zm_missing/" + missingParts[0]] == 1 && File.Exists(Path.Combine(output, "zm_missing", "zm_missing.ff"))
            && !Directory.Exists(Path.Combine(CommunityDownloader.ScratchRoot, "zm_missing")), "trying again picks up from the kept part instead of starting over");

        error = Download("mp_flat");
        Program.Check(error == null && File.Exists(Path.Combine(output, "mp_flat", "mp_flat.ff")) && File.Exists(Path.Combine(output, "mp_flat", "snd", "all", "mp_flat.all.sabl")),
            "a single-file zip with no top folder, size or checksum works too");

        string modsOutput = Path.Combine(scratch, "out", "mods");
        string modFolder = Path.Combine(modsOutput, "coolmod");
        error = Fails(() => CommunityDownloader.DownloadAsync(Map("coolmod"), modFolder, reporter, Log, new PauseGate(), default));
        Program.Check(error == null && modFiles.All(pair => File.ReadAllBytes(Path.Combine(modsOutput, pair.Key.Replace('/', '\\'))).AsSpan().SequenceEqual(pair.Value))
            && !File.Exists(Path.Combine(output, "coolmod", "core_mod.ff")), $"a mod unpacks into mods\\<mod> with its zones ({error?.Message ?? "ok"})");
        error = Fails(() => CommunityDownloader.DownloadAsync(Map("zm_looks_like_a_map"), Path.Combine(modsOutput, "zm_looks_like_a_map"), reporter, Log, new PauseGate(), default));
        Program.Check(error is InvalidDataException && !Directory.Exists(Path.Combine(modsOutput, "zm_looks_like_a_map")), $"a download typed as a mod that holds no mod zone is refused ({error?.Message})");

        server.Slow = "/t7/maps/zm_slow/zm_slow.zip";
        using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1.2)))
            error = Download("zm_slow", cancel.Token);
        long fetched = CommunityDownloader.Fetched(Map("zm_slow"));
        CommunityDownloader.Discard(Map("zm_slow"));
        Program.Check(error is OperationCanceledException && fetched > 0 && fetched < slowZip.Length && CommunityDownloader.Fetched(Map("zm_slow")) == 0
            && !Directory.Exists(Path.Combine(CommunityDownloader.ScratchRoot, "zm_slow")) && !Directory.Exists(Path.Combine(output, "zm_slow")),
            $"cancelling stops the download with what was fetched still there ({fetched:N0} bytes) until it is discarded");
        server.Slow = null;

        Program.Check(log.Count > 6 && !log.Any(line => line.Contains("127.0.0.1") || line.Contains("catalog.json") || line.Contains("/t7/")),
            "nothing written to the log names the server or a path on it");

        var library = CommunityLibrary.Load(Path.Combine(scratch, "library.json"));
        string demoFolder = Path.Combine(output, "zm_demo");
        Program.Check(library.Check(Map("zm_demo"), demoFolder) == LocalCopies.None, "a folder the app did not download counts as not downloaded");
        library.Remember(Map("zm_demo"), demoFolder);
        var newer = new CommunityMap { Id = "zm_demo", Title = "x", Folder = "zm_demo", Parts = Map("zm_demo").Parts, Sha256 = new string('1', 64) };
        Program.Check(CommunityLibrary.Load(Path.Combine(scratch, "library.json")).Check(Map("zm_demo"), demoFolder) == LocalCopies.Current
            && library.Check(newer, demoFolder) == LocalCopies.Outdated, "a download is remembered, and a different checksum in the list means an update");

        Windows(scratch, address, found.Count);
    }

    private static void Windows(string scratch, string address, int listed)
    {
        var settings = AppSettings.Current;
        void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(settings, value);
        Edition real = Edition.Current;
        Edition.Use(new ListEdition(real, address));
        Set("_outputOverride", Path.Combine(scratch, "out"));
        Set("_deleteOriginals", false);

        var window = new MainWindow();
        var content = (UIElement)window.Content;
        window.Content = null;
        Border host = Program.Host(window, content, 1364, 841);
        var panel = (CommunityPanel)window.FindName("Community");
        typeof(CommunityPanel).GetField("_library", Private)!.SetValue(panel, CommunityLibrary.Load(Path.Combine(scratch, "library.json")));
        var convert = (Button)window.FindName("ConvertButton");
        Program.Check(panel.Visibility == Visibility.Collapsed && convert.Visibility == Visibility.Visible, "the app opens on the Convert tab");

        ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;
        var maps = Field<System.Collections.ObjectModel.ObservableCollection<CommunityMap>>(panel, "_maps");
        Pump(() => Field<bool>(panel, "_loaded") && maps.Where(map => map.Thumbnail != null).All(map => map.HasPicture), 10);
        Program.Check(panel.Visibility == Visibility.Visible && ((FrameworkElement)window.FindName("ConvertView")).Visibility == Visibility.Collapsed && convert.Visibility == Visibility.Collapsed
            && maps.Count == listed, $"the Community tab shows the list ({maps.Count} maps) and hides the Convert button");
        CommunityMap Map(string id) => maps.First(map => map.Id == id);
        Program.Check(Map("zm_demo").Local == LocalCopies.Current && Map("zm_demo").Note == "Downloaded" && !Map("zm_demo").CanStart && Map("zm_demo").CanOpen
            && Map("mp_flat").Local == LocalCopies.None && Map("mp_flat").CanStart && Map("mp_flat").ActionText == "Download", "an already downloaded map says so; the others offer Download");
        Console.WriteLine($"   subtitle: {((TextBlock)window.FindName("SubtitleText")).Text}");
        string Footer(MainWindow from) => ((Run)from.FindName("StatusText")).Text + ((Run)from.FindName("DetailText")).Text;
        Console.WriteLine($"   status: {panel.Status} | footer: {Footer(window)}");
        Program.Render(host, Path.Combine(scratch, "community_list.png"));
        IEnumerable<string> Texts(MainWindow from, CommunityPanel on) => new[]
        {
            on.Status, Footer(from), ((TextBlock)on.FindName("EmptyTitle")).Text, ((TextBlock)on.FindName("EmptyText")).Text, ((TextBlock)from.FindName("SubtitleText")).Text,
        }.Concat(maps.SelectMany(map => new[] { map.Title, map.Meta, map.Tip, map.Note }));
        Program.Check(panel.Status == "7 maps and 3 mods to download" && !Texts(window, panel).Any(text => text.Contains("127.0.0.1") || text.Contains("catalog")),
            "the tab counts the list without naming the server anywhere, tooltips included");
        Program.Check(Footer(window) == panel.Status && panel.FindName("StatusText") == null && ((TextBlock)window.FindName("SubtitleText")).Text.StartsWith("Converted maps and mods, ready to download"),
            "the Community tab has no status strip of its own: its status shows in the window's footer");

        Directory.Delete(Path.Combine(scratch, "out", "usermaps", "mp_flat"), true);
        MethodInfo start = typeof(CommunityPanel).GetMethod("StartClick", Private)!;
        start.Invoke(panel, [new Button { DataContext = Map("mp_flat") }, new RoutedEventArgs()]);
        start.Invoke(panel, [new Button { DataContext = Map("zm_notmap") }, new RoutedEventArgs()]);
        Program.Check(Map("zm_notmap").State == RunStates.Queued && Map("zm_notmap").Note == "Waiting" && panel.IsDownloading, "a second download waits its turn");
        Pump(() => !panel.IsDownloading, 30);
        Program.Check(Map("mp_flat").State == RunStates.Done && Map("mp_flat").Local == LocalCopies.Current && File.Exists(Path.Combine(scratch, "out", "usermaps", "mp_flat", "mp_flat.ff")),
            "Download in the tab fetches the map into the output folder and marks it Downloaded");
        Program.Check(Map("zm_notmap").State == RunStates.Failed && Map("zm_notmap").ActionText == "Retry" && Map("zm_notmap").Note.StartsWith("The download holds no zm_notmap.ff"),
            $"a failed one says why and offers Retry ({Map("zm_notmap").Note})");

        using var console = new MiniFtp(Path.Combine(scratch, "ftp", "tab"));
        Set("_consoleAddress", $"127.0.0.1:{console.Port}");
        Set("_sendToConsole", false);
        panel.SettingsChanged();
        Program.Check(Map("mp_flat").HasConsole && !Map("mp_flat").Sent && Map("mp_flat").CanStart && Map("mp_flat").ActionText == "Send to console" && !Map("zm_slow").CanResend
            && Map("zm_slow").ActionText == "Download", "with a console saved, a downloaded map offers Send to console; the others still offer Download");
        start.Invoke(panel, [new Button { DataContext = Map("mp_flat") }, new RoutedEventArgs()]);
        Pump(() => !panel.IsDownloading, 30);
        string onConsole = console.Local("/data/BO3-Customs/usermaps/mp_flat");
        Program.Check(Map("mp_flat").State == RunStates.Done && Map("mp_flat").Sent && Map("mp_flat").Note == "Downloaded · on the console" && File.Exists(Path.Combine(onConsole, "mp_flat.ff"))
            && File.Exists(Path.Combine(onConsole, "snd", "all", "mp_flat.all.sabl")) && !Map("mp_flat").CanStart && Map("mp_flat").CanResend,
            $"Send to console copies the map over and the row says it is on the console ({Map("mp_flat").Note})");

        Directory.Delete(Path.Combine(scratch, "out", "mods", "coolmod"), true);
        Set("_sendToConsole", true);
        panel.SettingsChanged();
        start.Invoke(panel, [new Button { DataContext = Map("coolmod") }, new RoutedEventArgs()]);
        Pump(() => !panel.IsDownloading, 30);
        Program.Check(Map("coolmod").State == RunStates.Done && Map("coolmod").Local == LocalCopies.None && Map("coolmod").Sent && Map("coolmod").Note == "On the console"
            && File.Exists(console.Local("/data/BO3-Customs/mods/coolmod/core_mod.ff")) && !Directory.Exists(Path.Combine(scratch, "out", "mods", "coolmod")),
            $"with the option on, a download goes straight on to the console, a mod into mods, and the PC copy is cleared once it is there ({Map("coolmod").Note})");
        Program.Render(host, Path.Combine(scratch, "community_console.png"));

        console.Dispose();
        typeof(CommunityPanel).GetMethod("ResendClick", Private)!.Invoke(panel, [new Button { DataContext = Map("mp_flat") }, new RoutedEventArgs()]);
        Pump(() => !panel.IsDownloading, 40);
        Program.Check(Map("mp_flat").State == RunStates.Failed && Map("mp_flat").Note.StartsWith("Not sent to the console") && Map("mp_flat").Local == LocalCopies.Current && Map("mp_flat").CanResend,
            $"when the console is off, the row says it was not sent and keeps the download ({Map("mp_flat").Note})");
        Program.Render(host, Path.Combine(scratch, "community_console_off.png"));
        Set("_consoleAddress", null);
        Set("_sendToConsole", false);
        panel.SettingsChanged();
        Program.Check(!Map("mp_flat").HasConsole && !Map("mp_flat").CanStart && !Map("mp_flat").CanResend && Map("coolmod").Note == "",
            "with no console saved, nothing offers to send or claims to be on a console");

        Map("zm_missing").StageText = "Downloading · 1.21 GB of 4.21 GB · 18.3 MB/s";
        Map("zm_missing").Progress = 0.25;
        Map("zm_missing").State = RunStates.Running;
        Map("zm_slow").State = RunStates.Queued;
        Map("zm_badsum").Local = LocalCopies.Outdated;
        Program.Render(host, Path.Combine(scratch, "community_states.png"));
        Map("zm_missing").State = RunStates.Ready;
        Map("zm_slow").State = RunStates.Ready;

        var search = (TextBox)panel.FindName("SearchBox");
        search.Text = "flat";
        string Count(string name) => ((Run)panel.FindName(name)).Text.Trim();
        string[] Shown() => [.. ((ItemsControl)panel.FindName("MapList")).Items.Cast<CommunityMap>().Select(map => map.Id)];
        Program.Check(Count("AllCount") == "1" && Shown().SequenceEqual(["mp_flat"]), "search narrows the list");
        search.Text = "nothing like this";
        Program.Check(((TextBlock)panel.FindName("EmptyTitle")).Text == "Nothing matches your search", "a search with no match says so");
        search.Text = "";

        Program.Check(Count("AllCount") == "10" && Count("ZombiesCount") == "5" && Count("MultiplayerCount") == "1" && Count("ModsCount") == "3" && Shown().Length == 10,
            $"the filters count what is in the list (all {Count("AllCount")}, zombies {Count("ZombiesCount")}, multiplayer {Count("MultiplayerCount")}, mods {Count("ModsCount")})");
        ((RadioButton)panel.FindName("ZombiesFilter")).IsChecked = true;
        Program.Check(Shown().SequenceEqual(["zm_demo", "zm_badsum", "zm_missing", "zm_notmap", "zm_slow"]), "Zombies shows only the zombies maps");
        ((RadioButton)panel.FindName("MultiplayerFilter")).IsChecked = true;
        Program.Check(Shown().SequenceEqual(["mp_flat"]), "Multiplayer shows only the multiplayer maps");
        ((RadioButton)panel.FindName("ModsFilter")).IsChecked = true;
        Program.Check(Shown().SequenceEqual(["coolmod", "zm_looks_like_a_map", "weaponpack"])
            && settings.CommunityFolder(Map("coolmod")) == Path.Combine(scratch, "out", "mods", "coolmod"), "Mods shows only the mods, and they save under mods");
        Program.Render(host, Path.Combine(scratch, "community_mods.png"));
        search.Text = "weapon";
        Program.Check(Shown().SequenceEqual(["weaponpack"]) && Count("ModsCount") == "1" && Count("ZombiesCount") == "0" && Count("AllCount") == "1", "search and a filter work together, and the counts follow the search");
        search.Text = "demo";
        Program.Check(Shown().Length == 0 && ((TextBlock)panel.FindName("EmptyTitle")).Text == "Nothing matches your search" && Count("ZombiesCount") == "1",
            "a search with matches only in another filter says so, and the counts show where they are");
        search.Text = "";
        ((RadioButton)panel.FindName("AllFilter")).IsChecked = true;
        Program.Check(Shown().Length == 10, "All shows everything again, including the campaign map");

        ((RadioButton)window.FindName("ConvertTab")).IsChecked = true;
        Program.Check(panel.Visibility == Visibility.Collapsed && convert.Visibility == Visibility.Visible && Footer(window) == "Drop something to convert",
            "back on Convert the queue and the Convert button return, and the footer talks about the queue again");
        Program.Render(host, Path.Combine(scratch, "community_convert_tab.png"));

        Edition.Use(new ListEdition(real, "http://127.0.0.1:9/nothing.json"));
        var second = new MainWindow();
        var secondContent = (UIElement)second.Content;
        second.Content = null;
        Border secondHost = Program.Host(second, secondContent, 1364, 841);
        var unreachable = (CommunityPanel)second.FindName("Community");
        ((RadioButton)second.FindName("CommunityTab")).IsChecked = true;
        Pump(() => ((TextBlock)unreachable.FindName("EmptyTitle")).Text == "Could not load the map list", 30);
        string why = ((TextBlock)unreachable.FindName("EmptyText")).Text;
        Console.WriteLine($"   unreachable list: {((TextBlock)unreachable.FindName("EmptyTitle")).Text} - {why}");
        Program.Check(((TextBlock)unreachable.FindName("EmptyTitle")).Text == "Could not load the map list" && why.Length > 0 && !why.Contains("127.0.0.1") && !why.Contains(":9"),
            "a list that cannot be reached says so, without naming the server");
        Program.Render(secondHost, Path.Combine(scratch, "community_error.png"));
        Edition.Use(real);

        using var probe = new MiniFtp(Path.Combine(scratch, "ftp", "settings"));
        string saved = $"127.0.0.1:{probe.Port}";
        Set("_consoleAddress", saved);
        Set("_sendToConsole", true);
        SettingsWindow dialog = Program.Settled(new SettingsWindow(null));
        var body = (UIElement)dialog.Content;
        dialog.Content = null;
        Border frame = Program.Host(dialog, body, Program.SettingsWidth, double.NaN);
        var box = (TextBox)dialog.FindName("ConsoleBox");
        var send = (CheckBox)dialog.FindName("SendSwitch");
        var result = (TextBlock)dialog.FindName("ConsoleResult");
        Program.Check(box.Text == saved && send.IsChecked == true && send.IsEnabled, "Settings shows the saved console address and the send option");
        typeof(SettingsWindow).GetMethod("ConsoleTestClick", Private)!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        Pump(() => result.Text.StartsWith("Connected"), 30);
        Console.WriteLine($"   test button: {result.Text}");
        Program.Check(result.Visibility == Visibility.Visible && result.Text == "Connected. No BO3-Customs there yet. No usable drives." && !Directory.Exists(probe.Local("/data")),
            "the Test button connects and says what it found, changing nothing on the console");
        Program.Render(frame, Path.Combine(scratch, "settings_final.png"));
        MethodInfo closing = typeof(SettingsWindow).GetMethod("OnClosing", Private)!;
        box.Text = "not an address";
        var attempt = new CancelEventArgs();
        closing.Invoke(dialog, [attempt]);
        Program.Check(attempt.Cancel && result.Text.StartsWith("Not an address") && settings.ConsoleAddress == saved && !send.IsEnabled == false,
            "Settings does not close on something that is not an address, and keeps the saved one");
        box.Text = saved;
        attempt = new CancelEventArgs();
        closing.Invoke(dialog, [attempt]);
        Program.Check(!attempt.Cancel && settings.ConsoleAddress == saved, "a proper address closes normally");
        Set("_consoleAddress", null);
        Set("_sendToConsole", false);
        Program.Check(dialog.FindName("CommunityListBox") == null && typeof(AppSettings).GetMethod("SetCommunityList") == null && typeof(AppSettings).GetProperty("CommunityList") == null
            && typeof(AppSettings).GetNestedType("UiFile", BindingFlags.NonPublic)!.GetProperty("CommunityList") == null,
            "Settings has no list address, and nothing can save or load one");
    }
}

internal sealed class ListEdition(Edition inner, string? list, string? releases = null) : Edition
{
    public override string? UpdateSource => releases;
    public override string? ConsoleArchive => releases == null ? null : inner.ConsoleArchive;
    public override string? ConsolePlugin => releases == null ? null : inner.ConsolePlugin;
    public override string? ConsoleFolder(Job job) => inner.ConsoleFolder(job);
    public override string? ConsoleFolder(string name, bool mod) => inner.ConsoleFolder(name, mod);
    public override bool IsConverted(string zone) => inner.IsConverted(zone);
    public override string? Author => inner.Author;
    public override bool FindsInstalled => inner.FindsInstalled;
    public override IReadOnlyList<Installed> FindInstalled(string? gameFolder) => inner.FindInstalled(gameFolder);
    public override string Codename => inner.Codename;
    public override string Title => inner.Title;
    public override string Subtitle => inner.Subtitle;
    public override string GameName => inner.GameName;
    public override string GameLabel => inner.GameLabel;
    public override string SettingsFile => inner.SettingsFile;
    public override string GameFolderName => inner.GameFolderName;
    public override string GameFolderHint => inner.GameFolderHint;
    public override string GameFolderOption => inner.GameFolderOption;
    public override string? CommunityList => list;
    public override bool TryGameFolder(string chosen, out string? folder) => inner.TryGameFolder(chosen, out folder);
    public override void PrepareTools(Action<string> log) => inner.PrepareTools(log);
    public override IReadOnlyList<string> CacheFolders() => inner.CacheFolders();
    public override string? CacheFolder(Job job) => inner.CacheFolder(job);
    public override bool IsGameFolder(string folder) => inner.IsGameFolder(folder);
    public override Originals? FindOriginals(Job job) => inner.FindOriginals(job);
    public override bool Accepts(string file, out string? why) => inner.Accepts(file, out why);
    public override IEnumerable<Job> Jobs(IReadOnlyList<string> files) => inner.Jobs(files);
    public override int RunBackend(string[] arguments, TextWriter output, TextWriter error) => inner.RunBackend(arguments, output, error);
}
