using TrayClock.Services;

namespace TrayClock.Tests;

[TestClass]
public sealed class SavedTimersTests
{
    [TestMethod]
    public void Decode_EncodedList_RoundTrips()
    {
        SavedTimer[] timers = [new("Tea", TimeSpan.FromMinutes(4)), new("Laundry", TimeSpan.FromSeconds(3725))];

        IReadOnlyList<SavedTimer> back = SavedTimers.Decode(SavedTimers.Encode(timers));

        CollectionAssert.AreEqual(timers, back.ToArray());
    }

    [TestMethod]
    public void Decode_NullOrGarbage_SkipsBadLines()
    {
        Assert.AreEqual(0, SavedTimers.Decode(null).Count);
        Assert.AreEqual(0, SavedTimers.Decode("nonsense\n-5\tx\n0\ty\n60\t").Count);
    }

    [TestMethod]
    public void CleanName_SeparatorsAndLength_AreRemovedAndCapped()
    {
        Assert.AreEqual("abc", SavedTimers.CleanName("  ab\t\nc \r"));
        Assert.AreEqual(SavedTimers.MaxNameLength, SavedTimers.CleanName(new string('x', 100)).Length);
    }
}
