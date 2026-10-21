using System.Collections.ObjectModel;
using FFPorter.Core.Common.Fidelity;

namespace FFPorter.Desktop.Models;

public sealed class FidelityView : Observable
{
    private string _map = "", _state = FidelityStates.Running, _stage = "", _detail = "", _headline = "", _grade = "", _percentText = "—", _elapsedText = "";
    private string _absentSummary = "", _stepSummary = "";
    private double _progress;
    private bool _hasScore, _hasRunningSteps;
    private int? _percent;
    private double? _remaining;
    private DateTime _remainingAt;
    private bool _held;

    private const double Kept = 0.5, UnderMinute = 45, MostMinutes = 59.5;

    public double? RemainingSeconds => IsRunning && _remaining is double left ? _held ? left : Math.Max(0, left - (DateTime.UtcNow - _remainingAt).TotalSeconds) : null;

    public void Hold(bool held)
    {
        if (held == _held)
            return;
        _remaining = RemainingSeconds;
        _remainingAt = DateTime.UtcNow;
        _held = held;
        foreach (StepView step in Steps)
            step.Hold(held);
    }

    public static string Remaining(double seconds)
    {
        if (seconds < UnderMinute)
            return "under 1 min left";
        double minutes = Math.Round(seconds / 60);
        return minutes <= MostMinutes ? $"about {Math.Max(1, minutes):0} min left" : $"about {Math.Floor(minutes / 60):0}h {minutes % 60:00}m left";
    }

    public FidelityView()
    {
        foreach (FidelityDimension dimension in FidelityScale.Dimensions)
            Dimensions.Add(new DimensionView(FidelityScale.Name(dimension), FidelityScale.Description(dimension)));
    }

    public ObservableCollection<DimensionView> Dimensions { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];
    public ObservableCollection<StepView> Steps { get; } = [];

    public bool HasSteps => Steps.Count > 0;

