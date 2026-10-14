using System.Globalization;

namespace TrayClock.Services;

/// <summary>A timer the user named or set a custom time for, kept across restarts.</summary>
internal readonly record struct SavedTimer(string Name, TimeSpan Duration);

/// <summary>Text encoding for the saved timer list, free of WinRT so it is easy to test. One timer per line: seconds, a tab, the name.</summary>
internal static class SavedTimers
{
    public const int MaxNameLength = 40;

    public static string Encode(IEnumerable<SavedTimer> timers) =>
        string.Join('\n', timers.Select(t =>
            $"{(long)t.Duration.TotalSeconds}\t{CleanName(t.Name)}"));

    public static IReadOnlyList<SavedTimer> Decode(string? text)
    {
        List<SavedTimer> result = [];
        foreach (string line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('\t', 2);
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
                && seconds > 0
                && CleanName(parts[1]) is { Length: > 0 } name)
            {
                result.Add(new SavedTimer(name, TimeSpan.FromSeconds(seconds)));
            }
        }

        return result;
    }

    /// <summary>Trims, drops the separator characters, and caps the length.</summary>
    public static string CleanName(string? name)
    {
        string clean = new((name ?? "").Where(c => c is not ('\t' or '\n' or '\r')).ToArray());
        clean = clean.Trim();
        return clean.Length > MaxNameLength ? clean[..MaxNameLength] : clean;
    }
}
