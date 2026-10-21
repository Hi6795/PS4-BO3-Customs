using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FFPorter.Desktop;
using FFPorter.Desktop.Controls;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal static class ConsoleChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Maps = "/data/BO3-Customs/usermaps", Mods = "/data/BO3-Customs/mods";

    public static void Run(string scratch)
    {
        Console.WriteLine("== the console in the Community tab");
        string root = Path.Combine(scratch, "console");
        string site = Path.Combine(root, "site");
        string[] ids = ["zm_here", "zm_gone", "zm_kept"];
        var entries = new List<object>();
        foreach (string id in ids)
        {
            byte[] zip = CommunityChecks.Zip(CommunityChecks.MapFiles(id, id + "/"));
            Directory.CreateDirectory(Path.Combine(site, "t7", "maps", id));
            File.WriteAllBytes(Path.Combine(site, "t7", "maps", id, id + ".zip"), zip);
            entries.Add(new { name = id, title = "Map " + id[3..], size = zip.Length, file = $"maps/{id}/{id}.zip", sha256 = CommunityChecks.Sha(zip) });
        }
        entries.Add(new { name = "coolmod", title = "Cool Mod", type = "mod", file = "maps/zm_here/zm_here.zip", sha256 = new string('c', 64) });
        File.WriteAllText(Path.Combine(site, "t7", "catalog.json"), JsonSerializer.Serialize(new { name = "Console maps", maps = entries }), new UTF8Encoding(true));
        using var server = new MiniServer(site);
        string address = $"http://127.0.0.1:{server.Port}/t7/catalog.json";

        var package = new Dictionary<string, byte[]>
        {
            ["BO3-Customs/lui/ui/t7/utility/pcutility.lua"] = CommunityChecks.Noise(4000),
            ["BO3-Customs/ui_scripts/mapselect.lua"] = CommunityChecks.Noise(9000),
            ["BO3-Customs/zone/zm_levelcommon.ff"] = CommunityChecks.Noise(600_000, "TAff"),
            ["BO3-Customs/zone/zm_levelcommon.xpak"] = CommunityChecks.Noise(900_000),
            ["BO3-Customs/zone/snd/all/zm_levelcommon.all.sabl"] = CommunityChecks.Noise(50_000),
        };
        byte[] archive = CommunityChecks.Zip(new Dictionary<string, byte[]>(package) { ["BO3-Customs/readme.txt"] = CommunityChecks.Noise(100), ["BO3-Customs/tool.exe"] = CommunityChecks.Noise(900, "MZ") });
        byte[] plugin = CommunityChecks.Noise(160_000), older = CommunityChecks.Noise(5000), app = CommunityChecks.Noise(300_000, "MZ");
        new byte[] { 0x4F, 0x15, 0x3D, 0x1D }.CopyTo(plugin, 0);
        using var hub = new FakeGitHub();
        DateTimeOffset when = new(2026, 10, 1, 5, 0, 0, TimeSpan.Zero);
        string Link(string tag, string name) => $"{hub.Address}/download/{tag}/{name}";
        hub.Files["/download/v2.00/Console.zip"] = archive;
        hub.Files["/download/v2.00/BO3-Customs.sprx"] = CommunityChecks.Noise(plugin.Length);
        hub.Releases = UpdateChecks.Json(
            UpdateChecks.Release("v1.00", when.AddDays(-30), false, false, UpdateChecks.Asset("Console.zip", older, Link("v1.00", "Console.zip"), when.AddDays(-30)),
                UpdateChecks.Asset("BO3-Customs.sprx", older, Link("v1.00", "BO3-Customs.sprx"), when.AddDays(-30))),
            UpdateChecks.Release("v2.00", when, false, false, UpdateChecks.Asset("PS4.FF.Porter.exe", app, Link("v2.00", "PS4.FF.Porter.exe"), when),
                UpdateChecks.Asset("Console.zip", archive, Link("v2.00", "Console.zip"), when), UpdateChecks.Asset("BO3-Customs.sprx", plugin, Link("v2.00", "BO3-Customs.sprx"), when)),
            UpdateChecks.Release("v2.50", when.AddDays(1), false, false, UpdateChecks.Asset("PS4.FF.Porter.exe", app, Link("v2.50", "PS4.FF.Porter.exe"), when.AddDays(1))),
            UpdateChecks.Release("v3.00-beta", when.AddDays(3), false, true, UpdateChecks.Asset("Console.zip", older, Link("beta", "Console.zip"), when.AddDays(3)),
                UpdateChecks.Asset("BO3-Customs.sprx", older, Link("beta", "BO3-Customs.sprx"), when.AddDays(3))));

        Task<ConsolePackage?> asking = Task.Run(() => AppUpdate.ConsoleAsync(hub.List, "Console.zip", "BO3-Customs.sprx", CancellationToken.None));
        CommunityChecks.Pump(() => asking.IsCompleted);
        ConsolePackage? offered = asking.IsCompletedSuccessfully ? asking.Result : null;
        Console.WriteLine($"   console files: {offered?.Release}, {offered?.Archive.Name} {offered?.Archive.Size:N0} bytes + {offered?.Plugin.Name} {offered?.Plugin.Size:N0} bytes");
        Program.Check(offered is { Release: "v2.00" } && offered.Archive.Sha256 == CommunityChecks.Sha(archive) && offered.Plugin.Sha256 == CommunityChecks.Sha(plugin)
            && offered.Archive.Link.ToString() == Link("v2.00", "Console.zip"),
            "the console files come from the newest published release that has them: one without them and a pre-release are passed over");

        var settings = AppSettings.Current;
        object? Get(string field) => typeof(AppSettings).GetField(field, Private)!.GetValue(settings);
        void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(settings, value);
        Edition real = Edition.Current;
        object? outputBefore = Get("_outputOverride");
        using var console = new MiniFtp(Path.Combine(root, "ps4"));
        foreach (string id in (string[])["zm_here", "zm_kept", "zm_stranger"])
        {
            Directory.CreateDirectory(console.Local($"{Maps}/{id}/snd/all"));
            File.WriteAllBytes(console.Local($"{Maps}/{id}/{id}.ff"), CommunityChecks.Noise(4000));
            File.WriteAllBytes(console.Local($"{Maps}/{id}/snd/all/{id}.all.sabl"), CommunityChecks.Noise(2000));
        }
        Edition.Use(new ListEdition(real, address, hub.List.ToString()));
        Set("_outputOverride", Path.Combine(root, "out"));
        Set("_consoleAddress", $"127.0.0.1:{console.Port}");
        Set("_sendToConsole", false);
        try
        {
            CommunityCatalog.TryAddress(address, out Uri list);
            Task<IReadOnlyList<CommunityMap>> loading = Task.Run(() => CommunityCatalog.LoadAsync(list, _ => { }, CancellationToken.None));
            CommunityChecks.Pump(() => loading.IsCompleted);
            string libraryFile = Path.Combine(root, "library.json");
            CommunityLibrary library = CommunityLibrary.Load(libraryFile);
            string Target(CommunityMap map) => ConsoleFtp.Target("127.0.0.1", console.Port, ConsoleFtp.Folder(map));
            foreach (string id in (string[])["zm_gone", "zm_kept", "coolmod"])
                library.RememberSent(loading.Result.First(map => map.Id == id), Target(loading.Result.First(map => map.Id == id)));

            hub.Seen.Clear();
            var window = new MainWindow();
            var content = (UIElement)window.Content;
            window.Content = null;
            Border host = Program.Host(window, content, 1364, 841);
            var panel = (CommunityPanel)window.FindName("Community");
            typeof(CommunityPanel).GetField("_library", Private)!.SetValue(panel, library);
            ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;
            var maps = CommunityChecks.Field<ObservableCollection<CommunityMap>>(panel, "_maps");
            bool Looked() => !CommunityChecks.Field<bool>(panel, "_looking") && typeof(CommunityPanel).GetField("_onConsole", Private)!.GetValue(panel) != null;
            CommunityChecks.Pump(() => CommunityChecks.Field<bool>(panel, "_loaded") && Looked(), 30);
            CommunityMap Map(string id) => maps.First(map => map.Id == id);
            void Finish() => CommunityChecks.Pump(() => !panel.IsDownloading, 90);
            void Remove(CommunityMap map)
            {
                object work = Activator.CreateInstance(typeof(CommunityPanel).GetNestedType("Work", BindingFlags.NonPublic)!, map, false, false, true, false)!;
                typeof(CommunityPanel).GetMethod("Enqueue", Private)!.Invoke(panel, [work]);
            }
            string Noted()
            {
                var view = (LogView)window.FindName("Log");
                return ((TextBox)view.FindName("Text")).Text + typeof(LogView).GetField("_pending", Private)!.GetValue(view);
            }

            Console.WriteLine("   rows: " + string.Join(" | ", maps.Select(map => $"{map.Title}: [{(map.CanStart ? map.ActionText : "")}] '{map.Note}' remove={map.CanRemove}")));
            Program.Check(maps.Count == 4 && !maps.Any(map => map.IsConsole) && panel.ConsoleFiles == null && hub.Seen.IsEmpty && panel.Status == "3 maps and 1 mod to download"
                && ((Run)panel.FindName("AllCount")).Text.Trim() == "4",
                "the Community list holds maps and mods only: the console files are not a row there any more, and loading it asks GitHub for nothing");
            Program.Check(Map("zm_here") is { Found: true, Sent: false, Note: "On the console", NoteState: RunStates.Done, CanRemove: true, HasMenu: true, ActionText: "Download" },
                "a map that is on the console says so, even though this PC never sent it there");
            Program.Check(Map("zm_kept") is { Sent: true, Note: "On the console", CanRemove: true } && Map("zm_gone") is { Sent: false, Found: false, Note: "", CanRemove: false, HasMenu: false }
                && !library.Knows(Target(Map("zm_gone"))) && !CommunityLibrary.Load(libraryFile).Knows(Target(Map("zm_gone"))),
                "a map this PC sent earlier that is no longer on the console stops claiming to be there");
            Program.Check(Map("coolmod") is { Sent: true, Note: "On the console" } && library.Knows(Target(Map("coolmod"))),
                "where the console lists nothing of a kind at all (no mods here), what was sent is still trusted rather than forgotten");
            Program.Render(host, Path.Combine(scratch, "console_rows.png"));

            console.Forget();
            Remove(Map("zm_here"));
            Finish();
            Console.WriteLine($"   after removing: '{Map("zm_here").Note}' state {Map("zm_here").State}; console has {string.Join(", ", Directory.GetDirectories(console.Local(Maps)).Select(Path.GetFileName))}");
            Program.Check(!Directory.Exists(console.Local($"{Maps}/zm_here")) && Directory.Exists(console.Local($"{Maps}/zm_kept")) && Directory.Exists(console.Local($"{Maps}/zm_stranger"))
                && Map("zm_here") is { Found: false, Sent: false, Note: "", CanRemove: false, State: RunStates.Ready, ActionText: "Download" } && Noted().Contains("Map here was removed from the console"),
                "Remove from the console deletes that map's folder there and nothing else, and the row stops saying it is there");
            Remove(Map("zm_kept"));
            Finish();
            Program.Check(!Directory.Exists(console.Local($"{Maps}/zm_kept")) && Map("zm_kept") is { Sent: false, Note: "", CanRemove: false } && !library.Knows(Target(Map("zm_kept"))),
                "removing a map this PC had sent also forgets that it was sent");

            Console.WriteLine("== the console files in Settings");
            var dialog = new SettingsWindow(null, null, panel);
            var row = (Grid)dialog.FindName("FilesRow");
            var text = (TextBlock)dialog.FindName("FilesText");
            var start = (Button)dialog.FindName("FilesStart");
            var more = (Button)dialog.FindName("FilesMore");
            var bar = (ProgressBar)dialog.FindName("FilesProgress");
            MenuItem[] items = [(MenuItem)dialog.FindName("FilesOpenItem"), (MenuItem)dialog.FindName("FilesResendItem"), (MenuItem)dialog.FindName("FilesDeleteItem")];
            string checking = text.Text;
            Program.Settled(dialog);
            CommunityChecks.Pump(() => panel.ConsoleFiles != null && start.Visibility == Visibility.Visible, 30);
            CommunityMap files = panel.ConsoleFiles!;
            var page = (UIElement)dialog.Content;
            dialog.Content = null;
            Border shown = Program.Host(dialog, page, Program.SettingsWidth, double.NaN);
            var spilled = new List<string>();
            void Look(string name)
            {
                Program.Render(shown, Path.Combine(scratch, name));
                spilled.AddRange(ShellChecks.Spilled(shown));
            }
            string Row() => $"[{(start.Visibility == Visibility.Visible ? start.Content : "")}] '{text.Text}'{(more.Visibility == Visibility.Visible ? " + " + string.Join("/", items.Where(item => item.Visibility == Visibility.Visible).Select(item => item.Header)) : "")}";
            void Press(string handler) => typeof(SettingsWindow).GetMethod(handler, Private)!.Invoke(dialog, [dialog, new RoutedEventArgs()]);

            Console.WriteLine($"   on opening Settings: '{checking}', then {Row()}");
            Program.Check(row.Visibility == Visibility.Visible && checking == "Checking the latest release…" && files is { IsConsole: true, Title: "Console files", Version: "v2.00", CanRemove: false }
                && files.Extras.Count == 1 && Row() == $"[Download] 'Plugin, menu scripts and shared zone · {Job.SizeText(archive.Length)}'" && ((Run)dialog.FindName("FilesVersion")).Text.Trim() == "v2.00"
                && bar.Visibility == Visibility.Collapsed && !maps.Any(map => map.IsConsole),
                "Settings lists the newest release's console files in its CONSOLE section, with a Download button");
            Look("settings_files.png");

            Press("FilesStartClick");
            Finish();
            string folder = Path.Combine(root, "out", "t7", "BO3-Customs");
            Console.WriteLine($"   wrong plugin: {files.State}, {Row()}");
            Program.Check(files.State == RunStates.Failed && text.Text.Contains("does not match its checksum") && Row().StartsWith("[Retry] ") && !Directory.Exists(folder) && !Directory.Exists(folder + ".incoming")
                && CommunityDownloader.Fetched(files) == 0, "a plugin that does not match GitHub's checksum is thrown away with the rest of the download, and the row offers Retry");

            hub.Files["/download/v2.00/BO3-Customs.sprx"] = plugin;
            Press("FilesStartClick");
            Finish();
            string[] got = Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/')).Order()] : [];
            Console.WriteLine($"   console files on this PC: {string.Join(" | ", got)}");
            Console.WriteLine($"   downloaded: {Row()}");
            Program.Check(files is { State: RunStates.Done, Local: LocalCopies.Current } && Row() == "[Send to console] 'Downloaded' + Open its folder/Delete from this PC"
                && got.SequenceEqual(package.Keys.Select(name => name["BO3-Customs/".Length..]).Append("BO3-Customs.sprx").Order())
                && package.All(pair => File.ReadAllBytes(Path.Combine(root, "out", "t7", pair.Key.Replace('/', '\\'))).AsSpan().SequenceEqual(pair.Value))
                && File.ReadAllBytes(Path.Combine(folder, "BO3-Customs.sprx")).AsSpan().SequenceEqual(plugin),
                "Download fetches the console files and the plugin into one folder, menu scripts included, and leaves out what does not belong");
            Look("settings_files_downloaded.png");

            var box = (TextBox)dialog.FindName("ConsoleBox");
            string typed = box.Text;
            box.Text = "";
            string unset = Row();
            box.Text = typed;
            Console.WriteLine($"   with no console address: {unset}");
            Program.Check(unset == "[] 'Downloaded' + Open its folder/Delete from this PC" && Row().StartsWith("[Send to console] "),
                "Send to console is only offered while a console address is set, and follows the address box as it is typed");

            console.Forget();
            console.Sizes["/data/BO3-Customs/BO3-Customs.sprx.part"] = plugin.Length - 4096;
            Press("FilesStartClick");
            Finish();
            console.Sizes.Clear();
            string[] placed = [.. console.Commands.Where(line => line.StartsWith("RNTO ")).Select(line => line[5..])];
            Console.WriteLine($"   sent: {files.State}, {Row()}; last put in place: {placed.LastOrDefault()}");
            Program.Check(files is { State: RunStates.Done, Sent: true, CanRemove: false } && Row() == "[] 'Downloaded · on the console' + Open its folder/Send to the console again/Delete from this PC"
                && placed.Length == package.Count + 1 && placed[^1] == "/data/BO3-Customs/BO3-Customs.sprx"
                && package.All(pair => File.ReadAllBytes(console.Local("/data/" + pair.Key)).AsSpan().SequenceEqual(pair.Value))
                && File.ReadAllBytes(console.Local("/data/BO3-Customs/BO3-Customs.sprx")).AsSpan().SequenceEqual(plugin) && Directory.Exists(console.Local($"{Maps}/zm_stranger")),
                "Send to console puts them straight into BO3-Customs, the plugin last, beside the maps that are already there");
            Look("settings_files_sent.png");

            files.State = RunStates.Running;
            files.Progress = 0.42;
            files.CanPause = true;
            files.StageText = "Sending to the console · zm_levelcommon.xpak · 362.1 MB of 822.2 MB · 18.3 MB/s";
            string sending = text.Text;
            bool working = bar.Visibility == Visibility.Visible && bar.Value == 0.42 && start.Visibility == Visibility.Collapsed && more.Visibility == Visibility.Collapsed
                && ((Button)dialog.FindName("FilesPause")).Visibility == Visibility.Visible && ((Button)dialog.FindName("FilesCancel")).Visibility == Visibility.Visible;
            Look("settings_files_sending.png");
            files.StageText = "Downloading · 362.1 MB of 822.2 MB · 18.3 MB/s";
            string fetching = text.Text;
            Look("settings_files_downloading.png");
            files.StageText = "";
            files.CanPause = false;
            files.Progress = 1;
            files.State = RunStates.Done;
            Console.WriteLine($"   while working: '{fetching}' | '{sending}'");
            Program.Check(working && fetching == "Downloading · 362.1 MB of 822.2 MB · 18.3 MB/s" && sending == "Sending · 362.1 MB of 822.2 MB · 18.3 MB/s" && bar.Visibility == Visibility.Collapsed
                && Row().StartsWith("[] 'Downloaded · on the console'"), "while it works the row shows a bar, the amount and the speed, with Pause and Cancel in place of the button");
            Program.Check(spilled.Count == 0, $"in every state the Console files row fits on its line at the window's real width{(spilled.Count > 0 ? ": " + string.Join(" | ", spilled.Distinct()) : "")}");

            ((RadioButton)window.FindName("ConvertTab")).IsChecked = true;
            string Detail() => ((Run)window.FindName("DetailText")).Text.Trim();
            void Working(CommunityMap? map)
            {
                typeof(CommunityPanel).GetField("_current", Private)!.SetValue(panel, map);
                typeof(CommunityPanel).GetMethod("ShowStatus", Private)!.Invoke(panel, null);
            }
            string idle = Detail();
            Working(files);
            string busy = Detail();
            Working(null);
            Console.WriteLine($"   Convert tab footer while the console files download: '{busy}'");
            Program.Check(!idle.Contains("console files") && busy.EndsWith("downloading the console files") && !Detail().Contains("console files"),
                "after Settings is closed the Convert tab's footer still says the console files are downloading");
            ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;

            Console.WriteLine("== a newer plugin on its own");
            byte[][] plugins = [CommunityChecks.Noise(170_000), CommunityChecks.Noise(180_000), CommunityChecks.Noise(190_000)];
            foreach (byte[] made in plugins)
                new byte[] { 0x4F, 0x15, 0x3D, 0x1D }.CopyTo(made, 0);
            string[] tags = ["v2.10", "v2.20", "v2.30"];
            CommunityMap Publish(int upTo)
            {
                var releases = new List<Dictionary<string, object?>>
                {
                    UpdateChecks.Release("v2.00", when, false, false, UpdateChecks.Asset("Console.zip", archive, Link("v2.00", "Console.zip"), when),
                        UpdateChecks.Asset("BO3-Customs.sprx", plugin, Link("v2.00", "BO3-Customs.sprx"), when)),
                };
                for (int index = 0; index <= upTo; index++)
                {
                    hub.Files[$"/download/{tags[index]}/BO3-Customs.sprx"] = plugins[index];
                    releases.Add(UpdateChecks.Release(tags[index], when.AddDays(2 + index), false, false,
                        UpdateChecks.Asset("PS4.FF.Porter.exe", app, Link(tags[index], "PS4.FF.Porter.exe"), when.AddDays(2 + index)),
                        UpdateChecks.Asset("BO3-Customs.sprx", plugins[index], Link(tags[index], "BO3-Customs.sprx"), when.AddDays(2 + index))));
                }
                hub.Releases = UpdateChecks.Json([.. releases]);
                typeof(CommunityPanel).GetField("_listed", Private)!.SetValue(panel, default(DateTime));
                Task<CommunityMap?> listing = panel.FindConsoleFilesAsync();
                CommunityChecks.Pump(() => listing.IsCompleted, 30);
                hub.Seen.Clear();
                console.Forget();
                return panel.ConsoleFiles!;
            }
            string[] Stored() => [.. console.Commands.Where(line => line.StartsWith("STOR ")).Select(line => line[5..])];
            bool Fetched(string name) => hub.Seen.Any(line => line.Contains(name));
            byte[] OnConsole() => File.ReadAllBytes(console.Local("/data/BO3-Customs/BO3-Customs.sprx"));

            CommunityMap second = Publish(0);
            Console.WriteLine($"   a release with only a newer plugin: {second.Version}, [{second.ActionText}] '{second.Note}'");
            Program.Check(!ReferenceEquals(second, files) && second is { Version: "v2.10", Local: LocalCopies.Outdated, Light: true, ActionText: "Update", Note: "A newer plugin is available" }
                && second.Sha256 == CommunityChecks.Sha(archive) && second.Extras[0].Sha256 == CommunityChecks.Sha(plugins[0]) && second.Parts[0].ToString() == Link("v2.00", "Console.zip"),
                "a later release that carries only a newer plugin is picked up: the latest plugin with the console files of the release that has them");
            panel.Start(second, null);
            Finish();
            Console.WriteLine($"   after Update: [{(second.CanStart ? second.ActionText : "")}] '{second.Note}'; asked GitHub for {string.Join(", ", hub.Seen.Select(line => line.Split(' ')[0]))}");
            Program.Check(second is { State: RunStates.Done, Local: LocalCopies.Current, Light: false, ActionText: "Send to console", Note: "Downloaded" } && !Fetched("Console.zip") && Fetched("v2.10/BO3-Customs.sprx")
                && File.ReadAllBytes(Path.Combine(folder, "BO3-Customs.sprx")).AsSpan().SequenceEqual(plugins[0])
                && package.All(pair => File.ReadAllBytes(Path.Combine(root, "out", "t7", pair.Key.Replace('/', '\\'))).AsSpan().SequenceEqual(pair.Value)) && Stored().Length == 0,
                "Update then downloads the plugin alone and puts it with the files already on this PC");
            panel.Start(second, null);
            Finish();
            Console.WriteLine($"   sent: stored {string.Join(", ", Stored())}; '{second.Note}'");
            Program.Check(second is { Sent: true, Note: "Downloaded · on the console" } && Stored().SequenceEqual(["/data/BO3-Customs/BO3-Customs.sprx.part"]) && OnConsole().AsSpan().SequenceEqual(plugins[0])
                && package.All(pair => File.ReadAllBytes(console.Local("/data/" + pair.Key)).AsSpan().SequenceEqual(pair.Value)),
                "and Send to console sends only that plugin to /data/BO3-Customs, since the rest is already there");
            panel.Resend(second);
            Finish();
            Program.Check(Stored().Length == 1 + package.Count + 1, "Send to the console again still sends everything");

            Set("_sendToConsole", true);
            Program.Asked.Clear();
            CommunityMap third = Publish(1);
            panel.Start(third, null);
            Finish();
            Console.WriteLine($"   Auto-FTP: stored {string.Join(", ", Stored())}; '{third.Note}'; asked: {string.Join(" || ", Program.Asked)}");
            Program.Check(third is { Version: "v2.20", State: RunStates.Done, Local: LocalCopies.None, Sent: true, Note: "On the console" } && !Fetched("Console.zip")
                && Stored().SequenceEqual(["/data/BO3-Customs/BO3-Customs.sprx.part"]) && OnConsole().AsSpan().SequenceEqual(plugins[1]) && !Directory.Exists(folder)
                && Program.Asked.Count == 1 && Program.Asked[0].StartsWith("Overwrite Console files on the console?"),
                "with Auto-FTP on, Update fetches the newer plugin, asks before replacing it on the console, sends it alone and clears the PC copy as usual");

            CommunityMap fourth = Publish(2);
            string waiting = $"[{fourth.ActionText}] '{fourth.Note}'";
            File.Delete(console.Local("/data/BO3-Customs/BO3-Customs.sprx"));
            panel.Start(fourth, null);
            Finish();
            Console.WriteLine($"   with no copy on this PC: {waiting}; console wiped: stored {Stored().Length} files, '{fourth.Note}'");
            Program.Check(waiting == "[Update] 'On the console · a newer plugin is available'" && fourth is { State: RunStates.Done, Sent: true } && Fetched("Console.zip")
                && Stored().Length == package.Count + 1 && OnConsole().AsSpan().SequenceEqual(plugins[2]),
                "if the plugin is no longer on the console, everything is downloaded and sent again instead of the plugin alone");
            Set("_sendToConsole", false);
            hub.Releases = UpdateChecks.Json(UpdateChecks.Release("v2.00", when, false, false, UpdateChecks.Asset("Console.zip", archive, Link("v2.00", "Console.zip"), when),
                UpdateChecks.Asset("BO3-Customs.sprx", plugin, Link("v2.00", "BO3-Customs.sprx"), when)));
            typeof(CommunityPanel).GetField("_listed", Private)!.SetValue(panel, default(DateTime));
            Task<CommunityMap?> back = panel.FindConsoleFilesAsync();
            CommunityChecks.Pump(() => back.IsCompleted, 30);
            files = panel.ConsoleFiles!;
            panel.Start(files, null);
            Finish();
            panel.Start(files, null);
            Finish();
            hub.Seen.Clear();

            byte[] newer = CommunityChecks.Zip(new Dictionary<string, byte[]>(package) { ["BO3-Customs/ui_scripts/new.lua"] = CommunityChecks.Noise(700) });
            hub.Files["/download/v2.60/Console.zip"] = newer;
            hub.Files["/download/v2.60/BO3-Customs.sprx"] = plugin;
            hub.Releases = UpdateChecks.Json(UpdateChecks.Release("v2.60", when.AddDays(9), false, false, UpdateChecks.Asset("Console.zip", newer, Link("v2.60", "Console.zip"), when.AddDays(9)),
                UpdateChecks.Asset("BO3-Customs.sprx", plugin, Link("v2.60", "BO3-Customs.sprx"), when.AddDays(9))));
            hub.Seen.Clear();
            SettingsWindow soon = Program.Settled(new SettingsWindow(null, null, panel));
            bool kept = hub.Seen.IsEmpty && ReferenceEquals(panel.ConsoleFiles, files) && ((Run)soon.FindName("FilesVersion")).Text.Trim() == "v2.00";
            typeof(CommunityPanel).GetField("_listed", Private)!.SetValue(panel, default(DateTime));
            SettingsWindow later = Program.Settled(new SettingsWindow(null, null, panel));
            var laterStart = (Button)later.FindName("FilesStart");
            CommunityChecks.Pump(() => !ReferenceEquals(panel.ConsoleFiles, files) && laterStart.Visibility == Visibility.Visible, 30);
            CommunityMap updated = panel.ConsoleFiles!;
            Console.WriteLine($"   after a newer release: {updated.Version}, [{laterStart.Content}] '{((TextBlock)later.FindName("FilesText")).Text}'");
            Program.Check(kept && !hub.Seen.IsEmpty && updated is { Version: "v2.60", Local: LocalCopies.Outdated, ActionText: "Update", CanStart: true, Sent: false, Stale: true, Note: "A newer version is available" }
                && (string)laterStart.Content == "Update" && ((TextBlock)later.FindName("FilesText")).Text == "A newer version is available" && ((Run)later.FindName("FilesVersion")).Text.Trim() == "v2.60",
                "opening Settings again soon asks GitHub nothing; later it does, and a newer release's console files are offered as Update");

            hub.ListStatus = 403;
            var lone = new CommunityPanel();
            SettingsWindow failing = Program.Settled(new SettingsWindow(null, null, lone));
            var retry = (Button)failing.FindName("FilesStart");
            var reason = (TextBlock)failing.FindName("FilesText");
            CommunityChecks.Pump(() => retry.Visibility == Visibility.Visible, 30);
            string refused = $"[{retry.Content}] '{reason.Text}'";
            hub.ListStatus = 0;
            typeof(SettingsWindow).GetMethod("FilesStartClick", Private)!.Invoke(failing, [failing, new RoutedEventArgs()]);
            CommunityChecks.Pump(() => lone.ConsoleFiles != null && (string)retry.Content != "Retry", 30);
            Console.WriteLine($"   GitHub refusing: {refused}; after Retry: [{retry.Content}] '{reason.Text}'");
            Program.Check(refused.StartsWith("[Retry] 'GitHub answered 403") && refused.EndsWith(".'") && lone.ConsoleFiles is { Version: "v2.60" } && retry.Visibility == Visibility.Visible
                && (string)retry.Content is "Download" or "Update" && !reason.Text.Contains("403"),
                "when GitHub cannot be asked the row says why and offers Retry, which lists the files once GitHub answers");

            console.Dispose();
            Remove(Map("coolmod"));
            Finish();
            Console.WriteLine($"   console off: '{Map("coolmod").Note}' state {Map("coolmod").State}");
            Program.Check(Map("coolmod") is { State: RunStates.Ready, Sent: true, CanRemove: true } && Map("coolmod").Note.StartsWith("Not removed from the console: ") && Map("coolmod").NoteState == RunStates.Failed
                && library.Knows(Target(Map("coolmod"))), "if the console cannot be reached, nothing is forgotten and the row says it was not removed");
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
