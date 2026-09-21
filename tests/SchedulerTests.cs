using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class SchedulerTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static Scheduling NewCard(SchedulerKind kind = SchedulerKind.Sm2) =>
        new(1, kind, 1, Scheduler.StartEase, 0, 0, 0, Now, null);

    [Fact]
    public void FirstGoodGivesOneDay()
    {
        var next = Scheduler.Apply(NewCard(), Grade.Good, Now);
        Assert.Equal(1, next.IntervalDays);
        Assert.Equal(1, next.Repetitions);
        Assert.Equal(Scheduler.StartEase, next.Ease);
        Assert.Equal(Now.Date.AddDays(1), next.DueUtc);
    }

    [Fact]
    public void SecondGoodGivesSixDays()
    {
        var card = NewCard() with { Repetitions = 1, IntervalDays = 1 };
        Assert.Equal(6, Scheduler.Apply(card, Grade.Good, Now).IntervalDays);
    }

    [Fact]
    public void ThirdGoodMultipliesByEase()
    {
        var card = NewCard() with { Repetitions = 2, IntervalDays = 6 };
        Assert.Equal(15, Scheduler.Apply(card, Grade.Good, Now).IntervalDays);
    }

    [Fact]
    public void FirstEasyGivesFourDaysAndRaisesEase()
    {
        var next = Scheduler.Apply(NewCard(), Grade.Easy, Now);
        Assert.Equal(4, next.IntervalDays);
        Assert.Equal(2.65, next.Ease, 3);
    }

    [Fact]
    public void EasyCompoundsTheUpdatedEase()
    {
        var card = NewCard() with { Repetitions = 2, IntervalDays = 6 };
        var next = Scheduler.Apply(card, Grade.Easy, Now);
        Assert.Equal(2.65, next.Ease, 3);
        Assert.Equal(21, next.IntervalDays);
    }

    [Fact]
    public void HardStretchesTheIntervalAndLowersEase()
    {
        var card = NewCard() with { Repetitions = 2, IntervalDays = 10 };
        var next = Scheduler.Apply(card, Grade.Hard, Now);
        Assert.Equal(12, next.IntervalDays);
        Assert.Equal(2.35, next.Ease, 3);
        Assert.Equal(0, next.Lapses);
    }

    [Fact]
    public void HardOnANewCardStillYieldsAtLeastOneDay() =>
        Assert.Equal(1, Scheduler.Apply(NewCard(), Grade.Hard, Now).IntervalDays);

    [Fact]
    public void AgainResetsRepetitionsRecordsALapseAndIsDueNow()
    {
        var card = NewCard() with { Repetitions = 4, IntervalDays = 30, Lapses = 1 };
        var next = Scheduler.Apply(card, Grade.Again, Now);

        Assert.Equal(0, next.IntervalDays);
        Assert.Equal(0, next.Repetitions);
        Assert.Equal(2, next.Lapses);
        Assert.Equal(2.3, next.Ease, 3);
        Assert.Equal(Now, next.DueUtc);
    }

    [Fact]
    public void EaseNeverFallsBelowTheFloor()
    {
        var card = NewCard() with { Ease = 1.35, Repetitions = 3, IntervalDays = 10 };
        Assert.Equal(Scheduler.MinEase, Scheduler.Apply(card, Grade.Again, Now).Ease, 3);
    }

    [Fact]
    public void IntervalsAreCappedAtOneYear()
    {
        var card = NewCard() with { Repetitions = 9, IntervalDays = 300 };
        Assert.Equal(Scheduler.MaxIntervalDays, Scheduler.Apply(card, Grade.Good, Now).IntervalDays);
    }

    [Fact]
    public void FuzzOnlyAppliesAboveTwoDays()
    {
        var newCard = NewCard();
        Assert.Equal(1, Scheduler.Apply(newCard, Grade.Good, Now, fuzz: 1.05).IntervalDays);

        var mature = NewCard() with { Repetitions = 2, IntervalDays = 20 };
        Assert.Equal(53, Scheduler.Apply(mature, Grade.Good, Now, fuzz: 1.05).IntervalDays);
    }

    [Fact]
    public void RandomFuzzStaysWithinFivePercent()
    {
        var random = new Random(1);
        for (var i = 0; i < 500; i++)
        {
            var fuzz = Scheduler.RandomFuzz(random);
            Assert.InRange(fuzz, 0.95, 1.05);
        }
    }

    [Fact]
    public void PreviewMatchesApplyWithoutFuzz()
    {
        var card = NewCard() with { Repetitions = 2, IntervalDays = 6 };
        foreach (var grade in new[] { Grade.Again, Grade.Hard, Grade.Good, Grade.Easy })
            Assert.Equal(Scheduler.Apply(card, grade, Now).IntervalDays,
                         Scheduler.PreviewIntervalDays(card, grade));
    }

    [Fact]
    public void LeitnerPromotesOnGoodHoldsOnHardAndResetsOnAgain()
    {
        var card = NewCard(SchedulerKind.Leitner) with { Box = 3, IntervalDays = 4 };

        Assert.Equal(4, Scheduler.Apply(card, Grade.Good, Now).Box);
        Assert.Equal(8, Scheduler.Apply(card, Grade.Good, Now).IntervalDays);
        Assert.Equal(3, Scheduler.Apply(card, Grade.Hard, Now).Box);
        Assert.Equal(1, Scheduler.Apply(card, Grade.Again, Now).Box);
        Assert.Equal(1, Scheduler.Apply(card, Grade.Again, Now).IntervalDays);
    }

    [Fact]
    public void LeitnerStopsAtBoxFive()
    {
        var card = NewCard(SchedulerKind.Leitner) with { Box = 5, IntervalDays = 16 };
        Assert.Equal(5, Scheduler.Apply(card, Grade.Easy, Now).Box);
        Assert.Equal(16, Scheduler.Apply(card, Grade.Easy, Now).IntervalDays);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 3)]
    [InlineData(7, 4)]
    [InlineData(40, 5)]
    public void ConvertingToLeitnerSeedsTheBoxFromTheInterval(int interval, int expectedBox)
    {
        var card = NewCard() with { IntervalDays = interval };
        var converted = Scheduler.Convert(card, SchedulerKind.Leitner);

        Assert.Equal(SchedulerKind.Leitner, converted.Scheduler);
        Assert.Equal(expectedBox, converted.Box);
        Assert.Equal(card.DueUtc, converted.DueUtc);
    }

    [Fact]
    public void ConvertingToSm2KeepsProgressAndDueDate()
    {
        var card = NewCard(SchedulerKind.Leitner) with { Box = 4, IntervalDays = 8, Repetitions = 1 };
        var converted = Scheduler.Convert(card, SchedulerKind.Sm2);

        Assert.Equal(SchedulerKind.Sm2, converted.Scheduler);
        Assert.Equal(8, converted.IntervalDays);
        Assert.Equal(4, converted.Repetitions);
        Assert.Equal(card.DueUtc, converted.DueUtc);
    }

    [Fact]
    public void ConvertingToTheSameSchedulerIsANoop()
    {
        var card = NewCard();
        Assert.Same(card, Scheduler.Convert(card, SchedulerKind.Sm2));
    }
}
