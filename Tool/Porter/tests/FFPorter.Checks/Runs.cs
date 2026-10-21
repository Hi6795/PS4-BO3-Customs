using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Core.Common.Tools;
using FFPorter.Desktop;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal static class RunChecks
{
    public const string TickVerb = "--tick", LeafVerb = "--tick-leaf";

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int Beat = 40, Longest = 60_000;

    public static int Tick(string[] args)
    {
        if (args[0] == LeafVerb)
        {
            using FileStream stream = new(args[1], FileMode.Append, FileAccess.Write, FileShare.Read);
            for (int spent = 0; spent < Longest; spent += Beat)
            {
                stream.WriteByte(1);
                stream.Flush();
                Thread.Sleep(Beat);
            }
            return 0;
        }
        var leaf = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        leaf.ArgumentList.Add(LeafVerb);
        leaf.ArgumentList.Add(args[1]);
        using Process? child = Process.Start(leaf);
        ActiveClock active = ActiveClock.StartNew();
        Stopwatch wall = Stopwatch.StartNew();
        while (wall.ElapsedMilliseconds < Longest)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{active.ElapsedMilliseconds} {wall.ElapsedMilliseconds}"));
            Thread.Sleep(Beat);
        }
        return 0;
    }

    public static void Run(string scratch)
    {
        Console.WriteLine("== pausing a conversion");
        Directory.CreateDirectory(Path.Combine(scratch, "runs"));
        string file = Path.Combine(scratch, "runs", "tick.bin");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(TickVerb);
        start.ArgumentList.Add(file);
        using Process process = Process.Start(start)!;
        int lines = 0;
        string last = "";
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
                return;
            Volatile.Write(ref last, e.Data);
            Interlocked.Increment(ref lines);
        };
        process.BeginOutputReadLine();
        long Size() => File.Exists(file) ? new FileInfo(file).Length : 0;
        bool Until(Func<bool> done, int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            while (!done() && clock.ElapsedMilliseconds < milliseconds)
                Thread.Sleep(20);
            return done();
        }
        (double Active, double Wall) Clocks()
        {
            string[] parts = Volatile.Read(ref last).Split(' ');
            return parts.Length == 2 ? (double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture)) : (0, 0);
        }
        try
        {
            bool started = Until(() => Volatile.Read(ref lines) > 5 && Size() > 5, 15_000);
            (double activeBefore, double wallBefore) = Clocks();
            using ProcessTree.Hold? hold = ProcessTree.Suspend(process.Id);
            Thread.Sleep(300);
            int linesAt = Volatile.Read(ref lines);
            long sizeAt = Size();
            Thread.Sleep(3000);
            bool frozen = Volatile.Read(ref lines) == linesAt && Size() == sizeAt;
            int held = hold?.Count ?? 0;
            hold?.Dispose();
            bool resumed = Until(() => Volatile.Read(ref lines) > linesAt + 5 && Size() > sizeAt + 5, 10_000);
            (double active, double wall) = Clocks();
            double skipped = wall - wallBefore - (active - activeBefore);
            Console.WriteLine($"   held {held} processes; while paused: {(frozen ? "nothing moved" : "something moved")}; after: wall +{wall - wallBefore:0} ms, its own clock +{active - activeBefore:0} ms");
            Program.Check(started && held >= 2 && frozen, "pausing stops the converter and everything it started, not just the first process");
            Program.Check(resumed, "resuming lets all of them carry on where they were");
            Program.Check(skipped is > 2500 and < 3800, "the time spent paused is left out of the converter's own clock, so step times and estimates stay true");
        }
        finally
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        var view = FidelityView.From(new FidelitySnapshot
        {
            Map = "zm_x", State = FidelityStates.Running, Progress = 0.4, ElapsedSeconds = 60, RemainingSeconds = 90,
            Steps = [new FidelityStepReport { Key = "assets", Title = "Convert assets", State = FidelityStepStates.Running, Progress = 0.5, ElapsedSeconds = 10 }],
        });
        view.Hold(true);
        double? leftAt = view.RemainingSeconds;
        Thread.Sleep(1200);
        view.Tick();
        bool still = view.Steps[0].ElapsedText == "10s" && view.RemainingSeconds == leftAt;
        view.Hold(false);
        Thread.Sleep(1200);
        view.Tick();
        Program.Check(still && view.Steps[0].ElapsedText == "11s" && view.RemainingSeconds < leftAt, "a paused row stops counting its step time and its time left, and carries on from there");

        string maps = Path.Combine(scratch, "runs", "usermaps");
        foreach (string name in (string[])["zm_first", "zm_second", "zm_third"])
        {
            string zone = Path.Combine(maps, name, "zone", name + ".ff");
            Directory.CreateDirectory(Path.GetDirectoryName(zone)!);
            var data = new byte[4000];
            "TAff"u8.CopyTo(data);
            File.WriteAllBytes(zone, data);
        }
        var window = new MainWindow();
        window.AddPaths([maps]);
        var jobs = (ObservableCollection<Job>)typeof(MainWindow).GetField("_jobs", Private)!.GetValue(window)!;
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
        object? Call(string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, arguments);
        foreach (Job job in jobs)
        {
            job.State = RunStates.Queued;
            job.StatusText = "Queued";
        }
        typeof(MainWindow).GetField("_count", Private)!.SetValue(window, jobs.Count);
        Call("SetBusy", true);
        try
        {
            Call("JobCancelClick", new Button { DataContext = jobs[1] }, new RoutedEventArgs());
            Program.Check(jobs.Count == 3 && jobs[1] is { State: RunStates.Cancelled, Note: "Cancelled" } && jobs[0].IsQueued && jobs[2].IsQueued && Field<HashSet<Job>>("_dropped").Contains(jobs[1])
                && Field<int>("_done") == 1, "a waiting item can be taken out of the run: it is marked cancelled and the others stay queued");
            Call("JobPauseClick", new Button { DataContext = jobs[0] }, new RoutedEventArgs());
            Program.Check(!jobs[0].IsPaused && jobs[0].PauseText == "Pause", "Pause does nothing for an item that is not converting");
            jobs[0].State = RunStates.Running;
            Field<HashSet<Job>>("_dropped").Add(jobs[0]);
            bool converted = (bool)Call("Finish", jobs[0], -1, new List<string>())!;
            jobs[2].State = RunStates.Running;
            bool other = (bool)Call("Finish", jobs[2], 0, new List<string>())!;
            Program.Check(!converted && jobs[0].State == RunStates.Cancelled && !Field<bool>("_stop") && other && jobs[2].State == RunStates.Done,
                "cancelling one converting item ends only that one as cancelled; the run is not stopped and the next item still converts");
        }
        finally
        {
            Call("SetBusy", false);
        }
    }
}
