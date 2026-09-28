using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var db = Db.Open(Db.DefaultPath());
            // Translations come straight from the bundled atlas.dict data: new words
            // get theirs as they are added, and older blank ones are filled here.
            var library = new Library(db) { Translator = OfflineDictionary.Instance.TranslationFor };
            library.FillMissingTranslations();
            var settings = new Settings(db);
            Motion.Enabled = settings.Motion;
            AppTheme.Apply(settings.Theme);

            // A first run with nothing in it gives you nothing to do. Seeds only
            // when the library is genuinely empty, so it never surprises a
            // returning user with words they did not choose.
            StarterLibrary.SeedIfEmpty(library);
            MainWindow Build() => new(library, settings, new Search(db), new Stats(db), new Lookup());
            desktop.MainWindow = Build();

            // Controls take their brushes when they are built, so a theme switch
            // rebuilds the window. The old one closes first so it saves its bounds
            // and releases the capture hotkey before the new one claims both.
            AppTheme.Switched += () =>
            {
                var old = desktop.MainWindow;
                var mode = desktop.ShutdownMode;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                old?.Close();

                var next = Build();
                next.Navigate("settings");
                desktop.MainWindow = next;
                next.Show();
                desktop.ShutdownMode = mode;
                next.ShowToast($"{AppTheme.Current.Name} theme.");
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
