using TrayClock.Services;

namespace TrayClock.Tests;

[TestClass]
public sealed class LapStatsTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [TestMethod]
    public void Build_NoLaps_IsEmpty()
    {
        Assert.AreEqual(0, LapStats.Build([]).Count);
    }

    [TestMethod]
    public void Build_Totals_ComputesSplitsNewestFirst()
    {
        IReadOnlyList<Lap> laps = LapStats.Build([S(10), S(25), S(31)]);

        Assert.AreEqual(3, laps[0].Number);
        Assert.AreEqual(S(6), laps[0].Split);
        Assert.AreEqual(S(31), laps[0].Total);
        Assert.AreEqual(S(15), laps[1].Split);
        Assert.AreEqual(S(10), laps[2].Split);
    }

    [TestMethod]
    public void Build_SeveralLaps_MarksFastestAndSlowest()
    {
        IReadOnlyList<Lap> laps = LapStats.Build([S(10), S(25), S(31)]);

        Assert.AreEqual(LapRank.Fastest, laps[0].Rank); // lap 3, 6 s
        Assert.AreEqual(LapRank.Slowest, laps[1].Rank); // lap 2, 15 s
        Assert.AreEqual(LapRank.None, laps[2].Rank);
    }

    [TestMethod]
    public void Build_OneLap_MarksNothing()
    {
        Assert.AreEqual(LapRank.None, LapStats.Build([S(10)])[0].Rank);
    }

    [TestMethod]
    public void ToTable_Laps_TabSeparatedOldestFirstWithHeader()
    {
        string table = LapStats.ToTable([S(10), S(25), S(31)]);

        Assert.AreEqual(
            "Lap\tSplit\tTotal\tNote\r\n" +
            "1\t00:10.00\t00:10.00\t\r\n" +
            "2\t00:15.00\t00:25.00\tSlowest\r\n" +
            "3\t00:06.00\t00:31.00\tFastest\r\n",
            table);
    }

    [TestMethod]
    public void ToTable_NoLaps_IsHeaderOnly()
    {
        Assert.AreEqual("Lap\tSplit\tTotal\tNote\r\n", LapStats.ToTable([]));
    }

    [TestMethod]
    public void Build_EqualSplits_MarksNothing()
    {
        Assert.IsTrue(LapStats.Build([S(10), S(20)]).All(l => l.Rank == LapRank.None));
    }
}
