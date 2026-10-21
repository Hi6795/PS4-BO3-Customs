using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FFPorter.Desktop;
using FFPorter.Desktop.Controls;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal static class RealChecks
{
    public static void Console(string address)
    {
        System.Console.WriteLine("== the console, read only");
        if (!ConsoleFtp.TryAddress(address, out string host, out int port))
        {
            Program.Check(false, "that is not a console address");
            return;
        }
        var entries = new List<ConsoleEntry>();
        var sizes = new List<ConsoleSize>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan listed = TimeSpan.Zero;
        var found = new Progress<IReadOnlyList<ConsoleEntry>>(list =>
        {
            entries.AddRange(list);
            listed = clock.Elapsed;
        });
        var measured = new Progress<ConsoleSize>(sizes.Add);
        Task scan = Task.Run(() => ConsoleFtp.InstalledAsync(host, port, found, measured, CancellationToken.None));
        CommunityChecks.Pump(() => scan.IsCompleted, 300);
        CommunityChecks.Pump(() => sizes.Count == entries.Count, 2);
        if (!scan.IsCompletedSuccessfully)
        {
            Program.Check(false, $"{host}:{port}: {scan.Exception?.InnerException?.Message ?? "no answer"}");
            return;
        }
        Program.Check(true, $"{host}:{port} answered: {entries.Count(entry => !entry.IsMod)} maps and {entries.Count(entry => entry.IsMod)} mods, "
            + $"names in {listed.TotalSeconds:0.0} s, sizes in {clock.Elapsed.TotalSeconds:0.0} s, {Job.SizeText(sizes.Sum(size => size.Bytes))} in all");
        foreach (IGrouping<string, ConsoleSize> drive in sizes.GroupBy(size => ConsoleFtp.StorageName(size.Entry.Storage)))
        {
            System.Console.WriteLine($"   {drive.Key}: {drive.Count()} items, {Job.SizeText(drive.Sum(size => size.Bytes))}");
            foreach (ConsoleSize size in drive.OrderBy(size => size.Entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                System.Console.WriteLine($"      {(size.Entry.IsMod ? "mod" : "map")} {size.Entry.Name,-28} {Job.SizeText(size.Bytes),10}  {size.Files.Count,3} files  {size.Entry.Folder}");
                System.Console.WriteLine($"          {string.Join(", ", size.Files.OrderByDescending(file => file.Size).Take(4).Select(file => $"{file.Path} {Job.SizeText(file.Size)}"))}");
            }
        }
    }

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static T Wait<T>(Func<Task<T>> work)
    {
        Task<T> task = Task.Run(work);
        CommunityChecks.Pump(() => task.IsCompleted, 90);
        return task.GetAwaiter().GetResult();
    }

    public static void Update(string published)
    {
        System.Console.WriteLine("== the app's own releases on GitHub");
        const string name = "PS4 .FF Porter";
        Uri releases = AppUpdate.Releases(Edition.Current.UpdateSource!);
        System.Console.WriteLine("   " + releases);
        UpdateFile? latest;
        try
        {
            latest = Wait(() => AppUpdate.LatestAsync(releases, name, CancellationToken.None));
        }
        catch (IOException error)
        {
            Program.Check(false, "GitHub answers: " + error.Message);
            return;
        }
        if (latest == null)
        {
            Program.Check(false, "a release with the app's exe is found");
            return;
        }
        System.Console.WriteLine($"   {latest.Release}: {latest.Name}, {latest.Size:N0} bytes, uploaded {latest.Uploaded:u}\n   sha256 {latest.Sha256}\n   {latest.Link}");
        Program.Check(latest.Name.EndsWith(".exe") && latest.Sha256.Length == 64 && latest.Size > 50_000_000 && latest.Uploaded != null && latest.Link.Host == "github.com",
            "the newest release with the app's exe is found, with its SHA-256, size and upload time");
        if (!File.Exists(published))
        {
            Program.Check(false, "the published app is there to compare");
            return;
        }
        DateTimeOffset? built = AppUpdate.BuiltOf(typeof(T7Edition).Assembly);
        string own = Wait(() => AppUpdate.HashAsync(published, CancellationToken.None));
        System.Console.WriteLine($"   published app: sha256 {own}, this build's stamp {built:u}");
        UpdateCheck now = Wait(() => AppUpdate.CheckAsync(releases, published, name, built, CancellationToken.None));
        UpdateCheck old = Wait(() => AppUpdate.CheckAsync(releases, published, name, latest.Uploaded!.Value.AddDays(-1), CancellationToken.None));
        UpdateCheck bare = Wait(() => AppUpdate.CheckAsync(releases, published, name, null, CancellationToken.None));
        Program.Check(own != latest.Sha256 && now.State == UpdateStates.Ahead, $"today's build differs from {latest.Release} and is newer than it, so it is left alone ({now.State})");
        Program.Check(old.State == UpdateStates.Available && bare.State == UpdateStates.Available, $"the same exe with an older build time, or none, would be replaced by {latest.Release}");

        using var request = new HttpRequestMessage(HttpMethod.Get, latest.Link);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PS4-FF-Porter");
        using HttpResponseMessage response = Wait(() => client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None));
        byte[] start = Wait(() => response.Content.ReadAsByteArrayAsync());
        System.Console.WriteLine($"   first two bytes: {(int)response.StatusCode} {response.Content.Headers.ContentRange} from {response.RequestMessage?.RequestUri?.Host}");
        Program.Check((int)response.StatusCode == 206 && response.Content.Headers.ContentRange?.Length == latest.Size && start.Length == 2 && start[0] == 'M' && start[1] == 'Z',
            "the release file answers through GitHub's redirect, is the size the list says, and a download can be continued part-way");
    }

    public static void Download(string scratch, string id)
    {
        System.Console.WriteLine($"== a real download of {id}, paused and resumed part-way");
        var settings = AppSettings.Current;
        void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(settings, value);
        object? outputBefore = typeof(AppSettings).GetField("_outputOverride", Private)!.GetValue(settings);
        Set("_outputOverride", Path.Combine(scratch, "out"));
        Set("_consoleAddress", null);
        Set("_sendToConsole", false);
        try
        {
            var window = new MainWindow();
            var content = (UIElement)window.Content;
            window.Content = null;
            var host = Program.Host(window, content, 1364, 841);
            var panel = (CommunityPanel)window.FindName("Community");
            typeof(CommunityPanel).GetField("_library", Private)!.SetValue(panel, CommunityLibrary.Load(Path.Combine(scratch, "library.json")));
            ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;
            var shown = (System.Collections.ObjectModel.ObservableCollection<CommunityMap>)typeof(CommunityPanel).GetField("_maps", Private)!.GetValue(panel)!;
            CommunityChecks.Pump(() => (bool)typeof(CommunityPanel).GetField("_loaded", Private)!.GetValue(panel)!, 60);
            CommunityMap? map = shown.FirstOrDefault(entry => entry.Id == id);
            if (map == null)
            {
                Program.Check(false, $"{id} is in the list");
                return;
            }
            void Click(string handler) => typeof(CommunityPanel).GetMethod(handler, Private)!.Invoke(panel, [new Button { DataContext = map }, new RoutedEventArgs()]);
            string folder = settings.CommunityFolder(map);
            System.Console.WriteLine($"   {map.Title}: {map.SizeText} in {map.Parts.Count} part{(map.Parts.Count == 1 ? "" : "s")}, into {folder}");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Click("StartClick");
            CommunityChecks.Pump(() => map.Progress > 0.2 || !panel.IsDownloading, 900);
            Click("PauseClick");
            CommunityChecks.Pump(() => map.StageText.StartsWith("Paused") || !panel.IsDownloading, 60);
            long held = CommunityDownloader.Fetched(map);
            string paused = map.StageText;
            Program.Render(host, Path.Combine(scratch, "real_paused.png"));
            CommunityChecks.Pump(() => false, 4);
            long later = CommunityDownloader.Fetched(map);
            System.Console.WriteLine($"   paused after {clock.Elapsed.TotalSeconds:0}s: '{paused}'; {held:N0} bytes kept, {later:N0} four seconds later; footer: {panel.Status}");
            Program.Check(map.IsPaused && paused.StartsWith("Paused · ") && held > 0 && held == later && panel.IsDownloading && panel.Status.Contains("paused"),
                "Pause holds the real download where it is: nothing more arrives while it is paused");
            Click("PauseClick");
            CommunityChecks.Pump(() => !panel.IsDownloading, 3600);
            clock.Stop();
            string[] files = Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)] : [];
            long bytes = files.Sum(file => new FileInfo(file).Length);
            System.Console.WriteLine($"   finished in {clock.Elapsed.TotalSeconds:0}s: {map.State}, '{map.Note}'; {files.Length} files, {Job.SizeText(bytes)} unpacked");
            foreach (string file in files.OrderByDescending(file => new FileInfo(file).Length).Take(6))
                System.Console.WriteLine($"      {Path.GetRelativePath(folder, file),-44} {new FileInfo(file).Length,14:N0}");
            Program.Check(map is { State: RunStates.Done, Local: LocalCopies.Current, Note: "Downloaded" } && File.Exists(Path.Combine(folder, id + ".ff")) && bytes > map.Size / 2
                && CommunityDownloader.Fetched(map) == 0 && !Directory.Exists(folder + ".incoming"),
                "Resume carries on and the map arrives whole: its checksum matched, it unpacked, and nothing temporary is left");
            byte[] head = Fastfiles.Head(Path.Combine(folder, id + ".ff"), 16);
            Program.Check(Fastfiles.Game(head) == Edition.Current.GameName && head.Length == 16 && head[14] != 0, "what arrived is a console zone of the right game");
            Program.Render(host, Path.Combine(scratch, "real_downloaded.png"));
        }
        finally
        {
            Set("_outputOverride", outputBefore);
        }
    }

    public static void Install(string scratch, string probes)
    {
        System.Console.WriteLine("== a real update: an older build replaces itself with the latest release from GitHub");
        string probe = Path.Combine(probes, "probe_one", "PS4 .FF Porter.exe");
        if (!File.Exists(probe))
        {
            Program.Check(false, $"the probe app is there ({probe})");
            return;
        }
        Uri releases = AppUpdate.Releases(Edition.Current.UpdateSource!);
        UpdateFile? latest = Wait(() => AppUpdate.LatestAsync(releases, "PS4 .FF Porter", CancellationToken.None));
        if (latest == null)
        {
            Program.Check(false, "a release with the app's exe is found");
            return;
        }
        string folder = Path.Combine(scratch, "real update");
        string exe = Path.Combine(folder, "PS4 .FF Porter.exe"), note = exe + ".log";
        Directory.CreateDirectory(folder);
        File.Copy(probe, exe, true);
        string before = Wait(() => AppUpdate.HashAsync(exe, CancellationToken.None));
        System.Console.WriteLine($"   stand-in for an older build: {new FileInfo(exe).Length:N0} bytes, sha256 {before[..16]}…\n   release {latest.Release}: {latest.Name}, {latest.Size:N0} bytes, sha256 {latest.Sha256[..16]}…");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { "update-exact", releases.ToString(), AppHost.BackendVerb, "t7", "--help" } };
        using (var first = System.Diagnostics.Process.Start(start)!)
            first.WaitForExit(1_800_000);
        string Log() => File.Exists(note) ? File.ReadAllText(note) : "";
        CommunityChecks.Pump(() => Log().Contains("helper done"), 120);
        clock.Stop();
        string log = Log();
        System.Console.WriteLine("     " + log.TrimEnd().Replace("\n", "\n     "));
        string after = File.Exists(exe) ? Wait(() => AppUpdate.HashAsync(exe, CancellationToken.None)) : "";
        Program.Check(log.Contains("check=Available") && log.Contains($"downloaded {latest.Size}") && log.Contains("handed over"),
            $"the older build finds {latest.Release} on GitHub and downloads it ({Job.SizeText(latest.Size)} in {clock.Elapsed.TotalSeconds:0}s, hand-over included)");
        Program.Check(log.Contains("helper done code=0") && after == latest.Sha256 && after != before && !File.Exists(exe + ".new"),
            "its helper copy puts the release in place once the app has closed: the exe is now the release, byte for byte");
    }

    public static void Run(string scratch)
    {
        System.Console.WriteLine("== the built-in list, as the app loads it");
        string address = Edition.Current.CommunityList!;
        byte[] program = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "PS4 .FF Porter.dll"));
        bool Holds(byte[] text) => program.AsSpan().IndexOf(text) >= 0;
        string server = new Uri(address).Host;
        string plain = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(address))[..24];
        Program.Check(address.StartsWith("https://") && address.EndsWith("/catalog.json") && !Holds(System.Text.Encoding.UTF8.GetBytes(server)) && !Holds(System.Text.Encoding.Unicode.GetBytes(server))
            && !Holds(System.Text.Encoding.Unicode.GetBytes(plain)) && !Holds(System.Text.Encoding.UTF8.GetBytes(plain)),
            "the address decodes at run time and is not readable in the program file, as text or as plain base64");
        Program.Check(CommunityCatalog.TryAddress(address, out Uri list), "the built-in address is usable");

        var log = new List<string>();
        Task<IReadOnlyList<CommunityMap>> loading = Task.Run(() => CommunityCatalog.LoadAsync(list, line => { lock (log) log.Add(line); }, CancellationToken.None));
        CommunityChecks.Pump(() => loading.IsCompleted, 60);
        if (loading.IsFaulted)
        {
            Program.Check(false, "the list loads: " + loading.Exception!.InnerException!.Message);
            return;
        }
        IReadOnlyList<CommunityMap> maps = loading.Result;
        foreach (string line in log)
            System.Console.WriteLine("   log: " + line);
        Program.Check(maps.Count > 0 && log.Count == 0, $"the list loads: {maps.Count} maps, none skipped");
        System.Console.WriteLine("   titles: " + string.Join(" | ", maps.Select(map => map.Title)));
        Program.Check(!maps.Any(map => map.Title.StartsWith("[MP]", StringComparison.OrdinalIgnoreCase)), "no title starts with [MP]");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PS4-FF-Porter");
        http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
        using var direct = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        direct.DefaultRequestHeaders.UserAgent.ParseAdd("PS4-FF-Porter");
        (int Code, string Range, int Bytes, bool Encoded) Piece(Uri part, long from, long? to)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, part);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);
            Task<HttpResponseMessage> asking = Task.Run(() => direct.SendAsync(request, HttpCompletionOption.ResponseHeadersRead));
            CommunityChecks.Pump(() => asking.IsCompleted, 40);
            if (!asking.IsCompletedSuccessfully)
                return (0, "", 0, false);
            using HttpResponseMessage response = asking.Result;
            int bytes = 0;
            if ((int)response.StatusCode == 206 && response.Content.Headers.ContentLength is <= 16)
            {
                Task<byte[]> reading = Task.Run(() => response.Content.ReadAsByteArrayAsync());
                CommunityChecks.Pump(() => reading.IsCompleted, 40);
                bytes = reading.IsCompletedSuccessfully ? reading.Result.Length : 0;
            }
            return ((int)response.StatusCode, response.Content.Headers.ContentRange?.ToString() ?? "", bytes, response.Content.Headers.ContentEncoding.Count > 0);
        }
        foreach (CommunityMap map in maps)
        {
            long total = 0, firstLength = 0;
            int reachable = 0;
            string kind = "";
            foreach (Uri part in map.Parts)
            {
                Task<HttpResponseMessage> asking = Task.Run(() => http.SendAsync(new HttpRequestMessage(HttpMethod.Head, part)));
                CommunityChecks.Pump(() => asking.IsCompleted, 40);
                if (asking.IsFaulted || !asking.IsCompleted)
                    continue;
                using HttpResponseMessage response = asking.Result;
                if (!response.IsSuccessStatusCode)
                    continue;
                reachable++;
                total += response.Content.Headers.ContentLength ?? 0;
                if (reachable == 1)
                    firstLength = response.Content.Headers.ContentLength ?? 0;
                if (kind.Length == 0)
                    kind = $"{response.Content.Headers.ContentType?.MediaType ?? "no type"}{(response.Content.Headers.ContentEncoding.Count > 0 ? ", encoded " + string.Join("+", response.Content.Headers.ContentEncoding) : "")}";
            }
            System.Console.WriteLine($"   {map.Id,-22} {map.Title,-30} {map.SizeText,9}  {reachable}/{map.Parts.Count} parts answer, {Job.SizeText(total)} in all, {kind}, checksum {(map.Sha256.Length == 64 ? "yes" : "no")}, picture {(map.Thumbnail != null ? "yes" : "no")}");
            Program.Check(reachable == map.Parts.Count && total == map.Size, $"{map.Id}: every part is on the server and they add up to the size in the list");
            if (firstLength > 16)
            {
                var middle = Piece(map.Parts[0], firstLength / 2, firstLength / 2 + 1);
                var past = Piece(map.Parts[0], firstLength, null);
                System.Console.WriteLine($"      part-way: {middle.Code} {middle.Range} ({middle.Bytes} bytes read); past the end: {past.Code} {past.Range}");
                Program.Check(middle.Code == 206 && middle.Range == $"bytes {firstLength / 2}-{firstLength / 2 + 1}/{firstLength}" && middle.Bytes == 2 && !middle.Encoded && past.Code == 416,
                    $"{map.Id}: a paused download can carry on part-way through a part");
            }
        }

        var settings = AppSettings.Current;
        void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(settings, value);
        Set("_outputOverride", Path.Combine(scratch, "out"));
        Set("_deleteOriginals", false);
        var window = new MainWindow();
        var content = (UIElement)window.Content;
        window.Content = null;
        var host = Program.Host(window, content, 1364, 841);
        var panel = (CommunityPanel)window.FindName("Community");
        typeof(CommunityPanel).GetField("_library", Private)!.SetValue(panel, CommunityLibrary.Load(Path.Combine(scratch, "library.json")));
        ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;
        var shown = (System.Collections.ObjectModel.ObservableCollection<CommunityMap>)typeof(CommunityPanel).GetField("_maps", Private)!.GetValue(panel)!;
        CommunityChecks.Pump(() => (bool)typeof(CommunityPanel).GetField("_loaded", Private)!.GetValue(panel)! && shown.Where(map => map.Thumbnail != null).All(map => map.HasPicture), 60);
        CommunityMap[] listed = [.. shown];
        System.Console.WriteLine($"   tab: {panel.Status}; pictures loaded {shown.Count(map => map.HasPicture)}/{shown.Count(map => map.Thumbnail != null)}");
        Program.Check(listed.Length == maps.Count && shown.All(map => map.Thumbnail == null || map.HasPicture), "the Community tab shows the same maps with their pictures");
        Program.Render(host, Path.Combine(scratch, "community_real.png"));
        string[] texts =
        [
            panel.Status, ((System.Windows.Documents.Run)window.FindName("StatusText")).Text, ((TextBlock)window.FindName("SubtitleText")).Text,
            .. shown.SelectMany(map => new[] { map.Title, map.Meta, map.Tip, map.Note }),
        ];
        Program.Check(panel.Status == $"{listed.Length} maps to download" && !texts.Any(text => text.Contains("r2.dev") || text.Contains("pub-") || text.Contains("catalog")),
            $"the tab says \"{panel.Status}\" and names the server nowhere");
        Task<CommunityMap?> finding = panel.FindConsoleFilesAsync();
        CommunityChecks.Pump(() => finding.IsCompleted, 30);
        CommunityMap? files = finding.IsCompletedSuccessfully ? finding.Result : null;
        System.Console.WriteLine($"   console files in Settings: {(files == null ? "none" : $"{files.Title} {files.Version}, {files.SizeText}, plus {string.Join(", ", files.Extras.Select(extra => $"{extra.Name} ({extra.Size:N0} bytes)"))}")}");
        Program.Check(files is { Extras.Count: 1 } && files.Sha256.Length == 64 && files.Extras[0].Sha256.Length == 64 && files.Size > 100_000_000 && !shown.Any(map => map.IsConsole),
            "the newest release's console files are listed for Settings, with GitHub's checksums, and are not a row of the list");

        string Count(string name) => ((System.Windows.Documents.Run)panel.FindName(name)).Text.Trim();
        string[] Shown() => [.. ((ItemsControl)panel.FindName("MapList")).Items.Cast<CommunityMap>().Select(map => map.Id)];
        System.Console.WriteLine($"   filters: all {Count("AllCount")}, zombies {Count("ZombiesCount")}, multiplayer {Count("MultiplayerCount")}, mods {Count("ModsCount")}");
        ((RadioButton)panel.FindName("ZombiesFilter")).IsChecked = true;
        string[] zombies = Shown();
        ((RadioButton)panel.FindName("MultiplayerFilter")).IsChecked = true;
        string[] multiplayer = Shown();
        Program.Render(host, Path.Combine(scratch, "community_real_multiplayer.png"));
        ((RadioButton)panel.FindName("ModsFilter")).IsChecked = true;
        string mods = ((TextBlock)panel.FindName("EmptyTitle")).Text;
        Program.Render(host, Path.Combine(scratch, "community_real_mods.png"));
        System.Console.WriteLine($"   zombies: {string.Join(", ", zombies)} | multiplayer: {string.Join(", ", multiplayer)} | mods: {(Shown().Length == 0 ? mods : string.Join(", ", Shown()))}");
        Program.Check(zombies.Length + multiplayer.Length + Shown().Length == listed.Length && zombies.All(id => id.StartsWith("zm_")) && multiplayer.All(id => id.StartsWith("mp_"))
            && (Shown().Length > 0 || mods == "No mods in the list yet"), "every entry of the real list falls under Zombies, Multiplayer or Mods");
        ((RadioButton)panel.FindName("ZombiesFilter")).IsChecked = true;
        Program.Render(host, Path.Combine(scratch, "community_real_zombies.png"));
    }
}
