using System.IO;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Core.T7.Port;
using FFPorter.Desktop;
using FFPorter.Desktop.Models;

internal static class EstimateChecks
{
    public static void Run(string scratch)
    {
        Console.WriteLine("== estimate: time left for a conversion and for the queue");
        Wording();
        Queue();
        Steps();
        Learning(Path.Combine(scratch, "estimate"));
    }

    private static bool Near(double? value, double expected, double slack = 2) => value is double found && Math.Abs(found - expected) <= slack;

    private static void Wording()
    {
        Program.Check(FidelityView.Remaining(20) == "under 1 min left" && FidelityView.Remaining(60) == "about 1 min left" && FidelityView.Remaining(318) == "about 5 min left"
            && FidelityView.Remaining(3000) == "about 50 min left", "time left reads in whole minutes, and under a minute says so");
        Program.Check(FidelityView.Remaining(3600) == "about 1h 00m left" && FidelityView.Remaining(5400) == "about 1h 30m left", "an hour or more reads in hours and minutes");
        var view = FidelityView.From(new FidelitySnapshot { State = FidelityStates.Running, RemainingSeconds = 300 });
        Program.Check(Near(view.RemainingSeconds, 300), "a running conversion carries the converter's time left");
        view.Update(new FidelitySnapshot { State = FidelityStates.Running });
        bool cleared = view.RemainingSeconds == null;
        view.Update(new FidelitySnapshot { State = FidelityStates.Running, RemainingSeconds = 200 });
        view.Update(new FidelitySnapshot { State = FidelityStates.Done, RemainingSeconds = 200 });
        Program.Check(cleared && view.RemainingSeconds == null, "no time left is shown before the converter has one, or once it has finished");
    }

    private static Job Item(string name, string state, double? estimate, double startedAgo = 0) => new()
    {
        Kind = "Map", Name = name, MainFile = $@"D:\bo3\usermaps\{name}\{name}.ff", State = state, Estimate = estimate,
        StartedAt = state == RunStates.Running ? DateTime.UtcNow.AddSeconds(-startedAgo) : null,
    };

    private static void Queue()
    {
        Job running = Item("zm_a", RunStates.Running, 600, 100), second = Item("zm_b", RunStates.Queued, 300), third = Item("zm_c", RunStates.Queued, 200);
        Job[] all = [running, second, third];
        Program.Check(Near(running.Remaining, 500) && second.Remaining == null, "before the converter reports, a running item counts down from its rough estimate");
        Program.Check(Near(MainWindow.QueueRemaining([running], all, 1), 1000), "one at a time, the queue takes the running item's time plus every waiting one");
        Program.Check(Near(MainWindow.QueueRemaining([running], all, 2), 500), "with two at once, waiting items fill whichever slot frees first");
        running.Report = FidelityView.From(new FidelitySnapshot { State = FidelityStates.Running, RemainingSeconds = 120 });
        Program.Check(Near(running.Remaining, 120) && Near(MainWindow.QueueRemaining([running], all, 1), 620), "the converter's own figure replaces the rough one as soon as it arrives");
        Job unknown = Item("zm_d", RunStates.Queued, null);
        Program.Check(MainWindow.QueueRemaining([running], [running, second, unknown], 1) == null && MainWindow.QueueRemaining([], all, 1) == null,
            "the queue shows no time left while any item has no estimate, or when nothing is running");
        Job late = Item("zm_e", RunStates.Running, 60, 600);
        Program.Check(Near(late.Remaining, 6, 0.5), "an item that outlasts its rough estimate never reads as finished");
    }

