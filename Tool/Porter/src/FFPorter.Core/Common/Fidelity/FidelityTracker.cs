using System.Diagnostics;
using System.Globalization;

namespace FFPorter.Core.Common.Fidelity;

public sealed class FidelityTracker
{
    private sealed class NoteState
    {
        public required FidelityGrade? Grade { get; init; }
        public required Func<long, string?, string> Text { get; init; }
        public required int Order { get; init; }
        public long Count { get; set; }
        public string? Sample { get; set; }
    }

    private sealed class Part
    {
        public readonly long[] Units = new long[Enum.GetValues<FidelityGrade>().Length];
        public readonly Dictionary<string, (string Singular, string Plural, long Count, int Order)> Contents = new(StringComparer.Ordinal);
        public readonly Dictionary<string, NoteState> Notes = new(StringComparer.Ordinal);
        public bool Changed;

        public long Total => Units.Sum();

        public double? Score => Total == 0 ? null : Units.Select((count, grade) => count * FidelityScale.Score((FidelityGrade)grade)).Sum() / Total;

        public bool AllExact => Total > 0 && Units[(int)FidelityGrade.Exact] == Total;
    }

    private sealed class StepState
    {
        public required string Key { get; init; }
        public required string Title { get; init; }
        public string State { get; set; } = FidelityStepStates.Waiting;
        public double Progress { get; set; }
        public string Detail { get; set; } = "";
        public long Started { get; set; } = -1;
        public long Ended { get; set; } = -1;
        public bool Failed { get; set; }
    }

    private readonly Part[] _parts = FidelityScale.Dimensions.Select(_ => new Part()).ToArray();
    private readonly List<StepState> _steps = [];
    private readonly Action<FidelitySnapshot>? _emit;
    private readonly Tools.ActiveClock _clock = Tools.ActiveClock.StartNew();
    private readonly List<string> _problems = [];
    private readonly HashSet<string> _placed = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _lastEmit = long.MinValue / 2;
    private string _stage = "", _detail = "", _state = FidelityStates.Running, _headline = "";
    private double _progress;
    private double? _remaining;
    private int _order;

    public FidelityTracker(string game, string map, Action<FidelitySnapshot>? emit)
    {
        Game = game;
        Map = map;
        _emit = emit;
    }

    public string Game { get; }
    public string Map { get; }

    public string Subject { get; set; } = "map";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(250);

    public bool Overlap { get; set; }