    public string Map { get => _map; private set => Set(ref _map, value); }
    public string State { get => _state; private set { if (Set(ref _state, value)) { Raise(nameof(IsRunning)); Raise(nameof(StateLabel)); RaiseScore(); } } }
    public string Stage { get => _stage; private set => Set(ref _stage, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public string Grade { get => _grade; private set { if (Set(ref _grade, value)) RaiseScore(); } }
    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }
    public int? Percent { get => _percent; private set => Set(ref _percent, value); }
    public bool HasScore { get => _hasScore; private set { if (Set(ref _hasScore, value)) RaiseScore(); } }
    public string AbsentSummary { get => _absentSummary; private set { if (Set(ref _absentSummary, value)) Raise(nameof(HasAbsent)); } }
    public bool HasAbsent => AbsentSummary.Length > 0;
    public string StepSummary { get => _stepSummary; private set => Set(ref _stepSummary, value); }
    public bool HasRunningSteps { get => _hasRunningSteps; private set => Set(ref _hasRunningSteps, value); }

    public string ScoreGrade => State is FidelityStates.Running or FidelityStates.Done ? Grade : "";

    public bool ShowGrade => HasScore && ScoreGrade.Length > 0;

    private void RaiseScore()
    {
        Raise(nameof(ScoreGrade));
        Raise(nameof(ShowGrade));
    }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string ElapsedText { get => _elapsedText; private set => Set(ref _elapsedText, value); }
    public bool IsRunning => State == FidelityStates.Running;
    public bool HasProblems => Problems.Count > 0;

    public string ProblemSummary => Problems.Count switch
    {
        0 => "",
        1 => Problems[0],
        _ => $"{Problems.Count} problems. First: {Problems[0]}",
    };

    public string StateLabel => State switch
    {
        FidelityStates.Running => "Converting",
        FidelityStates.Done => "Finished",
        FidelityStates.Cancelled => "Cancelled",
        _ => "Failed",
    };

    public static FidelityView From(FidelitySnapshot snapshot)
    {
        var view = new FidelityView();
        view.Update(snapshot);
        return view;
    }

    public void Update(FidelitySnapshot snapshot)
    {
        Map = snapshot.Map;
        State = snapshot.State;
        Stage = snapshot.Stage;
        Detail = snapshot.Detail;
        Headline = snapshot.Headline;
        Grade = snapshot.Grade ?? "";
        HasScore = snapshot.Percent != null;
        Percent = snapshot.Percent;
        PercentText = snapshot.Percent is int percent ? $"{percent}%" : "—";
        Progress = snapshot.Progress;
        ElapsedText = Elapsed(snapshot.ElapsedSeconds);
        if (snapshot.State == FidelityStates.Running && snapshot.RemainingSeconds is double left)
        {
            _remaining = RemainingSeconds is double shown ? shown * Kept + left * (1 - Kept) : left;
            _remainingAt = DateTime.UtcNow;
        }
        else
        {
            _remaining = null;
        }
        for (int i = 0; i < Dimensions.Count && i < snapshot.Dimensions.Count; i++)
            Dimensions[i].Update(snapshot.Dimensions[i], snapshot.State);
        SummarizeAbsent();
        List<FidelityStepReport> steps = snapshot.Steps ?? [];
        if (Steps.Count != steps.Count || Steps.Zip(steps).Any(pair => pair.First.Key != pair.Second.Key || pair.First.Title != pair.Second.Title))
        {
            Steps.Clear();
            foreach (FidelityStepReport step in steps)
                Steps.Add(new StepView(step.Key, step.Title));
            Raise(nameof(HasSteps));
        }
        for (int i = 0; i < Steps.Count; i++)
            Steps[i].Update(steps[i]);
        SummarizeSteps();
        if (!Problems.SequenceEqual(snapshot.Problems))
        {
            Problems.Clear();
            foreach (string problem in snapshot.Problems)
                Problems.Add(problem);
            Raise(nameof(HasProblems));
            Raise(nameof(ProblemSummary));
        }
    }

    public void Interrupt(string state, string headline, IEnumerable<string>? problems = null)
    {
        State = state;
        _remaining = null;
        Stage = state == FidelityStates.Cancelled ? "Cancelled" : "Failed";
        Detail = "";
        Headline = headline;
        foreach (DimensionView dimension in Dimensions)
            dimension.Settle();
        foreach (StepView step in Steps)
            step.Settle(state);
        foreach (string problem in problems ?? [])
            Problems.Add(problem);
        Raise(nameof(HasProblems));
        Raise(nameof(ProblemSummary));
        SummarizeAbsent();
        SummarizeSteps();
    }

    public void Tick()
    {
        foreach (StepView step in Steps)
            step.Tick();
    }

    private void SummarizeAbsent()
    {
        string[] absent = [.. Dimensions.Where(dimension => dimension.IsAbsent).Select(dimension => dimension.Name)];
        AbsentSummary = absent.Length == 0 ? "" : "Not in this zone: " + string.Join(" · ", absent);
    }

    private void SummarizeSteps()
    {
        int done = Steps.Count(step => step.State is FidelityStepStates.Done or FidelityStepStates.Skipped);
        int running = Steps.Count(step => step.IsRunning);
        int failed = Steps.Count(step => step.State == FidelityStepStates.Failed);
        HasRunningSteps = running > 0;
        if (Steps.Count == 0)
        {
            StepSummary = "";
            return;
        }
        var parts = new List<string> { $"{done} of {Steps.Count} steps done" };
        if (running > 0)
            parts.Add($"{running} running");
        if (failed > 0)
            parts.Add($"{failed} failed");
        StepSummary = string.Join(" · ", parts);
    }

    internal static string Elapsed(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m {time.Seconds}s" : $"{time.Seconds}s";
    }
}

public sealed class DimensionView : Observable
{
    private string _percentText = "—", _grade = "", _gradeLabel = "waiting", _summary = "";
    private double _scoreFraction;
    private bool _active, _hasUnits;