    private static void Steps()
    {
        const string walk = "zone:zm_x.ff:walk", shaders = "zone:zm_x.ff:shaders", streams = "zone:zm_x.ff:streams", assets = "zone:zm_x.ff:assets", link = "zone:zm_x.ff:link";
        const string first = "bank:zm_x.all.sabl:all", second = "bank:zm_x.en.sabl:en";
        T7Fidelity Planned(bool together)
        {
            var fidelity = new T7Fidelity("zm_x", null) { Concurrent = together };
            fidelity.AddStep("first", "Sound", [(first, 30)]);
            fidelity.AddStep("second", "Sound", [(second, 30)]);
            fidelity.AddStep("walk", "Read the PC zone", [(walk, 10)]);
            fidelity.AddStep("shaders", "Compile shaders", [(shaders, 5)]);
            fidelity.AddStep("streams", "Convert streamed data", [(streams, 50)]);
            fidelity.AddStep("assets", "Convert assets", [(assets, 40)]);
            fidelity.AddStep("link", "Link and check", [(link, 20)]);
            fidelity.Plan(
            [
                new T7Fidelity.PlanUnit([first], [], "sound", 2), new T7Fidelity.PlanUnit([second], [], "sound", 2), new T7Fidelity.PlanUnit([walk], [], null, 1),
                new T7Fidelity.PlanUnit([shaders], [2], null, 1), new T7Fidelity.PlanUnit([streams], [2], null, 1), new T7Fidelity.PlanUnit([assets], [0, 1, 2, 3, 4], null, 1),
                new T7Fidelity.PlanUnit([link], [5], null, 1),
            ], together, shaders);
            return fidelity;
        }

        T7Fidelity turn = Planned(false);
        bool early = turn.Remaining() == null;
        turn.Reweigh(shaders, 100);
        Program.Check(early && Near(turn.Remaining(), 280), "one step at a time, time left is every unfinished step added up, and only once the shader count is known");
        T7Fidelity together = Planned(true);
        together.Reweigh(shaders, 100);
        Program.Check(Near(together.Remaining(), 170), "side by side, time left follows the longest chain of steps that wait on each other");
        together.Skip("shaders");
        Program.Check(Near(together.Remaining(), 120), "a step that drops out stops counting");
        Program.Check(T7Fidelity.Kind(walk) == "walk" && T7Fidelity.Kind(first) == "bank" && T7Fidelity.Kind("movie:intro.mkv") == "movie" && T7Fidelity.Kind("prepare") == "",
            "each step is timed as its own kind of work");
    }

