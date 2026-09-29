using System.Globalization;

namespace TrayClock.Services;

/// <summary>Pure formatting and time-zone math, free of WinRT so it is easy to test.</summary>
internal static class ClockFormat
{
    /// <summary>The instant at <paramref name="timeOfDay"/> today in the local zone.</summary>
    public static DateTimeOffset LocalInstant(TimeSpan timeOfDay)
    {
        DateTime local = DateTime.Today + timeOfDay;
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    /// <summary>"Today · +5h", "Tomorrow · -3:30h", or just the day for the local zone.</summary>
    public static string Describe(DateTimeOffset instant, TimeZoneInfo zone)
    {
        DateTimeOffset there = TimeZoneInfo.ConvertTime(instant, zone);
        DateTimeOffset here = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local);
        int days = (there.Date - here.Date).Days;
        string day = days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            _ => there.ToString("ddd", CultureInfo.CurrentCulture),
        };
        TimeSpan diff = there.Offset - here.Offset;
        return diff == TimeSpan.Zero ? day : $"{day} · {Offset(diff)}";
    }

    public static string Offset(TimeSpan diff)
    {
        string sign = diff < TimeSpan.Zero ? "-" : "+";
        TimeSpan abs = diff.Duration();
        return abs.Minutes == 0 ? $"{sign}{abs.Hours}h" : $"{sign}{abs.Hours}:{abs.Minutes:00}h";
    }

    public static string Stopwatch(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";

    public static string Countdown(TimeSpan t)
    {
        // Round up so a timer reads 00:01 until it actually finishes.
        t = TimeSpan.FromSeconds(Math.Ceiling(t.TotalSeconds));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
