namespace TrayClock.Services;

/// <summary>
/// Draws the running-stopwatch tray icon: a round watch with a crown and a single hand. Pure pixel
/// math with no WinRT dependency, so it is covered by the plain test project.
/// </summary>
internal static class StopwatchIcon
{
    private const int Samples = 4; // 4x4 supersampling per pixel

    // Shapes in unit coordinates (0..1 across the icon, y down).
    private const double CenterX = 0.5;
    private const double CenterY = 0.58;
    private const double OuterRadius = 0.40;
    private const double RingWidth = 0.11;
    private const double HandLength = 0.27;
    private const double HandHalfWidth = 0.055;
    private const double CrownLeft = 0.41;
    private const double CrownRight = 0.59;
    private const double CrownTop = 0.04;
    private const double CrownBottom = 0.22;

    /// <summary>Hand angle in degrees clockwise from 12 o'clock, stepping once every <paramref name="stepSeconds"/>.</summary>
    public static double HandDegrees(TimeSpan elapsed, int stepSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stepSeconds, 1);
        double seconds = elapsed.TotalSeconds % 60;
        double stepped = Math.Floor(seconds / stepSeconds) * stepSeconds;
        return stepped / 60 * 360;
    }

    /// <param name="size">Icon width and height in pixels (1-255).</param>
    /// <param name="handDegrees">Hand angle clockwise from 12 o'clock.</param>
    /// <param name="color">Drawing color as 0xAARRGGBB; the alpha scales the whole glyph.</param>
    public static byte[] Create(int size, double handDegrees, uint color)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 255);

        double radians = handDegrees * Math.PI / 180;
        double tipX = CenterX + (Math.Sin(radians) * HandLength);
        double tipY = CenterY - (Math.Cos(radians) * HandLength);

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
                        if (Covered(u, v, tipX, tipY))
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

    private static bool Covered(double u, double v, double tipX, double tipY)
    {
        double dx = u - CenterX;
        double dy = v - CenterY;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance <= OuterRadius && distance >= OuterRadius - RingWidth)
        {
            return true;
        }

        if (u >= CrownLeft && u <= CrownRight && v >= CrownTop && v <= CrownBottom)
        {
            return true;
        }

        return DistanceToSegment(u, v, CenterX, CenterY, tipX, tipY) <= HandHalfWidth;
    }

    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double abx = bx - ax;
        double aby = by - ay;
        double t = Math.Clamp((((px - ax) * abx) + ((py - ay) * aby)) / ((abx * abx) + (aby * aby)), 0, 1);
        double cx = ax + (t * abx) - px;
        double cy = ay + (t * aby) - py;
        return Math.Sqrt((cx * cx) + (cy * cy));
    }
}
