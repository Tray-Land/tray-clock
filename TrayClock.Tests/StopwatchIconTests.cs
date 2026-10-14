using System.Buffers.Binary;
using TrayClock.Services;

namespace TrayClock.Tests;

[TestClass]
public sealed class StopwatchIconTests
{
    private const int Size = 16;

    [TestMethod]
    [DataRow(0, 3, 0.0)]
    [DataRow(2, 3, 0.0)]
    [DataRow(3, 3, 18.0)]
    [DataRow(15, 3, 90.0)]
    [DataRow(59, 3, 342.0)]
    [DataRow(60, 3, 0.0)]
    [DataRow(75, 3, 90.0)]
    public void HandDegrees_Elapsed_StepsAroundTheDial(int seconds, int step, double expected)
    {
        Assert.AreEqual(expected, StopwatchIcon.HandDegrees(TimeSpan.FromSeconds(seconds), step), 0.0001);
    }

    [TestMethod]
    public void Create_ValidSize_ProducesWellFormedIco()
    {
        byte[] ico = StopwatchIcon.Create(Size, 0, 0xFFFFFFFF);

        Assert.AreEqual(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        Assert.AreEqual(Size, ico[6]);
        int declared = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(14));
        Assert.AreEqual(ico.Length - IcoEncoder.ImageOffset, declared);
    }

    [TestMethod]
    public void Create_HandAtDifferentAngles_ProducesDifferentImages()
    {
        byte[] top = StopwatchIcon.Create(Size, 0, 0xFFFFFFFF);
        byte[] right = StopwatchIcon.Create(Size, 90, 0xFFFFFFFF);

        CollectionAssert.AreNotEqual(top, right);
    }

    [TestMethod]
    public void Create_Corner_IsTransparentAndDialCenterIsPartlyOpaque()
    {
        byte[] ico = StopwatchIcon.Create(Size, 0, 0xFFFFFFFF);

        // Bottom-up rows: the last row in the file is the top row of the image.
        Assert.AreEqual(0, AlphaAt(ico, 0, Size - 1));
        Assert.IsGreaterThan(0, AlphaAt(ico, Size / 2, (int)(Size * 0.58)));
    }

    private static byte AlphaAt(byte[] ico, int x, int y)
    {
        int offset = IcoEncoder.PixelOffset + (((Size - 1 - y) * Size) + x) * 4;
        return ico[offset + 3];
    }
}
