namespace TrayClock.Services;

/// <summary>The flyout's tabs, in the order they appear.</summary>
internal enum ClockTab
{
    Clocks = 0,
    Stopwatch = 1,
    Timers = 2,
}

/// <summary>
/// Picks the tab a tray click should land on when the icon isn't showing a live timer or stopwatch.
/// Pure logic with no WinRT dependency, so it is covered by the plain test project.
/// </summary>
internal static class TabPolicy
{
    /// <summary>
    /// The most useful tab with nothing running: a timer that is going off comes first, then a
    /// stopwatch holding a time, then a paused timer; otherwise the world clocks.
    /// </summary>
    public static ClockTab ForIdle(bool timerFinished, bool stopwatchHasTime, bool timerPaused)
    {
        if (timerFinished)
        {
            return ClockTab.Timers;
        }

        if (stopwatchHasTime)
        {
            return ClockTab.Stopwatch;
        }

        return timerPaused ? ClockTab.Timers : ClockTab.Clocks;
    }
}
