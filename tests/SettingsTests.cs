using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class SettingsTests
{
    [Fact]
    public void DefaultsMatchTheSpec()
    {
        var settings = new Settings(Db.OpenMemory());

        Assert.Equal("Petrol", settings.Palette);
        Assert.True(settings.Motion);
        Assert.Equal(SchedulerKind.Sm2, settings.Scheduler);
        Assert.Equal(30, settings.DailyGoal);
        Assert.Equal(20, settings.NewPerDay);
        Assert.Equal("Ctrl+Alt+W", settings.CaptureHotkey);
        Assert.True(settings.CaptureEnabled);
        Assert.True(settings.LookupEnabled);
        Assert.Null(settings.WindowBounds);
        Assert.True(settings.HisashiMenus);
        Assert.EndsWith("backups", settings.BackupFolder);
    }

    [Fact]
    public void ValuesPersistAcrossInstances()
    {
        var db = Db.OpenMemory();
        _ = new Settings(db) { Palette = "Midnight", NewPerDay = 5, Scheduler = SchedulerKind.Leitner };

        var reopened = new Settings(db);
        Assert.Equal("Midnight", reopened.Palette);
        Assert.Equal(5, reopened.NewPerDay);
        Assert.Equal(SchedulerKind.Leitner, reopened.Scheduler);
    }

    [Fact]
    public void SettingAValueRaisesChanged()
    {
        var settings = new Settings(Db.OpenMemory());
        var raised = 0;
        settings.Changed += () => raised++;

        settings.Motion = false;
        settings.DailyGoal = 50;

        Assert.Equal(2, raised);
    }

    [Fact]
    public void CorruptStoredValuesFallBackToTheDefault()
    {
        var db = Db.OpenMemory();
        var settings = new Settings(db);
        settings.Set("new_per_day", "not a number");
        settings.Set("motion", "perhaps");

        var reopened = new Settings(db);
        Assert.Equal(20, reopened.NewPerDay);
        Assert.True(reopened.Motion);
    }

    [Fact]
    public void NewPerDayAndDailyGoalAreClampedToSaneRanges()
    {
        var settings = new Settings(Db.OpenMemory()) { NewPerDay = -5, DailyGoal = 100000 };

        Assert.Equal(0, settings.NewPerDay);
        Assert.Equal(9999, settings.DailyGoal);
    }

    [Fact]
    public void GetReturnsTheFallbackForAnUnknownKey() =>
        Assert.Equal("x", new Settings(Db.OpenMemory()).Get("nope", "x"));
}
