using TrayClock.Services;

namespace TrayClock.Tests;

[TestClass]
public sealed class TimerIconTests
{
    private const int Size = 16;
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [TestMethod]
    [DataRow(60, 1.0)]
    [DataRow(59.5, 1.0)]
    [DataRow(30, 0.5)]
    [DataRow(29.5, 0.5)]
    [DataRow(0.5, 1.0 / 60)]
    [DataRow(0, 0.0)]
    public void Fraction_Remaining_RoundsUpToWholeSegment(double remainingSeconds, double expected)
    {
        Assert.AreEqual(expected, TimerIcon.Fraction(TimeSpan.FromSeconds(remainingSeconds), Minute), 0.0001);
    }

    [TestMethod]
    public void Fraction_NoDuration_IsEmpty()
    {
        Assert.AreEqual(0, TimerIcon.Fraction(TimeSpan.FromSeconds(5), TimeSpan.Zero));
    }

    [TestMethod]
    public void UntilNextSegment_MidSegment_ReturnsTimeIntoSegment()
    {
        // 60 s / 60 segments = 1 s segments; 30.25 s remaining is 0.25 s above the 30 s boundary.
        Assert.AreEqual(0.25, TimerIcon.UntilNextSegment(TimeSpan.FromSeconds(30.25), Minute).TotalSeconds, 0.0001);
    }

    [TestMethod]
    public void UntilNextSegment_OnBoundary_ReturnsWholeSegment()
    {
        Assert.AreEqual(1, TimerIcon.UntilNextSegment(TimeSpan.FromSeconds(30), Minute).TotalSeconds, 0.0001);
    }

    [TestMethod]
    public void Create_FullAndEmpty_Differ()
    {
        CollectionAssert.AreNotEqual(TimerIcon.Create(Size, 1, 0xFFFFFFFF), TimerIcon.Create(Size, 0, 0xFFFFFFFF));
    }

    [TestMethod]
    public void Create_Empty_StillDrawsOuterCircle()
    {
        byte[] ico = TimerIcon.Create(Size, 0, 0xFFFFFFFF);

        Assert.IsGreaterThan(0, AlphaAt(ico, 0, Size / 2));
        Assert.AreEqual(0, AlphaAt(ico, Size / 2, Size / 2)); // donut hole and empty donut
    }

    [TestMethod]
    public void Create_Half_FillsRightSideOnly()
    {
        byte[] ico = TimerIcon.Create(Size, 0.5, 0xFFFFFFFF);

        // The donut band sits about 4px from center: 3 o'clock is drawn, 9 o'clock is drained.
        Assert.IsGreaterThan(0, AlphaAt(ico, (Size / 2) + 4, Size / 2));
        Assert.AreEqual(0, AlphaAt(ico, (Size / 2) - 5, Size / 2));
    }

    private static byte AlphaAt(byte[] ico, int x, int y)
    {
        int offset = IcoEncoder.PixelOffset + (((Size - 1 - y) * Size) + x) * 4;
        return ico[offset + 3];
    }
}