    private static void Learning(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        string work = Path.Combine(folder, "work", "t7"), map = Path.Combine(folder, "usermaps", "zm_x");
        Directory.CreateDirectory(Path.Combine(map, "snd", "all"));
        void Sized(string path, long megabytes)
        {
            using FileStream stream = File.Create(path);
            stream.SetLength(megabytes << 20);
        }
        string zone = Path.Combine(map, "zm_x.ff");
        Sized(zone, 10);
        Sized(Path.Combine(map, "zm_x.xpak"), 100);
        Sized(Path.Combine(map, "snd", "all", "zm_x.all.sabl"), 20);

        T7Timing timing = T7Timing.Load(T7Timing.PathIn(work));
        var request = new T7Timing.Request(zone, Path.Combine(work, "zm_x"));
        double Step(string kind, double weight, string mode = "") => timing.Seconds(kind, weight, mode);
        double After(string mode = "") => Step("assets", 3.5, mode) + Step("link", 3, mode);
        double walk = Step("walk", 1.3), shaders = timing.ShaderSeconds(10, ""), streams = Step("streams", 6), bank = Step("bank", 7);
        double together = Math.Max(Step("walk", 1.3, "+") + Math.Max(timing.ShaderSeconds(10, "+"), Step("streams", 6, "+")), Step("bank", 7, "+")) + After("+");
        Program.Check(Near(timing.Before(request), walk + shaders + streams + bank + After(), 0.01) && Near(timing.Before(request with { Concurrent = true }), together, 0.01),
            "a waiting map gets a rough estimate from its file sizes, shorter when its steps run side by side");
        Program.Check(timing.ShaderSeconds(10, "") < timing.ShaderSeconds(300, "") && Step("walk", 1.3) < Step("walk", 10) && Step("assets", 1) < 1,
            "bigger zones take longer, and an empty language zone takes about a second, not a fixed guess");
        Directory.CreateDirectory(Path.Combine(work, "zm_x"));
        File.WriteAllText(Path.Combine(work, "zm_x", $"zm_x.{FFPorter.Core.T7.T7FastFileDelta.CacheKey(zone)}.pc.t7walk"), "");
        double cached = Step("walk" + T7Timing.CachedSuffix, 1.3) + Step("shaders", 1.2) + streams + bank + After();
        Program.Check(Near(timing.Before(request), cached, 0.01) && cached < walk + shaders + streams + bank + After() - 5,
            "a zone read in an earlier conversion counts its saved read and its already compiled shaders");
        Program.Check(Near(timing.Before(request with { Shaders = false, Streams = false, Sound = false }), Step("walk" + T7Timing.CachedSuffix, 1.3) + After(), 0.01),
            "switched-off steps are left out");

        double fresh = Step("link", 21);
        timing.Learn("link", 0.01, 30, "");
        timing.Learn("link", 21, 0.01, "");
        Program.Check(Near(Step("link", 21), fresh, 0.001), "steps too short to measure are ignored");
        for (int round = 0; round < 2; round++)
        {
            timing.Learn("assets", 51, 20, "");
            timing.Learn("assets", 11, 5, "");
        }
        Program.Check(Near(Step("assets", 51), 20, 2) && Near(Step("assets", 11), 5, 1.5), "this PC's measured speeds replace the built-in ones after a conversion or two");
        timing.Learn("walk", 10, 40, "");
        for (int zoneCount = 0; zoneCount < 12; zoneCount++)
            timing.Learn("walk", 1.01, 0.5, "");
        Program.Check(Near(Step("walk", 10), 40, 6) && Near(Step("walk", 1.01), 0.5, 0.3),
            "twelve small language zones do not wash out what the big zone of the same map measured");
        timing.LearnShaders(T7FastFileShaderGuess(300), 200, "");
        timing.LearnShaders(T7FastFileShaderGuess(300), 200, "");
        Program.Check(Near(timing.ShaderSeconds(300, ""), 200, 15), "whole-map shader compiles are learned against the zone's size");

        Program.Check(Near(Step("streams", 6, "+x3"), Step("streams", 6) * 1.7 * 1.3, 0.01) && Near(Step("streams", 6, T7Timing.Mode(false, 1, true)), Step("streams", 6) * 2.5, 0.01) && Near(Step("streams", 6, T7Timing.Mode(true, 3, true)), Step("streams", 6) * 1.7 * 1.3 * 2.5, 0.01),
            "several conversions at once, steps side by side and a single core each start from slower built-in speeds");
        Program.Check(Near(Step("assets", 51, "+x3"), 20 * 1.7 * 1.3, 4), "a mode never measured borrows the nearest measured one, scaled");
        timing.Learn("assets", 51, 90, "+x3");
        timing.Learn("assets", 51, 90, "+x3");
        Program.Check(Near(Step("assets", 51, "+x3"), 90, 8) && Near(Step("assets", 51), 20, 2), "each mode keeps its own measured speeds");

        timing.Save();
        T7Timing again = T7Timing.Load(T7Timing.PathIn(work));
        Program.Check(File.Exists(T7Timing.PathIn(work)) && Near(again.Seconds("assets", 51, ""), Step("assets", 51), 0.05) && Near(again.ShaderSeconds(10, ""), timing.ShaderSeconds(10, ""), 0.05)
            && again.Knows("assets", "+x3") && !T7Timing.Load(null).Knows("assets", ""),
            "measured speeds are kept between runs, beside the caches but not inside them");
        File.WriteAllText(T7Timing.PathIn(work), "{\"version\":1,\"rates\":{\"assets\":9}}");
        bool old = Near(T7Timing.Load(T7Timing.PathIn(work)).Seconds("assets", 51, ""), T7Timing.Load(null).Seconds("assets", 51, ""), 0.001);
        File.WriteAllText(T7Timing.PathIn(work), "not json");
        Program.Check(old && Near(T7Timing.Load(T7Timing.PathIn(work)).Seconds("assets", 51, ""), T7Timing.Load(null).Seconds("assets", 51, ""), 0.001),
            "an older or unreadable timing file falls back to the built-in speeds");
        Directory.Delete(folder, true);
    }

    private static double T7FastFileShaderGuess(double megabytes) => 1 + megabytes * 0.05;
}
