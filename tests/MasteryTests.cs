using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class MasteryTests
{
    [Fact]
    public void AnUnreviewedWordHasNoMastery() => Assert.Equal(0, Mastery.Percent(0, 0, 0));

    [Fact]
    public void PerfectRecallAtSixtyDaysIsFullMastery() => Assert.Equal(100, Mastery.Percent(60, 10, 10));

    [Fact]
    public void MasteryIsCappedAtSixtyDays() => Assert.Equal(100, Mastery.Percent(400, 10, 10));

    [Fact]
    public void MasteryScalesWithAccuracy() => Assert.Equal(50, Mastery.Percent(60, 5, 10));

    [Fact]
    public void MasteryScalesWithInterval() => Assert.Equal(50, Mastery.Percent(30, 10, 10));

    [Theory]
    [InlineData(0, 0, MasteryBucket.New)]
    [InlineData(1, 3, MasteryBucket.Learning)]
    [InlineData(2, 7, MasteryBucket.Familiar)]
    [InlineData(3, 29, MasteryBucket.Familiar)]
    [InlineData(4, 30, MasteryBucket.Strong)]
    [InlineData(5, 89, MasteryBucket.Strong)]
    [InlineData(6, 90, MasteryBucket.Mastered)]
    public void BucketsFollowTheIntervalThresholds(int reviews, int interval, MasteryBucket expected) =>
        Assert.Equal(expected, Mastery.Bucket(reviews, interval));
}
