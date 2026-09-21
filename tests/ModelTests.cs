using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class ModelTests
{
    [Theory]
    [InlineData(RelationKind.Synonym, "synonym")]
    [InlineData(RelationKind.Antonym, "antonym")]
    [InlineData(RelationKind.Related, "related")]
    [InlineData(RelationKind.Rhyme, "rhyme")]
    [InlineData(RelationKind.Form, "form")]
    public void RelationKindRoundTripsThroughItsColumnValue(RelationKind kind, string column)
    {
        Assert.Equal(column, kind.ToColumn());
        Assert.Equal(kind, Enums.RelationKindFrom(column));
    }

    [Theory]
    [InlineData(QuizMode.Flip, "flip")]
    [InlineData(QuizMode.Choice, "choice")]
    [InlineData(QuizMode.Cloze, "cloze")]
    [InlineData(QuizMode.Typed, "typed")]
    public void QuizModeRoundTripsThroughItsColumnValue(QuizMode mode, string column)
    {
        Assert.Equal(column, mode.ToColumn());
        Assert.Equal(mode, Enums.QuizModeFrom(column));
    }

    [Theory]
    [InlineData(SchedulerKind.Sm2, "sm2")]
    [InlineData(SchedulerKind.Leitner, "leitner")]
    public void SchedulerKindRoundTripsThroughItsColumnValue(SchedulerKind kind, string column)
    {
        Assert.Equal(column, kind.ToColumn());
        Assert.Equal(kind, Enums.SchedulerKindFrom(column));
    }

    [Fact]
    public void UnknownColumnValueFallsBackRatherThanThrowing()
    {
        Assert.Equal(RelationKind.Related, Enums.RelationKindFrom("nonsense"));
        Assert.Equal(QuizMode.Flip, Enums.QuizModeFrom("nonsense"));
        Assert.Equal(SchedulerKind.Sm2, Enums.SchedulerKindFrom("nonsense"));
    }

    [Fact]
    public void GradeValuesMatchTheStoredIntegers()
    {
        Assert.Equal(0, (int)Grade.Again);
        Assert.Equal(1, (int)Grade.Hard);
        Assert.Equal(2, (int)Grade.Good);
        Assert.Equal(3, (int)Grade.Easy);
    }

    [Fact]
    public void IsoRoundTripsUtcTimestamps()
    {
        var when = new DateTime(2026, 9, 22, 12, 30, 45, DateTimeKind.Utc);
        var parsed = Iso.Parse(Iso.Text(when));

        Assert.Equal(when, parsed);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        Assert.Null(Iso.ParseOrNull(null));
    }
}
