using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class SettingsView : UserControl
{
    private static readonly string[] Palettes = ["Petrol", "Evergreen", "Midnight"];

    private readonly ShellContext _context;
    private readonly ContentControl _body = new();

    public SettingsView(ShellContext context)
    {
        _context = context;
        Content = _body;
        Render();
    }

    public void ApplyScheduler(SchedulerKind kind)
    {
        _context.Settings.Scheduler = kind;

        foreach (var entry in _context.Library.AllEntries(includeArchived: true))
        {
            var current = _context.Library.GetScheduling(entry.Id);
            if (current.Scheduler == kind) continue;
            _context.Library.SaveScheduling(Scheduler.Convert(current, kind));
        }
    }

    public void ExportTo(string path, string format)
    {
        var content = format switch
        {
            "csv" => Portability.ExportCsv(_context.Library),
            "tsv" => Portability.ExportAnkiTsv(_context.Library),
            _ => Portability.ExportJson(_context.Library),
        };
        Backups.WriteAtomic(path, content);
    }

    public ImportReport ImportFrom(string path, MergePolicy policy)
    {
        var text = File.ReadAllText(path);
        return Path.GetExtension(path).ToLowerInvariant() is ".csv" or ".tsv"
            ? Portability.ImportCsv(_context.Library, text, policy)
            : Portability.ImportJson(_context.Library, text, policy);
    }

    public string WriteBackup()
    {
        var backups = new Backups(_context.Settings.BackupFolder);
        var path = backups.Write(_context.Library, DateTime.UtcNow);
        backups.Prune();
        return path;
    }

    private void Render()
    {
        var settings = _context.Settings;

        var palette = new ComboBox { ItemsSource = Palettes, Width = 200 };
        palette.SelectedIndex = Math.Max(0, Array.IndexOf(Palettes, settings.Palette));
        palette.SelectionChanged += (_, _) =>
        {
            if (palette.SelectedItem is not string chosen || chosen == settings.Palette) return;
            settings.Palette = chosen;
            _context.Toast("Palette saved. It applies to accents and the console.");
        };

        var motion = new ToggleSwitch
        { IsChecked = settings.Motion, OnContent = "Smooth", OffContent = "Reduced" };
        motion.IsCheckedChanged += (_, _) =>
        {
            settings.Motion = motion.IsChecked == true;
            Motion.Enabled = settings.Motion;
        };

        var scheduler = new ComboBox
        {
            ItemsSource = new[] { "SM-2 (spaced repetition)", "Leitner (five boxes)" },
            SelectedIndex = settings.Scheduler == SchedulerKind.Leitner ? 1 : 0,
            Width = 240,
        };
        scheduler.SelectionChanged += (_, _) =>
        {
            var kind = scheduler.SelectedIndex == 1 ? SchedulerKind.Leitner : SchedulerKind.Sm2;
            if (kind == settings.Scheduler) return;
            ApplyScheduler(kind);
            _context.Toast("Scheduler switched. Your due dates were kept.");
        };

        var goal = Number(settings.DailyGoal, value => settings.DailyGoal = value);
        var newPerDay = Number(settings.NewPerDay, value => settings.NewPerDay = value);

        var hotkey = new TextBox { Text = settings.CaptureHotkey, Width = 200 };
        hotkey.LostFocus += (_, _) =>
            settings.CaptureHotkey = string.IsNullOrWhiteSpace(hotkey.Text) ? "Ctrl+Alt+W" : hotkey.Text!.Trim();

        var capture = new ToggleSwitch { IsChecked = settings.CaptureEnabled };
        capture.IsCheckedChanged += (_, _) =>
        {
            settings.CaptureEnabled = capture.IsChecked == true;
            _context.Toast("The capture hotkey applies when the app next starts.");
        };

        var lookup = new ToggleSwitch { IsChecked = settings.LookupEnabled };
        lookup.IsCheckedChanged += (_, _) =>
        {
            settings.LookupEnabled = lookup.IsChecked == true;
            _context.Toast(settings.LookupEnabled
                ? "Lookup enabled."
                : "Lookup disabled. The app now makes no network calls at all.");
        };

        var hisashi = new ToggleSwitch { IsChecked = settings.HisashiMenus };
        hisashi.IsCheckedChanged += (_, _) => settings.HisashiMenus = hisashi.IsChecked == true;

        var stack = Ui.Stack(26,
            Ui.Heading("CONFIGURATION", "Settings.", "Everything here is stored with your library."),
            Ui.Rule(),
            Group("APPEARANCE",
                Field("Palette", "Tints accents, the console and the heatmap.", palette),
                Field("Motion", "Turn this off to reduce movement.", motion)),
            Group("STUDY",
                Field("Scheduler", "Switching converts your progress rather than resetting it.", scheduler),
                Field("Daily goal", "Reviews you aim to finish each day.", goal),
                Field("New words per day", "How many unseen words enter the queue each day.", newPerDay)),
            Group("CAPTURE AND LOOKUP",
                Field("Capture hotkey", "Grabs the clipboard word from anywhere.", hotkey),
                Field("Capture enabled", "Unregisters the hotkey when off.", capture),
                Field("Online lookup", "Datamuse and dictionaryapi.dev. Off means no network at all.", lookup)),
            Group("INTEGRATION",
                Field("Hisashi menubar", "Shows File and Go in Hisashi's menubar while this window is in front.",
                    hisashi)),
            Group("DATA",
                Field("Export", "JSON is lossless and round-trips everything.",
                    Ui.Row(10,
                        Ui.Button("JSON", () => Save("json"), "outline"),
                        Ui.Button("CSV", () => Save("csv"), "outline"),
                        Ui.Button("Anki TSV", () => Save("tsv"), "outline"))),
                Field("Import", "Merges on the headword; you choose what wins.",
                    Ui.Row(10, Ui.Button("Import a file…", Open, "outline"))),
                Field("Backups", $"Kept in {settings.BackupFolder}. The newest 20 are retained.",
                    Ui.Row(10,
                        Ui.Button("Back up now",
                            () => _context.Toast($"Snapshot written to {WriteBackup()}."), "outline"),
                        Ui.Button("Restore…", ShowRestore, "outline"))),
                Field("Data location", Db.DataRoot(),
                    Ui.Button("Copy path", CopyPath, "outline"))),
            Ui.Rule(),
            Ui.Stack(6,
                Ui.Label("ABOUT"),
                Ui.Text($"{Product.Name} {Product.Version}", 13),
                Ui.Text("An independent project. Dictionary data from dictionaryapi.dev; " +
                        "thesaurus data from Datamuse.", 12, Ui.Muted)));

        _body.Content = Ui.Scroll(Ui.Pad(stack, 34));
    }

    private static Control Group(string caption, params Control[] fields)
    {
        var stack = Ui.Stack(18, Ui.Label(caption));
        foreach (var field in fields) stack.Children.Add(field);
        return stack;
    }

    private static Control Field(string name, string help, Control control)
    {
        var text = Ui.Text(help, 12, Ui.Muted);
        text.TextWrapping = TextWrapping.Wrap;
        return Ui.Columns("320,*", Ui.Stack(5, Ui.Text(name, 13), text), control);
    }

    private static TextBox Number(int value, Action<int> set)
    {
        var box = new TextBox { Text = value.ToString(), Width = 110 };
        box.LostFocus += (_, _) =>
        {
            if (int.TryParse(box.Text, out var parsed)) set(parsed);
            else box.Text = value.ToString();
        };
        return box;
    }

    private async void CopyPath()
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(Db.DataRoot());
            _context.Toast("Path copied.");
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            _context.Toast("Could not reach the clipboard.");
        }
    }

    private async void Save(string format)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the library",
            SuggestedFileName = $"sir-word-a-lot.{format}",
            DefaultExtension = format,
        });

        if (file?.TryGetLocalPath() is not { } path) return;

        try
        {
            ExportTo(path, format);
            _context.Toast($"Exported to {path}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _context.Toast($"Could not write that file: {e.Message}");
        }
    }

    private async void Open()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import into the library",
            AllowMultiple = false,
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        ShowMergePrompt(path);
    }

    private void ShowMergePrompt(string path)
    {
        void Run(MergePolicy policy)
        {
            try
            {
                var report = ImportFrom(path, policy);
                _context.Toast($"Added {report.Added}, updated {report.Updated}, skipped {report.Skipped}.");
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _context.Toast($"Could not import that file: {e.Message}");
            }
            Render();
        }

        _body.Content = Ui.Pad(Ui.Stack(18,
            Ui.Heading("IMPORT", "What wins on a clash?",
                "Words that already exist in your library can be kept, replaced or duplicated."),
            Ui.Text(path, 12, Ui.Muted, mono: true),
            Ui.Row(12,
                Ui.Button("Keep mine", () => Run(MergePolicy.KeepMine), "outline"),
                Ui.Button("Take theirs", () => Run(MergePolicy.TakeTheirs), "outline"),
                Ui.Button("Keep both", () => Run(MergePolicy.KeepBoth), "outline"),
                Ui.Button("Cancel", Render, "outline"))), 34);
    }

    private void ShowRestore()
    {
        var backups = new Backups(_context.Settings.BackupFolder);
        var snapshots = backups.List();

        if (snapshots.Count == 0)
        {
            _context.Toast("There are no snapshots yet.");
            return;
        }

        var picker = new ComboBox
        {
            ItemsSource = snapshots.Select(Path.GetFileName).ToList(),
            SelectedIndex = 0,
            Width = 340,
        };

        _body.Content = Ui.Pad(Ui.Stack(18,
            Ui.Heading("RESTORE", "Replace the library from a snapshot.",
                "A snapshot of the current library is written first, so this is reversible."),
            picker,
            Ui.Row(12,
                Ui.Button("Cancel", Render, "outline"),
                Ui.Button("Restore", () =>
                {
                    if (picker.SelectedIndex < 0) return;
                    WriteBackup();
                    backups.Restore(_context.Library, snapshots[picker.SelectedIndex]);
                    _context.Toast("Library restored.");
                    Render();
                }, "primary"))), 34);
    }
}
