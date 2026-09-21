using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class ReviewSessionTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static Library Seed(int count, bool withSentences = false)
    {
        var library = new Library(Db.OpenMemory());
        var words = new[] { "eminence", "laconic", "ersatz", "obdurate", "pellucid", "quixotic" };
        for (var i = 0; i < count; i++)
        {
            var id = library.AddEntry(words[i % words.Length] + (i >= words.Length ? i.ToString() : ""));
            library.UpdateEntry(library.GetEntry(id)! with
            { PartOfSpeech = "adjective", Definition = $"Definition {i}." });
            if (withSentences)
                library.AddSentence($"The {library.GetEntry(id)!.Headword} thing happened.", id);
        }
        return library;
    }

    [Fact]
    public void ANewLibraryQueuesEveryNewWordUpToTheDailyCap()
    {
        var library = Seed(6);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(NewPerDay: 4, Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(4, session.Total);
        Assert.Equal(1, session.Position);
        Assert.NotNull(session.Current);
    }

    [Fact]
    public void AnsweringAdvancesThroughTheQueue()
    {
        var library = Seed(3);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        session.Answer(Grade.Good, 900, Now);
        Assert.Equal(2, session.Position);
        Assert.Equal(1, session.Answered);

        session.Answer(Grade.Good, 900, Now);
        session.Answer(Grade.Good, 900, Now);

        Assert.True(session.IsFinished);
        Assert.Null(session.Current);
    }

    [Fact]
    public void EveryAnswerWritesAReviewRowImmediately()
    {
        var library = Seed(2);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);
        var first = session.Current!.Entry.Id;

        session.Answer(Grade.Good, 1500, Now);

        var review = Assert.Single(library.ReviewsFor(first));
        Assert.Equal(Grade.Good, review.Grade);
        Assert.True(review.Correct);
        Assert.True(review.Scheduled);
        Assert.Equal(1500, review.ElapsedMs);
        Assert.Equal(0, review.IntervalBefore);
        Assert.Equal(1, review.IntervalAfter);
    }

    [Fact]
    public void AnsweringUpdatesTheSchedulingRow()
    {
        var library = Seed(1);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);
        var id = session.Current!.Entry.Id;

        session.Answer(Grade.Good, 900, Now);

        var scheduling = library.GetScheduling(id);
        Assert.Equal(1, scheduling.IntervalDays);
        Assert.Equal(1, scheduling.Repetitions);
        Assert.Equal(Now, scheduling.LastReviewedUtc);
    }

    [Fact]
    public void AgainRequeuesTheCardAtTheEnd()
    {
        var library = Seed(2);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);
        var first = session.Current!.Entry.Id;

        session.Answer(Grade.Again, 900, Now);
        Assert.NotEqual(first, session.Current!.Entry.Id);

        session.Answer(Grade.Good, 900, Now);
        Assert.Equal(first, session.Current!.Entry.Id);

        session.Answer(Grade.Good, 900, Now);
        Assert.True(session.IsFinished);
    }

    [Fact]
    public void AgainCountsAsIncorrectButHardDoesNot()
    {
        var library = Seed(2);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.All),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        session.Answer(Grade.Hard, 900, Now);
        session.Answer(Grade.Again, 900, Now);
        session.Answer(Grade.Good, 900, Now);

        Assert.Equal(3, session.Summary.Reviewed);
        Assert.Equal(2, session.Summary.Correct);
        Assert.Equal(1, session.Summary.Lapsed);
    }

    [Fact]
    public void FreePracticeLogsReviewsButLeavesSchedulingAlone()
    {
        var library = Seed(1);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.FreePractice),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);
        var id = session.Current!.Entry.Id;
        var before = library.GetScheduling(id);

        session.Answer(Grade.Easy, 900, Now);

        Assert.Equal(before, library.GetScheduling(id));
        var review = Assert.Single(library.ReviewsFor(id));
        Assert.False(review.Scheduled);
    }

    [Fact]
    public void DeckScopeQueuesOnlyDeckMembers()
    {
        var library = Seed(4);
        var deck = library.CreateDeck("Exam");
        var chosen = library.AllEntries()[0].Id;
        library.AddToDeck(deck, chosen);

        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Deck, DeckId: deck),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, session.Total);
        Assert.Equal(chosen, session.Current!.Entry.Id);
    }

    [Fact]
    public void TagScopeQueuesOnlyTaggedEntries()
    {
        var library = Seed(4);
        var chosen = library.AllEntries()[1].Id;
        library.TagEntry(chosen, "exam");

        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Tag, Tag: "exam"),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, session.Total);
        Assert.Equal(chosen, session.Current!.Entry.Id);
    }

    [Fact]
    public void SingleScopeQueuesExactlyOneWord()
    {
        var library = Seed(4);
        var chosen = library.AllEntries()[3].Id;

        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Single, EntryId: chosen),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, session.Total);
        Assert.Equal(chosen, session.Current!.Entry.Id);

        session.Answer(Grade.Good, 900, Now);
        Assert.True(session.IsFinished);
        Assert.Single(library.ReviewsFor(chosen));
    }

    [Fact]
    public void SingleScopeWithAMissingEntryIsFinishedImmediately()
    {
        var library = Seed(1);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Single, EntryId: 9999),
            new SessionOptions(Seed: 1), Now);

        Assert.True(session.IsFinished);
    }

    [Fact]
    public void StarredScopeQueuesOnlyStarredEntries()
    {
        var library = Seed(4);
        var chosen = library.AllEntries()[2].Id;
        library.SetStarred(chosen, true);

        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Starred),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, session.Total);
    }

    [Fact]
    public void ChoiceCardsCarryFourDistinctOptionsIncludingTheAnswer()
    {
        var library = Seed(6);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.All),
            new SessionOptions(Mode: QuizMode.Choice, Seed: 7), Now);
        var card = session.Current!;

        Assert.Equal(4, card.Choices.Count);
        Assert.Equal(4, card.Choices.Distinct().Count());
        Assert.Equal(card.Entry.Headword, card.Choices[card.CorrectChoice]);
        Assert.Equal(card.Entry.Definition, card.Prompt);
    }

    [Fact]
    public void ChoiceFallsBackToFlipWhenThereAreTooFewEntries()
    {
        var library = Seed(3);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.All),
            new SessionOptions(Mode: QuizMode.Choice, Seed: 1), Now);

        Assert.Equal(QuizMode.Flip, session.Current!.Mode);
    }

    [Fact]
    public void ClozeCardsCarryABlankedSentence()
    {
        var library = Seed(2, withSentences: true);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.All),
            new SessionOptions(Mode: QuizMode.Cloze, Seed: 1), Now);
        var card = session.Current!;

        Assert.Equal(QuizMode.Cloze, card.Mode);
        Assert.NotNull(card.Sentence);
        Assert.Contains(Cloze.BlankChar, card.Prompt);
        Assert.DoesNotContain(card.Entry.Headword, card.Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClozeFallsBackToFlipWithoutAUsableSentence()
    {
        var library = Seed(2);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.All),
            new SessionOptions(Mode: QuizMode.Cloze, Seed: 1), Now);

        Assert.Equal(QuizMode.Flip, session.Current!.Mode);
    }

    [Fact]
    public void MixedModePrefersClozeThenChoiceThenFlip()
    {
        var withSentences = Seed(6, withSentences: true);
        Assert.Equal(QuizMode.Cloze,
            new ReviewSession(withSentences, new ReviewScope(ScopeKind.All),
                new SessionOptions(Seed: 1), Now).Current!.Mode);

        var withoutSentences = Seed(6);
        Assert.Equal(QuizMode.Choice,
            new ReviewSession(withoutSentences, new ReviewScope(ScopeKind.All),
                new SessionOptions(Seed: 1), Now).Current!.Mode);

        var tiny = Seed(2);
        Assert.Equal(QuizMode.Flip,
            new ReviewSession(tiny, new ReviewScope(ScopeKind.All),
                new SessionOptions(Seed: 1), Now).Current!.Mode);
    }

    [Fact]
    public void PreviewIntervalDaysMatchesTheScheduler()
    {
        var library = Seed(1);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, session.PreviewIntervalDays(Grade.Good));
        Assert.Equal(4, session.PreviewIntervalDays(Grade.Easy));
        Assert.Equal(0, session.PreviewIntervalDays(Grade.Again));
    }

    [Fact]
    public void AnEmptyScopeProducesAFinishedSession()
    {
        var library = new Library(Db.OpenMemory());
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Seed: 1), Now);

        Assert.True(session.IsFinished);
        Assert.Equal(0, session.Total);
        Assert.Equal(0, session.Summary.Reviewed);
    }

    [Fact]
    public void AnsweringAFinishedSessionThrows()
    {
        var library = Seed(1);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);
        session.Answer(Grade.Good, 900, Now);

        Assert.Throws<InvalidOperationException>(() => session.Answer(Grade.Good, 900, Now));
    }

    [Fact]
    public void TheDailyCapCountsNewCardsAlreadyStudiedToday()
    {
        var library = Seed(6);
        var first = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(NewPerDay: 3, Mode: QuizMode.Flip, Seed: 1), Now);
        first.Answer(Grade.Good, 900, Now);
        first.Answer(Grade.Good, 900, Now);

        var second = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(NewPerDay: 3, Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, second.Total);
    }
}
