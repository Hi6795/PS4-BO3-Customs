using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Desktop;
using FFPorter.Desktop.Controls;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal static class PreviewShots
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const double Scale = 1.5;

    public static string? Reports { get; set; }

    public static void Run(string scratch, string[] which)
    {
        bool Wanted(string name) => which.Length == 0 || which.Contains(name);
        var settings = AppSettings.Current;
        void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(settings, value);
        Edition real = Edition.Current;
        object? output = typeof(AppSettings).GetField("_outputOverride", Private)!.GetValue(settings);
        try
        {
            if (Wanted("community"))
                Community(scratch, real, Set);
            Edition.Use(real);
            if (Wanted("convert"))
                Convert(scratch, Set);
            if (Wanted("settings"))
                Settings(scratch, Set);
            if (Wanted("installed"))
                Installed(scratch);
            if (Wanted("console"))
                OnConsole(scratch, Set);
            if (Wanted("converted"))
                Converted(scratch, Set);
            if (Wanted("update"))
                Update(scratch, Set);
        }
        finally
        {
            Edition.Use(real);
            Set("_outputOverride", output);
            Set("_consoleAddress", null);
            Set("_consoleStorage", null);
            Set("_sendToConsole", false);
        }
    }

    private static RenderTargetBitmap Snap(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * Scale), (int)Math.Ceiling(size.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void WithMenu(Border host, FrameworkElement anchor, FrameworkElement menu, string path, Point? at = null)
    {
        var size = new Size(host.Width, host.Height);
        RenderTargetBitmap page = Snap(host, size);
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size wanted = menu.DesiredSize;
        RenderTargetBitmap popup = Snap(menu, wanted);
        Point corner = at ?? anchor.TransformToAncestor(host).Transform(new Point(0, anchor.ActualHeight + 3));
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawImage(page, new Rect(size));
            context.DrawImage(popup, new Rect(corner, wanted));
        }
        var bitmap = new RenderTargetBitmap(page.PixelWidth, page.PixelHeight, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Save(bitmap, path);
    }

    private static FidelityView Report(string key, int score)
    {
        if (Reports != null && FidelityProtocol.Load(Path.Combine(Reports, key + ".fidelity.json")) is { } saved)
            return FidelityView.From(saved);
        return FidelityView.From(new FidelitySnapshot { Map = key, State = FidelityStates.Done, Stage = "Finished", Progress = 1, Percent = score, Score = score, Grade = "strong" });
    }

    private static void Convert(string scratch, Action<string, object?> set)
    {
        set("_outputOverride", Path.Combine(scratch, "shots", "out"));
        set("_consoleAddress", null);
        var window = new MainWindow();
        var content = (UIElement)window.Content;
        window.Content = null;
        Border host = Program.Host(window, content, 1364, 841);
        Program.Render(host, Path.Combine(scratch, "shot_empty.png"));

        var jobs = (ObservableCollection<Job>)typeof(MainWindow).GetField("_jobs", Private)!.GetValue(window)!;
        var groups = (Dictionary<string, JobGroup>)typeof(MainWindow).GetField("_groups", Private)!.GetValue(window)!;
        Job Add(string name, long length, int streams, int banks, string? mod = null)
        {
            var job = new Job { Kind = mod == null ? "Map" : "Mod", Name = name, MainFile = $@"D:\bo3\{(mod == null ? "usermaps" : "mods")}\{mod ?? name}\{name}.ff", Mod = mod, Length = length,
                Streams = streams, SoundBanks = banks };
            if (mod != null)
            {
                if (!groups.TryGetValue(mod, out JobGroup? group))
                    groups[mod] = group = new JobGroup(mod);
                job.Group = group;
                group.Add(job);
            }
            jobs.Add(job);
            return job;
        }
        void Done(Job job, int score, bool earlier = false)
        {
            job.Report = Report(job.Key, score);
            job.State = earlier ? RunStates.Ready : RunStates.Done;
            job.StatusText = earlier ? "Converted earlier" : "Converted";
            job.Progress = 1;
        }

        Job minecraft = Add("zm_minecraft", 670_566_195, 13, 18);
        Job killhouse = Add("mp_killhouse_z", 662_203_520, 1, 2);
        Job range = Add("mp_firingrange", 1_274_806_272, 2, 2);
        Job jungle = Add("mp_jungle", 1_932_735_283, 2, 3);
        Add("zm_poopofthedead_vk", 2_306_867_200, 9, 4);
        Job core = Add("core_mod", 21_495_808, 1, 0, "free_perkaholic"), zm = Add("zm_mod", 48_234_496, 1, 1, "free_perkaholic");
        Done(minecraft, 99);
        Done(killhouse, 97);
        Done(range, 96);
        Done(jungle, 98, earlier: true);
        Done(core, 100);
        Done(zm, 99);
        range.OnConsole = true;
        set("_consoleAddress", "192.168.1.50");
        typeof(MainWindow).GetMethod("RefreshQueue", Private)!.Invoke(window, null);
        var list = (ListBox)window.FindName("JobList");
        list.SelectedItem = killhouse;
        Program.Render(host, Path.Combine(scratch, "shot_convert.png"));
        list.SelectedItem = range;
        Program.Render(host, Path.Combine(scratch, "shot_convert_ps4.png"));
        minecraft.IsExpanded = true;
        Program.Render(host, Path.Combine(scratch, "shot_convert_open.png"));
        minecraft.IsExpanded = false;
        var log = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("LogToggle");
        log.IsChecked = true;
        Program.Render(host, Path.Combine(scratch, "shot_convert_log.png"));
        log.IsChecked = false;
        list.SelectedItem = killhouse;
        Console.WriteLine($"convert: status='{((System.Windows.Documents.Run)window.FindName("StatusText")).Text}' send='{((TextBlock)window.FindName("SendText")).Text}' "
            + $"visible={((Button)window.FindName("SendButton")).Visibility} tip='{((Button)window.FindName("SendButton")).ToolTip}'");
        Console.WriteLine("convert rows: " + string.Join(" | ", jobs.Select(job => $"{job.Title}: note='{job.Note}'")));

        var add = (Button)window.FindName("AddButton");
        WithMenu(host, add, add.ContextMenu!, Path.Combine(scratch, "shot_add_menu.png"));

        typeof(MainWindow).GetMethod("JobMenuOpening", Private)!.Invoke(window, [list, null]);
        var row = (FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(killhouse);
        Point corner = row.TransformToAncestor(host).Transform(new Point(220, row.ActualHeight - 6));
        WithMenu(host, row, list.ContextMenu!, Path.Combine(scratch, "shot_row_menu.png"), corner);

        Job running = jobs.First(job => job.Name == "zm_poopofthedead_vk"), waiting = jobs.First(job => job.Name == "mp_jungle");
        void Field(string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
        FidelityStepReport Step(string key, string title, string state, double progress, double seconds, string detail = "") =>
            new() { Key = key, Title = title, State = state, Progress = progress, ElapsedSeconds = seconds, Detail = detail };
        running.State = RunStates.Running;
        running.StatusText = "Converting";
        running.StageText = "Compiling shaders";
        running.StartedAt = DateTime.UtcNow.AddSeconds(-132);
        running.Progress = 0.31;
        running.Report = FidelityView.From(new FidelitySnapshot
        {
            Map = running.Key, State = FidelityStates.Running, Stage = "Compiling shaders", Detail = "188 of 449 programs", Progress = 0.31, ElapsedSeconds = 132, RemainingSeconds = 318,
            Steps =
            [
                Step("walk", "Read the PC zone", FidelityStepStates.Done, 1, 14),
                Step("shaders", "Compile shaders", FidelityStepStates.Running, 0.42, 118, "188 of 449 programs"),
                Step("streams", "Convert streamed data", FidelityStepStates.Waiting, 0, 0),
                Step("assets", "Convert assets", FidelityStepStates.Waiting, 0, 0),
                Step("link", "Link and check", FidelityStepStates.Waiting, 0, 0),
            ],
        });
        waiting.State = RunStates.Queued;
        waiting.StatusText = "Queued";
        waiting.Estimate = 410;
        minecraft.State = RunStates.Running;
        minecraft.StatusText = "Converting";
        minecraft.StartedAt = DateTime.UtcNow.AddSeconds(-54);
        minecraft.Progress = 0.38;
        minecraft.Report = FidelityView.From(new FidelitySnapshot
        {
            Map = minecraft.Key, State = FidelityStates.Running, Stage = "Linking", Progress = 0.38, ElapsedSeconds = 54, RemainingSeconds = 120,
            Steps =
            [
                Step("walk", "Read the PC zone", FidelityStepStates.Done, 1, 4),
                Step("assets", "Convert assets", FidelityStepStates.Done, 1, 30),
                Step("link", "Link and check", FidelityStepStates.Running, 0.6, 20),
                Step("languages", "Convert 12 language zones", FidelityStepStates.Running, 0.25, 9),
            ],
        });
        var active = (ObservableCollection<Job>)typeof(MainWindow).GetField("_active", Private)!.GetValue(window)!;
        active.Add(minecraft);
        active.Add(running);
        Field("_count", 3);
        Field("_done", 0);
        Field("_width", 2);
        Field("_started", DateTime.Now.AddSeconds(-132));
        Field("_stage", "Compiling shaders");
        Field("_detail", "188 of 449 programs");
        typeof(MainWindow).GetMethod("SetBusy", Private)!.Invoke(window, [true]);
        typeof(MainWindow).GetMethod("ShowProgress", Private)!.Invoke(window, null);
        list.SelectedItem = null;
        Program.Render(host, Path.Combine(scratch, "shot_convert_running.png"));
        running.IsPaused = true;
        running.Report!.Hold(true);
        typeof(MainWindow).GetMethod("ShowProgress", Private)!.Invoke(window, null);
        Program.Render(host, Path.Combine(scratch, "shot_convert_paused.png"));
        running.IsPaused = false;
        running.Report!.Hold(false);
        typeof(MainWindow).GetMethod("ShowProgress", Private)!.Invoke(window, null);
        Console.WriteLine($"running: card='{running.RemainingText}' footer='{((TextBlock)window.FindName("PercentText")).Text}'");
    }

    private static void Settings(string scratch, Action<string, object?> set)
    {
        using var console = new MiniFtp(Path.Combine(FFPorter.Core.Workspace.WorkDirectory, "shot console"));
        Directory.CreateDirectory(console.Local("/mnt/usb0/PS4"));
        Directory.CreateDirectory(console.Local("/mnt/usb1/PS4"));
        set("_consoleAddress", $"127.0.0.1:{console.Port}");
        set("_consoleStorage", "usb1");
        set("_sendToConsole", true);
        set("_async", true);
        set("_multiConversion", false);
        set("_deleteOriginals", false);
        set("_deleteCache", false);
        var panel = new CommunityPanel();
        var files = new CommunityMap
        {
            Id = "BO3-Customs", Title = "Console files", Folder = "BO3-Customs", Kind = CommunityKinds.Console, Parts = [new Uri("https://example.com/Console.zip")],
            Size = 862_170_323, Version = "v2.00",
        };
        typeof(CommunityPanel).GetField("_files", Private)!.SetValue(panel, files);
        typeof(CommunityPanel).GetField("_listed", Private)!.SetValue(panel, DateTime.UtcNow);
        SettingsWindow dialog = Program.Settled(new SettingsWindow(null, () => Task.FromResult(false), panel));
        ((TextBlock)dialog.FindName("StateText")).Text = "Up to date (v2.00)";
        ((TextBlock)dialog.FindName("StateText")).Visibility = Visibility.Visible;
        ((TextBlock)dialog.FindName("BuildText")).Text = $"Built {DateTime.Now:d MMM yyyy, HH:mm}";
        var body = (UIElement)dialog.Content;
        dialog.Content = null;
        Border page = Program.Host(dialog, body, Program.SettingsWidth, double.NaN);
        Program.Render(page, Path.Combine(scratch, "shot_settings.png"));
        files.State = RunStates.Running;
        files.Progress = 0.44;
        files.CanPause = true;
        files.StageText = "Downloading · 362.1 MB of 822.2 MB · 18.3 MB/s";
        Program.Render(page, Path.Combine(scratch, "shot_settings_downloading.png"));
        files.StageText = "";
        files.CanPause = false;
        files.State = RunStates.Done;
        files.Local = LocalCopies.Current;
        files.HasConsole = true;
        Program.Render(page, Path.Combine(scratch, "shot_settings_downloaded.png"));
        files.Sent = true;
        Program.Render(page, Path.Combine(scratch, "shot_settings_sent.png"));
        files.Sent = false;
        files.Local = LocalCopies.Outdated;
        files.Light = true;
        Program.Render(page, Path.Combine(scratch, "shot_settings_plugin.png"));
        files.Light = false;
        files.Local = LocalCopies.Current;
        files.Sent = true;
        var more = (Button)dialog.FindName("FilesMore");
        ContextMenu menu = more.ContextMenu!;
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        page.Height = page.ActualHeight;
        WithMenu(page, more, menu, Path.Combine(scratch, "shot_settings_menu.png"),
            more.TransformToAncestor(page).Transform(new Point(more.ActualWidth - menu.DesiredSize.Width, -menu.DesiredSize.Height - 3)));
        using (var off = new TcpListenerProbe())
        {
            set("_consoleAddress", $"127.0.0.1:{off.Port}");
            SettingsWindow locked = Program.Settled(new SettingsWindow(null, () => Task.FromResult(false), panel));
            var shut = (UIElement)locked.Content;
            locked.Content = null;
            Program.Render(Program.Host(locked, shut, Program.SettingsWidth, double.NaN), Path.Combine(scratch, "shot_settings_locked.png"));
        }
        var lost = (Window)typeof(MessageDialog).GetConstructors(Private)[0].Invoke([null, DialogKind.Warning, "USB 1 is not usable", "A test file could not be written. Keep Auto-FTP on?",
            "Use console storage", "Turn off Auto-FTP", false]);
        var asked = (UIElement)lost.Content;
        lost.Content = null;
        Program.Render(Program.Host(lost, asked, 444, double.NaN), Path.Combine(scratch, "shot_lost_drive.png"));
        (string Name, string Heading, string Body)[] questions =
        [
            ("shot_overwrite_one.png", "Overwrite zm_town on the console?", "It is already there and gets replaced."),
            ("shot_overwrite_several.png", "Overwrite 3 on the console?", "zm_town, zm_die and mp_slums are already there."),
        ];
        foreach (var question in questions)
        {
            var dialogue = (Window)typeof(MessageDialog).GetConstructors(Private)[0].Invoke([null, DialogKind.Warning, question.Heading, question.Body, "Overwrite", "Skip", true]);
            var inside = (UIElement)dialogue.Content;
            dialogue.Content = null;
            Program.Render(Program.Host(dialogue, inside, 444, double.NaN), Path.Combine(scratch, question.Name));
        }
        (string Name, string Heading, string Body)[] dlls =
        [
            ("shot_dll_one.png", "zm_qol_mod uses a PC DLL", "Parts that need the DLL may not work on PS4."),
            ("shot_dll_several.png", "2 mods use a PC DLL", "zm_qol_mod, all_around_enhancement\n\nParts that need the DLL may not work on PS4."),
        ];
        foreach (var dll in dlls)
        {
            var dialogue = (Window)typeof(MessageDialog).GetConstructors(Private)[0].Invoke([null, DialogKind.Warning, dll.Heading, dll.Body, "Convert anyway", "Cancel", false]);
            var inside = (UIElement)dialogue.Content;
            dialogue.Content = null;
            Program.Render(Program.Host(dialogue, inside, 444, double.NaN), Path.Combine(scratch, dll.Name));
        }
    }

    private static void Installed(string scratch)
    {
        IReadOnlyList<Installed> found = Edition.Current.FindInstalled(AppSettings.Current.GameFolder);
        Console.WriteLine($"installed: {found.Count} found");
        foreach (Installed item in found)
            Console.WriteLine($"   {item.Kind} | {item.Title} | {item.Name} | {Job.SizeText(item.Size)} | picture={(item.Picture != null ? Path.GetFileName(item.Picture) : "none")}");
        var dialog = new InstalledWindow(null, found);
        var items = CommunityChecks.Field<ObservableCollection<InstalledItem>>(dialog, "_items");
        CommunityChecks.Pump(() => items.Where(item => item.Source.Picture != null).All(item => item.HasPicture), 20);
        var list = (ListBox)dialog.FindName("Items");
        foreach (InstalledItem item in items.Take(2))
            list.SelectedItems.Add(item);
        var body = (UIElement)dialog.Content;
        dialog.Content = null;
        Border picker = Program.Host(dialog, body, 684, 641);
        Program.Render(picker, Path.Combine(scratch, "shot_installed.png"));
        list.SelectedItems.Clear();
        ((RadioButton)dialog.FindName("MultiplayerFilter")).IsChecked = true;
        Program.Render(picker, Path.Combine(scratch, "shot_installed_filtered.png"));

        var none = new InstalledWindow(null, []);
        var empty = (UIElement)none.Content;
        none.Content = null;
        Program.Render(Program.Host(none, empty, 684, 400), Path.Combine(scratch, "shot_installed_empty.png"));
    }

    private static void OnConsole(string scratch, Action<string, object?> set)
    {
        set("_outputOverride", Path.Combine(scratch, "shots", "out"));
        set("_consoleAddress", "192.168.1.50");
        var window = new MainWindow();
        var content = (UIElement)window.Content;
        window.Content = null;
        Border host = Program.Host(window, content, 1364, 841);
        var panel = (ConsolePanel)window.FindName("OnConsole");
        void Field(string name, object value) => typeof(ConsolePanel).GetField(name, Private)!.SetValue(panel, value);
        (string Name, bool Mod, string? Storage, long Size, int Files, string? Odd)[] found =
        [
            ("zm_levelcommon_test", false, null, 412_000_000, 9, null),
            ("free_perkaholic", true, null, 66_500_000, 6, null),
            ("mp_firingrange", false, "usb1", 1_274_806_272, 14, null),
            ("mp_killhouse_z", false, "usb1", 662_203_520, 11, null),
            ("zm_buriedchallenge", false, "usb1", 1_048_471_142, 47, null),
            ("zm_ccube", false, "usb1", 1_180_591_620, 38, null),
            ("zm_halloween", false, "usb1", 5_862_741_606, 122, null),
            ("zm_kyassuruz", false, "usb1", 3_339_888_230, 64, null),
            ("zm_lf_town", false, "usb1", 2_147_483_648, 51, "part"),
            ("zm_poopofthedead_vk", false, "usb1", 2_306_867_200, 71, null),
            ("zm_waterparkfinale", false, "usb1", 1_932_735_283, 58, null),
            ("nuketown_hd", false, "ext0", 3_049_426_780, 80, null),
            ("zm_seppuku", false, "ext0", 4_402_341_478, 96, "zone"),
        ];
        ConsoleEntry[] entries = [.. found.Select(item => new ConsoleEntry(item.Name, item.Mod, item.Storage,
            $"{(item.Mod ? ConsoleFtp.Mods(ConsoleFtp.RootOf(item.Storage)) : ConsoleFtp.Maps(ConsoleFtp.RootOf(item.Storage)))}/{item.Name}"))];
        Field("_address", "192.168.1.50:2121");
        typeof(ConsolePanel).GetMethod("Found", Private)!.Invoke(panel, [entries]);
        for (int index = 0; index < found.Length; index++)
        {
            var item = found[index];
            string main = item.Mod ? "zm_mod" : item.Name;
            var files = new List<ConsoleFile>
            {
                new(item.Odd == "zone" ? "other.ff" : main + (item.Odd == "part" ? ".ff.part" : ".ff"), item.Size / 6),
                new(main + ".xpak", item.Size - item.Size / 6 - 1000 * (item.Files - 2)),
            };
            files.AddRange(Enumerable.Range(0, item.Files - 2).Select(number => new ConsoleFile($"snd/all/{main}.{number}.sabl", 1000)));
            typeof(ConsolePanel).GetMethod("Measured", Private)!.Invoke(panel, [new ConsoleSize(entries[index], item.Size, files)]);
        }
        Field("_stale", false);
        Field("_scanned", DateTime.UtcNow);
        typeof(ConsolePanel).GetMethod("ShowStatus", Private)!.Invoke(panel, null);
        ((RadioButton)window.FindName("InstalledTab")).IsChecked = true;
        Program.Render(host, Path.Combine(scratch, "shot_installed_tab.png"));

        set("_consoleAddress", null);
        typeof(ConsolePanel).GetMethod("Changed")!.Invoke(panel, null);
        Program.Render(host, Path.Combine(scratch, "shot_installed_tab_unset.png"));
    }

    private static void Converted(string scratch, Action<string, object?> set)
    {
        string output = Path.Combine(scratch, "shots", "out");
        set("_outputOverride", output);
        set("_consoleAddress", "192.168.1.50");
        var window = new MainWindow();
        var content = (UIElement)window.Content;
        window.Content = null;
        Border host = Program.Host(window, content, 1364, 841);
        var panel = (ConvertedPanel)window.FindName("Converted");
        CommunityChecks.Pump(() => CommunityChecks.Field<bool>(panel, "_listed") && !CommunityChecks.Field<bool>(panel, "_scanning"), 20);
        typeof(ConvertedPanel).GetField("_scanning", Private)!.SetValue(panel, true);
        var items = CommunityChecks.Field<ObservableCollection<ConvertedItem>>(panel, "_items");
        (string Name, bool Mod, long Size, int Files, string Score, string Grade, bool Downloaded, bool Sent)[] made =
        [
            ("mp_atomic", false, 2_801_000_000, 12, "99%", "strong", false, false),
            ("mp_firingrange", false, 1_274_806_272, 14, "99%", "strong", false, true),
            ("mp_killhouse_z", false, 662_203_520, 9, "99%", "strong", false, true),
            ("zm_buriedchallenge", false, 1_048_471_142, 47, "", "", true, false),
            ("zm_levelcommon", false, 412_000_000, 9, "99%", "strong", false, false),
            ("zm_poopofthedead_vk", false, 2_306_867_200, 71, "99%", "strong", false, false),
            ("free_perkaholic", true, 66_500_000, 6, "100%", "exact", false, false),
            ("zombieui", true, 48_234_496, 5, "97–99%", "strong", false, false),
        ];
        foreach (var entry in made)
        {
            string folder = entry.Mod ? Path.Combine(output, "t7", "mods", entry.Name) : Path.Combine(output, "t7", entry.Name);
            items.Add(new ConvertedItem
            {
                Name = entry.Name, Folder = folder, Place = Path.GetRelativePath(output, folder), IsMod = entry.Mod, Remote = Edition.Current.ConsoleFolder(entry.Name, entry.Mod),
                Size = entry.Size, Files = entry.Files, When = DateTime.Now, Downloaded = entry.Downloaded, ScoreText = entry.Score, ScoreGrade = entry.Grade, HasConsole = true, Sent = entry.Sent,
            });
        }
        typeof(ConvertedPanel).GetMethod("ShowList", Private)!.Invoke(panel, null);
        typeof(ConvertedPanel).GetMethod("ShowStatus", Private)!.Invoke(panel, null);
        ((RadioButton)window.FindName("ConvertedTab")).IsChecked = true;
        Program.Render(host, Path.Combine(scratch, "shot_converted_tab.png"));

        var clear = (Button)window.FindName("ClearCacheButton");
        ((TextBlock)typeof(MainWindow).GetField("CacheSizeText", Private)!.GetValue(window)!).Text = "2.31 GB in the cache";
        var tip = (ToolTip)clear.ToolTip;
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        WithMenu(host, clear, tip, Path.Combine(scratch, "shot_cache_tip.png"),
            clear.TransformToAncestor(host).Transform(new Point(clear.ActualWidth - tip.DesiredSize.Width, -tip.DesiredSize.Height - 6)));
    }

    private static void Update(string scratch, Action<string, object?> set)
    {
        set("_consoleAddress", null);
        var window = new MainWindow();
        var content = (UIElement)window.Content;
        window.Content = null;
        Border host = Program.Host(window, content, 1364, 841);
        string notes = (string)typeof(AppUpdate).GetMethod("Readable", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
            ["## What's new\n- **Community tab**: download maps that are already converted, or send them straight to the console\n- Pause and resume for downloads and console transfers\n"
                + "- Pick from your installed maps and mods instead of browsing for folders\n- Converted maps can be sent to the console from the Convert tab\n\n## Fixes\n"
                + "- Sounds with a one-frame loop no longer fail the whole sound bank\n- A second launch now goes to the window that is already open"])!;
        ((TextBlock)window.FindName("UpdateHeading")).Text = "Installing an update";
        ((TextBlock)window.FindName("UpdateBody")).Text = "v2.01 is downloading. The app restarts by itself.";
        ((TextBlock)window.FindName("UpdateNotes")).Text = notes;
        ((Border)window.FindName("UpdateNotesBox")).Visibility = Visibility.Visible;
        ((ProgressBar)window.FindName("UpdateProgress")).Value = 0.37;
        ((TextBlock)window.FindName("UpdateDetail")).Text = "58.1 MB of 157 MB · 21.4 MB/s";
        ((Grid)window.FindName("UpdateOverlay")).Visibility = Visibility.Visible;
        Program.Render(host, Path.Combine(scratch, "shot_update.png"));
    }

    private static void Community(string scratch, Edition real, Action<string, object?> set)
    {
        string site = Path.Combine(scratch, "shots", "site");
        Directory.CreateDirectory(Path.Combine(site, "t7", "thumbnails"));
        (string Id, string Title, long Size, string Author, string About, Color Colour)[] maps =
        [
            ("zm_buriedchallenge", "The Buried Challenge", 735_786_598, "Optus", "A challenge take on Buried with new routes, side quests and a boss fight.", Colors.SaddleBrown),
            ("zm_ccube", "Cheese Cube Unlimited V1.0", 839_489_126, "", "One room, endless cheese.", Colors.Goldenrod),
            ("zm_halloween", "WEEPING PINES", 4_209_067_950, "Madgaz", "A haunted forest with a full quest line, custom weapons and its own perks.", Colors.DarkOliveGreen),
            ("zm_kyassuruz", "Kyassuru", 2_512_555_704, "", "", Colors.SteelBlue),
            ("mp_atomic", "[MP] Nuketown '68", 3_049_426_780, "", "The classic, rebuilt.", Colors.IndianRed),
        ];
        foreach (var map in maps)
            File.WriteAllBytes(Path.Combine(site, "t7", "thumbnails", map.Id + ".png"), CommunityChecks.Picture(map.Colour));
        object[] entries = [.. maps.Select(map => (object)new { name = map.Id, title = map.Title, author = map.Author, description = map.About, size = map.Size, file = $"maps/{map.Id}.zip",
            thumbnail = $"thumbnails/{map.Id}.png", sha256 = new string('a', 64) })];
        File.WriteAllText(Path.Combine(site, "t7", "catalog.json"), JsonSerializer.Serialize(new { name = "Shots", maps = entries }), new UTF8Encoding(true));
        using var server = new MiniServer(site);
        Edition.Use(new ListEdition(real, $"http://127.0.0.1:{server.Port}/t7/catalog.json"));
        set("_outputOverride", Path.Combine(scratch, "shots", "out"));
        set("_consoleAddress", null);

        var window = new MainWindow();
        var content = (UIElement)window.Content;
        window.Content = null;
        Border host = Program.Host(window, content, 1364, 841);
        var panel = (CommunityPanel)window.FindName("Community");
        typeof(CommunityPanel).GetField("_library", Private)!.SetValue(panel, CommunityLibrary.Load(Path.Combine(scratch, "shots", "library.json")));
        ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;
        var list = CommunityChecks.Field<ObservableCollection<CommunityMap>>(panel, "_maps");
        CommunityChecks.Pump(() => CommunityChecks.Field<bool>(panel, "_loaded") && list.Where(map => map.Thumbnail != null).All(map => map.HasPicture), 15);
        CommunityMap Map(string id) => list.First(map => map.Id == id);

        foreach (CommunityMap map in list)
            map.HasConsole = true;
        Map("zm_buriedchallenge").Sent = true;
        Map("zm_ccube").State = RunStates.Running;
        Map("zm_ccube").Progress = 0.42;
        Map("zm_ccube").StageText = "Downloading · 336 MB of 800.6 MB · 18.3 MB/s";
        Map("zm_ccube").CanPause = true;
        Map("zm_halloween").State = RunStates.Queued;
        Map("zm_kyassuruz").Local = LocalCopies.Current;
        Map("mp_atomic").Fetched = 1_288_490_188;
        typeof(CommunityPanel).GetMethod("ShowCounts", Private)!.Invoke(panel, null);
        typeof(CommunityPanel).GetProperty("Status")!.SetValue(panel, "Downloading Cheese Cube Unlimited V1.0 · 1 waiting");
        Program.Render(host, Path.Combine(scratch, "shot_community.png"));
        Console.WriteLine("community: " + string.Join(" | ", list.Select(map => $"{map.Title}: [{(map.CanStart ? map.ActionText : "")}] note='{map.Note}' menu={map.HasMenu}")));
    }
}
