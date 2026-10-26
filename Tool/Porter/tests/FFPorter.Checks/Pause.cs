using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FFPorter.Desktop;
using FFPorter.Desktop.Controls;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal static class PauseChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class Reports : IProgress<DownloadProgress>
    {
        public ConcurrentQueue<DownloadProgress> Seen { get; } = new();
        public Action<DownloadProgress>? Each;

        public void Report(DownloadProgress value)
        {
            Seen.Enqueue(value);
            Each?.Invoke(value);
        }

        public bool Paused => Seen.Any(report => report.Stage == DownloadProgress.Paused);
    }

    private static bool WaitFor(Func<bool> done, double seconds)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until)
            Thread.Sleep(25);
        return done();
    }

    private static Exception? End(Task task, double seconds = 90)
    {
        try
        {
            return task.Wait(TimeSpan.FromSeconds(seconds)) ? null : new TimeoutException("it did not finish");
        }
        catch (AggregateException error)
        {
            return error.InnerException;
        }
    }

    private static bool Same(string folder, Dictionary<string, byte[]> files, string prefix) =>
        files.All(pair => File.Exists(Path.Combine(folder, pair.Key[prefix.Length..].Replace('/', '\\')))
            && File.ReadAllBytes(Path.Combine(folder, pair.Key[prefix.Length..].Replace('/', '\\'))).AsSpan().SequenceEqual(pair.Value));

    private static void Drop(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    public static void Run(string scratch)
    {
        Console.WriteLine("== pausing: the gate");
        var gate = new PauseGate();
        CancellationToken first = gate.Asked;
        Program.Check(!gate.IsPaused && gate.WaitAsync(default).IsCompletedSuccessfully && !first.IsCancellationRequested, "nothing waits while it is not paused");
        gate.Pause();
        Task waiting = gate.WaitAsync(default);
        Program.Check(gate.IsPaused && !waiting.IsCompleted && first.IsCancellationRequested, "pausing holds whoever waits and tells work in flight to stop where it is");
        gate.Resume();
        Program.Check(End(waiting, 5) == null && !gate.IsPaused && !gate.Asked.IsCancellationRequested, "carrying on lets it through, ready to be paused again");
        gate.Pause();
        using (var cancel = new CancellationTokenSource())
        {
            Task stopped = gate.WaitAsync(cancel.Token);
            cancel.Cancel();
            Program.Check(End(stopped, 5) is OperationCanceledException, "a cancel gets through a pause");
        }
        gate.Resume();

        Console.WriteLine("== pausing: downloads");
        string root = Path.Combine(scratch, "pause");
        string site = Path.Combine(root, "site");
        string maps = Path.Combine(site, "t7", "maps");
        Dictionary<string, byte[]> files = CommunityChecks.MapFiles("zm_pause", "zm_pause/", 2);
        byte[] zip = CommunityChecks.Zip(files);
        List<string> parts = CommunityChecks.Split(zip, Path.Combine(maps, "zm_pause"), "zm_pause", 7_000_000);
        Dictionary<string, byte[]> nextFiles = CommunityChecks.MapFiles("zm_next", "zm_next/");
        byte[] nextZip = CommunityChecks.Zip(nextFiles);
        Directory.CreateDirectory(Path.Combine(maps, "zm_next"));
        File.WriteAllBytes(Path.Combine(maps, "zm_next", "zm_next.zip"), nextZip);
        using var server = new MiniServer(site);
        string address = $"http://127.0.0.1:{server.Port}/t7/catalog.json";
        object[] entries =
        [
            new { name = "zm_pause", title = "Pause Me", size = zip.Length, folder = "zm_pause", parts = parts.Select(part => "maps/zm_pause/" + part), sha256 = CommunityChecks.Sha(zip) },
            new { name = "zm_next", title = "Next In Line", size = nextZip.Length, file = "maps/zm_next/zm_next.zip", sha256 = CommunityChecks.Sha(nextZip) },
        ];
        File.WriteAllText(Path.Combine(site, "t7", "catalog.json"), JsonSerializer.Serialize(new { name = "Pause maps", maps = entries }), new UTF8Encoding(true));
        string one = "/t7/maps/zm_pause/" + parts[0], two = "/t7/maps/zm_pause/" + parts[1];
        var map = new CommunityMap
        {
            Id = "zm_pause", Title = "Pause Me", Folder = "zm_pause", Size = zip.Length, Sha256 = CommunityChecks.Sha(zip),
            Parts = [.. parts.Select(part => new Uri($"http://127.0.0.1:{server.Port}/t7/maps/zm_pause/{part}"))],
        };
        string output = Path.Combine(root, "direct", "zm_pause");
        string held = Path.Combine(CommunityDownloader.ScratchRoot, "zm_pause");
        string partial = Path.Combine(held, "001.part.partial");
        var log = new ConcurrentQueue<string>();
        Console.WriteLine($"   {parts.Count} parts, {zip.Length:N0} bytes");

        (Task Work, Reports Seen, PauseGate Gate, CancellationTokenSource Stop) Start(Func<DownloadProgress, bool> when)
        {
            var stop = new CancellationTokenSource();
            var pause = new PauseGate();
            var reports = new Reports();
            int asked = 0;
            reports.Each = report =>
            {
                if (when(report) && Interlocked.Exchange(ref asked, 1) == 0)
                    pause.Pause();
            };
            return (Task.Run(() => CommunityDownloader.DownloadAsync(map, output, reports, log.Enqueue, pause, stop.Token)), reports, pause, stop);
        }

        server.Slow = one;
        var run = Start(report => report.Stage == "Downloading" && report.Fraction > 0.08);
        bool paused = WaitFor(() => run.Seen.Paused, 30);
        long kept = File.Exists(partial) ? new FileInfo(partial).Length : -1;
        int requests = server.Asked.Count;
        Thread.Sleep(1300);
        DownloadProgress? shown = run.Seen.Seen.LastOrDefault();
        Console.WriteLine($"   paused at {kept:N0} bytes; shown: {shown?.Stage} · {shown?.Detail}");
        Program.Check(paused && kept > 0 && kept < new FileInfo(Path.Combine(maps, "zm_pause", parts[0])).Length && !run.Work.IsCompleted && new FileInfo(partial).Length == kept
            && server.Asked.Count == requests && shown is { Stage: DownloadProgress.Paused } && shown.Detail.Contains(" of "),
            "Pause stops a download where it is: nothing more is fetched or asked for, and what was fetched is kept");
        server.Slow = null;
        run.Gate.Resume();
        Exception? error = End(run.Work);
        string[] asked = [.. server.Asked];
        Console.WriteLine($"   requests: {string.Join(" || ", asked)}");
        Program.Check(error == null && Same(output, files, "zm_pause/") && asked.Length == 3 && asked[1] == $"{one} bytes={kept}-" && asked[2] == two && !Directory.Exists(held),
            "Resume carries on from the same byte instead of starting the part again, and the map comes out byte for byte");
        Program.Check(run.Seen.Seen.Last().Fraction <= 1 && run.Seen.Seen.SkipWhile(report => report.Stage != DownloadProgress.Paused).Any(report => report.Stage == "Downloading"),
            "progress picks up again after the pause");

        Drop(output);
        server.Asked.Clear();
        server.NoRange = true;
        server.Slow = one;
        run = Start(report => report.Stage == "Downloading" && report.Fraction > 0.08);
        paused = WaitFor(() => run.Seen.Paused, 30);
        server.Slow = null;
        run.Gate.Resume();
        error = End(run.Work);
        server.NoRange = false;
        Program.Check(paused && error == null && Same(output, files, "zm_pause/") && server.Asked.Count == 3,
            "a server that cannot carry on part-way is asked for that part again from the start, and the result is still right");

        foreach (string stage in (string[])["Checking", "Unpacking"])
        {
            Drop(output);
            run = Start(report => report.Stage == stage);
            paused = WaitFor(() => run.Seen.Paused, 30);
            Thread.Sleep(600);
            bool waits = !run.Work.IsCompleted && !Directory.Exists(output);
            run.Gate.Resume();
            error = End(run.Work);
            Program.Check(paused && waits && error == null && Same(output, files, "zm_pause/"), $"Pause also holds while {stage.ToLowerInvariant()}, and Resume finishes the job");
        }

        Drop(output);
        server.Slow = one;
        run = Start(report => report.Stage == "Downloading" && report.Fraction > 0.08);
        paused = WaitFor(() => run.Seen.Paused, 30);
        run.Stop.Cancel();
        error = End(run.Work);
        server.Slow = null;
        long left = CommunityDownloader.Fetched(map);
        CommunityDownloader.Discard(map);
        Program.Check(paused && error is OperationCanceledException && left > 0 && !Directory.Exists(held) && !Directory.Exists(output),
            "a cancel gets through a paused download; what was fetched is only dropped when it is discarded");

        server.Asked.Clear();
        server.Cut = one;
        server.CutAfter = 700_000;
        run = Start(_ => false);
        error = End(run.Work);
        asked = [.. server.Asked];
        Console.WriteLine($"   after a dropped connection: {string.Join(" || ", asked)}");
        Program.Check(error == null && Same(output, files, "zm_pause/") && asked.Length == 3 && asked[1] == $"{one} bytes=700000-" && log.Any(line => line.Contains("trying again")),
            "a connection that drops part-way now carries on from where it stopped too, instead of fetching the part again");

        Drop(output);
        server.Asked.Clear();
        string source = $"{map.Sha256}|{map.Size}|{map.Parts.Count}";
        Directory.CreateDirectory(held);
        File.WriteAllText(Path.Combine(held, "source.txt"), source);
        File.WriteAllBytes(partial, new byte[new FileInfo(Path.Combine(maps, "zm_pause", parts[0])).Length + 100]);
        run = Start(_ => false);
        error = End(run.Work);
        Program.Check(error == null && Same(output, files, "zm_pause/") && server.Asked.Count(line => line.StartsWith(one)) == 2,
            "a leftover piece that cannot belong to the part is thrown away and the part fetched afresh");

        Drop(output);
        server.Asked.Clear();
        Directory.CreateDirectory(held);
        File.WriteAllText(Path.Combine(held, "source.txt"), source);
        File.Copy(Path.Combine(maps, "zm_pause", parts[0]), partial);
        run = Start(_ => false);
        error = End(run.Work);
        asked = [.. server.Asked];
        Program.Check(error == null && Same(output, files, "zm_pause/") && asked.Length == 2 && asked[0].StartsWith(one + " bytes=") && asked[1] == two,
            "a piece that turns out to be the whole part is kept as it is");

        Drop(output);
        server.Asked.Clear();
        Directory.CreateDirectory(held);
        File.WriteAllText(Path.Combine(held, "source.txt"), "another version of the map");
        File.WriteAllBytes(partial, new byte[500_000]);
        File.WriteAllBytes(Path.Combine(held, "002.part"), new byte[900_000]);
        run = Start(_ => false);
        error = End(run.Work);
        asked = [.. server.Asked];
        Program.Check(error == null && Same(output, files, "zm_pause/") && asked.SequenceEqual([one, two]),
            "pieces left from another version of the map are thrown away instead of being mixed in");

        Sends(root);
        Windows(scratch, root, server, address, one, files);
        Restarts(scratch, root, server, address, one, two, maps, parts, files);
    }

    private static void Sends(string root)
    {
        Console.WriteLine("== pausing: sending to the console");
        string local = Path.Combine(root, "local", "zm_p");
        var dice = new Random(31);
        Dictionary<string, byte[]> Write()
        {
            var written = new Dictionary<string, byte[]>
            {
                ["zm_p.ff"] = new byte[900_000], ["en_zm_p.ff"] = new byte[60_000], ["zm_p.xpak"] = new byte[2_600_000], ["snd/all/zm_p.all.sabl"] = new byte[300_000], ["workshop.json"] = new byte[40],
            };
            foreach ((string name, byte[] data) in written)
            {
                dice.NextBytes(data);
                string path = Path.Combine(local, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
            }
            return written;
        }
        bool On(MiniFtp where, string folder, Dictionary<string, byte[]> expected) =>
            expected.All(pair => File.Exists(where.Local($"{folder}/{pair.Key}")) && File.ReadAllBytes(where.Local($"{folder}/{pair.Key}")).AsSpan().SequenceEqual(pair.Value));
        int Count(MiniFtp where, string verb) => where.Commands.Count(line => line.StartsWith(verb + " ") || line == verb);

        const string remote = "/data/BO3-Customs/usermaps/zm_p";
        string[] main = ["zm_p.ff"];
        var staged = new HashSet<string>();
        using var console = new MiniFtp(Path.Combine(root, "console"));

        (Task Work, Reports Seen, PauseGate Gate, CancellationTokenSource Stop) Send(MiniFtp to, string? during)
        {
            var stop = new CancellationTokenSource();
            var pause = new PauseGate();
            var reports = new Reports();
            Task work = Task.Run(() => ConsoleFtp.SendAsync("127.0.0.1", to.Port, local, remote, main, staged, reports, _ => { }, pause, stop.Token));
            if (during != null && WaitFor(() => to.Commands.Contains($"STOR {remote}/{during}.part"), 30))
                pause.Pause();
            return (work, reports, pause, stop);
        }

        Dictionary<string, byte[]> files = Write();
        console.Slow = $"{remote}/zm_p.xpak.part";
        var run = Send(console, "zm_p.xpak");
        bool paused = WaitFor(() => run.Seen.Paused, 30);
        Thread.Sleep(300);
        string[] before = console.Commands;
        string xpak = console.Local($"{remote}/zm_p.xpak.part");
        bool whole = File.Exists(xpak) && new FileInfo(xpak).Length == files["zm_p.xpak"].Length;
        Thread.Sleep(1000);
        DownloadProgress? shown = run.Seen.Seen.LastOrDefault();
        Console.WriteLine($"   paused after: {string.Join(" | ", before.Where(line => line.StartsWith("STOR ")).Select(line => Path.GetFileName(line)))}; last command {before.LastOrDefault()}; shown: {shown?.Stage} · {shown?.Detail}");
        Program.Check(paused && whole && before.Count(line => line.StartsWith("STOR ")) == 4 && before[^1] == "QUIT" && !before.Any(line => line.StartsWith("RNTO ")),
            "Pause during a send lets the file in flight arrive whole, then signs off from the console instead of cutting it");
        Program.Check(console.Commands.Length == before.Length && !run.Work.IsCompleted && shown is { Stage: DownloadProgress.Paused } && shown.Detail.EndsWith(" sent"),
            "while paused nothing is sent and no connection to the console is held open");
        console.Slow = null;
        run.Gate.Resume();
        Exception? error = End(run.Work);
        Console.WriteLine($"   after Resume: {error?.Message ?? "ok"}; signed in {Count(console, "USER")} times, {Count(console, "STOR")} files stored, {Count(console, "RNTO")} put in place");
        Program.Check(error == null && On(console, remote, files) && Count(console, "USER") == 2 && Count(console, "STOR") == 5 && Count(console, "RNTO") == 5
            && !Directory.EnumerateFiles(console.Local(remote), "*.part", SearchOption.AllDirectories).Any(),
            "Resume signs in again, sends only what is left and puts the map in place; no file goes twice");

        Dictionary<string, byte[]> second = Write();
        console.Forget();
        console.Slow = $"{remote}/zm_p.xpak.part";
        run = Send(console, "zm_p.xpak");
        paused = WaitFor(() => run.Seen.Paused, 30);
        run.Stop.Cancel();
        error = End(run.Work);
        console.Slow = null;
        int stored = Count(console, "STOR");
        Program.Check(paused && error is OperationCanceledException && stored == 4 && On(console, remote, files) && !console.Commands.Any(line => line.StartsWith("RNTO ")),
            "Cancel on a paused send stops it there, and the map already on the console is left as it was");
        run = Send(console, null);
        error = End(run.Work);
        Program.Check(error == null && On(console, remote, second) && Count(console, "STOR") == stored + 1, "sending it again later still picks up from what had arrived");

        Dictionary<string, byte[]> third = Write();
        console.Forget();
        console.Slow = $"{remote}/zm_p.ff.part";
        run = Send(console, "zm_p.ff");
        error = End(run.Work);
        console.Slow = null;
        Program.Check(error == null && !run.Seen.Paused && run.Gate.IsPaused && On(console, remote, third) && Count(console, "USER") == 1,
            "a pause asked for during the last file has nothing left to hold back: the send just finishes");

        Write();
        var other = new MiniFtp(Path.Combine(root, "console gone"));
        other.Slow = $"{remote}/zm_p.xpak.part";
        run = Send(other, "zm_p.xpak");
        paused = WaitFor(() => run.Seen.Paused, 30);
        other.Dispose();
        run.Gate.Resume();
        error = End(run.Work);
        Console.WriteLine($"   console gone on Resume: {error?.Message}");
        Program.Check(paused && error is IOException && error.Message.Contains("did not answer"), "if the console is off when the send is resumed, it says so plainly");
    }

    private static void Restarts(string scratch, string root, MiniServer server, string address, string one, string two, string maps, List<string> parts, Dictionary<string, byte[]> files)
    {
        Console.WriteLine("== pausing: closing the app and coming back");
        var settings = AppSettings.Current;
        void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(settings, value);
        Edition real = Edition.Current;
        Edition.Use(new ListEdition(real, address));
        object? outputBefore = typeof(AppSettings).GetField("_outputOverride", Private)!.GetValue(settings);
        Set("_outputOverride", Path.Combine(root, "out"));
        Set("_consoleAddress", null);
        Set("_sendToConsole", false);
        string folder = Path.Combine(root, "out", "usermaps", "zm_pause");
        string held = Path.Combine(CommunityDownloader.ScratchRoot, "zm_pause");
        try
        {
            (MainWindow Window, Border Host, CommunityPanel Panel, System.Collections.ObjectModel.ObservableCollection<CommunityMap> Maps) Open()
            {
                var window = new MainWindow();
                var content = (UIElement)window.Content;
                window.Content = null;
                Border host = Program.Host(window, content, 1364, 841);
                var panel = (CommunityPanel)window.FindName("Community");
                typeof(CommunityPanel).GetField("_library", Private)!.SetValue(panel, CommunityLibrary.Load(Path.Combine(root, "library.json")));
                ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;
                CommunityChecks.Pump(() => CommunityChecks.Field<bool>(panel, "_loaded"), 15);
                return (window, host, panel, CommunityChecks.Field<System.Collections.ObjectModel.ObservableCollection<CommunityMap>>(panel, "_maps"));
            }
            void Click(CommunityPanel panel, string handler, CommunityMap map) =>
                typeof(CommunityPanel).GetMethod(handler, Private)!.Invoke(panel, [new Button { DataContext = map }, new RoutedEventArgs()]);
            string Noted(MainWindow window)
            {
                var view = (LogView)window.FindName("Log");
                return ((TextBox)view.FindName("Text")).Text + typeof(LogView).GetField("_pending", Private)!.GetValue(view);
            }

            Drop(folder);
            Drop(held);
            var first = Open();
            CommunityMap map = first.Maps.First(entry => entry.Id == "zm_pause");
            bool closed = false;
            first.Window.Closed += (_, _) => closed = true;
            server.Slow = one;
            Click(first.Panel, "StartClick", map);
            CommunityChecks.Pump(() => map.Progress > 0.05, 30);
            first.Window.Close();
            bool waited = !closed;
            CommunityChecks.Pump(() => closed, 10);
            server.Slow = null;
            long kept = CommunityDownloader.Fetched(map);
            Console.WriteLine($"   closed with {kept:N0} bytes fetched");
            Program.Check(waited && closed && kept > 0 && File.Exists(Path.Combine(held, "001.part.partial")) && !Directory.Exists(folder) && !Directory.Exists(folder + ".incoming"),
                "closing the app during a download stops it cleanly first and keeps what was fetched");

            server.Asked.Clear();
            var second = Open();
            map = second.Maps.First(entry => entry.Id == "zm_pause");
            Console.WriteLine($"   after starting again: row '{map.Note}', button '{map.ActionText}'");
            Program.Check(map.Fetched == kept && map.Note == $"Partly downloaded · {Job.SizeText(kept)} of {map.SizeText}" && map.ActionText == "Resume" && map.CanStart && map.CanDelete && !map.CanOpen,
                "started again, the row shows how much is already there and offers Resume");
            Program.Render(second.Host, Path.Combine(scratch, "community_resume.png"));
            Click(second.Panel, "StartClick", map);
            CommunityChecks.Pump(() => !second.Panel.IsDownloading, 60);
            string[] asked = [.. server.Asked.Where(line => line.Contains("/maps/"))];
            Console.WriteLine($"   requests: {string.Join(" || ", asked)}");
            Program.Check(map.State == RunStates.Done && map.Fetched == 0 && asked.Length == 2 && asked[0] == $"{one} bytes={kept}-" && asked[1] == two
                && files.All(pair => File.ReadAllBytes(Path.Combine(root, "out", "usermaps", pair.Key.Replace('/', '\\'))).AsSpan().SequenceEqual(pair.Value)) && !Directory.Exists(held),
                "Resume carries on from the same byte and finishes the map");

            Drop(folder);
            Directory.CreateDirectory(held);
            File.WriteAllText(Path.Combine(held, "source.txt"), $"{map.Sha256}|{map.Size}|{map.Parts.Count}");
            for (int index = 0; index < parts.Count; index++)
                File.Copy(Path.Combine(maps, "zm_pause", parts[index]), Path.Combine(held, $"{index + 1:000}.part"));
            second.Panel.OutputChanged();
            server.Asked.Clear();
            bool whole = map.Note == "Downloaded, not unpacked yet" && map.ActionText == "Resume";
            Click(second.Panel, "StartClick", map);
            CommunityChecks.Pump(() => !second.Panel.IsDownloading, 60);
            Program.Check(whole && map.State == RunStates.Done && !server.Asked.Any(line => line.Contains("/maps/")) && File.Exists(Path.Combine(folder, "zm_pause.ff")),
                "a download that was closed while unpacking only needs unpacking again, with nothing fetched twice");

            Drop(folder);
            second.Panel.OutputChanged();
            server.Missing[two] = true;
            Click(second.Panel, "StartClick", map);
            CommunityChecks.Pump(() => !second.Panel.IsDownloading, 60);
            server.Missing.Clear();
            bool failed = map.State == RunStates.Failed && map.Fetched > 0 && map.CanDelete && map.ActionText == "Retry";
            long before = map.Fetched;
            Click(second.Panel, "DeleteClick", map);
            Console.WriteLine($"   discarded {before:N0} bytes; row '{map.Note}', button '{map.ActionText}'");
            Program.Check(failed && map.Fetched == 0 && !map.CanDelete && !Directory.Exists(held) && map.State == RunStates.Ready && map.ActionText == "Download" && map.Note == ""
                && Noted(second.Window).Contains("Pause Me: the part that was downloaded was deleted"),
                "the trash icon on a partly downloaded row throws the part away without asking, since nothing finished is lost");
        }
        finally
        {
            Edition.Use(real);
            Set("_outputOverride", outputBefore);
            Set("_consoleAddress", null);
            Set("_sendToConsole", false);
        }
    }

    private static void Windows(string scratch, string root, MiniServer server, string address, string one, Dictionary<string, byte[]> files)
    {
        Console.WriteLine("== pausing: the Community tab");
        var settings = AppSettings.Current;
        void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(settings, value);
        Edition real = Edition.Current;
        Edition.Use(new ListEdition(real, address));
        object? outputBefore = typeof(AppSettings).GetField("_outputOverride", Private)!.GetValue(settings);
        Set("_outputOverride", Path.Combine(root, "out"));
        Set("_consoleAddress", null);
        Set("_sendToConsole", false);
        try
        {
            var window = new MainWindow();
            var content = (UIElement)window.Content;
            window.Content = null;
            Border host = Program.Host(window, content, 1364, 841);
            var panel = (CommunityPanel)window.FindName("Community");
            typeof(CommunityPanel).GetField("_library", Private)!.SetValue(panel, CommunityLibrary.Load(Path.Combine(root, "library.json")));
            ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;
            var maps = CommunityChecks.Field<System.Collections.ObjectModel.ObservableCollection<CommunityMap>>(panel, "_maps");
            CommunityChecks.Pump(() => CommunityChecks.Field<bool>(panel, "_loaded"), 15);
            CommunityMap Map(string id) => maps.First(map => map.Id == id);
            void Click(string handler, string id) => typeof(CommunityPanel).GetMethod(handler, Private)!.Invoke(panel, [new Button { DataContext = Map(id) }, new RoutedEventArgs()]);
            string Status() => panel.Status;
            string folder = Path.Combine(root, "out", "usermaps", "zm_pause");
            string held = Path.Combine(CommunityDownloader.ScratchRoot, "zm_pause");

            Program.Check(maps.Count == 2 && !Map("zm_pause").CanPause && Map("zm_pause").PauseText == "Pause", "a row that is not working has no Pause button");
            server.Slow = one;
            Click("StartClick", "zm_pause");
            Click("StartClick", "zm_next");
            CommunityMap map = Map("zm_pause");
            CommunityChecks.Pump(() => map.Progress > 0.05, 30);
            Program.Check(map.CanPause && !map.IsPaused && !Map("zm_next").CanPause, "the row that is downloading gets a Pause button; a waiting one does not");
            Click("PauseClick", "zm_pause");
            bool flipped = map.IsPaused && map.PauseText == "Resume" && map.CanPause;
            CommunityChecks.Pump(() => Map("zm_next").State == RunStates.Done && !panel.IsDownloading, 30);
            double at = map.Progress;
            long kept = CommunityDownloader.Fetched(map);
            CommunityChecks.Pump(() => false, 1.2);
            Console.WriteLine($"   row: '{map.StageText}' button '{map.PauseText}', {kept:N0} bytes kept; footer: '{Status()}'");
            Program.Check(flipped && map.IsPaused && map.CanPause && map.CanCancel && map.StageText.StartsWith("Paused · ") && map.Progress == at && map.IsRunning && kept > 0
                && Directory.Exists(held) && Status() == "Pause Me · paused" && Map("zm_next").State == RunStates.Done && !Map("zm_next").IsPaused
                && File.Exists(Path.Combine(root, "out", "usermaps", "zm_next", "zm_next.ff")),
                "Pause turns the button into Resume and the row says Paused and stays where it is; the download that was waiting does not wait for it");
            Program.Render(host, Path.Combine(scratch, "community_paused.png"));

            server.Slow = null;
            Click("PauseClick", "zm_pause");
            Program.Check(!map.IsPaused && map.PauseText == "Pause", "Resume turns it back into Pause");
            CommunityChecks.Pump(() => !panel.IsDownloading, 60);
            Program.Check(map.State == RunStates.Done && Map("zm_next").State == RunStates.Done && !map.CanPause && !map.IsPaused && File.Exists(Path.Combine(folder, "zm_pause.ff"))
                && files.All(pair => File.ReadAllBytes(Path.Combine(root, "out", "usermaps", pair.Key.Replace('/', '\\'))).AsSpan().SequenceEqual(pair.Value)),
                "after Resume the paused download carries on and finishes");

            Directory.Delete(folder, true);
            Directory.Delete(Path.Combine(root, "out", "usermaps", "zm_next"), true);
            panel.OutputChanged();
            server.Slow = one;
            Click("StartClick", "zm_pause");
            CommunityChecks.Pump(() => map.Progress > 0.05, 30);
            Click("PauseClick", "zm_pause");
            CommunityChecks.Pump(() => map.StageText.StartsWith("Paused"), 10);
            bool holding = map.IsPaused && panel.IsDownloading && Status().Contains("Pause Me · paused");
            Click("StartClick", "zm_next");
            CommunityChecks.Pump(() => Map("zm_next").State == RunStates.Done && !panel.IsDownloading, 30);
            Console.WriteLine($"   started while another is paused: {Map("zm_next").State}; paused row: '{map.StageText}'");
            Program.Check(holding && Map("zm_next").State == RunStates.Done && map.IsPaused && map.IsRunning && map.StageText.StartsWith("Paused · ") && Directory.Exists(held),
                "a download started while another one is paused starts right away");
            Click("CancelClick", "zm_pause");
            server.Slow = null;
            CommunityChecks.Pump(() => !panel.IsDownloading, 60);
            Program.Check(map.State == RunStates.Cancelled && map.Note == "Cancelled" && !map.CanPause && !map.IsPaused && !Directory.Exists(held) && !Directory.Exists(folder)
                && Map("zm_next").State == RunStates.Done && !Map("zm_next").IsPaused, "Cancel on a paused row cancels it and clears what it had fetched");

            var waiting = new CommunityMap { Id = "zm_wait", Title = "Wait", Folder = "zm_wait", Parts = [new Uri("https://x/y")] };
            waiting.Local = LocalCopies.Current;
            string before = waiting.NoteState;
            waiting.State = RunStates.Queued;
            Program.Check(before == RunStates.Done && waiting.Note == "Waiting" && waiting.NoteState == "", "a waiting row's note is plain grey, also when the map is already downloaded");

            using var console = new MiniFtp(Path.Combine(root, "tab console"));
            Click("StartClick", "zm_pause");
            CommunityChecks.Pump(() => !panel.IsDownloading, 60);
            Set("_consoleAddress", $"127.0.0.1:{console.Port}");
            panel.SettingsChanged();
            CommunityChecks.Pump(() => !CommunityChecks.Field<bool>(panel, "_looking") && console.Commands.Contains("QUIT"), 20);
            console.Forget();
            const string remote = "/data/BO3-Customs/usermaps/zm_pause";
            console.Slow = $"{remote}/zm_pause.xpak.part";
            Click("StartClick", "zm_pause");
            CommunityChecks.Pump(() => console.Commands.Contains($"STOR {remote}/zm_pause.xpak.part"), 30);
            Click("PauseClick", "zm_pause");
            string pausing = map.StageText;
            Program.Render(host, Path.Combine(scratch, "community_pausing_send.png"));
            CommunityChecks.Pump(() => map.StageText.StartsWith("Paused"), 30);
            string xpak = console.Local($"{remote}/zm_pause.xpak.part");
            Console.WriteLine($"   send: '{pausing}' then '{map.StageText}'; footer: '{Status()}'");
            Program.Check(pausing.EndsWith(" · pauses when this file is done") && map.StageText.StartsWith("Paused · ") && map.StageText.EndsWith(" sent") && map.IsPaused
                && File.Exists(xpak) && new FileInfo(xpak).Length == files["zm_pause/zm_pause.xpak"].Length && !console.Commands.Any(line => line == $"STOR {remote}/zm_pause.ff.part")
                && Status().Contains("to the console · paused"),
                "Pause while sending says it waits for the file in flight, lets it arrive whole, and only then shows Paused");
            Program.Render(host, Path.Combine(scratch, "community_paused_send.png"));
            console.Slow = null;
            Click("PauseClick", "zm_pause");
            CommunityChecks.Pump(() => !panel.IsDownloading, 60);
            Program.Check(map.State == RunStates.Done && map.Sent && map.Note == "Downloaded · on the console" && File.Exists(console.Local($"{remote}/zm_pause.ff")) && !File.Exists(xpak)
                && console.Commands.Count(line => line.StartsWith("USER ")) == 2, "Resume signs in to the console again and finishes the send");

            var view = (LogView)window.FindName("Log");
            string noted = ((TextBox)view.FindName("Text")).Text + typeof(LogView).GetField("_pending", Private)!.GetValue(view);
            Program.Check(noted.Contains("Pause Me: paused") && noted.Contains("Pause Me: carrying on"), "the log notes each pause and resume");

            Console.WriteLine("== cancelling: the Community tab");
            Directory.Delete(folder, true);
            panel.OutputChanged();
            server.Slow = one;
            Click("StartClick", "zm_pause");
            CommunityChecks.Pump(() => map.Progress > 0.05, 30);
            Click("CancelClick", "zm_pause");
            string asked = map.StageText;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CommunityChecks.Pump(() => !panel.IsDownloading, 30);
            clock.Stop();
            server.Slow = null;
            Console.WriteLine($"   cancelled while downloading: '{asked}', {map.State} after {clock.Elapsed.TotalSeconds:0.0}s");
            Program.Check(asked == "Cancelling" && map.State == RunStates.Cancelled && clock.Elapsed.TotalSeconds < 5 && !Directory.Exists(held) && !Directory.Exists(folder),
                "Cancel on a row that is downloading stops it at once and leaves nothing behind");

            Directory.Delete(console.Local(remote), true);
            Set("_sendToConsole", true);
            panel.SettingsChanged();
            CommunityChecks.Pump(() => !CommunityChecks.Field<bool>(panel, "_looking"), 20);
            console.Forget();
            console.Greet = 2500;
            Click("StartClick", "zm_pause");
            CommunityChecks.Pump(() => map.StageText == "Checking the console" || !panel.IsDownloading, 60);
            string checking = map.StageText;
            Click("CancelClick", "zm_pause");
            CommunityChecks.Pump(() => !panel.IsDownloading, 60);
            console.Greet = 0;
            string[] stored = [.. console.Commands.Where(line => line.StartsWith("STOR "))];
            Console.WriteLine($"   cancelled at '{checking}': {map.State}, {stored.Length} files sent, copy on this PC: {map.Local}");
            Program.Check(checking == "Checking the console" && map.State == RunStates.Cancelled && stored.Length == 0 && map.Local == LocalCopies.Current
                && File.Exists(Path.Combine(folder, "zm_pause.ff")), "Cancel between the download and the send stops there: nothing goes to the console and the download is kept");
        }
        finally
        {
            Edition.Use(real);
            Set("_outputOverride", outputBefore);
            Set("_consoleAddress", null);
            Set("_sendToConsole", false);
        }
    }
}
