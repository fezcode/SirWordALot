namespace SirWordALot.Core;

/// <summary>Typed access to the key/value settings table. Every setter writes
/// through immediately, so preferences survive a crash.</summary>
public sealed class Settings(Db db)
{
    public event Action? Changed;

    public string Get(string key, string fallback) =>
        db.Scalar("SELECT value FROM settings WHERE key = $key", ("$key", key)) as string ?? fallback;

    public void Set(string key, string value)
    {
        db.Execute(
            """
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """,
            ("$key", key), ("$value", value));
        Changed?.Invoke();
    }

    private bool GetBool(string key, bool fallback) =>
        bool.TryParse(Get(key, ""), out var value) ? value : fallback;

    private int GetInt(string key, int fallback, int min, int max) =>
        int.TryParse(Get(key, ""), out var value) ? Math.Clamp(value, min, max) : fallback;

    public string Palette
    {
        get => Get("palette", "Petrol");
        set => Set("palette", value);
    }

    public bool Motion
    {
        get => GetBool("motion", true);
        set => Set("motion", value.ToString());
    }

    public SchedulerKind Scheduler
    {
        get => Enums.SchedulerKindFrom(Get("scheduler", "sm2"));
        set => Set("scheduler", value.ToColumn());
    }

    public int DailyGoal
    {
        get => GetInt("daily_goal", 30, 0, 9999);
        set => Set("daily_goal", Math.Clamp(value, 0, 9999).ToString());
    }

    public int NewPerDay
    {
        get => GetInt("new_per_day", 20, 0, 9999);
        set => Set("new_per_day", Math.Clamp(value, 0, 9999).ToString());
    }

    public string CaptureHotkey
    {
        get => Get("capture_hotkey", "Ctrl+Alt+W");
        set => Set("capture_hotkey", value);
    }

    public bool CaptureEnabled
    {
        get => GetBool("capture_enabled", true);
        set => Set("capture_enabled", value.ToString());
    }

    public bool LookupEnabled
    {
        get => GetBool("lookup_enabled", true);
        set => Set("lookup_enabled", value.ToString());
    }

    public string BackupFolder
    {
        get => Get("backup_folder", Path.Combine(Db.DataRoot(), "backups"));
        set => Set("backup_folder", value);
    }

    public string? WindowBounds
    {
        get => Get("window_bounds", "") is { Length: > 0 } value ? value : null;
        set => Set("window_bounds", value ?? "");
    }
}
