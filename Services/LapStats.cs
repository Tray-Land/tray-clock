using System.Globalization;
using System.Text;

namespace TrayClock.Services;

internal enum LapRank
{
    None,
    Fastest,
    Slowest,
}

/// <summary>One recorded lap: its own time (split) and the stopwatch total when it was taken.</summary>
internal readonly record struct Lap(int Number, TimeSpan Split, TimeSpan Total, LapRank Rank);

/// <summary>Turns lap totals into splits and marks the fastest and slowest. Free of WinRT so it is easy to test.</summary>
internal static class LapStats
{
    /// <summary>Laps newest first. Fastest and slowest are only marked once there are two different splits to compare.</summary>
    public static IReadOnlyList<Lap> Build(IReadOnlyList<TimeSpan> totals)
    {
        TimeSpan[] splits = new TimeSpan[totals.Count];
        for (int i = 0; i < totals.Count; i++)
        {
            splits[i] = i == 0 ? totals[0] : totals[i] - totals[i - 1];
        }

        int fastest = -1;
        int slowest = -1;
        if (splits.Length >= 2 && splits.Min() != splits.Max())
        {
            fastest = Array.IndexOf(splits, splits.Min());
            slowest = Array.IndexOf(splits, splits.Max());
        }

        List<Lap> laps = new(splits.Length);
        for (int i = splits.Length - 1; i >= 0; i--)
        {
            LapRank rank = i == fastest ? LapRank.Fastest : i == slowest ? LapRank.Slowest : LapRank.None;
            laps.Add(new Lap(i + 1, splits[i], totals[i], rank));
        }

        return laps;
    }

    /// <summary>
    /// Laps oldest first as tab-separated rows under a header, which Excel and other spreadsheets
    /// paste as one cell per value.
    /// </summary>
    public static string ToTable(IReadOnlyList<TimeSpan> totals)
    {
        StringBuilder table = new("Lap\tSplit\tTotal\tNote\r\n");
        foreach (Lap lap in Build(totals).Reverse())
        {
            string note = lap.Rank switch { LapRank.Fastest => "Fastest", LapRank.Slowest => "Slowest", _ => "" };
            table.Append(CultureInfo.InvariantCulture,
                $"{lap.Number}\t{ClockFormat.Stopwatch(lap.Split)}\t{ClockFormat.Stopwatch(lap.Total)}\t{note}\r\n");
        }

        return table.ToString();
    }
}
