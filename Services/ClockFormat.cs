using System.Globalization;

namespace TrayClock.Services;

/// <summary>Pure formatting and time-zone math, free of WinRT so it is easy to test.</summary>
internal static class ClockFormat
{
    /// <summary>Daylight hours (6 AM to 6 PM) at that place, for the sun or moon beside a clock.</summary>
    public static bool IsDaytime(DateTimeOffset local) => local.Hour is >= 6 and < 18;

    /// <summary>"Now", "+3h", "-45m", or "+2h 15m" for the compare-times slider.</summary>
    public static string Shift(TimeSpan shift)
    {
        if (shift == TimeSpan.Zero)
        {
            return "Now";
        }

        string sign = shift < TimeSpan.Zero ? "-" : "+";
        TimeSpan abs = shift.Duration();
        int hours = (int)abs.TotalHours;
        return hours == 0 ? $"{sign}{abs.Minutes}m"
            : abs.Minutes == 0 ? $"{sign}{hours}h"
            : $"{sign}{hours}h {abs.Minutes}m";
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

    /// <summary>The stopwatch text split for display: ("01:24", ".09"), so the hundredths can be drawn smaller.</summary>
    public static (string Whole, string Fraction) StopwatchParts(TimeSpan t)
    {
        string text = Stopwatch(t);
        int dot = text.LastIndexOf('.');
        return (text[..dot], text[dot..]);
    }

    public static string Countdown(TimeSpan t)
    {
        // Round up so a timer reads 00:01 until it actually finishes.
        t = TimeSpan.FromSeconds(Math.Ceiling(t.TotalSeconds));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
