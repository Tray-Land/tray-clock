using Microsoft.UI.Dispatching;

namespace TrayClock.Services;

/// <summary>A countdown timer. Uses the monotonic tick counter so clock changes don't affect it.</summary>
internal sealed class TimerItem(int id, string label, TimeSpan duration, bool saved)
{
    private long _endTick;
    private TimeSpan _left = duration;

    public int Id { get; } = id;
    public string Label { get; } = label;
    public TimeSpan Duration { get; } = duration;

    /// <summary>Named or custom: kept (and remembered across restarts) after it finishes and is dismissed. Quick-start timers are not.</summary>
    public bool Saved { get; } = saved;
    public bool Running { get; private set; }

    /// <summary>Finished and going off until the user dismisses or restarts it.</summary>
    public bool Done { get; private set; }

    /// <summary>Stopped at its full duration: new, or dismissed after going off.</summary>
    public bool Ready => !Running && !Done && _left == Duration;

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
        Reset();
        Start();
    }

    public void Reset()
    {
        _left = Duration;
        Done = false;
        Running = false;
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

    /// <summary>Raised when the stopwatch starts, pauses, or resets, or a timer starts, pauses, finishes, or is removed; the tray icon follows it.</summary>
    public static event Action? ActivityChanged;

    /// <summary>The running timer that finishes first, which the tray icon counts down; null when none is running.</summary>
    public static TimerItem? NextTimer => TimerList.Where(t => t.Running).MinBy(t => t.Remaining);

    /// <summary>Every running timer in creation order, so each keeps its own tray icon slot.</summary>
    public static IReadOnlyList<TimerItem> RunningTimers => TimerList.Where(t => t.Running).OrderBy(t => t.Id).ToList();

    /// <summary>Tab the flyout should open on next; the flyout consumes it.</summary>
    public static ClockTab? PendingTab { get; set; }

    /// <summary>The tab worth opening when nothing is running: a finished timer, a stopwatch holding a time, a paused timer, else the clocks.</summary>
    public static ClockTab IdleTab => TabPolicy.ForIdle(
        timerFinished: TimerList.Any(t => t.Done),
        stopwatchHasTime: Watch.Elapsed > TimeSpan.Zero,
        timerPaused: TimerList.Any(t => !t.Running && !t.Done && !t.Ready));

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

        ActivityChanged?.Invoke();
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
        ActivityChanged?.Invoke();
    }

    // Timers
    public static IReadOnlyList<TimerItem> Timers => TimerList;

    /// <summary>Adds the timers remembered from last time, stopped at their full duration. Call once at startup.</summary>
    public static void LoadSaved()
    {
        foreach (SavedTimer saved in SettingsService.SavedTimerList)
        {
            TimerList.Add(new TimerItem(_nextId++, saved.Name, saved.Duration, saved: true));
        }
    }

    /// <summary>Adds a timer and starts it. A custom time or a name makes it a saved timer that outlives its first run.</summary>
    public static TimerItem AddTimer(TimeSpan duration, string? label = null, bool saved = false)
    {
        string name = SavedTimers.CleanName(label);
        saved |= name.Length > 0;
        TimerItem item = new(_nextId++, name.Length > 0 ? name : DefaultLabel(duration), duration, saved);
        TimerList.Add(item);
        LastDuration = duration;
        item.Start();
        if (saved)
        {
            PersistSaved();
        }

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

    /// <summary>Silences a finished timer. A saved one goes back to its full duration, ready to start again; a quick-start one is removed.</summary>
    public static void DismissTimer(int id)
    {
        if (TimerList.FirstOrDefault(t => t.Id == id) is not { } item)
        {
            return;
        }

        if (item.Saved)
        {
            item.Reset();
        }
        else
        {
            TimerList.Remove(item);
        }

        Reschedule();
    }

    public static void RemoveTimer(int id)
    {
        if (TimerList.FirstOrDefault(t => t.Id == id) is { } item)
        {
            TimerList.Remove(item);
            if (item.Saved)
            {
                PersistSaved();
            }
        }

        Reschedule();
    }

    private static void PersistSaved() =>
        SettingsService.SavedTimerList = [.. TimerList.Where(t => t.Saved).Select(t => new SavedTimer(t.Label, t.Duration))];

    private static string DefaultLabel(TimeSpan d) =>
        d.TotalHours >= 1 && d.Minutes == 0 && d.Seconds == 0 ? $"{(int)d.TotalHours} hour timer"
        : d.TotalMinutes >= 1 && d.Seconds == 0 ? $"{(int)d.TotalMinutes} min timer"
        : d.TotalMinutes < 1 ? $"{(int)d.TotalSeconds} sec timer"
        : $"{ClockFormat.Countdown(d)} timer";

    private static void Reschedule()
    {
        ArmAlarm();
        ActivityChanged?.Invoke();
    }

    private static void ArmAlarm()
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
                PendingTab = ClockTab.Timers;
                TimerFinished?.Invoke(item);
            }
        }

        Reschedule();
    }
}
