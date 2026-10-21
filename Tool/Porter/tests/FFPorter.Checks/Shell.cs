using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FFPorter.Desktop;
using FFPorter.Desktop.Controls;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal sealed class PlainEdition(Edition inner) : Edition
{
    public override string Codename => inner.Codename;
    public override string Title => inner.Title;
    public override string Subtitle => inner.Subtitle;
    public override string GameName => inner.GameName;
    public override string GameLabel => inner.GameLabel;
    public override string SettingsFile => inner.SettingsFile;
    public override string GameFolderName => inner.GameFolderName;
    public override string GameFolderHint => inner.GameFolderHint;
    public override string GameFolderOption => inner.GameFolderOption;
    public override bool TryGameFolder(string chosen, out string? folder) => inner.TryGameFolder(chosen, out folder);
    public override bool Accepts(string file, out string? why) => inner.Accepts(file, out why);
    public override IEnumerable<Job> Jobs(IReadOnlyList<string> files) => inner.Jobs(files);
    public override int RunBackend(string[] arguments, TextWriter output, TextWriter error) => inner.RunBackend(arguments, output, error);
}

internal static class ShellChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run(string scratch)
    {
        string root = Path.Combine(scratch, "shell");
        var settings = AppSettings.Current;
        object? outputBefore = typeof(AppSettings).GetField("_outputOverride", Private)!.GetValue(settings);
        Edition real = Edition.Current;
        Edition.Use(new ListEdition(real, null));
        try
        {
            Instance(root);
            Picker(scratch, root, real);
            Options(scratch, root, real);
            Boxes(scratch);
            Queue(scratch, root);
            Storage(scratch, root);
            Sending(scratch, root);
        }
        finally
        {
            Edition.Use(real);
            Set("_outputOverride", outputBefore);
            Set("_consoleAddress", null);
            Set("_sendToConsole", false);
            AppUpdate.Summary = "";
        }
    }

    private static void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(AppSettings.Current, value);

    private static void Touch(string path, int bytes = 4000, byte platform = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[bytes];
        if (path.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
        {
            "TAff"u8.CopyTo(data);
            data[14] = platform;
        }
        File.WriteAllBytes(path, data);
    }

    private static void Queue(string scratch, string root)
    {
        Console.WriteLine("== one list");
        var (window, host, jobs) = Open();
        string maps = Path.Combine(root, "queue", "usermaps");
        foreach (string name in (string[])["zm_first", "zm_second"])
            Touch(Path.Combine(maps, name, "zone", name + ".ff"));
        Program.Check(window.FindName("ActivityBand") == null && window.FindName("Details") == null && window.FindName("Fidelity") == null && window.FindName("JobList") is ListBox,
            "the Convert page is one list: no separate cards on top and no report panel beside it");
        window.AddPaths([Path.Combine(maps, "zm_first")]);
        Program.Check(jobs.Count == 1 && jobs[0].IsExpanded, "a single item in the queue shows its report right away");
        window.AddPaths([Path.Combine(maps, "zm_second")]);
        Program.Check(jobs.Count == 2 && jobs.All(job => !job.IsExpanded), "with more than one item every row is folded, so the whole queue is in view");
        MethodInfo toggle = typeof(MainWindow).GetMethod("ExpandClick", Private)!;
        toggle.Invoke(window, [new Button { DataContext = jobs[1] }, new RoutedEventArgs()]);
        bool opened = jobs[1].IsExpanded && !jobs[0].IsExpanded;
        Program.Render(host, Path.Combine(scratch, "shell_queue_open.png"));
        toggle.Invoke(window, [new Button { DataContext = jobs[1] }, new RoutedEventArgs()]);
        Program.Check(opened && !jobs[1].IsExpanded, "the arrow on a row opens and closes that row's report, and only that row's");
    }

    private static bool WaitFor(Func<bool> done, double seconds)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until)
            Thread.Sleep(25);
        return done();
    }

    private static (MainWindow Window, Border Host, ObservableCollection<Job> Jobs) Open()
    {
        var window = new MainWindow();
        var content = (UIElement)window.Content;
        window.Content = null;
        return (window, Program.Host(window, content, 1364, 841), (ObservableCollection<Job>)typeof(MainWindow).GetField("_jobs", Private)!.GetValue(window)!);
    }

    private static string Status(MainWindow window) => ((Run)window.FindName("StatusText")).Text + " |" + ((Run)window.FindName("DetailText")).Text;

    private static string Noted(MainWindow window)
    {
        var view = (LogView)window.FindName("Log");
        return ((TextBox)view.FindName("Text")).Text + typeof(LogView).GetField("_pending", Private)!.GetValue(view);
    }

    private static object? Call(MainWindow window, string method, params object?[] arguments) => typeof(MainWindow).GetMethod(method, Private)!.Invoke(window, arguments);

    internal static List<string> Spilled(DependencyObject shown)
    {
        var spilled = new List<string>();
        void Walk(DependencyObject node)
        {
            if (node is TextBlock { ActualHeight: > 0 } line && Said(line) is { Length: > 0 } said)
            {
                double shownWidth = line.ActualWidth;
                bool wrapped = line.ActualHeight > line.FontSize * 2.2;
                line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (wrapped || line.DesiredSize.Width - line.Margin.Left - line.Margin.Right > shownWidth + 1)
                    spilled.Add(said);
            }
            for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); index++)
                Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, index));
        }
        Walk(shown);
        return spilled;
    }

    private static string Said(TextBlock line) => line.Text.Length > 0 ? line.Text : string.Concat(line.Inlines.OfType<Run>().Select(run => run.Text));

    private static void Instance(string root)
    {
        Console.WriteLine("== one window only");
        string name = $"checks.{Environment.ProcessId}";
        string[] handed = [@"D:\maps\zm one\zone\zm_one.ff", @"D:\mods\cool mod"];
        var got = new ConcurrentQueue<string[]>();
        SingleInstance? first = SingleInstance.Claim(name, ["not handed anywhere"]);
        Program.Check(first is { IsFirst: true }, "the first start takes the app's place and opens its window");
        SingleInstance? second = SingleInstance.Claim(name, handed);
        Program.Check(second == null, "a second start hands over what it was given and opens no window of its own");
        first!.Received = got.Enqueue;
        bool arrived = WaitFor(() => got.Count == 1, 5);
        Program.Check(arrived && got.TryPeek(out string[]? paths) && paths.SequenceEqual(handed), "what it handed over reaches the first window intact, even when it arrives before the window is ready");
        SingleInstance? third = SingleInstance.Claim(name, []);
        arrived = WaitFor(() => got.Count == 2, 5);
        Program.Check(third == null && arrived && got.Last().Length == 0, "a start with nothing to hand over still reaches the open window, which comes to the front");
        first.Dispose();
        SingleInstance? fourth = SingleInstance.Claim(name, []);
        Program.Check(fourth is { IsFirst: true }, "once the app has closed, the next start is the first again");
        fourth?.Dispose();

        string usermaps = Path.Combine(root, "handed", "usermaps");
        Touch(Path.Combine(usermaps, "zm_handed", "zone", "zm_handed.ff"));
        Touch(Path.Combine(usermaps, "zm_late", "zone", "zm_late.ff"));
        var (window, _, jobs) = Open();
        ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;
        window.Receive([Path.Combine(usermaps, "zm_handed")]);
        Program.Check(jobs.Select(job => job.Name).SequenceEqual(["zm_handed"]) && ((RadioButton)window.FindName("ConvertTab")).IsChecked == true,
            "what a second start hands over is added to the open window's queue, which switches to Convert");
        typeof(MainWindow).GetField("_running", Private)!.SetValue(window, true);
        window.Receive([Path.Combine(usermaps, "zm_late")]);
        typeof(MainWindow).GetField("_running", Private)!.SetValue(window, false);
        Program.Check(jobs.Count == 1 && Noted(window).Contains("Not added while the app is busy"), "while something converts, nothing is added behind its back, and the log says so");
    }

    private static void Picker(string scratch, string root, Edition real)
    {
        Console.WriteLine("== installed maps and mods");
        string game = Path.Combine(root, "game");
        Touch(Path.Combine(game, "zone", "base.xpak"));
        string alpha = Path.Combine(game, "usermaps", "1001"), built = Path.Combine(game, "usermaps", "zm_built"), arena = Path.Combine(game, "usermaps", "1003"), mod = Path.Combine(game, "mods", "2001");
        foreach (string file in (string[])["zm_alpha.ff", "en_zm_alpha.ff", "fr_zm_alpha.ff", "zm_alpha.xpak", "snd/all/zm_alpha.all.sabl"])
            Touch(Path.Combine(alpha, file));
        File.WriteAllText(Path.Combine(alpha, "workshop.json"), "{\"Title\":\"Alpha Station\",\"FolderName\":\"zm_alpha\",\"Type\":\"map\"}");
        File.WriteAllBytes(Path.Combine(alpha, "previewimage.png"), CommunityChecks.Picture(System.Windows.Media.Colors.SteelBlue));
        Touch(Path.Combine(built, "zone", "zm_built.ff"));
        File.WriteAllText(Path.Combine(built, "zone", "workshop.json"), "{\"Title\":\"  \",\"FolderName\":\"zm_built\"}");
        File.WriteAllBytes(Path.Combine(built, "zone", "loadingimage.png"), CommunityChecks.Picture(System.Windows.Media.Colors.IndianRed));
        Touch(Path.Combine(arena, "mp_arena.ff"));
        Touch(Path.Combine(game, "usermaps", "1004", "zm_done.ff"), platform: 1);
        Touch(Path.Combine(game, "usermaps", "1005", "notes.txt"));
        File.WriteAllText(Path.Combine(game, "usermaps", "1006.txt"), "a file, not a folder");
        Directory.CreateDirectory(Path.Combine(game, "usermaps", "empty"));
        foreach (string file in (string[])["core_mod.ff", "en_core_mod.ff", "zm_mod.ff", "zm_mod.xpak"])
            Touch(Path.Combine(mod, file));
        File.WriteAllText(Path.Combine(mod, "workshop.json"), "{\"Title\":\"Cool Mod\",\"FolderName\":\"cool mod?\",\"Type\":\"mod\"}");

        Installed[] mine = [.. real.FindInstalled(Path.Combine(game, "zone")).Where(item => item.Folder.StartsWith(game, StringComparison.OrdinalIgnoreCase))];
        foreach (Installed item in mine)
            Console.WriteLine($"   {item.Kind} | {item.Title} | {item.Name} | {item.Size:N0} | {(item.Picture == null ? "no picture" : Path.GetRelativePath(item.Folder, item.Picture))}");
        Program.Check(mine.Select(item => (item.Title, item.Kind, item.Name)).SequenceEqual([("Alpha Station", "Zombies map", "zm_alpha"), ("Cool Mod", "Mod", "cool mod_"), ("mp_arena", "Multiplayer map", "mp_arena"),
            ("zm_built", "Zombies map", "zm_built")]),
            "the maps and mods in the game files folder are found and named: by their Workshop title, or by their zone when there is none");
        Program.Check(mine[0].Folder == alpha && mine[0].Size > 20_000 && mine[0].Picture == Path.Combine(alpha, "previewimage.png") && mine[3].Picture == Path.Combine(built, "zone", "loadingimage.png")
            && mine[2].Picture == null, "each comes with its folder, its size and its picture when it has one");
        Program.Check(!mine.Any(item => item.Folder.EndsWith("1004") || item.Folder.EndsWith("1005") || item.Folder.EndsWith("empty")),
            "a folder with nothing to convert in it (already a PS4 zone, no zone at all, empty) is not offered");
        Program.Check(real.FindInstalled(null).All(item => !item.Folder.StartsWith(game, StringComparison.OrdinalIgnoreCase)) && real.FindInstalled(Path.Combine(root, "nowhere", "zone")).Count >= 0,
            "with no game files folder set, or one that is gone, looking still works");

        var dialog = new InstalledWindow(null, mine);
        var items = CommunityChecks.Field<ObservableCollection<InstalledItem>>(dialog, "_items");
        CommunityChecks.Pump(() => items.Count(item => item.HasPicture) == 2, 15);
        var list = (ListBox)dialog.FindName("Items");
        var search = (TextBox)dialog.FindName("SearchBox");
        var add = (Button)dialog.FindName("AddButton");
        var count = (TextBlock)dialog.FindName("CountText");
        string[] Shown() => [.. list.Items.Cast<InstalledItem>().Select(item => item.Source.Name)];
        var body = (UIElement)dialog.Content;
        dialog.Content = null;
        Border frame = Program.Host(dialog, body, 684, 420);
        Program.Check(Shown().Length == 4 && items.Count(item => item.HasPicture) == 2 && !add.IsEnabled && count.Text.StartsWith("4 found") && items[0].Meta.StartsWith("Zombies map  ·  zm_alpha  ·  "),
            "the picker lists them with their pictures, and Add waits for a choice");
        list.SelectedItems.Add(items[0]);
        list.SelectedItems.Add(items[1]);
        var chosen = (IReadOnlyList<string>)typeof(InstalledWindow).GetMethod("Selected", Private)!.Invoke(dialog, null)!;
        Program.Check(add.IsEnabled && (string)add.Content == "Add 2" && count.Text == "2 of 4 selected" && chosen.SequenceEqual([alpha, mod]), "several can be chosen at once; Add says how many");
        Program.Render(frame, Path.Combine(scratch, "installed.png"));
        search.Text = "station";
        bool byTitle = Shown().SequenceEqual(["zm_alpha"]);
        search.Text = "MP_AR";
        bool byZone = Shown().SequenceEqual(["mp_arena"]);
        search.Text = "nothing like this";
        var emptyTitle = (TextBlock)dialog.FindName("EmptyTitle");
        Program.Check(byTitle && byZone && Shown().Length == 0 && emptyTitle.Text == "Nothing matches your search" && ((FrameworkElement)dialog.FindName("Empty")).Visibility == Visibility.Visible,
            "search narrows the list by title or zone name, and says when nothing matches");
        search.Text = "";
        string Counts() => string.Join("/", ((string[])["AllCount", "ZombiesCount", "MultiplayerCount", "ModsCount"]).Select(name => ((Run)dialog.FindName(name)).Text.Trim()));
        string everything = Counts();
        ((RadioButton)dialog.FindName("ZombiesFilter")).IsChecked = true;
        string[] zombies = Shown();
        Program.Render(frame, Path.Combine(scratch, "installed_zombies.png"));
        ((RadioButton)dialog.FindName("MultiplayerFilter")).IsChecked = true;
        string[] multiplayer = Shown();
        search.Text = "alpha";
        string narrowed = $"{Counts()} {emptyTitle.Text}";
        search.Text = "";
        ((RadioButton)dialog.FindName("ModsFilter")).IsChecked = true;
        string[] mods = Shown();
        list.SelectedItems.Add(items[1]);
        bool picked = (string)add.Content == "Add" && add.IsEnabled;
        ((RadioButton)dialog.FindName("AllFilter")).IsChecked = true;
        Console.WriteLine($"   picker filters: {everything}; zombies {string.Join(",", zombies)}; multiplayer {string.Join(",", multiplayer)}; mods {string.Join(",", mods)}; searching: {narrowed}");
        Program.Check(everything == "4/2/1/1" && zombies.SequenceEqual(["zm_alpha", "zm_built"]) && multiplayer.SequenceEqual(["mp_arena"]) && mods.SequenceEqual(["cool mod_"]) && picked
            && narrowed == "1/1/0/0 Nothing matches your search" && Shown().Length == 4,
            "the picker narrows to Zombies, Multiplayer or Mods, with counts that follow the search");
        var lone = new InstalledWindow(null, [mine[1]]);
        ((RadioButton)lone.FindName("ZombiesFilter")).IsChecked = true;
        Program.Check(((TextBlock)lone.FindName("EmptyTitle")).Text == "No Zombies maps were found" && ((ListBox)lone.FindName("Items")).Visibility == Visibility.Collapsed,
            "a kind with nothing in it says so");
        var none = new InstalledWindow(null, []);
        Program.Check(((TextBlock)none.FindName("EmptyTitle")).Text == "Nothing was found" && ((TextBlock)none.FindName("EmptyText")).Text.Contains("game files folder")
            && !((Button)none.FindName("AddButton")).IsEnabled && ((TextBlock)none.FindName("CountText")).Text == "", "with nothing installed, the picker says where it looked instead of showing an empty list");

        var (window, _, jobs) = Open();
        window.AddPaths(chosen);
        Console.WriteLine("   queued from the picker: " + string.Join(", ", jobs.Select(job => $"{job.Title} ({job.Kind})")));
        Program.Check(jobs.Select(job => (job.Name, job.Kind, job.Mod)).SequenceEqual([("core_mod", "Mod", "cool mod_"), ("zm_mod", "Mod", "cool mod_"), ("zm_alpha", "Map", (string?)null)]),
            "what is picked goes into the queue exactly as if its folder had been dropped");
    }

    private static void Options(string scratch, string root, Edition real)
    {
        Console.WriteLine("== settings and the footer");
        var (window, host, _) = Open();
        var add = (Button)window.FindName("AddButton");
        string[] menu = [.. add.ContextMenu!.Items.Cast<MenuItem>().Select(item => (string)item.Header)];
        Program.Check(window.FindName("AddFilesButton") == null && window.FindName("AddFolderButton") == null && menu.SequenceEqual(["Files…", "A folder…", "Installed maps and mods…"]),
            "the queue has one Add button with a menu");
        Edition.Use(new PlainEdition(real));
        MainWindow plain = Open().Window;
        Edition.Use(new ListEdition(real, null));
        Program.Check(((MenuItem)plain.FindName("AddInstalledItem")).Visibility == Visibility.Collapsed && ((Button)plain.FindName("InstalledButton")).Visibility == Visibility.Collapsed
            && ((MenuItem)window.FindName("AddInstalledItem")).Visibility == Visibility.Visible && ((Button)plain.FindName("SendButton")).Visibility == Visibility.Collapsed,
            "an edition that cannot look for installed maps does not offer to");
        Program.Check(((Button)window.FindName("RemoveButton")).Visibility == Visibility.Collapsed && ((Button)window.FindName("ClearButton")).Visibility == Visibility.Collapsed
            && ((Button)window.FindName("SendButton")).Visibility == Visibility.Collapsed && ((Button)window.FindName("InstalledButton")).IsEnabled
            && ((TextBlock)window.FindName("GameFolderLabel")).Text == "Game files" && ((TextBlock)window.FindName("OutputLabel")).Text == "Output",
            "an empty queue shows only what can be used: no remove, clear or send buttons");
        Program.Render(host, Path.Combine(scratch, "shell_empty.png"));
        var logo = (Image)window.FindName("AppLogo");
        var icon = window.Icon as System.Windows.Media.Imaging.BitmapFrame;
        int[] sizes = [.. (icon?.Decoder.Frames ?? []).Select(frame => frame.PixelWidth).Order()];
        Console.WriteLine($"   icon sizes: {string.Join(", ", sizes)}; header logo {logo.ActualWidth}x{logo.ActualHeight}");
        Program.Check(ReferenceEquals(logo.Source, Application.Current.FindResource("Logo")) && logo.ActualWidth == 42 && sizes.SequenceEqual([16, 20, 24, 32, 40, 48, 64, 128, 256])
            && ReferenceEquals(new SettingsWindow(null).Icon, window.Icon), "the app has its own logo: in the header, and as the icon of every window in all the sizes Windows asks for");
        Assembly app = typeof(T7Edition).Assembly;
        var author = (TextBlock)window.FindName("AuthorText");
        Console.WriteLine($"   credit: header '{author.Text}', exe details '{app.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright}' / company '{app.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company}'");
        Program.Check(author.Text == "by ItsJokerZz" && author.ActualWidth > 0 && ((TextBlock)plain.FindName("AuthorText")).Text == ""
            && app.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright == "GPLv3+ © 2026 ItsJokerZz" && app.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company == "ItsJokerZz",
            "the header carries the author's byline beside the title, and the exe's details give ItsJokerZz as the company and GPLv3+ as its copyright");

        string cache = Path.Combine(FFPorter.Core.Workspace.WorkDirectory, "t7");
        if (Directory.Exists(cache))
        {
            foreach (string folder in Directory.GetDirectories(cache))
                Directory.Delete(folder, true);
        }
        Touch(Path.Combine(cache, "zm_x", "walk"), 700_000);
        Touch(Path.Combine(cache, "shader_cache", "a", "b"), 500_000);
        var clear = (Button)window.FindName("ClearCacheButton");
        var convert = (Button)window.FindName("ConvertButton");
        string[] order = [.. ((Panel)clear.Parent).Children.OfType<FrameworkElement>().Select(element => element.Name)];
        Console.WriteLine($"   footer: {string.Join(", ", order)}");
        Program.Check(order.SequenceEqual(["UpdateButton", "SettingsButton", "ClearCacheButton", "LogToggle", "SendButton", "CancelButton", "ConvertButton"])
            && clear.Visibility == Visibility.Visible && clear.IsEnabled, "Clear cache is in the footer again, between Settings and Log, beside Convert");
        window.AddPaths([Path.Combine(root, "handed", "usermaps", "zm_handed")]);
        bool ready = convert.IsEnabled;
        Call(window, "SetClearing", true);
        bool held = !convert.IsEnabled && !clear.IsEnabled;
        var clearing = (Task)Call(window, "ClearCache", real.CacheFolders(), 1_200_000L)!;
        CommunityChecks.Pump(() => clearing.IsCompleted, 20);
        Call(window, "SetClearing", false);
        Console.WriteLine($"   after clearing: {Status(window)}");
        Program.Check(ready && held && convert.IsEnabled && clear.IsEnabled && Status(window).StartsWith("Cache cleared |   1.1 MB freed") && Directory.GetDirectories(cache).Length == 0,
            "it empties every cache folder, says what was freed, and holds Convert while it works");
        var dialog = new SettingsWindow(null);
        Program.Check(dialog.FindName("ClearCacheButton") == null && dialog.FindName("CacheText") == null && (string)((CheckBox)dialog.FindName("DeleteCacheSwitch")).Content == "Delete the cache"
            && ((Button)dialog.FindName("CheckButton")).Visibility == Visibility.Collapsed, "Settings keeps only the switch that deletes the cache after converting");
        SettingsWindow bare = Program.Settled(new SettingsWindow(null, null, (CommunityPanel)window.FindName("Community")));
        Program.Check(((Grid)dialog.FindName("FilesRow")).Visibility == Visibility.Collapsed && ((Grid)bare.FindName("FilesRow")).Visibility == Visibility.Collapsed,
            "an edition whose releases carry no console files shows no Console files row in Settings");

        foreach (string name in (string[])["AsyncSwitch", "MultiConversionSwitch", "MultiCoreSwitch", "GpuVideoSwitch", "DeleteOriginalsSwitch", "DeleteCacheSwitch", "SendSwitch"])
        {
            var row = (CheckBox)dialog.FindName(name);
            Console.WriteLine($"   {row.Content}: {row.Tag}");
            if (((string)row.Tag).Length > 52)
                Program.Check(false, $"the description of {row.Content} is short enough for one line ({((string)row.Tag).Length} characters)");
        }
        Program.Check(true, "every Settings description is short enough to stay on one line");

        int asked = 0;
        bool found = false;
        AppUpdate.Summary = "Up to date (v2.10)";
        var checking = new SettingsWindow(null, () =>
        {
            asked++;
            AppUpdate.Summary = found ? "v2.20 is out" : "Up to date (v2.10)";
            return Task.FromResult(found);
        });
        var check = (Button)checking.FindName("CheckButton");
        var state = (TextBlock)checking.FindName("StateText");
        Program.Check(check.Visibility == Visibility.Visible && (string)check.Content == "Check for updates" && state.Visibility == Visibility.Visible && state.Text == "Up to date (v2.10)",
            "the packed app's Settings shows where it stands and offers to check for updates");
        MethodInfo click = typeof(SettingsWindow).GetMethod("CheckClick", Private)!;
        click.Invoke(checking, [check, new RoutedEventArgs()]);
        CommunityChecks.Pump(() => check.IsEnabled, 5);
        bool same = asked == 1 && (string)check.Content == "Check for updates" && state.Text == "Up to date (v2.10)";
        found = true;
        click.Invoke(checking, [check, new RoutedEventArgs()]);
        CommunityChecks.Pump(() => check.IsEnabled, 5);
        Program.Check(same && asked == 2 && (string)check.Content == "Get the update" && state.Text == "v2.20 is out", "Check for updates asks again on the spot, and turns into Get the update when there is one");
        var body = (UIElement)checking.Content;
        checking.Content = null;
        Program.Render(Program.Host(checking, body, Program.SettingsWidth, double.NaN), Path.Combine(scratch, "settings_update.png"));

        using var hub = new FakeGitHub();
        DateTimeOffset when = new(2026, 10, 1, 5, 0, 0, TimeSpan.Zero);
        byte[] mine = CommunityChecks.Noise(300_000, "MZ"), theirs = CommunityChecks.Noise(320_000, "MZ");
        string exe = Path.Combine(root, "summary", "PS4 .FF Porter.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllBytes(exe, mine);
        hub.Releases = UpdateChecks.Json(UpdateChecks.Release("v2.10", when, false, false, UpdateChecks.Asset("PS4.FF.Porter.exe", theirs, $"{hub.Address}/download/v2.10/PS4.FF.Porter.exe", when)));
        Edition.Use(new ListEdition(real, null, hub.List.ToString()));
        typeof(MainWindow).GetField("_exe", Private)!.SetValue(window, exe);
        (UpdateFile? File, string Summary) Find()
        {
            var finding = (Task<UpdateFile?>)Call(window, "FindUpdate")!;
            CommunityChecks.Pump(() => finding.IsCompleted, 20);
            return (finding.Result, AppUpdate.Summary);
        }
        var out1 = Find();
        File.WriteAllBytes(exe, theirs);
        var out2 = Find();
        hub.Releases = "[]";
        var out3 = Find();
        hub.ListStatus = 403;
        var out4 = Find();
        hub.ListStatus = 0;
        Console.WriteLine($"   summaries: '{out1.Summary}' | '{out2.Summary}' | '{out3.Summary}' | '{out4.Summary}'");
        Program.Check(out1.File?.Release == "v2.10" && out1.Summary == "v2.10 is out" && out2.File == null && out2.Summary == "Up to date (v2.10)" && out3.File == null
            && out3.Summary == "No release to compare with" && out4.File == null && out4.Summary == "Could not check for updates", "the update check sums itself up in a few words, whatever it finds");
        Edition.Use(new ListEdition(real, null));
        AppUpdate.Summary = "";
    }

    private static void Boxes(string scratch)
    {
        Console.WriteLine("== text boxes");
        var starts = new List<string>();
        bool aligned = true;
        void Measure(string name, Border host, TextBox box, TextBlock hint, string typed)
        {
            box.Text = typed;
            Program.Render(host, Path.Combine(scratch, $"box_{name}.png"));
            double text = box.GetRectFromCharacterIndex(0).X, placeholder = hint.Margin.Left;
            starts.Add($"{name}: text at {text:0.#}, placeholder at {placeholder:0.#}, padding {box.Padding.Left:0.#}");
            aligned &= Math.Abs(text - placeholder) < 0.5 && text - box.Padding.Left <= 4 && hint.Visibility == Visibility.Collapsed;
            box.Text = "";
            aligned &= hint.Visibility == Visibility.Visible;
        }

        var (window, host, _) = Open();
        ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;
        var community = (CommunityPanel)window.FindName("Community");
        Measure("community", host, (TextBox)community.FindName("SearchBox"), (TextBlock)community.FindName("SearchHint"), "buried");
        ((RadioButton)window.FindName("InstalledTab")).IsChecked = true;
        var console = (ConsolePanel)window.FindName("OnConsole");
        Measure("installed", host, (TextBox)console.FindName("SearchBox"), (TextBlock)console.FindName("SearchHint"), "usb1");
        ((RadioButton)window.FindName("ConvertTab")).IsChecked = true;

        var picker = new InstalledWindow(null, []);
        var list = (UIElement)picker.Content;
        picker.Content = null;
        Measure("picker", Program.Host(picker, list, 684, 400), (TextBox)picker.FindName("SearchBox"), (TextBlock)picker.FindName("SearchHint"), "jungle");

        var dialog = new SettingsWindow(null);
        var page = (UIElement)dialog.Content;
        dialog.Content = null;
        Measure("settings", Program.Host(dialog, page, Program.SettingsWidth, double.NaN), (TextBox)dialog.FindName("ConsoleBox"), (TextBlock)dialog.FindName("ConsoleHint"), "10.0.0.83");
        Set("_consoleAddress", null);
        Set("_sendToConsole", false);

        foreach (string line in starts)
            Console.WriteLine("   " + line);
        Program.Check(aligned, "in every text box what is typed starts where the placeholder was, right after the padding and not further in");
    }

    private static void Storage(string scratch, string root)
    {
        Console.WriteLine("== which drives can be used");
        const string trial = "ffporter-write-test.tmp";
        AppSettings settings = AppSettings.Current;
        using var console = new MiniFtp(Path.Combine(root, "drives"));
        Directory.CreateDirectory(console.Local("/mnt/ext1/PS4"));
        Directory.CreateDirectory(console.Local("/mnt/usb2/PS4"));
        console.ReadOnly["/mnt/usb2/"] = true;
        Set("_consoleAddress", $"127.0.0.1:{console.Port}");
        Set("_consoleStorage", "usb2");
        Set("_sendToConsole", true);

        SettingsWindow dialog = Program.Settled(new SettingsWindow(null));
        var storage = (ComboBox)dialog.FindName("StorageBox");
        var path = (TextBlock)dialog.FindName("StorageText");
        var result = (TextBlock)dialog.FindName("ConsoleResult");
        ComboBoxItem[] items = [.. storage.Items.Cast<ComboBoxItem>()];
        string[] usable = [.. items.Where(item => item.IsEnabled).Select(item => (string)item.Content)];
        string[] listed = [.. items.Where(item => item.Visibility == Visibility.Visible).Select(item => (string)item.Content)];
        Console.WriteLine($"   on opening Settings: {result.Text}; listed: {string.Join(", ", listed)}");
        Program.Check(items.Length == 17 && (string)items[0].Content == "Console storage" && (string)items[1].Content == "USB 0" && (string)items[8].Content == "USB 7"
            && (string)items[9].Content == "Extended 0" && (string)items[16].Content == "Extended 7",
            "Settings offers where maps and mods go: the console's own storage, USB 0-7 or extended storage 0-7");
        Program.Check(usable.SequenceEqual(["Console storage", "Extended 1"]) && listed.SequenceEqual(usable) && console.Commands.Count(line => line.StartsWith("STOR ")) == 2
            && console.Commands.Contains($"DELE /mnt/ext1/{trial}") && !Directory.EnumerateFiles(console.Local("/mnt"), trial, SearchOption.AllDirectories).Any(),
            "opening Settings writes a test file to every drive that shows up and deletes it again; only the ones that take it are listed under Send to");
        Program.Check(settings.ConsoleStorage == null && (string)((ComboBoxItem)storage.SelectedItem).Content == "Console storage" && path.Text == "/data/BO3-Customs"
            && result.Text == "USB 2 is not usable. Back on console storage." && settings.SendToConsole,
            "a chosen drive that cannot be written to any more is dropped for console storage, with a warning");
        storage.SelectedItem = items[10];
        string saved = File.ReadAllText(Path.Combine(AppSettings.Folder, Edition.Current.SettingsFile));
        Program.Check(settings.ConsoleStorage == "ext1" && path.Text == "/mnt/ext1/BO3-Customs" && saved.Contains("\"ConsoleStorage\": \"ext1\""), "a usable drive can be chosen: the folder shows and the choice is saved");
        typeof(SettingsWindow).GetMethod("ConsoleTestClick", Private)!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        Program.Settled(dialog);
        Console.WriteLine($"   Test: {result.Text}");
        Program.Check(result.Text == "Connected. No BO3-Customs there yet. Drives: Extended 1." && !Directory.Exists(console.Local("/data")) && ((Button)dialog.FindName("ConsoleTest")).IsEnabled
            && storage.IsEnabled, "Test does the same on request and says which drives can be used");
        var page = (UIElement)dialog.Content;
        dialog.Content = null;
        Border shown = Program.Host(dialog, page, Program.SettingsWidth, double.NaN);
        Program.Render(shown, Path.Combine(scratch, "settings_storage.png"));
        List<string> spilled = Spilled(shown);
        Program.Check(spilled.Count == 0, $"at the window's real width every line of Settings fits: nothing wraps or is cut short{(spilled.Count > 0 ? ": " + string.Join(" | ", spilled) : "")}");

        var (window, _, _) = Open();
        string? Lost()
        {
            var looking = (Task<string?>)Call(window, "LostDrive")!;
            CommunityChecks.Pump(() => looking.IsCompleted, 30);
            return looking.Result;
        }
        console.Forget();
        string? kept = Lost();
        Program.Check(kept == null && settings.ConsoleStorage == "ext1" && console.Commands.Count(line => line.StartsWith("STOR ")) == 1 && !File.Exists(console.Local($"/mnt/ext1/{trial}")),
            "when the app starts, the chosen drive gets a test file; one that takes it is kept");
        Set("_consoleStorage", "usb2");
        string? lost = Lost();
        Console.WriteLine($"   at start with a drive that is gone: {lost}; now sending to {settings.ConsoleRoot}");
        Program.Check(lost == "usb2" && settings.ConsoleStorage == null && settings.ConsoleRoot == "/data/BO3-Customs" && settings.SendToConsole && Noted(window).Contains("USB 2 could not be written to"),
            "one that does not is dropped for console storage, and the window is told so it can warn and ask about Auto-FTP");
        console.Forget();
        string? none = Lost();
        Program.Check(none == null && console.Commands.Length == 0, "with console storage chosen nothing is tested at start");
        using (var off = new TcpListenerProbe())
        {
            Set("_consoleAddress", $"127.0.0.1:{off.Port}");
            Set("_consoleStorage", "usb2");
            string? unknown = Lost();
            SettingsWindow away = Program.Settled(new SettingsWindow(null));
            string said = ((TextBlock)away.FindName("ConsoleResult")).Text;
            Console.WriteLine($"   console off: {said}");
            var locked = (ComboBox)away.FindName("StorageBox");
            Program.Check(unknown == null && settings.ConsoleStorage == "usb2" && said.StartsWith("The console at 127.0.0.1") && said.EndsWith("did not answer.")
                && !locked.IsEnabled && (string)((ComboBoxItem)locked.SelectedItem).Content == "USB 2" && (string)locked.ToolTip == "Locked until the console answers. Test tries again."
                && ((Button)away.FindName("ConsoleTest")).IsEnabled,
                "with the console off nothing can be told: the choice is kept, and Send to is locked until the console answers");
            var again = (Task)typeof(SettingsWindow).GetMethod("RetryAsync", Private)!.Invoke(away, null)!;
            CommunityChecks.Pump(() => again.IsCompleted, 30);
            bool still = !locked.IsEnabled && settings.ConsoleStorage == "usb2";
            Set("_consoleAddress", $"127.0.0.1:{console.Port}");
            console.Forget();
            again = (Task)typeof(SettingsWindow).GetMethod("RetryAsync", Private)!.Invoke(away, null)!;
            CommunityChecks.Pump(() => again.IsCompleted, 30);
            Program.Settled(away);
            string back = ((TextBlock)away.FindName("ConsoleResult")).Text;
            Console.WriteLine($"   console back: {back}; Send to {(locked.IsEnabled ? "unlocked" : "locked")}, can be chosen: {string.Join(", ", locked.Items.Cast<ComboBoxItem>().Where(item => item.IsEnabled).Select(item => (string)item.Content))}");
            Program.Check(still && locked.IsEnabled && back == "USB 2 is not usable. Back on console storage." && settings.ConsoleStorage == null
                && locked.Items.Cast<ComboBoxItem>().Where(item => item.IsEnabled).Select(item => (string)item.Content).SequenceEqual(["Console storage", "Extended 1"])
                && !((string)locked.ToolTip).StartsWith("Locked"),
                "it keeps trying while Settings is open: once the console answers the drives are tested, Send to unlocks, and only the usable ones can be picked");
            var typed = (TextBox)away.FindName("ConsoleBox");
            typed.Text = "10.9.9.9";
            Program.Check(!locked.IsEnabled && settings.ConsoleAddress == "10.9.9.9", "typing another address locks Send to again until that console has answered");
        }
        Set("_consoleAddress", null);
        Set("_consoleStorage", null);
        Set("_sendToConsole", false);
        SettingsWindow unset = Program.Settled(new SettingsWindow(null));
        Program.Check(!((ComboBox)unset.FindName("StorageBox")).IsEnabled && !((CheckBox)unset.FindName("SendSwitch")).IsEnabled, "without a console address there is nothing to choose or switch on");
    }

    private static void Sending(string scratch, string root)
    {
        Console.WriteLine("== sending conversions to the console");
        string source = Path.Combine(root, "pc"), output = Path.Combine(root, "out");
        Set("_outputOverride", output);
        Set("_consoleAddress", null);
        Set("_deleteOriginals", false);
        foreach (string map in (string[])["zm_one", "zm_two"])
        {
            Touch(Path.Combine(source, "usermaps", map, "zone", map + ".ff"));
            Touch(Path.Combine(source, "usermaps", map, "zone", map + ".xpak"));
        }
        Touch(Path.Combine(source, "usermaps", "zm_mycommon", "zone", "zm_mycommon.ff"));
        foreach (string zone in (string[])["core_mod.ff", "zm_mod.ff"])
            Touch(Path.Combine(source, "mods", "777", zone));
        File.WriteAllText(Path.Combine(source, "mods", "777", "workshop.json"), "{\"FolderName\":\"coolmod\"}");

        var dice = new Random(5);
        Dictionary<string, byte[]> Converted(string folder, params string[] names)
        {
            var files = new Dictionary<string, byte[]>();
            foreach (string name in names)
            {
                var data = new byte[name.EndsWith(".xpak") ? 2_400_000 : 300_000];
                dice.NextBytes(data);
                files[name] = data;
                string path = Path.Combine(output, "t7", folder, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
            }
            return files;
        }
        Dictionary<string, byte[]> one = Converted("zm_one", "zm_one.ff", "en_zm_one.ff", "zm_one.xpak", "snd/all/zm_one.all.sabl", "workshop.json");
        Dictionary<string, byte[]> two = Converted("zm_two", "zm_two.ff", "zm_two.xpak", "workshop.json");
        Dictionary<string, byte[]> cool = Converted(Path.Combine("mods", "coolmod"), "core_mod.ff", "zm_mod.ff", "zm_mod.xpak");
        Converted("zm_mycommon", "zm_mycommon.ff");
        File.WriteAllText(Path.Combine(output, "t7", "zm_one", "zm_one.fidelity.json"), "{}");
        File.WriteAllText(Path.Combine(output, "t7", "zm_one", "convert.log"), "x");

        var (window, host, jobs) = Open();
        window.AddPaths([Path.Combine(source, "usermaps"), Path.Combine(source, "mods")]);
        Job Named(string name) => jobs.First(job => job.Name == name);
        var send = (Button)window.FindName("SendButton");
        var label = (TextBlock)window.FindName("SendText");
        var list = (ListBox)window.FindName("JobList");
        var again = (MenuItem)window.FindName("JobSendItem");
        void Refresh() => Call(window, "RefreshQueue");
        void Done(params Job[] which)
        {
            foreach (Job job in which)
            {
                job.State = RunStates.Done;
                job.StatusText = "Converted";
            }
        }
        bool Running() => (bool)typeof(MainWindow).GetField("_running", Private)!.GetValue(window)!;
        bool On(MiniFtp where, string folder, Dictionary<string, byte[]> files) =>
            files.All(pair => File.Exists(where.Local($"{folder}/{pair.Key}")) && File.ReadAllBytes(where.Local($"{folder}/{pair.Key}")).AsSpan().SequenceEqual(pair.Value));

        Console.WriteLine("   queued: " + string.Join(", ", jobs.Select(job => $"{job.Title} ({job.Kind})")));
        Program.Check(jobs.Count == 5 && Named("zm_mycommon").Kind == "Zone" && Named("core_mod").Mod == "coolmod", "two maps, a zone and a mod of two zones are queued");
        Done([.. jobs]);
        Refresh();
        Program.Check(send.Visibility == Visibility.Collapsed, "with no console saved, the footer offers nothing to send");
        list.SelectedItem = Named("zm_one");
        Call(window, "JobMenuOpening", list, null);
        bool offered = again.IsEnabled && (string)again.Header == "Send to the console";
        Call(window, "JobSendClick", again, new RoutedEventArgs());
        Program.Check(offered && Status(window).StartsWith("No console is set") && !Running(), "a converted row's menu still offers it, and says where the console's address goes");

        using var console = new MiniFtp(Path.Combine(root, "ps4"));
        Set("_consoleAddress", $"127.0.0.1:{console.Port}");
        list.SelectedItems.Clear();
        Refresh();
        Console.WriteLine($"   footer button: '{label.Text}' ({send.Visibility}); {send.ToolTip}");
        Program.Check(send.Visibility == Visibility.Visible && label.Text == "Send 3 to console" && ((string)send.ToolTip).Contains("Send coolmod, zm_one, zm_two to /data/BO3-Customs on the console at 127.0.0.1") && !((string)send.ToolTip).Contains("zm_mycommon"),
            "with a console saved and things converted, the footer offers to send them: maps and mods, not loose zones");
        Program.Render(host, Path.Combine(scratch, "shell_send.png"));
        Named("zm_mod").State = RunStates.Ready;
        Named("zm_mod").StatusText = "Ready";
        Refresh();
        bool partMod = label.Text == "Send 2 to console";
        Named("zm_mod").StatusText = "Converted earlier";
        Refresh();
        bool earlier = label.Text == "Send 3 to console";
        Named("zm_two").State = RunStates.Failed;
        Refresh();
        bool failed = label.Text == "Send 2 to console";
        Done(Named("zm_two"), Named("zm_mod"));
        list.SelectedItems.Add(Named("zm_one"));
        list.SelectedItems.Add(Named("zm_two"));
        bool narrowed = label.Text == "Send 2 to console" && ((string)send.ToolTip).Contains("Send zm_one, zm_two to /data/BO3-Customs");
        list.SelectedItems.Clear();
        list.SelectedItem = Named("zm_two");
        Program.Check(partMod && earlier && failed && narrowed && label.Text == "Send 3 to console",
            "only what converted is offered: a mod waits for all of its zones, something converted earlier counts, and choosing several rows narrows it to those");

        console.Forget();
        Call(window, "SendClick", send, new RoutedEventArgs());
        bool busy = Running() && ((Button)window.FindName("CancelButton")).Visibility == Visibility.Visible && send.Visibility == Visibility.Collapsed && !((Button)window.FindName("AddButton")).IsEnabled;
        CommunityChecks.Pump(() => !Running(), 90);
        Console.WriteLine($"   after sending: {Status(window)}; rows: {string.Join(", ", jobs.Select(job => $"{job.Name}='{job.Note}'"))}");
        Program.Check(busy && On(console, "/data/BO3-Customs/usermaps/zm_one", one) && On(console, "/data/BO3-Customs/usermaps/zm_two", two) && On(console, "/data/BO3-Customs/mods/coolmod", cool),
            "Send to console copies each map into usermaps and the mod into mods, byte for byte");
        Program.Check(!File.Exists(console.Local("/data/BO3-Customs/usermaps/zm_one/zm_one.fidelity.json")) && !File.Exists(console.Local("/data/BO3-Customs/usermaps/zm_one/convert.log"))
            && !Directory.Exists(console.Local("/data/BO3-Customs/usermaps/zm_mycommon")) && console.Commands.Count(line => line.StartsWith("STOR ")) == one.Count + two.Count + cool.Count
            && console.Commands.Last(line => line.StartsWith("RNTO ") && line.Contains("/zm_one/")) == "RNTO /data/BO3-Customs/usermaps/zm_one/zm_one.ff",
            "reports and logs stay on the PC, the loose zone is not sent, and each map's own zone goes in last");
        Program.Check(jobs.Where(job => job.Kind != "Zone").All(job => job is { OnConsole: true, Note: "On the console" }) && !Named("zm_mycommon").OnConsole && send.Visibility == Visibility.Collapsed
            && Status(window).StartsWith("Sent to the console |   all 3") && Noted(window).Contains("zm_one is on the console in /data/BO3-Customs/usermaps/zm_one")
            && ((Button)window.FindName("ConvertButton")).Visibility == Visibility.Visible, "afterwards the rows say they are on the console and the button goes away");
        Program.Render(host, Path.Combine(scratch, "shell_sent.png"));

        console.Forget();
        list.SelectedItems.Clear();
        list.SelectedItem = Named("zm_two");
        Call(window, "JobMenuOpening", list, null);
        bool repeat = again.IsEnabled && (string)again.Header == "Send to the console again";
        Call(window, "JobSendClick", again, new RoutedEventArgs());
        CommunityChecks.Pump(() => !Running(), 60);
        Program.Check(repeat && console.Commands.Count(line => line.StartsWith("STOR ")) == two.Count && console.Commands.Where(line => line.StartsWith("STOR ")).All(line => line.Contains("/zm_two/"))
            && Status(window).StartsWith("Sent to the console |   zm_two"), "a row's menu sends just that one again");

        foreach (Job job in jobs)
            job.OnConsole = false;
        Dictionary<string, byte[]> fresh = Converted("zm_one", "zm_one.ff", "en_zm_one.ff", "zm_one.xpak", "snd/all/zm_one.all.sabl", "workshop.json");
        list.SelectedItems.Clear();
        list.SelectedItems.Add(Named("zm_one"));
        list.SelectedItems.Add(Named("zm_two"));
        Refresh();
        console.Forget();
        console.Slow = "/data/BO3-Customs/usermaps/zm_one/zm_one.xpak.part";
        Call(window, "SendClick", send, new RoutedEventArgs());
        CommunityChecks.Pump(() => console.Commands.Contains("STOR /data/BO3-Customs/usermaps/zm_one/zm_one.xpak.part"), 30);
        Call(window, "CancelClick", send, new RoutedEventArgs());
        string stopping = Status(window);
        CommunityChecks.Pump(() => !Running(), 60);
        console.Slow = null;
        string part = console.Local("/data/BO3-Customs/usermaps/zm_one/zm_one.xpak.part");
        Console.WriteLine($"   cancelled: '{stopping}' then '{Status(window)}'");
        Program.Check(stopping.StartsWith("Stopping once this file is sent") && Status(window).StartsWith("Stopped sending") && File.Exists(part) && new FileInfo(part).Length == fresh["zm_one.xpak"].Length
            && On(console, "/data/BO3-Customs/usermaps/zm_one", one) && !console.Commands.Any(line => line.StartsWith("RNTO ") || line.Contains("/zm_two/"))
            && jobs.All(job => !job.OnConsole) && label.Text == "Send 2 to console" && send.Visibility == Visibility.Visible,
            "Cancel lets the file in flight arrive whole, then stops: what was on the console is untouched and nothing is marked as sent");
        Call(window, "SendClick", send, new RoutedEventArgs());
        CommunityChecks.Pump(() => !Running(), 90);
        Program.Check(On(console, "/data/BO3-Customs/usermaps/zm_one", fresh) && Named("zm_one").OnConsole && Named("zm_two").OnConsole && !Named("core_mod").OnConsole
            && !Directory.EnumerateFiles(console.Local("/data/BO3-Customs/usermaps/zm_one"), "*.part", SearchOption.AllDirectories).Any(), "sending again finishes the job with the new files, and only for the rows that were chosen");

        Named("zm_one").OnConsole = false;
        Directory.Delete(Path.Combine(output, "t7", "zm_one"), true);
        Refresh();
        Call(window, "SendClick", send, new RoutedEventArgs());
        CommunityChecks.Pump(() => !Running(), 30);
        Console.WriteLine($"   output folder gone: {Status(window)}");
        Program.Check(Status(window).StartsWith("Not sent to the console |   zm_one: its converted files are no longer in") && !Named("zm_one").OnConsole && ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("LogToggle")).IsChecked == true,
            "if the converted files are gone from the output folder, it says so instead of sending nothing");

        Converted("zm_one", "zm_one.ff");
        using (var off = new TcpListenerProbe())
        {
            Set("_consoleAddress", $"127.0.0.1:{off.Port}");
            Refresh();
            Call(window, "SendClick", send, new RoutedEventArgs());
            CommunityChecks.Pump(() => !Running(), 60);
            Console.WriteLine($"   console off: {Status(window)}");
            Program.Check(Status(window).StartsWith("Not sent to the console |   zm_one: the console at 127.0.0.1") && !Named("zm_one").OnConsole && send.Visibility == Visibility.Visible,
                "if the console does not answer, it says so and the button stays for another try");
        }

        Set("_consoleAddress", $"127.0.0.1:{console.Port}");
        Refresh();
        Set("_consoleStorage", "usb0");
        Directory.CreateDirectory(console.Local("/mnt/usb0/PS4"));
        list.SelectedItems.Clear();
        list.SelectedItem = Named("zm_two");
        Call(window, "JobSendClick", again, new RoutedEventArgs());
        CommunityChecks.Pump(() => !Running(), 60);
        bool onDrive = On(console, "/mnt/usb0/BO3-Customs/usermaps/zm_two", two) && Status(window).StartsWith("Sent to the console |   zm_two");
        Set("_consoleStorage", "ext3");
        Call(window, "JobSendClick", again, new RoutedEventArgs());
        CommunityChecks.Pump(() => !Running(), 60);
        Console.WriteLine($"   to a drive: on USB 0 {onDrive}; to one that is not there: {Status(window)}");
        Program.Check(onDrive && Status(window).StartsWith("Not sent to the console |   zm_two: Extended 3 is not usable") && !Directory.Exists(console.Local("/mnt/ext3")),
            "with a drive chosen under Send to, conversions go to that drive's BO3-Customs folder, and a drive that is not plugged in is refused");
        Set("_consoleStorage", null);
        list.SelectedItems.Clear();
        list.SelectedItems.Add(Named("zm_one"));
        list.SelectedItems.Add(Named("zm_two"));
        Refresh();
        Dictionary<string, byte[]> rebuilt = Converted("zm_two", "zm_two.ff", "zm_two.xpak", "workshop.json");
        foreach (Job job in jobs)
            job.OnConsole = false;
        Named("zm_one").State = RunStates.Failed;
        Named("zm_mod").State = RunStates.Cancelled;
        Task Finished() => (Task)Call(window, "SendFinished", (IReadOnlyList<Job>)[.. jobs])!;
        console.Forget();
        Task switchedOff = Finished();
        CommunityChecks.Pump(() => switchedOff.IsCompleted, 30);
        bool untouched = console.Commands.Length == 0 && !Status(window).Contains("when converted");
        Set("_sendToConsole", true);
        Refresh();
        string idle = Status(window);
        typeof(MainWindow).GetField("_stop", Private)!.SetValue(window, true);
        Task cancelled = Finished();
        CommunityChecks.Pump(() => cancelled.IsCompleted, 30);
        untouched &= console.Commands.Length == 0;
        typeof(MainWindow).GetField("_stop", Private)!.SetValue(window, false);
        Program.Asked.Clear();
        Task auto = Finished();
        CommunityChecks.Pump(() => auto.IsCompleted, 90);
        Console.WriteLine($"   Auto-FTP: idle '{idle}'; after a run: {Status(window)}");
        Program.Check(untouched && idle.Contains("sent to the console when converted"), "with Auto-FTP off, or after a cancelled run, nothing is sent; with it on the footer says conversions will be sent");
        Program.Check(auto.IsCompletedSuccessfully && On(console, "/data/BO3-Customs/usermaps/zm_two", rebuilt) && Named("zm_two").OnConsole && Status(window).StartsWith("Converted and sent to the console |   zm_two")
            && console.Commands.Where(line => line.StartsWith("STOR ")).All(line => line.Contains("/zm_two/")) && !Named("zm_one").OnConsole && !Named("core_mod").OnConsole,
            "with Auto-FTP on, what a run converted goes to the console by itself: not what failed, and not a mod with a zone that did not convert");
        Program.Check(rebuilt.All(pair => File.Exists(Path.Combine(output, "t7", "zm_two", pair.Key))), "a conversion that was sent stays on the PC");
        Console.WriteLine($"   asked before replacing: {string.Join(" || ", Program.Asked)}");
        Program.Check(Program.Asked.SequenceEqual(["Overwrite zm_two on the console? | It is already there and gets replaced."]),
            "with Auto-FTP on, a conversion that is already on the console is only sent after asking whether to overwrite it");

        Program.Asked.Clear();
        Program.Agree = false;
        Dictionary<string, byte[]> newer = Converted("zm_two", "zm_two.ff", "zm_two.xpak", "workshop.json");
        Dictionary<string, byte[]> again1 = Converted("zm_one", "zm_one.ff", "en_zm_one.ff", "zm_one.xpak");
        Directory.Delete(console.Local("/data/BO3-Customs/usermaps/zm_one"), true);
        Done(Named("zm_one"), Named("zm_two"), Named("zm_mod"), Named("core_mod"));
        foreach (Job job in jobs)
            job.OnConsole = false;
        list.SelectedItems.Clear();
        Refresh();
        console.Forget();
        Task kept = Finished();
        CommunityChecks.Pump(() => kept.IsCompleted, 90);
        Program.Agree = true;
        Console.WriteLine($"   skipped: {string.Join(" || ", Program.Asked)}; then {Status(window)}; button '{label.Text}'");
        Program.Check(Program.Asked.SequenceEqual(["Overwrite 2 on the console? | coolmod and zm_two are already there."])
            && On(console, "/data/BO3-Customs/usermaps/zm_one", again1) && Named("zm_one").OnConsole && Status(window).StartsWith("Converted and sent to the console |   zm_one")
            && On(console, "/data/BO3-Customs/usermaps/zm_two", rebuilt) && !On(console, "/data/BO3-Customs/usermaps/zm_two", newer) && !Named("zm_two").OnConsole && !Named("core_mod").OnConsole
            && console.Commands.Where(line => line.StartsWith("STOR ")).All(line => line.Contains("/zm_one/")) && Noted(window).Contains("Not sent, already on the console: coolmod, zm_two")
            && send.Visibility == Visibility.Visible && label.Text == "Send 2 to console",
            "what is new goes up without a question; what is already there is asked about once, and Skip leaves the console's copies alone and the Send button in place");
        Program.Asked.Clear();
        Set("_sendToConsole", false);
        Done(Named("zm_one"), Named("zm_mod"));
        foreach (Job job in jobs)
            job.OnConsole = false;
        Refresh();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        console.Forget();
        console.Slow = "/data/BO3-Customs/usermaps/zm_one/zm_one.ff.part";
        Call(window, "SendClick", send, new RoutedEventArgs());
        CommunityChecks.Pump(() => console.Commands.Contains("STOR /data/BO3-Customs/usermaps/zm_one/zm_one.ff.part"), 30);
        window.Close();
        bool held = !closed && Status(window).Contains("the app closes as soon as it is done");
        CommunityChecks.Pump(() => closed, 60);
        console.Slow = null;
        Program.Check(held && closed && console.Commands.Contains("QUIT"), "closing the window during a send lets the file in flight finish first, then closes by itself");
    }
}
