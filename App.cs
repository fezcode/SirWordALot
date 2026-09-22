using Avalonia;
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
            var library = new Library(db);
            var settings = new Settings(db);
            Motion.Enabled = settings.Motion;

            // A first run with nothing in it gives you nothing to do. Seeds only
            // when the library is genuinely empty, so it never surprises a
            // returning user with words they did not choose.
            StarterLibrary.SeedIfEmpty(library);
            desktop.MainWindow = new MainWindow(library, settings, new Search(db), new Stats(db), new Lookup());
        }

        base.OnFrameworkInitializationCompleted();
    }
}
