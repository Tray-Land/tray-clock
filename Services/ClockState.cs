using Microsoft.UI.Dispatching;

namespace TrayClock.Services;

/// <summary>A countdown timer. Uses the monotonic tick counter so clock changes don't affect it.</summary>
internal sealed class TimerItem(int id, string label, TimeSpan duration)
{
    private long _endTick;
    private TimeSpan _left = duration;

    public int Id { get; } = id;
    public string Label { get; } = label;
    public TimeSpan Duration { get; } = duration;
    public bool Running { get; private set; }
    public bool Done { get; private set; }

    public TimeSpan Remaining => Done ? TimeSpan.Zero
        : Running ? TimeSpan.FromMilliseconds(Math.Max(0, _endTick - Environment.TickCount64))
        : _left;

    public void Start()
    {
        if (Running || Done)
        {
            return;
        }

        _endTick = Environment.TickCount64 + (long)_left.TotalMilliseconds;
        Running = true;
    }

    public void Pause()
    {
        if (!Running)
        {
            return;
        }

        _left = Remaining;
        Running = false;
    }

    public void Restart()
    {
        _left = Duration;
        Done = false;
        Running = false;
        Start();
    }

    internal bool CompleteIfElapsed()
    {
        if (!Running || Remaining > TimeSpan.Zero)
        {
            return false;
        }

        Running = false;
        Done = true;
        return true;
    }
}

/// <summary>
/// App-lifetime stopwatch and timers. They outlive the flyout window, which closes when idle. The
/// only background work is one one-shot dispatcher timer aimed at the next timer to finish, which
/// exists so a finished timer can alert the user while the flyout is hidden.
/// </summary>
internal static class ClockState
{
    private static readonly System.Diagnostics.Stopwatch Watch = new();
    private static readonly List<TimeSpan> LapTotals = [];
    private static readonly List<TimerItem> TimerList = [];
    private static DispatcherQueueTimer? _alarm;
    private static int _nextId = 1;

    public static event Action<TimerItem>? TimerFinished;

    /// <summary>Tab the flyout should open on next (0 clocks, 1 stopwatch, 2 timers); the flyout consumes it.</summary>
    public static int? PendingTab { get; set; }

    /// <summary>Duration of the most recently started timer, for "Repeat timer".</summary>
    public static TimeSpan? LastDuration { get; private set; }

    // Stopwatch
    public static bool StopwatchRunning => Watch.IsRunning;
    public static TimeSpan StopwatchElapsed => Watch.Elapsed;
    public static IReadOnlyList<TimeSpan> Laps => LapTotals;

    public static void StopwatchToggle()
    {
        if (Watch.IsRunning)
        {
            Watch.Stop();
        }
        else
        {
            Watch.Start();
        }
    }

    public static void StopwatchLap()
    {
        if (Watch.IsRunning)
        {
            LapTotals.Add(Watch.Elapsed);
        }
    }

    public static void StopwatchReset()
    {
        Watch.Reset();
        LapTotals.Clear();
    }

    // Timers
    public static IReadOnlyList<TimerItem> Timers => TimerList;

    public static TimerItem AddTimer(TimeSpan duration, string? label = null)
    {
        TimerItem item = new(_nextId++, string.IsNullOrWhiteSpace(label) ? DefaultLabel(duration) : label.Trim(), duration);
        TimerList.Add(item);
        LastDuration = duration;
        item.Start();
        Reschedule();
        return item;
    }

    public static void ToggleTimer(int id)
    {
        if (TimerList.FirstOrDefault(t => t.Id == id) is not { } item)
        {
            return;
        }

        if (item.Done)
        {
            item.Restart();
        }
        else if (item.Running)
        {
            item.Pause();
        }
        else
        {
            item.Start();
        }

        Reschedule();
    }

    public static void RemoveTimer(int id)
    {
        TimerList.RemoveAll(t => t.Id == id);
        Reschedule();
    }

    private static string DefaultLabel(TimeSpan d) =>
        d.TotalHours >= 1 && d.Minutes == 0 && d.Seconds == 0 ? $"{(int)d.TotalHours} hour timer"
        : d.TotalMinutes >= 1 && d.Seconds == 0 ? $"{(int)d.TotalMinutes} min timer"
        : $"{ClockFormat.Countdown(d)} timer";

    private static void Reschedule()
    {
        _alarm ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _alarm.Stop();

        TimeSpan? next = TimerList.Where(t => t.Running).Select(t => (TimeSpan?)t.Remaining).Min();
        if (next is null)
        {
            return;
        }

        _alarm.Interval = next.Value + TimeSpan.FromMilliseconds(50);
        _alarm.IsRepeating = false;
        _alarm.Tick -= OnAlarm;
        _alarm.Tick += OnAlarm;
        _alarm.Start();
    }

    private static void OnAlarm(DispatcherQueueTimer sender, object args)
    {
        foreach (TimerItem item in TimerList.ToList())
        {
            if (item.CompleteIfElapsed())
            {
                PendingTab = 2;
                TimerFinished?.Invoke(item);
            }
        }

        Reschedule();
    }
}
