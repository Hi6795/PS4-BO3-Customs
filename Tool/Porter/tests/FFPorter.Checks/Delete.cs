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

internal static class DeleteChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run(string scratch)
    {
        Console.WriteLine("== deleting community downloads");
        string root = Path.Combine(scratch, "delete");
        string site = Path.Combine(root, "site");
        string[] ids = ["zm_one", "zm_two", "zm_three", "zm_four", "zm_five", "zm_six"];
        var files = new Dictionary<string, Dictionary<string, byte[]>>();
        var entries = new List<object>();
        foreach (string id in ids)
        {
            files[id] = CommunityChecks.MapFiles(id, id + "/");
            byte[] zip = CommunityChecks.Zip(files[id]);
            Directory.CreateDirectory(Path.Combine(site, "t7", "maps", id));
            File.WriteAllBytes(Path.Combine(site, "t7", "maps", id, id + ".zip"), zip);
            entries.Add(new { name = id, title = "Map " + id[3..], size = zip.Length, file = $"maps/{id}/{id}.zip", sha256 = CommunityChecks.Sha(zip) });
        }
        File.WriteAllText(Path.Combine(site, "t7", "catalog.json"), JsonSerializer.Serialize(new { name = "Delete maps", maps = entries }), new UTF8Encoding(true));
        using var server = new MiniServer(site);

        var settings = AppSettings.Current;
        object? Get(string field) => typeof(AppSettings).GetField(field, Private)!.GetValue(settings);
        void Set(string field, object? value) => typeof(AppSettings).GetField(field, Private)!.SetValue(settings, value);
        Edition real = Edition.Current;
        object? outputBefore = Get("_outputOverride");
        Edition.Use(new ListEdition(real, $"http://127.0.0.1:{server.Port}/t7/catalog.json"));
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
            string libraryFile = Path.Combine(root, "library.json");
            CommunityLibrary library = CommunityLibrary.Load(libraryFile);
            typeof(CommunityPanel).GetField("_library", Private)!.SetValue(panel, library);
            ((RadioButton)window.FindName("CommunityTab")).IsChecked = true;
            var maps = CommunityChecks.Field<System.Collections.ObjectModel.ObservableCollection<CommunityMap>>(panel, "_maps");
            CommunityChecks.Pump(() => CommunityChecks.Field<bool>(panel, "_loaded"), 15);
            CommunityMap Map(string id) => maps.First(map => map.Id == id);
            string Folder(string id) => Path.Combine(root, "out", "usermaps", id);
            void Click(string handler, string id) => typeof(CommunityPanel).GetMethod(handler, Private)!.Invoke(panel, [new Button { DataContext = Map(id) }, new RoutedEventArgs()]);
            void Finish() => CommunityChecks.Pump(() => !panel.IsDownloading, 60);
            Removal Remove(string id) => (Removal)typeof(CommunityPanel).GetMethod("RemoveLocal", Private)!.Invoke(panel, [Map(id), Folder(id), ""])!;
            string Noted()
            {
                var view = (LogView)window.FindName("Log");
                return ((TextBox)view.FindName("Text")).Text + typeof(LogView).GetField("_pending", Private)!.GetValue(view);
            }
            bool OnConsole(MiniFtp console, string id) => files[id].All(pair => File.Exists(console.Local("/data/BO3-Customs/usermaps/" + pair.Key))
                && File.ReadAllBytes(console.Local("/data/BO3-Customs/usermaps/" + pair.Key)).AsSpan().SequenceEqual(pair.Value));

            Program.Check(maps.Count == ids.Length && maps.All(map => !map.CanDelete), "nothing offers Delete before anything is downloaded");
            Click("StartClick", "zm_one");
            Finish();
            Program.Check(Map("zm_one").Local == LocalCopies.Current && Map("zm_one").CanDelete && Map("zm_one").CanOpen && !Map("zm_two").CanDelete && File.Exists(Path.Combine(Folder("zm_one"), "zm_one.ff")),
                "a downloaded map gets a Delete button; the others do not");
            Program.Render(host, Path.Combine(scratch, "delete_button.png"));
            var confirm = (Window)typeof(MessageDialog).GetConstructors(Private)[0].Invoke([null, DialogKind.Warning, $"Delete {Map("zm_one").Title} from this PC?",
                $"{Folder("zm_one")}\n\nEverything in that folder ({Job.SizeText(Cleanup.FolderSize(Folder("zm_one")))}) is deleted for good; it does not go to the Recycle Bin. It can be downloaded again.",
                "Delete", "Cancel", true]);
            var confirmBody = (UIElement)confirm.Content;
            confirm.Content = null;
            Program.Render(Program.Host(confirm, confirmBody, 484, double.NaN), Path.Combine(scratch, "delete_confirm.png"));
            Removal removed = Remove("zm_one");
            Console.WriteLine($"   deleted: {removed.Bytes:N0} bytes, problems {removed.Problems.Count}; row: '{Map("zm_one").Note}' button '{Map("zm_one").ActionText}'");
            Program.Check(removed.Problems.Count == 0 && removed.Bytes > 5_000_000 && !Directory.Exists(Folder("zm_one")) && Map("zm_one").Local == LocalCopies.None && !Map("zm_one").CanDelete
                && !Map("zm_one").CanOpen && Map("zm_one").CanStart && Map("zm_one").ActionText == "Download" && Map("zm_one").Note == ""
                && CommunityLibrary.Load(libraryFile).Check(Map("zm_one"), Folder("zm_one")) == LocalCopies.None && Noted().Contains("Map one: deleted from this PC ("),
                "Delete removes the map's folder from the PC, forgets the download and offers Download again");

            using var console = new MiniFtp(Path.Combine(root, "console"));
            Set("_consoleAddress", $"127.0.0.1:{console.Port}");
            Set("_sendToConsole", true);
            panel.SettingsChanged();
            Program.Asked.Clear();
            Click("StartClick", "zm_two");
            Finish();
            CommunityMap two = Map("zm_two");
            Console.WriteLine($"   upload option on: state {two.State}, row '{two.Note}' ({two.NoteState}), button '{two.ActionText}'");
            Program.Check(two.State == RunStates.Done && two.Sent && two.Local == LocalCopies.None && two.Note == "On the console" && two.NoteState == RunStates.Done && two.ActionText == "Download"
                && two.CanStart && !two.CanOpen && !two.CanDelete && !two.CanResend && !Directory.Exists(Folder("zm_two")) && OnConsole(console, "zm_two")
                && Noted().Contains("Map two: deleted from this PC now that it is on the console ("),
                "with the upload option on, a download is sent to the console and then deleted from the PC; the row says it is on the console");
            Program.Render(host, Path.Combine(scratch, "delete_after_send.png"));

            Set("_sendToConsole", false);
            panel.SettingsChanged();
            Click("StartClick", "zm_three");
            Finish();
            Click("StartClick", "zm_three");
            Finish();
            CommunityMap three = Map("zm_three");
            Program.Check(three.Sent && three.Local == LocalCopies.Current && three.Note == "Downloaded · on the console" && three.CanDelete && Directory.Exists(Folder("zm_three")) && OnConsole(console, "zm_three"),
                "with the upload option off, sending a map by hand keeps the copy on the PC");
            Remove("zm_three");
            Program.Check(three.Note == "On the console" && !Directory.Exists(Folder("zm_three")) && OnConsole(console, "zm_three"), "deleting it by hand afterwards leaves the copy on the console alone");

            Click("StartClick", "zm_four");
            Finish();
            Set("_sendToConsole", true);
            panel.SettingsChanged();
            bool waiting = Map("zm_four").Local == LocalCopies.Current && Map("zm_four").ActionText == "Send to console";
            Click("StartClick", "zm_four");
            Finish();
            Program.Check(waiting && Map("zm_four").Sent && Map("zm_four").Local == LocalCopies.None && Map("zm_four").Note == "On the console" && !Directory.Exists(Folder("zm_four")) && OnConsole(console, "zm_four"),
                "with the upload option on, a map that was already downloaded is deleted from the PC once it is sent too");

            console.NoSize = true;
            Click("StartClick", "zm_five");
            Finish();
            console.NoSize = false;
            Program.Check(Map("zm_five").Sent && Map("zm_five").Local == LocalCopies.Current && Map("zm_five").Note == "Downloaded · on the console" && Directory.Exists(Folder("zm_five"))
                && Noted().Contains("Map five was kept on this PC"),
                "if the console cannot confirm what it received, the copy on the PC is kept and the log says why");

            using (var off = new TcpListenerProbe())
            {
                Set("_consoleAddress", $"127.0.0.1:{off.Port}");
                panel.SettingsChanged();
                Click("StartClick", "zm_six");
                Finish();
                Program.Check(Map("zm_six").State == RunStates.Failed && Map("zm_six").Note.StartsWith("Not sent to the console") && Map("zm_six").Local == LocalCopies.Current
                    && File.Exists(Path.Combine(Folder("zm_six"), "zm_six.ff")), "if the send fails, the download stays on the PC");
            }
            Set("_consoleAddress", $"127.0.0.1:{console.Port}");
            panel.SettingsChanged();

            using (new FileStream(Path.Combine(Folder("zm_six"), "zm_six.xpak"), FileMode.Open, FileAccess.Read, FileShare.None))
                removed = Remove("zm_six");
            Console.WriteLine($"   file in use: {string.Join("; ", removed.Problems)}");
            Program.Check(removed.Problems.Count == 1 && Map("zm_six").Local == LocalCopies.None && !Map("zm_six").CanDelete && File.Exists(Path.Combine(Folder("zm_six"), "zm_six.xpak"))
                && Noted().Contains("Could not delete all of Map six"), "a file that is in use is reported, and the half-deleted folder no longer counts as a download");

            string target = ConsoleFtp.Target("127.0.0.1", console.Port, ConsoleFtp.Folder(two));
            library.RememberSent(new CommunityMap { Id = "zm_two", Title = "x", Folder = "zm_two", Parts = two.Parts, Sha256 = new string('7', 64) }, target);
            panel.SettingsChanged();
            Console.WriteLine($"   older copy on the console: row '{two.Note}', button '{two.ActionText}'");
            Program.Check(!two.Sent && two.Stale && two.Note == "On the console · a newer version is available" && two.ActionText == "Update" && two.CanStart,
                "a map that is only on the console still shows when the list has a newer version, and offers Update");
            Click("StartClick", "zm_two");
            Finish();
            Program.Check(two.Sent && !two.Stale && two.Note == "On the console" && !Directory.Exists(Folder("zm_two")), "Update fetches it, sends it and clears it from the PC again");
            Console.WriteLine($"   asked before replacing: {string.Join(" || ", Program.Asked)}");
            Program.Check(Program.Asked.SequenceEqual(["Overwrite Map two on the console? | It is already there and gets replaced."]),
                "with Auto-FTP on, a download that is already on the console asks before it is overwritten; a new one, or one sent by hand, does not ask");

            Program.Asked.Clear();
            Program.Agree = false;
            console.Forget();
            Click("StartClick", "zm_three");
            Finish();
            Program.Agree = true;
            Console.WriteLine($"   skipped: state {three.State}, row '{three.Note}', button '{three.ActionText}'");
            Program.Check(Program.Asked.SequenceEqual(["Overwrite Map three on the console? | It is already there and gets replaced."]) && three.State == RunStates.Done && three.Local == LocalCopies.Current
                && Directory.Exists(Folder("zm_three")) && OnConsole(console, "zm_three") && !console.Commands.Any(line => line.StartsWith("STOR ") || line.StartsWith("RNTO ") || line.StartsWith("DELE "))
                && Noted().Contains("Map three was not sent: it is already on the console"),
                "answering Skip leaves the console's copy untouched and keeps the download on the PC");
            Program.Asked.Clear();

            panel.Converting(Folder("zm_five"));
            Program.Check(Map("zm_five").Local == LocalCopies.None && !Map("zm_five").CanDelete && Map("zm_five").Note == "On the console" && File.Exists(Path.Combine(Folder("zm_five"), "zm_five.ff"))
                && CommunityLibrary.Load(libraryFile).Check(Map("zm_five"), Folder("zm_five")) == LocalCopies.None,
                "converting into a downloaded map's folder makes it yours: it no longer counts as a download, so nothing here deletes it");

            SettingsWindow dialog = Program.Settled(new SettingsWindow(null));
            string about = (string)((CheckBox)dialog.FindName("SendSwitch")).Tag;
            Console.WriteLine($"   setting: {about}");
            string more = (string)((CheckBox)dialog.FindName("SendSwitch")).ToolTip;
            Program.Check((string)((CheckBox)dialog.FindName("SendSwitch")).Content == "Auto-FTP" && about == "Sends conversions and downloads when ready."
                && more.Contains("only deleted once the console has confirmed every file") && more.Contains("stay on this PC"),
                "the Auto-FTP option says what it sends, and its tooltip that a download's PC copy is only deleted once the console has confirmed every file");
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
