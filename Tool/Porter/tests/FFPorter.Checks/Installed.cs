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

internal static class InstalledChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Data = "/data/BO3-Customs", Usb = "/mnt/usb1/BO3-Customs", Ext = "/mnt/ext0/BO3-Customs";

    public static void Run(string scratch)
    {
        Console.WriteLine("== the Installed tab");
        string root = Path.Combine(scratch, "installed");
        var settings = AppSettings.Current;
        void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(settings, value);
        using var console = new MiniFtp(Path.Combine(root, "ps4"));
        void Put(string path, long stated = 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(console.Local(path))!);
            File.WriteAllBytes(console.Local(path), CommunityChecks.Noise(1000));
            if (stated > 0)
                console.Sizes[path] = stated;
        }
        Put($"{Data}/usermaps/zm_alpha/zm_alpha.ff", 120_000_000);
        Put($"{Data}/usermaps/zm_alpha/en_zm_alpha.ff", 2_000_000);
        Put($"{Data}/usermaps/zm_alpha/zm_alpha.xpak", 800_000_000);
        Put($"{Data}/usermaps/zm_alpha/snd/all/zm_alpha.all.sabl", 50_000_000);
        Put($"{Data}/usermaps/zm_alpha/previewimage.png");
        Put($"{Data}/mods/coolmod/zm_mod.ff", 30_000_000);
        Put($"{Data}/mods/coolmod/core_mod.ff", 5_000_000);
        Put($"{Data}/zone/zm_levelcommon.ff", 600_000_000);
        Put($"{Data}/BO3-Customs.sprx");
        Put($"{Usb}/usermaps/zm_beta/zm_beta.ff");
        Put($"{Usb}/usermaps/zm_beta/zm_beta.xpak", 3_000_000_000);
        Put($"{Usb}/usermaps/mp_gamma/zone/mp_gamma.ff", 400_000_000);
        Put($"{Usb}/usermaps/nuketown/nuketown.ff", 90_000_000);
        Put($"{Usb}/usermaps/zm_broken/zm_other.ff", 70_000_000);
        Put($"{Usb}/usermaps/zm_partial/zm_partial.ff.part", 10_000_000);
        Put($"{Usb}/usermaps/zm_partial/zm_partial.xpak.part", 20_000_000);
        Directory.CreateDirectory(console.Local($"{Usb}/usermaps/zm_empty"));
        Put($"{Usb}/usermaps/readme.txt");
        Put($"{Ext}/usermaps/cp_delta/cp_delta.ff", 250_000_000);
        Put($"{Ext}/mods/bigmod/zone/zm_mod.ff", 60_000_000);
        Directory.CreateDirectory(console.Local("/mnt/usb3/PS4"));
        Directory.CreateDirectory(console.Local("/mnt/usb5/BO3-Customs/usermaps"));
        long alpha = 120_000_000 + 2_000_000 + 800_000_000 + 50_000_000 + 1000, total = alpha + 35_000_000 + 3_000_001_000 + 400_000_000 + 90_000_000 + 70_000_000 + 30_000_000 + 250_000_000 + 60_000_000;

        var entries = new List<ConsoleEntry>();
        var sizes = new List<ConsoleSize>();
        Task scan = Task.Run(() => ConsoleFtp.InstalledAsync("127.0.0.1", console.Port, new Progress<IReadOnlyList<ConsoleEntry>>(entries.AddRange), new Progress<ConsoleSize>(sizes.Add),
            CancellationToken.None));
        CommunityChecks.Pump(() => scan.IsCompleted && sizes.Count == 10, 30);
        Console.WriteLine("   found: " + string.Join(", ", entries.Select(entry => $"{(entry.IsMod ? "mod " : "")}{entry.Name}@{entry.Storage ?? "console"}")));
        ConsoleSize? measured = sizes.FirstOrDefault(size => size.Entry.Name == "zm_alpha");
        Program.Check(scan.IsCompletedSuccessfully && entries.Count == 10 && entries.Count(entry => entry.IsMod) == 2
            && entries.Select(entry => entry.Storage ?? "console").Distinct().SequenceEqual(["console", "usb1", "ext0"])
            && entries.Any(entry => entry is { Name: "bigmod", IsMod: true, Storage: "ext0", Folder: "/mnt/ext0/BO3-Customs/mods/bigmod" })
            && !entries.Any(entry => entry.Name is "readme.txt" or "zm_levelcommon" or "zone"),
            "reading the console finds every map and mod folder on its own storage and on each drive that has any, and nothing else");
        Program.Check(measured is { Bytes: var bytes } && bytes == alpha && measured.Files.Count == 5 && measured.Files.Any(file => file is { Path: "snd/all/zm_alpha.all.sabl", Size: 50_000_000 })
            && sizes.Sum(size => size.Bytes) == total && sizes.First(size => size.Entry.Name == "zm_empty") is { Bytes: 0, Files.Count: 0 },
            "each one is measured through all its folders: its size, its files and their names");
        Program.Check(!console.Commands.Any(line => line.StartsWith("STOR ") || line.StartsWith("DELE ") || line.StartsWith("RMD ") || line.StartsWith("MKD ") || line.StartsWith("RNFR ")),
            "reading writes nothing to the console");

        object? outputBefore = typeof(AppSettings).GetField("_outputOverride", Private)!.GetValue(settings);
        Edition real = Edition.Current;
        Edition.Use(new ListEdition(real, null));
        Set("_outputOverride", Path.Combine(root, "out"));
        Set("_consoleAddress", $"127.0.0.1:{console.Port}");
        Set("_consoleStorage", "usb1");
        Set("_sendToConsole", false);
        try
        {
            console.Forget();
            var window = new MainWindow();
            var content = (UIElement)window.Content;
            window.Content = null;
            Border host = Program.Host(window, content, 1364, 841);
            var panel = (ConsolePanel)window.FindName("OnConsole");
            var tab = (RadioButton)window.FindName("InstalledTab");
            var items = CommunityChecks.Field<ObservableCollection<ConsoleItem>>(panel, "_items");
            var jobs = (ObservableCollection<Job>)typeof(MainWindow).GetField("_jobs", Private)!.GetValue(window)!;
            var sent = new Job { Kind = "Map", Name = "zm_beta", MainFile = @"D:\maps\zm_beta\zm_beta.ff", OnConsole = true };
            jobs.Add(sent);
            string Footer() => ((Run)window.FindName("StatusText")).Text + ((Run)window.FindName("DetailText")).Text;
            bool Scanning() => CommunityChecks.Field<bool>(panel, "_scanning");
            string Count(string name) => ((Run)panel.FindName(name)).Text.Trim();
            string[] Shown() => [.. ((ItemsControl)panel.FindName("ItemList")).Items.Cast<ConsoleItem>().Select(item => item.Name)];
            ConsoleItem Item(string name) => items.First(item => item.Name == name);
            string[] tabs = [.. ((Panel)tab.Parent).Children.OfType<RadioButton>().Select(button => (string)button.Content)];

            Program.Check(tabs.SequenceEqual(["Convert", "Converted", "Community", "Installed"]) && panel.Visibility == Visibility.Collapsed && items.Count == 0 && console.Commands.Length == 0,
                "the tabs are Convert, Converted, Community and Installed, and nothing is read from the console until Installed is opened");
            tab.IsChecked = true;
            string reading = Footer();
            CommunityChecks.Pump(() => !Scanning() && items.Count == 10 && items.All(item => item.IsMeasured), 30);
            Console.WriteLine($"   footer: '{reading}' then '{Footer()}'");
            Console.WriteLine("   rows: " + string.Join(" | ", items.Select(item => $"{item.Drive.Title}: [{item.Tag}] {item.Name} {item.SizeText} {item.CountText} '{item.Note}'")));
            Program.Check(panel.Visibility == Visibility.Visible && ((FrameworkElement)window.FindName("ConvertView")).Visibility == Visibility.Collapsed
                && ((Button)window.FindName("ConvertButton")).Visibility == Visibility.Collapsed && ((Button)window.FindName("SendButton")).Visibility == Visibility.Collapsed
                && ((TextBlock)window.FindName("SubtitleText")).Text == "Maps and mods installed on your console, on every drive." && reading == "Reading the console",
                "opening the tab shows it in place of the queue, hides Convert and reads the console");
            Program.Check(items.Select(item => item.Name).SequenceEqual(["zm_alpha", "coolmod", "mp_gamma", "nuketown", "zm_beta", "zm_broken", "zm_empty", "zm_partial", "cp_delta", "bigmod"])
                && items.Select(item => item.Tag).SequenceEqual(["ZM", "MOD", "MP", "MAP", "ZM", "ZM", "ZM", "ZM", "CP", "MOD"]),
                "the rows are in order: console storage first, then each drive, maps before mods, each tagged ZM, MP, CP, MOD or MAP");
            Program.Check(Item("zm_alpha") is { SizeText: var size, CountText: "5 files", Folder: "/data/BO3-Customs/usermaps/zm_alpha", Note: "" } && size == Job.SizeText(alpha)
                && Item("mp_gamma") is { CountText: "1 file", Note: "" } && Item("bigmod") is { Note: "", Folder: "/mnt/ext0/BO3-Customs/mods/bigmod" }
                && Item("zm_beta").SizeText == Job.SizeText(3_000_001_000) && Item("zm_alpha").Tip.Contains("zm_alpha.xpak") && Item("zm_alpha").Tip.Contains("Console storage"),
                "each row gives the name, its size, how many files it holds and its path; the tooltip lists its biggest files");
            Program.Check(Item("zm_broken") is { Note: "No zm_broken.ff inside", NoteState: RunStates.Cancelled } && Item("zm_partial").Note == "Send not finished" && Item("zm_empty") is { Note: "Empty folder", SizeText: "0 KB" }
                && Item("nuketown").Note == "" && Item("coolmod").Note == "",
                "a folder the game would skip says why: no zone of its own, a send that did not finish, or nothing in it");
            string[] summaries = [.. items.Select(item => item.Drive).Distinct().Select(drive => $"{drive.Title} ({drive.Root}): {drive.Summary}")];
            Console.WriteLine("   drives: " + string.Join(" | ", summaries));
            Program.Check(summaries.SequenceEqual([$"Console storage (/data/BO3-Customs): 1 map and 1 mod · {Job.SizeText(alpha + 35_000_000)}",
                $"USB 1 (/mnt/usb1/BO3-Customs): 6 maps · {Job.SizeText(3_000_001_000 + 400_000_000 + 90_000_000 + 70_000_000 + 30_000_000)}",
                $"Extended 0 (/mnt/ext0/BO3-Customs): 1 map and 1 mod · {Job.SizeText(310_000_000)}"])
                && Footer() == $"8 maps and 2 mods on the console · {Job.SizeText(total)}",
                "each drive has its own heading with what it holds and how much, and the footer sums up the console");
            Program.Check(Count("AllCount") == "10" && Count("ZombiesCount") == "5" && Count("MultiplayerCount") == "1" && Count("ModsCount") == "2", "the filters count what is there by kind");
            Program.Render(host, Path.Combine(scratch, "installed_tab.png"));
            List<string> spilled = ShellChecks.Spilled(panel);
            Program.Check(spilled.Count == 0, $"every line of the tab fits: nothing wraps or is cut short{(spilled.Count > 0 ? ": " + string.Join(" | ", spilled.Distinct()) : "")}");

            ((RadioButton)panel.FindName("ZombiesFilter")).IsChecked = true;
            string[] zombies = Shown();
            ((RadioButton)panel.FindName("ModsFilter")).IsChecked = true;
            string[] mods = Shown();
            ((RadioButton)panel.FindName("AllFilter")).IsChecked = true;
            var search = (TextBox)panel.FindName("SearchBox");
            search.Text = "usb1";
            string[] onUsb = Shown();
            string counted = Count("AllCount");
            search.Text = "GAMMA";
            string[] named = Shown();
            search.Text = "nothing like this";
            string none = ((TextBlock)panel.FindName("EmptyTitle")).Text;
            search.Text = "";
            Program.Check(zombies.SequenceEqual(["zm_alpha", "zm_beta", "zm_broken", "zm_empty", "zm_partial"]) && mods.SequenceEqual(["coolmod", "bigmod"]) && onUsb.Length == 6 && counted == "6"
                && named.SequenceEqual(["mp_gamma"]) && none == "Nothing matches your search" && Shown().Length == 10, "the filters and the search (by name or path) narrow the list");

            console.Forget();
            ((RadioButton)window.FindName("ConvertTab")).IsChecked = true;
            tab.IsChecked = true;
            CommunityChecks.Pump(() => !Scanning(), 5);
            bool cached = console.Commands.Length == 0 && Shown().Length == 10;
            Put($"{Data}/usermaps/zm_new/zm_new.ff", 45_000_000);
            Directory.Delete(console.Local($"{Ext}/usermaps/cp_delta"), true);
            typeof(ConsolePanel).GetMethod("RefreshClick", Private)!.Invoke(panel, [panel, new RoutedEventArgs()]);
            CommunityChecks.Pump(() => !Scanning() && items.All(item => item.IsMeasured) && items.Any(item => item.Name == "zm_new"), 30);
            Console.WriteLine($"   after Refresh: {string.Join(", ", items.Select(item => item.Name))}; footer '{Footer()}'");
            Program.Check(cached && items.Count == 10 && Item("zm_new") is { Tag: "ZM", CountText: "1 file" } && !items.Any(item => item.Name == "cp_delta")
                && items.IndexOf(Item("zm_new")) == 1 && Footer().StartsWith("8 maps and 2 mods on the console"),
                "coming back to the tab soon reads nothing again; Refresh does, and adds what is new and drops what is gone");

            MethodInfo remove = typeof(ConsolePanel).GetMethod("RemoveAsync", Private)!;
            console.Forget();
            var removing = (Task)remove.Invoke(panel, [Item("zm_beta"), "127.0.0.1", console.Port])!;
            bool busy = panel.IsRemoving && Footer() == "Removing from the console";
            CommunityChecks.Pump(() => removing.IsCompleted, 30);
            Console.WriteLine($"   after removing zm_beta: {string.Join(", ", items.Select(item => item.Name))}; footer '{Footer()}'");
            Program.Check(busy && !panel.IsRemoving && !Directory.Exists(console.Local($"{Usb}/usermaps/zm_beta")) && Directory.Exists(console.Local($"{Usb}/usermaps/mp_gamma"))
                && Directory.Exists(console.Local($"{Data}/usermaps/zm_alpha")) && !items.Any(item => item.Name == "zm_beta") && items.Count == 9
                && Footer().StartsWith("7 maps and 2 mods on the console") && !sent.OnConsole,
                "removing one deletes that folder on the console and nothing else, takes its row away, and a converted copy stops claiming to be on the console");

            using (var held = new MiniFtp(Path.Combine(root, "held")))
            {
                Directory.CreateDirectory(held.Local($"{Data}/usermaps/zm_wait"));
                File.WriteAllBytes(held.Local($"{Data}/usermaps/zm_wait/zm_wait.ff"), CommunityChecks.Noise(300_000));
                string local = Path.Combine(root, "send");
                Directory.CreateDirectory(local);
                File.WriteAllBytes(Path.Combine(local, "zm_slow.xpak"), CommunityChecks.Noise(1_500_000));
                held.Slow = $"{Data}/usermaps/zm_slow/zm_slow.xpak.part";
                Task<bool> sending = Task.Run(() => ConsoleFtp.SendAsync("127.0.0.1", held.Port, local, $"{Data}/usermaps/zm_slow", ["zm_slow.xpak"], new HashSet<string>(),
                    new Progress<DownloadProgress>(_ => { }), _ => { }, new PauseGate(), CancellationToken.None));
                CommunityChecks.Pump(() => held.Commands.Any(line => line.StartsWith("STOR ")), 20);
                Set("_consoleAddress", $"127.0.0.1:{held.Port}");
                typeof(ConsolePanel).GetMethod("Changed")!.Invoke(panel, null);
                string waiting = Footer();
                bool cleared = items.Count == 0;
                CommunityChecks.Pump(() => sending.IsCompleted && !Scanning() && items.Count == 2 && items.All(item => item.IsMeasured), 60);
                Console.WriteLine($"   another console, mid-transfer: '{waiting}' then {string.Join(", ", items.Select(item => item.Name))}");
                Program.Check(cleared && waiting == "Waiting for the console to be free" && sending.IsCompletedSuccessfully && items.Select(item => item.Name).SequenceEqual(["zm_slow", "zm_wait"]),
                    "another console address clears the list, and reading waits for a transfer that is under way instead of cutting in");
            }

            using (var off = new TcpListenerProbe())
            {
                Set("_consoleAddress", $"127.0.0.1:{off.Port}");
                typeof(ConsolePanel).GetMethod("Changed")!.Invoke(panel, null);
                CommunityChecks.Pump(() => !Scanning(), 30);
                string title = ((TextBlock)panel.FindName("EmptyTitle")).Text, why = ((TextBlock)panel.FindName("EmptyText")).Text;
                Console.WriteLine($"   console off: '{title}' / '{why}' / footer '{Footer()}'");
                Program.Check(items.Count == 0 && title == "Could not read the console" && why.StartsWith("The console at 127.0.0.1") && why.EndsWith("did not answer.") && Footer() == "The console could not be read"
                    && ((Button)panel.FindName("RefreshButton")).IsEnabled, "with the console off the tab says so and can be asked to try again");
            }

            Set("_consoleAddress", $"127.0.0.1:{console.Port}");
            typeof(ConsolePanel).GetMethod("Changed")!.Invoke(panel, null);
            CommunityChecks.Pump(() => !Scanning() && items.Count == 9 && items.All(item => item.IsMeasured), 30);
            console.Dispose();
            var failing = (Task)remove.Invoke(panel, [Item("zm_alpha"), "127.0.0.1", console.Port])!;
            CommunityChecks.Pump(() => failing.IsCompleted, 40);
            Console.WriteLine($"   removal with the console gone: '{Item("zm_alpha").Note}'");
            Program.Check(items.Count == 9 && Item("zm_alpha") is { NoteState: RunStates.Failed, CanRemove: true } && Item("zm_alpha").Note.StartsWith("Not removed: ") && !panel.IsRemoving,
                "a removal that cannot reach the console leaves the row in place and says it was not removed");

            Set("_consoleAddress", null);
            typeof(ConsolePanel).GetMethod("Changed")!.Invoke(panel, null);
            Program.Check(items.Count == 0 && ((TextBlock)panel.FindName("EmptyTitle")).Text == "No console set" && ((TextBlock)panel.FindName("EmptyText")).Text == "Add your console's IP in Settings."
                && Footer() == "On your console" && Count("AllCount") == "", "with no console address the tab asks for one instead of listing anything");
            ((RadioButton)window.FindName("ConvertTab")).IsChecked = true;
            Program.Check(((FrameworkElement)window.FindName("ConvertView")).Visibility == Visibility.Visible && panel.Visibility == Visibility.Collapsed
                && ((Button)window.FindName("ConvertButton")).Visibility == Visibility.Visible, "going back to Convert brings the queue and its buttons back");
        }
        finally
        {
            Edition.Use(real);
            Set("_outputOverride", outputBefore);
            Set("_consoleAddress", null);
            Set("_consoleStorage", null);
            Set("_sendToConsole", false);
        }
    }
}
