using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Desktop;
using FFPorter.Desktop.Controls;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal static class ConvertedChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(AppSettings.Current, value);

    private const byte Pc = 0, Ps4 = 2;

    private static void Put(string folder, params (string Name, int Bytes)[] files) => Put(folder, Ps4, files);

    private static void Put(string folder, byte platform, params (string Name, int Bytes)[] files)
    {
        foreach ((string name, int bytes) in files)
        {
            string path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] data = CommunityChecks.Noise(bytes);
            if (name.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
            {
                "TAff"u8.CopyTo(data);
                data[14] = platform;
            }
            File.WriteAllBytes(path, data);
        }
    }

    private static void Report(string key, int percent, string grade)
    {
        Directory.CreateDirectory(FFPorter.Core.Workspace.ReportDirectory);
        FidelityProtocol.Save(Path.Combine(FFPorter.Core.Workspace.ReportDirectory, key + ".fidelity.json"),
            new FidelitySnapshot { Map = key, State = FidelityStates.Done, Percent = percent, Score = percent, Grade = grade, Progress = 1 });
    }

    public static void Run(string scratch)
    {
        Console.WriteLine("== the Converted tab");
        string root = Path.Combine(scratch, "converted"), output = Path.Combine(root, "out"), made = Path.Combine(output, "t7");
        var settings = AppSettings.Current;
        object? outputBefore = typeof(AppSettings).GetField("_outputOverride", Private)!.GetValue(settings);
        Edition real = Edition.Current;
        Edition.Use(new ListEdition(real, null));
        using var console = new MiniFtp(Path.Combine(root, "ps4"));
        try
        {
            Set("_outputOverride", output);
            Set("_consoleAddress", null);
            Set("_sendToConsole", false);

            MainWindow bare = new();
            var none = (ConvertedPanel)bare.FindName("Converted");
            CommunityChecks.Pump(() => CommunityChecks.Field<bool>(none, "_listed"), 20);
            Program.Check(((RadioButton)bare.FindName("ConvertedTab")).Visibility == Visibility.Visible && none.Count == 0
                && ((TextBlock)none.FindName("EmptyTitle")).Text == "Nothing converted yet",
                "the Converted tab is always there; with nothing in the output folder it says so");

            Put(Path.Combine(made, "zm_one"), ("zm_one.ff", 300_000), ("en_zm_one.ff", 2000), ("zm_one.xpak", 900_000), ("snd/all/zm_one.all.sabl", 40_000), ("workshop.json", 120));
            Put(Path.Combine(made, "mp_two"), ("mp_two.ff", 200_000), ("mp_two.xpak", 500_000));
            Put(Path.Combine(made, "zm_mycommon"), ("zm_mycommon.ff", 150_000));
            Put(Path.Combine(made, "mods", "coolmod"), ("core_mod.ff", 60_000), ("zm_mod.ff", 80_000), ("zm_mod.xpak", 100_000));
            Put(Path.Combine(made, "zm_half.incoming"), ("zm_half.ff", 5000));
            Put(Path.Combine(made, "notes"), ("readme.txt", 50));
            Report("zm_one", 99, "strong");
            Report("coolmod.core_mod", 100, "exact");
            Report("coolmod.zm_mod", 97, "strong");

            var window = new MainWindow();
            var content = (UIElement)window.Content;
            window.Content = null;
            Border host = Program.Host(window, content, 1364, 841);
            var panel = (ConvertedPanel)window.FindName("Converted");
            var tab = (RadioButton)window.FindName("ConvertedTab");
            var items = CommunityChecks.Field<ObservableCollection<ConvertedItem>>(panel, "_items");
            CommunityChecks.Pump(() => items.Count == 4, 20);
            string Status() => ((Run)window.FindName("StatusText")).Text;
            string Counts() => string.Join("/", ((string[])["AllCount", "ZombiesCount", "MultiplayerCount", "ModsCount"]).Select(name => ((Run)panel.FindName(name)).Text.Trim()));
            var list = (ItemsControl)panel.FindName("ItemList");
            string[] Shown() => [.. list.Items.Cast<ConvertedItem>().Select(item => item.Name)];
            ConvertedItem Named(string name) => items.First(item => item.Name == name);

            Console.WriteLine("   rows: " + string.Join(" | ", items.Select(item => $"{item.Tag} {item.Name} {item.ScoreText} {item.CountText} {item.SizeText} send={item.ShowSend}")));
            Program.Check(tab.Visibility == Visibility.Visible && items.Select(item => (item.Tag, item.Name, item.ScoreText)).SequenceEqual([("MP", "mp_two", ""), ("ZONE", "zm_mycommon", ""), ("ZM", "zm_one", "99%"),
                ("MOD", "coolmod", "97–100%")]) && Named("zm_one") is { Files: 5, ScoreGrade: "strong" } && Named("coolmod") is { Files: 3, ScoreGrade: "strong", IsMod: true }
                && Named("zm_one").Folder == Path.Combine(made, "zm_one") && Named("zm_one").Place == Path.Combine("t7", "zm_one") && Named("coolmod").Place == Path.Combine("t7", "mods", "coolmod"),
                "it lists every map, zone and mod in the output folder with its score, files and size");
            tab.IsChecked = true;
            Program.Check(panel.Visibility == Visibility.Visible && ((FrameworkElement)window.FindName("ConvertView")).Visibility == Visibility.Collapsed
                && ((Button)window.FindName("ConvertButton")).Visibility == Visibility.Collapsed && Status() == $"3 maps and 1 mod converted · {Job.SizeText(items.Sum(item => item.Size))}"
                && ((TextBlock)window.FindName("SubtitleText")).Text.StartsWith("Converted maps and mods on this PC"),
                "opening it swaps the queue for the list, and the footer says how much is converted");
            Program.Check(items.All(item => !item.ShowSend && item.CanDelete) && Named("zm_one").Tip.Contains("Zombies map") && Named("coolmod").Tip.Contains("core_mod: 100% exact"),
                "without a console address no row offers Send, and a row's tooltip gives its kind, folder and each zone's score");
            Program.Render(host, Path.Combine(scratch, "converted_tab.png"));
            string[] spilled = [.. ShellChecks.Spilled(host)];
            Program.Check(spilled.Length == 0, "every line of the Converted tab fits" + (spilled.Length > 0 ? ": " + string.Join(" | ", spilled) : ""));

            string all = Counts();
            ((RadioButton)panel.FindName("ZombiesFilter")).IsChecked = true;
            string[] zombies = Shown();
            ((RadioButton)panel.FindName("ModsFilter")).IsChecked = true;
            string[] mods = Shown();
            ((RadioButton)panel.FindName("AllFilter")).IsChecked = true;
            ((TextBox)panel.FindName("SearchBox")).Text = "two";
            string sought = Counts();
            string[] found = Shown();
            ((TextBox)panel.FindName("SearchBox")).Text = "";
            Program.Check(all == "4/1/1/1" && zombies.SequenceEqual(["zm_one"]) && mods.SequenceEqual(["coolmod"]) && found.SequenceEqual(["mp_two"]) && sought == "1/0/1/0",
                "it filters by Zombies, Multiplayer and Mods like the other tabs, a loose zone showing only under All, and search narrows by name or path");

            Set("_consoleAddress", $"127.0.0.1:{console.Port}");
            panel.ConsoleChanged();
            Program.Check(Named("zm_one").CanSend && Named("coolmod").CanSend && !Named("zm_mycommon").ShowSend, "with a console address maps and mods get a Send button; a loose zone has nowhere to go");
            bool Running() => (bool)typeof(MainWindow).GetField("_running", Private)!.GetValue(window)!;
            void Send(string name)
            {
                panel.Send!(Named(name));
                CommunityChecks.Pump(() => !Running() && !Named(name).IsSending, 60);
            }
            console.Forget();
            Send("zm_one");
            string[] placed = [.. console.Commands.Where(line => line.StartsWith("RNTO ")).Select(line => line[5..])];
            Console.WriteLine($"   sent zm_one: '{Status()}', note '{Named("zm_one").Note}', last in place {placed.LastOrDefault()}");
            Program.Check(Named("zm_one") is { Sent: true, Note: "On the console" } && placed.Length == 5 && placed[^1] == "/data/BO3-Customs/usermaps/zm_one/zm_one.ff"
                && File.ReadAllBytes(console.Local("/data/BO3-Customs/usermaps/zm_one/zm_one.xpak")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(made, "zm_one", "zm_one.xpak")))
                && Status() == "Sent to the console", "Send puts a converted map into usermaps on the console, its zone last, and the row says it is there");
            Send("coolmod");
            Program.Check(Named("coolmod").Sent && File.Exists(console.Local("/data/BO3-Customs/mods/coolmod/zm_mod.xpak")) && File.Exists(console.Local("/data/BO3-Customs/mods/coolmod/core_mod.ff")),
                "a mod goes into mods");

            typeof(MainWindow).GetMethod("SetBusy", Private)!.Invoke(window, [true]);
            bool locked = items.All(item => !item.CanSend && !item.CanDelete);
            typeof(MainWindow).GetMethod("SetBusy", Private)!.Invoke(window, [false]);
            CommunityChecks.Pump(() => !CommunityChecks.Field<bool>(panel, "_scanning"), 20);
            Program.Check(locked && Named("mp_two").CanSend && Named("mp_two").CanDelete && Named("zm_one").Sent, "while something converts or is sent, the rows cannot be sent or deleted");

            Program.Asked.Clear();
            typeof(ConvertedPanel).GetMethod("DeleteClick", Private)!.Invoke(panel, [new Button { DataContext = Named("zm_one") }, new RoutedEventArgs()]);
            CommunityChecks.Pump(() => !panel.IsDeleting && items.Count == 3, 30);
            Console.WriteLine($"   asked: {string.Join(" || ", Program.Asked)}; after deleting: '{Status()}'");
            Program.Check(Program.Asked.Count == 1 && Program.Asked[0].StartsWith("Delete zm_one from this PC? | ") && Program.Asked[0].EndsWith("The copy on the console stays.")
                && !Directory.Exists(Path.Combine(made, "zm_one")) && Directory.Exists(Path.Combine(made, "mp_two")) && File.Exists(console.Local("/data/BO3-Customs/usermaps/zm_one/zm_one.ff"))
                && Status().StartsWith("2 maps and 1 mod converted"), "Delete asks first, then removes that folder from this PC only: the console keeps its copy");
            Program.Agree = false;
            typeof(ConvertedPanel).GetMethod("DeleteClick", Private)!.Invoke(panel, [new Button { DataContext = Named("mp_two") }, new RoutedEventArgs()]);
            Program.Agree = true;
            Program.Check(Directory.Exists(Path.Combine(made, "mp_two")) && items.Count == 3, "answering no keeps everything");

            Put(Path.Combine(output, "elsewhere", "zm_deep"), ("zone/zm_deep.ff", 6000), ("zone/zm_deep.xpak", 9000));
            Put(Path.Combine(output, "BO3-Customs", "mods", "sidemod"), ("zm_mod.ff", 4000));
            Put(Path.Combine(output, "pc copies", "zm_pcmap"), Pc, ("zm_pcmap.ff", 5000));
            Put(Path.Combine(output, "a", "b", "c", "zm_toodeep"), ("zm_toodeep.ff", 5000));
            panel.Refresh();
            CommunityChecks.Pump(() => items.Count == 5, 20);
            Console.WriteLine("   anywhere under the output folder: " + string.Join(" | ", items.Select(item => $"{item.Tag} {item.Name} ({item.Place})")));
            Program.Check(items.Count == 5 && Named("zm_deep") is { Tag: "ZM", Files: 2, IsMod: false } && Named("zm_deep").Place == Path.Combine("elsewhere", "zm_deep")
                && Named("sidemod") is { Tag: "MOD", IsMod: true } && items.All(item => item.Name is not ("zm_pcmap" or "zm_toodeep")),
                "a map or mod anywhere under the chosen output folder is listed, also outside the game's own subfolder; PC zones and folders buried deeper are not");
            foreach (string name in (string[])["zm_deep", "sidemod"])
            {
                typeof(ConvertedPanel).GetMethod("DeleteClick", Private)!.Invoke(panel, [new Button { DataContext = Named(name) }, new RoutedEventArgs()]);
                CommunityChecks.Pump(() => !panel.IsDeleting, 30);
            }
            CommunityChecks.Pump(() => items.Count == 3, 20);
            Put(Path.Combine(made, "zm_new"), ("zm_new.ff", 7000));
            Report("zm_new", 98, "strong");
            panel.Refresh();
            CommunityChecks.Pump(() => items.Count == 4, 20);
            Program.Check(Named("zm_new").ScoreText == "98%" && Named("coolmod").Sent, "a refresh picks up what was converted since and keeps what it knew about the rest");
            foreach (string name in (string[])["mp_two", "zm_mycommon", "zm_new", "coolmod"])
            {
                typeof(ConvertedPanel).GetMethod("DeleteClick", Private)!.Invoke(panel, [new Button { DataContext = Named(name) }, new RoutedEventArgs()]);
                CommunityChecks.Pump(() => !panel.IsDeleting, 30);
            }
            Program.Check(items.Count == 0 && tab.Visibility == Visibility.Visible && tab.IsChecked == true && panel.Visibility == Visibility.Visible
                && ((TextBlock)panel.FindName("EmptyTitle")).Text == "Nothing converted yet" && Directory.Exists(Path.Combine(output, "pc copies", "zm_pcmap")),
                "when the last one is deleted the tab stays and says nothing is converted; what it never listed is left alone");

            Console.WriteLine("== the cache size on the Clear cache button");
            string cache = Path.Combine(FFPorter.Core.Workspace.WorkDirectory, "t7");
            if (Directory.Exists(cache))
            {
                foreach (string folder in Directory.GetDirectories(cache))
                    Directory.Delete(folder, true);
            }
            var size = (TextBlock)typeof(MainWindow).GetField("CacheSizeText", Private)!.GetValue(window)!;
            bool Measuring() => (bool)typeof(MainWindow).GetField("_measuring", Private)!.GetValue(window)!;
            CommunityChecks.Pump(() => !Measuring(), 20);
            typeof(MainWindow).GetMethod("CacheTipOpening", Private)!.Invoke(window, [window, null]);
            CommunityChecks.Pump(() => !Measuring(), 20);
            string empty = size.Text;
            Put(Path.Combine(cache, "shader_cache", "a"), ("b", 700_000));
            Put(Path.Combine(cache, "zm_x"), ("walk", 500_000));
            typeof(MainWindow).GetMethod("CacheTipOpening", Private)!.Invoke(window, [window, null]);
            CommunityChecks.Pump(() => size.Text != empty, 20);
            Console.WriteLine($"   hover: '{empty}' then '{size.Text}'");
            var clear = (Button)window.FindName("ClearCacheButton");
            Program.Check(empty == "The cache is empty" && size.Text == "1.1 MB in the cache" && clear.ToolTip is ToolTip tip && ToolTipService.GetShowOnDisabled(clear)
                && tip.Content is StackPanel about && about.Children.Count == 2, "hovering Clear cache says how much is in the cache, measured again each time it opens");
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
