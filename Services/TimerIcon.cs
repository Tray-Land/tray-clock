namespace TrayClock.Services;

/// <summary>
/// Draws the running-timer tray icon: a thin circle around a donut that drains as time runs out.
/// Pure pixel math with no WinRT dependency, so it is covered by the plain test project.
/// </summary>
internal static class TimerIcon
{
    /// <summary>How many steps the donut drains in; also the most icon updates one timer causes.</summary>
    public const int Segments = 60;

    private const int Samples = 4; // 4x4 supersampling per pixel

    // Shapes in unit coordinates (0..1 across the icon), centered at 0.5.
    private const double OuterRingOuter = 0.50;
    private const double OuterRingInner = 0.42;
    private const double DonutOuter = 0.34;
    private const double DonutInner = 0.14;

    /// <summary>
    /// Fraction of the donut still showing, rounded up to a whole segment so the icon stays full until
    /// the first segment has elapsed and only empties when the timer is done.
    /// </summary>
    public static double Fraction(TimeSpan remaining, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || remaining <= TimeSpan.Zero)
        {
            return 0;
        }

        double fraction = Math.Min(1, remaining / duration);
        return Math.Ceiling(fraction * Segments) / Segments;
    }

    /// <summary>Time from now until <see cref="Fraction"/> next drops, i.e. until the icon needs redrawing.</summary>
    public static TimeSpan UntilNextSegment(TimeSpan remaining, TimeSpan duration)
    {
        TimeSpan segment = duration / Segments;
        if (segment <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        long ticks = remaining.Ticks % segment.Ticks;
        return TimeSpan.FromTicks(ticks == 0 ? segment.Ticks : ticks);
    }

    /// <param name="size">Icon width and height in pixels (1-255).</param>
    /// <param name="fraction">Portion of the donut to draw, 0-1, clockwise from 12 o'clock.</param>
    /// <param name="color">Drawing color as 0xAARRGGBB; the alpha scales the whole glyph.</param>
    public static byte[] Create(int size, double fraction, uint color)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 255);

        double sweep = Math.Clamp(fraction, 0, 1) * 2 * Math.PI;
        uint[] pixels = new uint[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < Samples; sy++)
                {
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        double u = (x + ((sx + 0.5) / Samples)) / size;
                        double v = (y + ((sy + 0.5) / Samples)) / size;
                        if (Covered(u, v, sweep))
                        {
                            hits++;
                        }
                    }
                }

                pixels[(y * size) + x] = IcoEncoder.WithCoverage(color, hits / (double)(Samples * Samples));
            }
        }

        return IcoEncoder.Encode(pixels, size);
    }

    private static bool Covered(double u, double v, double sweep)
    {
        double dx = u - 0.5;
        double dy = v - 0.5;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance <= OuterRingOuter && distance >= OuterRingInner)
        {
            return true;
        }

        if (distance > DonutOuter || distance < DonutInner)
        {
            return false;
        }

        // Angle clockwise from 12 o'clock, 0 to 2*pi.
        double angle = Math.Atan2(dx, -dy);
        if (angle < 0)
        {
            angle += 2 * Math.PI;
        }

        return angle <= sweep;
    }
}