    public DimensionView(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }
    public string Description { get; }
    public ObservableCollection<NoteView> Notes { get; } = [];

    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }
    public string Grade { get => _grade; private set => Set(ref _grade, value); }
    public string GradeLabel { get => _gradeLabel; private set { if (Set(ref _gradeLabel, value)) Raise(nameof(IsAbsent)); } }
    public bool IsAbsent => GradeLabel == "not in map";
    public string Summary { get => _summary; private set { if (Set(ref _summary, value)) Raise(nameof(HasSummary)); } }
    public bool HasSummary => Summary.Length > 0;
    public double ScoreFraction { get => _scoreFraction; private set => Set(ref _scoreFraction, value); }
    public bool Active { get => _active; private set => Set(ref _active, value); }
    public bool HasUnits { get => _hasUnits; private set => Set(ref _hasUnits, value); }

    public void Update(FidelityDimensionReport report, string state)
    {
        HasUnits = report.Units > 0;
        Grade = report.Grade ?? "";
        GradeLabel = report.Grade ?? (state switch { FidelityStates.Running => "waiting", FidelityStates.Done => "not in map", _ => "not reached" });
        PercentText = report.Percent is int percent ? $"{percent}%" : "—";
        ScoreFraction = (report.Score ?? 0) / 100.0;
        Summary = report.Summary;
        Active = report.Active;
        if (Notes.Count != report.Notes.Count || Notes.Zip(report.Notes).Any(pair => pair.First.Text != pair.Second.Text || pair.First.Grade != pair.Second.Grade))
        {
            Notes.Clear();
            foreach (FidelityNote note in report.Notes)
                Notes.Add(new NoteView(note.Grade, note.Text));
        }
    }

    public void Settle()
    {
        Active = false;
        if (!HasUnits)
            GradeLabel = "not reached";
    }
}

public sealed record NoteView(string Grade, string Text);

public sealed class StepView : Observable
{
    private string _state = FidelityStepStates.Waiting, _detail = "", _percentText = "", _elapsedText = "";
    private double _progress, _elapsedSeconds;
    private DateTime _seenAt = DateTime.UtcNow;
    private bool _held;

    public StepView(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }
    public string Title { get; }

    public string State { get => _state; private set { if (Set(ref _state, value)) { Raise(nameof(IsRunning)); Raise(nameof(HasDetail)); Raise(nameof(IconState)); } } }
    public string IconState => State == FidelityStepStates.Waiting ? RunStates.Ready : State;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string Detail { get => _detail; private set { if (Set(ref _detail, value)) Raise(nameof(HasDetail)); } }
    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }
    public string ElapsedText { get => _elapsedText; private set => Set(ref _elapsedText, value); }
    public bool IsRunning => State == FidelityStepStates.Running;
    public bool HasDetail => State is FidelityStepStates.Running or FidelityStepStates.Failed && Detail.Length > 0;

    public void Update(FidelityStepReport report)
    {
        State = report.State;
        Progress = report.Progress;
        Detail = report.Detail;
        _elapsedSeconds = report.ElapsedSeconds;
        _seenAt = DateTime.UtcNow;
        Refresh();
    }

    public void Tick()
    {
        if (IsRunning && !_held)
            Refresh();
    }

    public void Hold(bool held)
    {
        if (held == _held)
            return;
        if (IsRunning && held)
            _elapsedSeconds += (DateTime.UtcNow - _seenAt).TotalSeconds;
        _seenAt = DateTime.UtcNow;
        _held = held;
        Refresh();
    }

    public void Settle(string state)
    {
        if (State == FidelityStepStates.Running)
        {
            _elapsedSeconds += (DateTime.UtcNow - _seenAt).TotalSeconds;
            State = state == FidelityStates.Cancelled ? FidelityStepStates.Cancelled : FidelityStepStates.Failed;
        }
        else if (State == FidelityStepStates.Waiting)
        {
            State = FidelityStepStates.Skipped;
        }
        Refresh();
    }

    private void Refresh()
    {
        double seconds = IsRunning && !_held ? _elapsedSeconds + (DateTime.UtcNow - _seenAt).TotalSeconds : _elapsedSeconds;
        PercentText = IsRunning ? $"{Math.Floor(Progress * 100):0}%" : "";
        ElapsedText = State is FidelityStepStates.Waiting or FidelityStepStates.Skipped ? "" : FidelityView.Elapsed(seconds);
    }
}
