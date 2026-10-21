namespace FFPorter.Desktop.Services;

public sealed class PauseGate
{
    private readonly object _sync = new();
    private TaskCompletionSource? _held;
    private CancellationTokenSource _asked = new();

    public bool IsPaused
    {
        get
        {
            lock (_sync)
                return _held != null;
        }
    }

    public CancellationToken Asked
    {
        get
        {
            lock (_sync)
                return _asked.Token;
        }
    }

    public void Pause()
    {
        CancellationTokenSource asked;
        lock (_sync)
        {
            if (_held != null)
                return;
            _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            asked = _asked;
        }
        asked.Cancel();
    }

    public void Resume()
    {
        TaskCompletionSource? held;
        lock (_sync)
        {
            held = _held;
            if (held == null)
                return;
            _held = null;
            _asked = new CancellationTokenSource();
        }
        held.SetResult();
    }

    public Task WaitAsync(CancellationToken token)
    {
        Task? held;
        lock (_sync)
            held = _held?.Task;
        return held != null ? held.WaitAsync(token) : token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;
    }

    public void Wait(CancellationToken token) => WaitAsync(token).GetAwaiter().GetResult();
}
