using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SirWordALot.Shell;

public static class AppIcon
{
    private static Bitmap? _bitmap;

    private static Uri Asset(string name) =>
        new($"avares://{typeof(AppIcon).Assembly.GetName().Name}/Assets/{name}");

    public static WindowIcon Window()
    {
        using var stream = AssetLoader.Open(Asset("SirWordALot.ico"));
        return new WindowIcon(stream);
    }

    public static Control Mark(double size)
    {
        if (_bitmap is null)
        {
            using var stream = AssetLoader.Open(Asset("icon.png"));
            _bitmap = new Bitmap(stream);
        }
        return new Image { Width = size, Height = size, Source = _bitmap, Stretch = Stretch.Uniform };
    }
}
