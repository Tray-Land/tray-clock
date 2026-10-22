using TrayClock.Services;

namespace TrayClock.Tests;

[TestClass]
public sealed class TabPolicyTests
{
    [TestMethod]
    public void ForIdle_NothingToShow_OpensClocks()
    {
        Assert.AreEqual(ClockTab.Clocks, TabPolicy.ForIdle(timerFinished: false, stopwatchHasTime: false, timerPaused: false));
    }

    [TestMethod]
    public void ForIdle_TimerFinished_OpensTimers()
    {
        Assert.AreEqual(ClockTab.Timers, TabPolicy.ForIdle(timerFinished: true, stopwatchHasTime: false, timerPaused: false));
    }

    [TestMethod]
    public void ForIdle_StopwatchPausedWithTime_OpensStopwatch()
    {
        Assert.AreEqual(ClockTab.Stopwatch, TabPolicy.ForIdle(timerFinished: false, stopwatchHasTime: true, timerPaused: false));
    }

    [TestMethod]
    public void ForIdle_TimerPaused_OpensTimers()
    {
        Assert.AreEqual(ClockTab.Timers, TabPolicy.ForIdle(timerFinished: false, stopwatchHasTime: false, timerPaused: true));
    }

    [TestMethod]
    public void ForIdle_FinishedTimerAndStopwatchTime_PrefersTheFinishedTimer()
    {
        Assert.AreEqual(ClockTab.Timers, TabPolicy.ForIdle(timerFinished: true, stopwatchHasTime: true, timerPaused: false));
    }

    [TestMethod]
    public void ForIdle_StopwatchTimeAndPausedTimer_PrefersTheStopwatch()
    {
        Assert.AreEqual(ClockTab.Stopwatch, TabPolicy.ForIdle(timerFinished: false, stopwatchHasTime: true, timerPaused: true));
    }
}
