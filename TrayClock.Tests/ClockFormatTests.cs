using TrayClock.Services;

namespace TrayClock.Tests;

[TestClass]
public sealed class ClockFormatTests
{
    [TestMethod]
    [DataRow(5, 0, "+5h")]
    [DataRow(-3, -30, "-3:30h")]
    [DataRow(5, 45, "+5:45h")]
    public void Offset_Difference_FormatsSignAndMinutes(int hours, int minutes, string expected)
    {
        Assert.AreEqual(expected, ClockFormat.Offset(new TimeSpan(hours, minutes, 0)));
    }

    [TestMethod]
    public void Stopwatch_UnderOneHour_OmitsHours()
    {
        Assert.AreEqual("01:02.35", ClockFormat.Stopwatch(new TimeSpan(0, 0, 1, 2, 350)));
    }

    [TestMethod]
    public void Stopwatch_OverOneHour_ShowsHours()
    {
        Assert.AreEqual("2:03:04.05", ClockFormat.Stopwatch(new TimeSpan(0, 2, 3, 4, 50)));
    }

    [TestMethod]
    public void Countdown_PartialSecond_RoundsUp()
    {
        Assert.AreEqual("00:01", ClockFormat.Countdown(TimeSpan.FromMilliseconds(100)));
    }

    [TestMethod]
    public void Countdown_OverOneHour_ShowsHours()
    {
        Assert.AreEqual("1:00:00", ClockFormat.Countdown(TimeSpan.FromHours(1)));
    }

    [TestMethod]
    [DataRow(0, "Now")]
    [DataRow(180, "+3h")]
    [DataRow(-45, "-45m")]
    [DataRow(135, "+2h 15m")]
    [DataRow(-720, "-12h")]
    public void Shift_Minutes_FormatsCompactly(int minutes, string expected)
    {
        Assert.AreEqual(expected, ClockFormat.Shift(TimeSpan.FromMinutes(minutes)));
    }

    [TestMethod]
    [DataRow(5, false)]
    [DataRow(6, true)]
    [DataRow(17, true)]
    [DataRow(18, false)]
    public void IsDaytime_Hour_SixToSix(int hour, bool expected)
    {
        Assert.AreEqual(expected, ClockFormat.IsDaytime(new DateTimeOffset(2026, 10, 13, hour, 30, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void StopwatchParts_UnderOneHour_SplitsHundredths()
    {
        Assert.AreEqual(("01:24", ".09"), ClockFormat.StopwatchParts(new TimeSpan(0, 0, 1, 24, 90)));
    }

    [TestMethod]
    public void StopwatchParts_OverOneHour_KeepsHoursInWhole()
    {
        Assert.AreEqual(("2:03:04", ".05"), ClockFormat.StopwatchParts(new TimeSpan(0, 2, 3, 4, 50)));
    }

    [TestMethod]
    public void Describe_SameZone_IsToday()
    {
        Assert.AreEqual("Today", ClockFormat.Describe(DateTimeOffset.Now, TimeZoneInfo.Local));
    }
}
