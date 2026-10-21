using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Desktop;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int _checks, _failures;

    internal static List<string> Asked { get; } = [];

    internal static bool Agree { get; set; } = true;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 1 && args[0] is RunChecks.TickVerb or RunChecks.LeafVerb)
            return RunChecks.Tick(args);
        string tests = Tests();
        string scratch = Path.GetFullPath(args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : Path.Combine(tests, "work", "out"));
        if (Directory.Exists(scratch))
            Directory.Delete(scratch, true);
        Directory.CreateDirectory(scratch);
        Environment.SetEnvironmentVariable("FFPORTER_WORK", Path.Combine(scratch, "work"));
        Edition.Use(new T7Edition());
        string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS4 FF Porter");
        string[] kinds = [Edition.Current.SettingsFile, $"community.{Edition.Current.Codename}.json"];
        string own = Path.Combine(scratch, "settings");
        Directory.CreateDirectory(own);
        foreach (string kind in kinds.Where(kind => File.Exists(Path.Combine(real, kind))))
            File.Copy(Path.Combine(real, kind), Path.Combine(own, kind), true);
        Environment.SetEnvironmentVariable(AppSettings.FolderVariable, own);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        FFPorter.Desktop.Theme.ThemeManager.Initialize(app);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        typeof(MessageDialog).GetField("_answer", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, (Func<string, string, bool>)((heading, body) =>
        {
            Asked.Add($"{heading} | {body}");
            return Agree;
        }));

        string[] guarded = [.. kinds.Select(kind => Path.Combine(real, kind))];
        byte[]?[] kept = [.. guarded.Select(file => File.Exists(file) ? File.ReadAllBytes(file) : null)];
        try
        {
            string probes = Path.Combine(tests, "work");
            string published = Path.GetFullPath(Path.Combine(tests, "..", "publish", "PS4 .FF Porter.exe"));
            string[] only = args.Contains("--only") ? args[Array.IndexOf(args, "--only") + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
            bool Wanted(string name) => only.Length == 0 || only.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (args.Contains("--real"))
            {
                RealChecks.Run(scratch);
                RealChecks.Update(published);
            }
            else if (args.Contains("--real-download"))
            {
                RealChecks.Download(scratch, args[Array.IndexOf(args, "--real-download") + 1]);
            }
            else if (args.Contains("--real-update"))
            {
                RealChecks.Install(scratch, probes);
            }
            else if (args.Contains("--shots"))
            {
                PreviewShots.Reports = Path.GetFullPath(args.Contains("--reports") ? args[Array.IndexOf(args, "--reports") + 1] : Path.Combine(tests, "..", "publish", "work", "reports"));
                PreviewShots.Run(scratch, [.. args.SkipWhile(arg => arg != "--shots").Skip(1)]);
            }
            else if (args.Contains("--console"))
            {
                RealChecks.Console(args[Array.IndexOf(args, "--console") + 1]);
            }
            else
            {
                if (Wanted("settings"))
                    SettingsFile();
                if (Wanted("cleanup"))
                    CleanupRules(Path.Combine(scratch, "cases"));
                if (Wanted("windows"))
                    Windows(scratch);
                if (Wanted("ftp"))
                    FtpChecks.Run(scratch);
                if (Wanted("community"))
                    CommunityChecks.Run(scratch);
                if (Wanted("pause"))
                    PauseChecks.Run(scratch);
                if (Wanted("delete"))
                    DeleteChecks.Run(scratch);
                if (Wanted("extras"))
                    ExtraChecks.Run(scratch);
                if (Wanted("console"))
                    ConsoleChecks.Run(scratch);
                if (Wanted("installed"))
                    InstalledChecks.Run(scratch);
                if (Wanted("converted"))
                    ConvertedChecks.Run(scratch);
                if (Wanted("shell"))
                    ShellChecks.Run(scratch);
                if (Wanted("scale"))
                    ScaleChecks.Run();
                if (Wanted("runs"))
                    RunChecks.Run(scratch);
                if (Wanted("sound"))
                    SoundChecks.Run(scratch);
                if (Wanted("fidelity"))
                    FidelityChecks.Run();
                if (Wanted("shaders"))
                    ShaderChecks.Run();
                if (Wanted("scripts"))
                    ScriptChecks.Run();
                if (Wanted("acts"))
                    ScriptChecks.Acts(scratch);
                if (Wanted("estimate"))
                    EstimateChecks.Run(scratch);
                if (Wanted("updates"))
                    UpdateChecks.Run(scratch, probes);
            }
        }
        finally
        {
            for (int index = 0; index < guarded.Length; index++)
            {
                byte[]? now = File.Exists(guarded[index]) ? File.ReadAllBytes(guarded[index]) : null;
                if (now == null && kept[index] == null || now != null && kept[index] != null && now.AsSpan().SequenceEqual(kept[index]))
                    continue;
                if (kept[index] == null)
                    File.Delete(guarded[index]);
                else
                    File.WriteAllBytes(guarded[index], kept[index]!);
                Check(false, $"the checks changed {guarded[index]}; it was put back as it was");
            }
            Check(true, "the real settings files were left untouched");
        }

        Console.WriteLine($"\n{_checks} checks, {_failures} failed");
        return _failures == 0 ? 0 : 1;
    }

    private static string Tests()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            if (folder.Name.Equals("tests", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(folder.FullName, "FFPorter.Checks")))
                return folder.FullName;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    }

    internal static void Check(bool ok, string what)
    {
        _checks++;
        if (!ok)
            _failures++;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    }

    private static void SettingsFile()
    {
        Console.WriteLine("== settings file");
        Type file = typeof(AppSettings).GetNestedType("UiFile", BindingFlags.NonPublic)!;
        object old = JsonSerializer.Deserialize("{\"Output\":null,\"GameFolder\":\"D:\\\\bo3\\\\zone\",\"Async\":true,\"MultiMap\":true}", file)!;
        Check((bool)file.GetProperty("MultiConversion")!.GetValue(old)!, "an older file's MultiMap is read as Multi-conversion");
        Check(!(bool)file.GetProperty("DeleteOriginals")!.GetValue(old)! && !(bool)file.GetProperty("DeleteCache")!.GetValue(old)!, "an older file leaves both clean-up options off");
        object fresh = Activator.CreateInstance(file)!;
        file.GetProperty("MultiConversion")!.SetValue(fresh, true);
        file.GetProperty("DeleteOriginals")!.SetValue(fresh, true);
        file.GetProperty("DeleteCache")!.SetValue(fresh, true);
        string json = JsonSerializer.Serialize(fresh, file);
        Console.WriteLine("   " + json);
        Check(json.Contains("\"MultiMap\":true") && json.Contains("\"DeleteOriginals\":true") && json.Contains("\"DeleteCache\":true") && !json.Contains("MultiConversion"),
            "the saved file keeps the MultiMap key and adds the two new ones");
        Check(json.Contains("\"ConsoleAddress\":null") && json.Contains("\"ConsoleStorage\":null") && json.Contains("\"SendToConsole\":false"), "the console's address, the place to send to and Auto-FTP are saved with them");
    }

    private static Job MakeJob(string main, string? mod = null) =>
        new() { Kind = mod == null ? "Map" : "Mod", Name = Path.GetFileNameWithoutExtension(main), MainFile = main, Mod = mod };

    private static void Touch(string path, int bytes = 1000)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[bytes];
        if (path.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
            "TAff"u8.CopyTo(data);
        File.WriteAllBytes(path, data);
    }

    private static string[] Left(string root) =>
        Directory.Exists(root) ? [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(root, p)).Order()] : ["<gone>"];

    private static void CleanupRules(string cases)
    {
        Console.WriteLine("== deleting originals");
        Edition edition = Edition.Current;

        string a = Path.Combine(cases, "usermaps", "zm_fake");
        foreach (string file in (string[])["zone/zm_fake.ff", "zone/en_zm_fake.ff", "zone/fr_zm_fake.ff", "zone/zm_fake.xpak", "zone/en_zm_fake.xpak", "zone/zm_fake_d.xpak",
            "zone/snd/all/zm_fake.all.sabl", "zone/snd/all/zm_fake.all.sabs", "zone/snd/en/zm_fake.en.sabl", "workshop.json", "previewimage.png", "loadingimage.png", "video/intro.mkv", "video/intro.mp4"])
            Touch(Path.Combine(a, file));
        Removal removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(a, "zone", "zm_fake.ff"))], edition, null, []);
        Console.WriteLine($"   a: {removal.Files} files, {removal.Bytes} bytes, problems={removal.Problems.Count}, kept={removal.Kept}; left: {string.Join(" | ", Left(a))}");
        Check(removal.Files == 14 && removal.Bytes == 14000 && removal.Problems.Count == 0 && !Directory.Exists(a), "a downloaded map folder goes completely, with its folder");
        Check(Directory.Exists(Path.Combine(cases, "usermaps")), "the folder above it stays");

        string b = Path.Combine(cases, "usermaps", "zm_project");
        foreach (string file in (string[])["zone/zm_project.ff", "zone/en_zm_project.ff", "zone/zm_project.xpak", "zone/snd/all/zm_project.all.sabl", "workshop.json", "previewimage.png",
            "scripts/zm/zm_project.gsc", "zone_source/zm_project.zone", "video/intro.mkv"])
            Touch(Path.Combine(b, file));
        removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(b, "zone", "zm_project.ff"))], edition, null, []);
        Console.WriteLine($"   b: {removal.Files} files; left: {string.Join(" | ", Left(b))}");
        Check(removal.Files == 4 && !Directory.Exists(Path.Combine(b, "zone")), "a map project loses only its built zone files");
        Check(File.Exists(Path.Combine(b, "workshop.json")) && File.Exists(Path.Combine(b, "previewimage.png")) && File.Exists(Path.Combine(b, "video", "intro.mkv"))
            && File.Exists(Path.Combine(b, "scripts", "zm", "zm_project.gsc")) && File.Exists(Path.Combine(b, "zone_source", "zm_project.zone")),
            "its workshop.json, preview, movies, scripts and sources stay");

        string c = Path.Combine(cases, "mods", "1234");
        foreach (string file in (string[])["core_mod.ff", "en_core_mod.ff", "core_mod.xpak", "zm_mod.ff", "en_zm_mod.ff", "zm_mod.xpak", "snd/all/zm_mod.all.sabl", "snd/all/core_mod.all.sabl",
            "workshop.json", "previewimage.png"])
            Touch(Path.Combine(c, file));
        removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(c, "zm_mod.ff"), "1234")], edition, null, []);
        Console.WriteLine($"   c: {removal.Files} files; left: {string.Join(" | ", Left(c))}");
        Check(removal.Files == 4 && File.Exists(Path.Combine(c, "core_mod.ff")) && File.Exists(Path.Combine(c, "core_mod.xpak")) && File.Exists(Path.Combine(c, "snd", "all", "core_mod.all.sabl"))
            && File.Exists(Path.Combine(c, "workshop.json")), "one zone of a mod: the other zone and workshop.json stay");
        removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(c, "core_mod.ff"), "1234")], edition, null, []);
        Console.WriteLine($"   c2: {removal.Files} files; left: {string.Join(" | ", Left(c))}");
        Check(removal.Files == 6 && !Directory.Exists(c), "the last zone of the mod takes workshop.json, the preview and the folder with it");

        string d = Path.Combine(cases, "flat");
        foreach (string file in (string[])["zm_fake.ff", "zm_fake.xpak", "new_zm_fake.ff", "new_zm_fake.xpak", "zm_fake2.ff", "notes.txt", "snd/all/zm_fake.all.sabl", "snd/all/zm_fake2.all.sabl"])
            Touch(Path.Combine(d, file));
        removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(d, "zm_fake.ff"))], edition, null, []);
        Console.WriteLine($"   d: {removal.Files} files; left: {string.Join(" | ", Left(d))}");
        Check(removal.Files == 3 && File.Exists(Path.Combine(d, "new_zm_fake.ff")) && File.Exists(Path.Combine(d, "new_zm_fake.xpak")) && File.Exists(Path.Combine(d, "zm_fake2.ff"))
            && File.Exists(Path.Combine(d, "notes.txt")) && File.Exists(Path.Combine(d, "snd", "all", "zm_fake2.all.sabl")), "maps with similar names in the same folder are untouched");

        string game = Path.Combine(cases, "game", "zone");
        foreach (string file in (string[])["base.xpak", "zm_zod.ff", "en_zm_zod.ff", "zm_zod.xpak", "snd/all/zm_zod.all.sabl", "dlc/zm_island.ff"])
            Touch(Path.Combine(game, file));
        removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(game, "zm_zod.ff"))], edition, null, []);
        Check(removal.Kept != null && removal.Files == 0 && File.Exists(Path.Combine(game, "zm_zod.ff")), $"a zone in the game's own folder is kept ({removal.Kept})");
        removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(game, "dlc", "zm_island.ff"))], edition, game, []);
        Check(removal.Kept != null && File.Exists(Path.Combine(game, "dlc", "zm_island.ff")), "a zone anywhere under the chosen game files folder is kept");

        string e = Path.Combine(cases, "usermaps", "zm_locked");
        foreach (string file in (string[])["zone/zm_locked.ff", "zone/zm_locked.xpak", "workshop.json"])
            Touch(Path.Combine(e, file));
        using (new FileStream(Path.Combine(e, "zone", "zm_locked.xpak"), FileMode.Open, FileAccess.Read, FileShare.Read))
            removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(e, "zone", "zm_locked.ff"))], edition, null, []);
        Console.WriteLine($"   e: {removal.Files} files, problems: {string.Join("; ", removal.Problems)}; left: {string.Join(" | ", Left(e))}");
        Check(removal.Files == 1 && removal.Problems.Count == 1 && File.Exists(Path.Combine(e, "workshop.json")), "a file in use is reported and the rest of the folder is left");

        string f = Path.Combine(cases, "inplace", "zm_same");
        foreach (string file in (string[])["zone/zm_same.ff", "zone/zm_same.xpak", "workshop.json"])
            Touch(Path.Combine(f, file));
        removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(f, "zone", "zm_same.ff"))], edition, null, [Path.Combine(f, "zone")]);
        Check(removal.Kept != null && File.Exists(Path.Combine(f, "zone", "zm_same.ff")), $"converted into its own folder: nothing is deleted ({removal.Kept})");
        removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(f, "zone", "zm_same.ff"))], edition, null, [f]);
        Check(removal.Kept != null && File.Exists(Path.Combine(f, "zone", "zm_same.xpak")), "converted into the map's own package folder: nothing is deleted");
        removal = Cleanup.DeleteOriginals([MakeJob(Path.Combine(f, "zone", "zm_same.ff"))], edition, null, [Path.Combine(cases, "exports", "t7", "zm_same")]);
        Check(removal.Kept == null && removal.Files == 3 && !Directory.Exists(f), "converted somewhere else: the original goes");

        string work = Path.Combine(FFPorter.Core.Workspace.WorkDirectory, "t7");
        Touch(Path.Combine(work, "zm_fake", "snd", "all", "bank"), 5000);
        Touch(Path.Combine(work, "shader_cache", "abc", "ps_1"), 2000);
        Touch(Path.Combine(work, "game_zones.txt"), 10);
        Check(edition.CacheFolder(MakeJob(@"D:\x\zm_fake.ff")) == Path.Combine(work, "zm_fake") && edition.CacheFolder(MakeJob(@"D:\x\zm_mod.ff", "wds")) == Path.Combine(work, "wds.zm_mod"),
            "an item's own cache folder is work\\t7\\<name> (mods: <mod>.<zone>)");
        removal = Cleanup.ClearFolders(edition.CacheFolders());
        Check(removal.Bytes == 7000 && removal.Problems.Count == 0 && File.Exists(Path.Combine(work, "game_zones.txt")) && Directory.GetDirectories(work).Length == 0,
            "clearing the cache removes every cache folder and reports its size");
    }

    private static void Windows(string scratch)
    {
        Console.WriteLine("== windows");
        var settings = AppSettings.Current;
        void Set(string field, bool value) => typeof(AppSettings).GetField(field, Private)!.SetValue(settings, value);
        Set("_async", true);
        Set("_multiConversion", true);
        Set("_deleteOriginals", true);
        Set("_deleteCache", false);

        var window = new MainWindow();
        var content = (UIElement)window.Content;
        window.Content = null;
        var host = Host(window, content, 1364, 841);
        var jobs = (ObservableCollection<Job>)typeof(MainWindow).GetField("_jobs", Private)!.GetValue(window)!;
        MethodInfo command = typeof(MainWindow).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Static)!;
        Set("_multiCore", false);
        var spread = (List<string>)command.Invoke(null, [MakeJob(@"D:\x\zm_fake.ff"), scratch, 3])!;
        Set("_multiCore", true);
        var alone = (List<string>)command.Invoke(null, [MakeJob(@"D:\x\zm_fake.ff"), scratch, 1])!;
        Check(spread.Contains("--async") && string.Join(" ", spread).Contains("--slots 3") && spread.Contains("--single-core") && !alone.Contains("--slots") && !alone.Contains("--single-core"),
            "the converter is told how many conversions share the PC and whether it may use every core, so its estimates match");

        string usermaps = Path.Combine(scratch, "run", "usermaps");
        string mods = Path.Combine(scratch, "run", "mods", "777");
        foreach (string file in (string[])["zone/zm_one.ff", "zone/en_zm_one.ff", "zone/zm_one.xpak", "workshop.json"])
            Touch(Path.Combine(usermaps, "zm_one", file), 4000);
        foreach (string file in (string[])["zone/zm_two.ff", "zone/zm_two.xpak", "workshop.json"])
            Touch(Path.Combine(usermaps, "zm_two", file), 4000);
        foreach (string file in (string[])["core_mod.ff", "zm_mod.ff", "zm_mod.xpak", "workshop.json"])
            Touch(Path.Combine(mods, file), 4000);
        window.AddPaths([usermaps, mods]);
        Console.WriteLine($"   queued: {string.Join(", ", jobs.Select(job => $"{job.Title} ({job.Size})"))}");
        Check(jobs.Count == 4, "four items queued");
        Console.WriteLine($"   idle status: '{Text(window, "StatusText")}' '{Text(window, "DetailText").Trim()}'");
        Check(Text(window, "DetailText").Contains("PC originals are deleted after converting"), "the idle status says originals will be deleted");
        Console.WriteLine("   settings tooltip: " + ((string)((Button)window.FindName("SettingsButton")).ToolTip).Replace("\n", " / "));
        Render(host, Path.Combine(scratch, "main_idle.png"));

        Job one = jobs.First(job => job.Name == "zm_one"), two = jobs.First(job => job.Name == "zm_two");
        Job core = jobs.First(job => job.Name == "core_mod"), zm = jobs.First(job => job.Name == "zm_mod");
        Job[] queue = [.. jobs];
        foreach (Job job in queue)
            job.State = RunStates.Queued;
        Touch(Path.Combine(FFPorter.Core.Workspace.WorkDirectory, "t7", "zm_one", "walk"), 3000);
        Touch(Path.Combine(FFPorter.Core.Workspace.WorkDirectory, "t7", "shader_cache", "x", "y"), 3000);
        Set("_deleteCache", true);

        MethodInfo cleanUp = typeof(MainWindow).GetMethod("CleanUpAfter", Private)!;
        long After(Job job) => Wait((Task<long>)cleanUp.Invoke(window, [job, queue])!);
        void Finish(Job job, string state, int? percent = null)
        {
            job.State = state;
            if (percent is int score)
                job.Report = FidelityView.From(new FidelitySnapshot { Map = job.Title, State = FidelityStates.Done, Stage = "Finished", Progress = 1, Percent = score, Score = score, Grade = "strong" });
        }

        Finish(one, RunStates.Done, 97);
        long freed = After(one);
        Check(freed == 12000 + 4000 + 3000 && one.OriginalDeleted && !Directory.Exists(Path.Combine(usermaps, "zm_one")) && !Directory.Exists(Path.Combine(FFPorter.Core.Workspace.WorkDirectory, "t7", "zm_one")),
            $"a converted map: originals, folder and its own cache go ({freed} bytes)");
        Check(Directory.Exists(Path.Combine(FFPorter.Core.Workspace.WorkDirectory, "t7", "shader_cache")), "the shared cache waits for the end of the run");

        Finish(two, RunStates.Failed);
        freed = After(two);
        Check(freed == 0 && !two.OriginalDeleted && File.Exists(Path.Combine(usermaps, "zm_two", "zone", "zm_two.ff")), "a failed map keeps its originals");

        Finish(core, RunStates.Done, 100);
        freed = After(core);
        Check(freed == 0 && !core.OriginalDeleted && File.Exists(Path.Combine(mods, "core_mod.ff")), "a mod zone waits while another zone from its folder is still queued");
        Finish(zm, RunStates.Done, 99);
        freed = After(zm);
        Check(freed == 16000 && core.OriginalDeleted && zm.OriginalDeleted && !Directory.Exists(mods), $"once the whole mod converted, its folder goes ({freed} bytes)");

        MethodInfo clear = typeof(MainWindow).GetMethod("ClearCacheAfterRun", Private)!;
        freed = Wait((Task<long>)clear.Invoke(window, null)!);
        Check(freed == 3000 && !Directory.Exists(Path.Combine(FFPorter.Core.Workspace.WorkDirectory, "t7", "shader_cache")), "the end of the run clears the shared cache");

        var log = (FFPorter.Desktop.Controls.LogView)window.FindName("Log");
        Console.WriteLine("   log:\n     " + ((StringBuilder)typeof(FFPorter.Desktop.Controls.LogView).GetField("_pending", Private)!.GetValue(log)!).ToString().TrimEnd().Replace("\n", "\n     "));

        typeof(MainWindow).GetMethod("ShowTotals", Private)!.Invoke(window, null);
        typeof(MainWindow).GetMethod("SetBusy", Private)!.Invoke(window, [false]);
        Check(((Button)window.FindName("ConvertButton")).IsEnabled, "Convert stays available: the failed map can run again");
        Console.WriteLine($"   rows: {string.Join(", ", jobs.Select(job => $"{job.Title}: note='{job.Note}' score={job.Report?.PercentText}"))}");
        Console.WriteLine($"   total: '{((TextBlock)window.FindName("TotalText")).Text}'");
        Render(host, Path.Combine(scratch, "main_after.png"));

        two.OriginalDeleted = true;
        typeof(MainWindow).GetMethod("RefreshQueue", Private)!.Invoke(window, null);
        Console.WriteLine($"   all deleted: '{Text(window, "StatusText")}' '{Text(window, "DetailText").Trim()}' convert enabled={((Button)window.FindName("ConvertButton")).IsEnabled}");
        Check(!((Button)window.FindName("ConvertButton")).IsEnabled, "nothing left to convert disables Convert");
        Touch(Path.Combine(usermaps, "zm_one", "zone", "zm_one.ff"), 4000);
        window.AddPaths([Path.Combine(usermaps, "zm_one")]);
        Check(jobs.Count(job => job.Name == "zm_one") == 1 && !jobs.First(job => job.Name == "zm_one").OriginalDeleted, "adding the map again replaces its finished row");

        Set("_deleteOriginals", false);
        Set("_multiConversion", false);
        var dialog = new SettingsWindow(null);
        var body = (UIElement)dialog.Content;
        dialog.Content = null;
        var frame = Host(dialog, body, SettingsWidth, double.NaN);
        Render(frame, Path.Combine(scratch, "settings.png"));
        Set("_async", false);
        typeof(SettingsWindow).GetMethod("ShowSettings", Private)!.Invoke(dialog, null);
        Check(!((CheckBox)dialog.FindName("MultiConversionSwitch")).IsEnabled && ((CheckBox)dialog.FindName("MultiConversionSwitch")).IsChecked == false, "Multi-conversion is off and locked while Parallel is off");
        Render(frame, Path.Combine(scratch, "settings_async_off.png"));
        var confirm = (Window)typeof(MessageDialog).GetConstructors(Private)[0].Invoke([null, DialogKind.Warning, "Delete PC originals after converting?",
            "From now on, whatever converts with no problems has its PC files deleted for good: its zones, xpaks and sound banks. "
            + "They do not go to the Recycle Bin, so the only way to get them back is to download or build them again. "
            + "A map or mod folder that holds nothing else afterwards is removed too.\n\n"
            + "If anything from the same folder fails or is cancelled, that folder is left alone. Your Black Ops III game files are never deleted.",
            "Delete originals", "Cancel", true]);
        var confirmBody = (UIElement)confirm.Content;
        confirm.Content = null;
        Render(Host(confirm, confirmBody, 484, double.NaN), Path.Combine(scratch, "confirm.png"));
        foreach (string name in (string[])["AsyncSwitch", "MultiConversionSwitch", "MultiCoreSwitch", "GpuVideoSwitch", "DeleteOriginalsSwitch", "DeleteCacheSwitch"])
            Console.WriteLine($"   {((CheckBox)dialog.FindName(name)).Content}: {((CheckBox)dialog.FindName(name)).Tag}");
    }

    private static string Text(MainWindow window, string name) => ((Run)window.FindName(name)).Text;

    private static T Wait<T>(Task<T> task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        return task.GetAwaiter().GetResult();
    }

    internal const double SettingsWidth = 1004;

    internal static SettingsWindow Settled(SettingsWindow dialog)
    {
        FieldInfo probing = typeof(SettingsWindow).GetField("_probing", Private)!;
        CommunityChecks.Pump(() => !(bool)probing.GetValue(dialog)!, 30);
        return dialog;
    }

    internal static Border Host(Window window, UIElement content, double width, double height)
    {
        var host = new Border { Child = content, Background = window.Background, Width = width };
        if (!double.IsNaN(height))
            host.Height = height;
        TextOptions.SetTextFormattingMode(host, TextOptions.GetTextFormattingMode(window));
        TextElement.SetFontFamily(host, window.FontFamily);
        TextElement.SetFontSize(host, window.FontSize);
        TextElement.SetForeground(host, window.Foreground);
        return host;
    }

    internal static void Render(Border host, string path)
    {
        host.Measure(new Size(host.Width, double.IsNaN(host.Height) ? double.PositiveInfinity : host.Height));
        Size size = new(host.Width, double.IsNaN(host.Height) ? host.DesiredSize.Height : host.Height);
        host.Arrange(new Rect(size));
        host.UpdateLayout();
        host.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        host.UpdateLayout();
        const double scale = 1.5;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }
}
