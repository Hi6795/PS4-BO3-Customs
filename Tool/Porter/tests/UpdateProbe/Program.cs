using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using FFPorter.Desktop.Services;

internal static class Program
{
    private static readonly string Exe = Environment.ProcessPath!;
    private static readonly string Label = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "Label").Value ?? "?";
    private static string _note = "";

    private static void Say(string line)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                File.AppendAllText(_note, $"{DateTime.Now:HH:mm:ss.fff} [{Label} {Path.GetFileName(Exe)}] {line}\n");
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(20);
            }
        }
    }

    private static bool Loaded(string name) => AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == name);

    [STAThread]
    private static int Main(string[] args)
    {
        bool helper = args.Length > 1 && args[0] == AppUpdate.Verb;
        _note = (helper ? args[1] : Exe) + ".log";
        try
        {
            if (helper)
            {
                Say($"helper started packed={AppUpdate.Location != null}");
                int code = AppUpdate.Apply(args[1..], reason => Say($"stranded: {reason}"));
                Say($"helper done code={code}");
                return code;
            }
            if (args.Length > 1 && args[0] == "hazard")
                return Hazard(args[1]);
            if (args.Length > 1 && args[0] == "hold")
            {
                Say("holding");
                for (int tick = 0; tick < 600 && !File.Exists(args[1]); tick++)
                    Thread.Sleep(100);
                Say("released");
                return 0;
            }
            bool failed = args.Length > 0 && args[0] == AppUpdate.Failed;
            if (failed)
                args = args[1..];
            Say($"started failed={failed} packed={AppUpdate.Location != null} built={AppUpdate.Built:o} args={string.Join("|", args)}");
            if (args.Length > 1 && args[0] == "update")
                return Update(new Uri(args[1]), args[2..], false).GetAwaiter().GetResult();
            if (args.Length > 1 && args[0] == "update-exact")
                return Update(new Uri(args[1]), args[2..], true).GetAwaiter().GetResult();
            if (AppUpdate.Location is { } exe)
            {
                AppUpdate.CleanUpAsync(exe).GetAwaiter().GetResult();
                Say($"swept new={File.Exists(exe + ".new")} old={File.Exists(exe + ".old")}");
            }
            if (args.Length > 1 && args[0] == "check")
            {
                UpdateCheck check = AppUpdate.CheckAsync(new Uri(args[1]), Exe, AppUpdate.Name, AppUpdate.Built, CancellationToken.None).GetAwaiter().GetResult();
                Say($"check={check.State}");
            }
            Say("exit");
            return 0;
        }
        catch (Exception error)
        {
            Say($"error: {error}");
            return 2;
        }
    }

    private static async Task<int> Update(Uri releases, string[] pass, bool exact)
    {
        string exe = AppUpdate.Location ?? throw new InvalidOperationException("not a single-file app");
        await AppUpdate.CleanUpAsync(exe);
        UpdateCheck check = await AppUpdate.CheckAsync(releases, exe, AppUpdate.Name, exact ? null : AppUpdate.Built, CancellationToken.None);
        Say($"check={check.State} uploaded={check.File?.Uploaded:o} others={AppUpdate.Others(exe)}");
        if (check.State != UpdateStates.Available)
            return 0;
        await AppUpdate.DownloadAsync(check.File!, exe, new Progress<DownloadProgress>(_ => { }), CancellationToken.None);
        Say($"downloaded {new FileInfo(exe + ".new").Length}");
        AppUpdate.HandOver(exe, exact ? pass : ["check", releases.ToString(), .. pass]);
        Say("handed over");
        return 0;
    }

    private static int Hazard(string other)
    {
        bool before = Loaded("FFPorter.Core");
        File.Move(Exe, Exe + ".aside");
        File.Copy(other, Exe);
        string result;
        try
        {
            result = "loaded " + Lazy();
        }
        catch (Exception error)
        {
            result = $"{error.GetType().Name}: {error.Message}";
        }
        Say($"hazard: loaded before={before}; first use of a bundled library after the exe was swapped under the running app: {result}");
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Lazy() => typeof(FFPorter.Core.Common.Native.NativeChild).Assembly.GetName().Name ?? "";
}
