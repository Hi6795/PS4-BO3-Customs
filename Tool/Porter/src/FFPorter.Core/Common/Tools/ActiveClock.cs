using System.Diagnostics;

namespace FFPorter.Core.Common.Tools;

public sealed class ActiveClock
{
    private const int BeatMilliseconds = 250;

    private static readonly long Beat = Stopwatch.Frequency * BeatMilliseconds / 1000, Limit = Stopwatch.Frequency * 2;
    private static readonly object Gate = new();
    private static long _lost, _beatAt = Stopwatch.GetTimestamp();
    private static readonly Thread Pulse = Start();

    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly long _lostAtStart;

    private ActiveClock() => _lostAtStart = Lost(_start);

    public static ActiveClock StartNew() => new();

    public TimeSpan Elapsed
    {
        get
        {
            long now = Stopwatch.GetTimestamp();
            long ticks = Math.Max(0, now - _start - (Lost(now) - _lostAtStart));
            return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
        }
    }

    public long ElapsedMilliseconds => (long)Elapsed.TotalMilliseconds;

    private static long Lost(long now)
    {
        _ = Pulse;
        lock (Gate)
        {
            long pending = now - _beatAt;
            return _lost + (pending > Limit ? pending - Beat : 0);
        }
    }

    private static Thread Start()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(BeatMilliseconds);
                long now = Stopwatch.GetTimestamp();
                lock (Gate)
                {
                    long gap = now - _beatAt;
                    if (gap > Limit)
                        _lost += gap - Beat;
                    _beatAt = now;
                }
            }
        })
        { IsBackground = true, Name = "active clock", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        return thread;
    }
}