    public string State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (_gate)
                return [.. _problems];
        }
    }

    public void Add(FidelityDimension dimension, FidelityGrade grade, long count = 1)
    {
        if (count <= 0)
            return;
        lock (_gate)
        {
            Part part = _parts[(int)dimension];
            part.Units[(int)grade] += count;
            part.Changed = true;
            Tick();
        }
    }

    public void Downgrade(FidelityDimension dimension, FidelityGrade grade, long count = 1)
    {
        if (count <= 0 || grade == FidelityGrade.Exact)
            return;
        lock (_gate)
        {
            Part part = _parts[(int)dimension];
            long moved = Math.Min(count, part.Units[(int)FidelityGrade.Exact]);
            part.Units[(int)FidelityGrade.Exact] -= moved;
            part.Units[(int)grade] += count;
            part.Changed = true;
            Tick();
        }
    }

    public void Content(FidelityDimension dimension, string singular, string plural, long count = 1)
    {
        if (count <= 0)
            return;
        lock (_gate)
        {
            Part part = _parts[(int)dimension];
            part.Contents[plural] = part.Contents.TryGetValue(plural, out var existing)
                ? (singular, plural, existing.Count + count, existing.Order)
                : (singular, plural, count, _order++);
        }
    }

    public void Note(FidelityDimension dimension, string key, FidelityGrade? grade, long count, Func<long, string?, string> text, string? sample = null)
    {
        if (count <= 0)
            return;
        lock (_gate)
        {
            Part part = _parts[(int)dimension];
            if (!part.Notes.TryGetValue(key, out NoteState? note))
                part.Notes[key] = note = new NoteState { Grade = grade, Text = text, Order = _order++ };
            note.Count += count;
            note.Sample ??= sample;
            part.Changed = true;
        }
    }

    public void AddStep(string key, string title)
    {
        lock (_gate)
        {
            if (!_steps.Exists(s => s.Key == key))
                _steps.Add(new StepState { Key = key, Title = title });
        }
    }

    public void StartStep(string key, string detail = "")
    {
        lock (_gate)
        {
            if (FindStep(key) is not { State: FidelityStepStates.Waiting } step)
                return;
            if (!Overlap)
            {
                foreach (StepState running in _steps.Where(s => s.State == FidelityStepStates.Running))
                    EndStep(running, _state);
            }
            step.State = FidelityStepStates.Running;
            step.Started = _clock.ElapsedMilliseconds;
            step.Detail = detail;
            Tick();
        }
    }

    public void StepProgress(string key, double fraction, string? detail = null)
    {
        lock (_gate)
        {
            if (FindStep(key) is not { State: FidelityStepStates.Running } step)
                return;
            step.Progress = Math.Clamp(Math.Max(step.Progress, fraction), 0, 1);
            if (detail != null)
                step.Detail = detail;
            Tick();
        }
    }

    public void FailStep(string key)
    {
        lock (_gate)
        {
            if (FindStep(key) is { } step)
                step.Failed = true;
        }
    }

    public void SkipStep(string key)
    {
        lock (_gate)
        {
            if (FindStep(key) is not { State: FidelityStepStates.Waiting } step)
                return;
            step.State = FidelityStepStates.Skipped;
            Tick();
        }
    }

    public void EndStep(string key)
    {
        lock (_gate)
        {
            if (FindStep(key) is not { } step)
                return;
            if (step.State == FidelityStepStates.Running)
                EndStep(step, _state);
            else if (step.State == FidelityStepStates.Waiting)
                step.State = step.Failed ? FidelityStepStates.Failed : FidelityStepStates.Skipped;
            else
                return;
            Tick();
        }
    }

    public void PlaceNext(string key)
    {
        lock (_gate)
        {
            int at = _steps.FindIndex(s => s.Key == key);
            if (at < 0 || _steps[at].State != FidelityStepStates.Waiting || !_placed.Add(key))
                return;
            int target = _steps.FindLastIndex(s => s.State != FidelityStepStates.Waiting || (s.Key != key && _placed.Contains(s.Key))) + 1;
            if (target >= at)
                return;
            StepState step = _steps[at];
            _steps.RemoveAt(at);
            _steps.Insert(target, step);
        }
    }

    private StepState? FindStep(string key) => _steps.Find(s => s.Key == key);

    private void EndStep(StepState step, string state)
    {
        step.State = step.Failed ? FidelityStepStates.Failed : state == FidelityStates.Cancelled ? FidelityStepStates.Cancelled : FidelityStepStates.Done;
        if (step.State == FidelityStepStates.Done)
            step.Progress = 1;
        step.Ended = _clock.ElapsedMilliseconds;
    }

    public void Problem(string text)
    {
        lock (_gate)
        {
            if (_problems.Count < 200)
                _problems.Add(text);
            Tick();
        }
    }

    public void Stage(string stage, string detail = "")
    {
        lock (_gate)
        {
            _stage = stage;
            _detail = detail;
            Emit();
        }
    }

    public void Detail(string detail)
    {
        lock (_gate)
        {
            _detail = detail;
            Tick();
        }
    }

    public void Progress(double fraction)
    {
        lock (_gate)
        {
            _progress = Math.Clamp(Math.Max(_progress, fraction), 0, 1);
            Tick();
        }
    }

    public void Remaining(double? seconds)
    {
        lock (_gate)
            _remaining = seconds is double left && double.IsFinite(left) ? Math.Max(0, left) : null;
    }

    public FidelitySnapshot Finish(string state, string headline)
    {
        lock (_gate)
        {
            _state = state;
            _headline = headline;
            if (state == FidelityStates.Done)
                _progress = 1;
            _stage = state switch { FidelityStates.Done => "Finished", FidelityStates.Cancelled => "Cancelled", _ => "Failed" };
            _detail = "";
            foreach (StepState step in _steps)
            {
                if (step.State == FidelityStepStates.Running)
                    EndStep(step, state);
                else if (step.State == FidelityStepStates.Waiting)
                    step.State = FidelityStepStates.Skipped;
            }
            FidelitySnapshot snapshot = TakeSnapshot();
            _emit?.Invoke(snapshot);
            _lastEmit = _clock.ElapsedMilliseconds;
            return snapshot;
        }
    }

    public (double? Score, FidelityGrade? Grade) Overall()
    {
        lock (_gate)
        {
            double weighted = 0, weights = 0;
            bool allExact = true;
            foreach (FidelityDimension dimension in FidelityScale.Dimensions)
            {
                Part part = _parts[(int)dimension];
                if (part.Score is not double score)
                    continue;
                weighted += score * FidelityScale.Weight(dimension);
                weights += FidelityScale.Weight(dimension);
                allExact &= part.AllExact;
            }
            if (weights == 0)
                return (null, null);
            double overall = weighted / weights;
            return (overall, FidelityScale.GradeOf(overall, allExact));
        }
    }

    public string Headline(bool built, int problems)
    {
        if (!built)
            return problems == 0
                ? "Stopped before the PS4 fastfile was written."
                : $"Not written: {Count(problems, "problem", "problems")} stopped it. See the log.";
        if (problems > 0)
            return $"Written, but it failed its checks ({Count(problems, "problem", "problems")}) and may not load.";
        (double? score, FidelityGrade? grade) = Overall();
        return grade switch
        {
            FidelityGrade.Exact => "Everything converted exactly. Verify in-game.",
            FidelityGrade.Strong when score >= FidelityScale.Full => "Everything came across in PS4 formats. Verify in-game.",
            FidelityGrade.Strong => $"Nearly all of the {Subject} came across. Verify in-game.",
            FidelityGrade.Good => $"Some of the {Subject} was approximated. Test in-game.",
            FidelityGrade.Approximate => $"Much of the {Subject} was approximated or is missing.",
            FidelityGrade.Weak => $"Most of the {Subject} did not come across faithfully.",
            _ => "Built, but nothing in it could be scored.",
        };
    }

    public FidelitySnapshot Snapshot()
    {
        lock (_gate)
            return TakeSnapshot();
    }

    private FidelitySnapshot TakeSnapshot()
    {
        (double? score, FidelityGrade? grade) = Overall();
        List<StepState> running = Overlap && _state == FidelityStates.Running ? _steps.FindAll(s => s.State == FidelityStepStates.Running) : [];
        var snapshot = new FidelitySnapshot
        {
            Game = Game,
            Map = Map,
            State = _state,
            Stage = running.Count > 1 ? $"{running.Count} steps at once" : _stage,
            Detail = running.Count > 1 ? string.Join(" · ", running.Select(s => s.Title)) : _detail,
            Progress = _progress,
            Score = score is double s ? Math.Round(s, 2) : null,
            Percent = score is double p ? FidelityScale.Percent(p, grade == FidelityGrade.Exact) : null,
            Grade = grade is FidelityGrade g ? FidelityScale.Name(g) : null,
            Headline = _state == FidelityStates.Running ? "Converting. Scores fill in as each part converts." : _headline,
            Problems = [.. _problems],
            ElapsedSeconds = Math.Round(_clock.Elapsed.TotalSeconds, 1),
            RemainingSeconds = _state == FidelityStates.Running && _remaining is double left ? Math.Round(left, 1) : null,
            Steps = _steps.Select(s => new FidelityStepReport
            {
                Key = s.Key,
                Title = s.Title,
                State = s.State,
                Progress = Math.Round(s.Progress, 4),
                Detail = s.Detail,
                ElapsedSeconds = s.Started < 0 ? 0 : Math.Round(((s.Ended >= 0 ? s.Ended : _clock.ElapsedMilliseconds) - s.Started) / 1000.0, 1),
            }).ToList(),
        };
        foreach (FidelityDimension dimension in FidelityScale.Dimensions)
        {
            Part part = _parts[(int)dimension];
            double? partScore = part.Score;
            FidelityGrade? partGrade = partScore is double ps ? FidelityScale.GradeOf(ps, part.AllExact) : null;
            snapshot.Dimensions.Add(new FidelityDimensionReport
            {
                Name = FidelityScale.Name(dimension),
                Description = FidelityScale.Description(dimension),
                Score = partScore is double rounded ? Math.Round(rounded, 2) : null,
                Percent = partScore is double percent ? FidelityScale.Percent(percent, part.AllExact) : null,
                Grade = partGrade is FidelityGrade named ? FidelityScale.Name(named) : null,
                Units = part.Total,
                Active = part.Changed && _state == FidelityStates.Running,
                Summary = Summary(part),
                Notes = part.Notes.Values
                    .OrderBy(n => n.Grade is FidelityGrade severity ? -(int)severity : 1)
                    .ThenBy(n => n.Order)
                    .Select(n => new FidelityNote { Grade = n.Grade is FidelityGrade ng ? FidelityScale.Name(ng) : "info", Text = n.Text(n.Count, n.Sample) })
                    .ToList(),
            });
            part.Changed = false;
        }
        return snapshot;
    }

    public void Tick()
    {
        lock (_gate)
        {
            if (_emit != null && _clock.ElapsedMilliseconds - _lastEmit >= Interval.TotalMilliseconds)
                Emit();
        }
    }

    public void Emit()
    {
        if (_emit == null)
            return;
        lock (_gate)
        {
            _lastEmit = _clock.ElapsedMilliseconds;
            _emit(TakeSnapshot());
        }
    }

    public static string Count(long count, string singular, string plural) =>
        count.ToString("N0", CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural);

    private static string Summary(Part part)
    {
        var items = part.Contents.Values.OrderByDescending(c => c.Count).ThenBy(c => c.Order).ToList();
        if (items.Count == 0)
            return "";
        var shown = items.Take(items.Count > 4 ? 3 : 4).Select(c => Count(c.Count, c.Singular, c.Plural)).ToList();
        if (items.Count > 4)
            shown.Add(Count(items.Skip(3).Sum(c => c.Count), "other item", "other items"));
        return (shown.Count == 1 ? shown[0] : string.Join(", ", shown.Take(shown.Count - 1)) + " and " + shown[^1]) + ".";
    }
}
