using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace SirWordALot.Shell;

/// <summary>The physical parts of the Braun theme that a stylesheet cannot express:
/// speaker grilles, brushed aluminium, indicator lamps and recessed readouts.
/// Only built while <see cref="AppTheme.IsBraun"/>; Classic never touches them.</summary>
public static class BraunParts
{
    private static readonly BoxShadows Raised = BoxShadows.Parse("0 1 2 0 #30000000, inset 0 1 0 0 #FFFFFFFF");
    private static readonly BoxShadows Recessed = BoxShadows.Parse("inset 0 2 4 0 #38000000, 0 1 0 0 #FFFFFFFF");
    private static readonly BoxShadows ReadoutShadow =
        BoxShadows.Parse("inset 0 2 6 0 #CC000000, 0 1 0 0 #FFFFFFFF");

    /// <summary>A moulded key: rounded, lit from above, casting a short shadow.</summary>
    public static Border Key(Border border, double radius)
    {
        border.CornerRadius = new CornerRadius(radius);
        border.BorderBrush = Brush.Parse("#B5B1A7");
        border.BoxShadow = Raised;
        return border;
    }

    /// <summary>A shallow tray pressed into the housing.</summary>
    public static Border Well(Control child, Thickness padding) => new()
    {
        CornerRadius = new CornerRadius(AppTheme.Current.Radius),
        Background = Brush.Parse("#E3E0D8"),
        BorderBrush = Brush.Parse("#CFCBC1"),
        BorderThickness = new Thickness(1),
        BoxShadow = Recessed,
        Padding = padding,
        Child = child,
    };

    /// <summary>The charcoal window a calculator shows its figures through.</summary>
    public static Border Readout(Control child, Thickness padding, double radius = 5) => new()
    {
        CornerRadius = new CornerRadius(radius),
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#141413"), 0),
                new GradientStop(Color.Parse("#2A2A27"), 1),
            },
        },
        BoxShadow = ReadoutShadow,
        Padding = padding,
        Child = child,
    };

    public static TextBlock ReadoutText(string text, double size) => new()
    {
        Text = text,
        FontFamily = Ui.Mono,
        FontSize = size,
        Foreground = Ui.Phosphor,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A figure in a readout above its caption, the index as a model number.</summary>
    public static Control StatTile(string index, string value, string caption)
    {
        var figure = ReadoutText(value, 22);
        figure.HorizontalAlignment = HorizontalAlignment.Right;

        var model = Ui.Label(index, Ui.Accent);
        return Well(Ui.Stack(10,
            Ui.Columns("*,Auto", Ui.Label(caption), model),
            Readout(figure, new Thickness(12, 8))), new Thickness(12, 12, 12, 12));
    }

    /// <summary>The round lamp beside each sidebar entry. The stylesheet lights it
    /// orange while its button carries the selected class.</summary>
    public static Ellipse Lamp() => new()
    {
        Width = 7,
        Height = 7,
        Classes = { "lamp" },
        Stroke = Brush.Parse("#40000000"),
        StrokeThickness = 0.8,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A highlight laid over a coloured button so it reads as convex.</summary>
    public static Ellipse Gloss(double size) => new()
    {
        Width = size,
        Height = size,
        IsHitTestVisible = false,
        Fill = new RadialGradientBrush
        {
            Center = new RelativePoint(0.4, 0.3, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.4, 0.25, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#B0FFFFFF"), 0),
                new GradientStop(Color.Parse("#00FFFFFF"), 0.7),
                new GradientStop(Color.Parse("#26000000"), 1),
            },
        },
    };
}

/// <summary>The perforated speaker grille of the T 3 pocket radio: a field of
/// round holes, each with a lit lower lip so it reads as punched rather than printed.</summary>
public sealed class Grille : Control
{
    public double Pitch { get; init; } = 6;
    public double Hole { get; init; } = 2.4;

    private static readonly IBrush HoleFill = Brush.Parse("#5C5A55");
    private static readonly IBrush Lip = Brush.Parse("#FFFFFFFF");

    public override void Render(DrawingContext context)
    {
        var columns = (int)Math.Floor((Bounds.Width - Hole) / Pitch) + 1;
        var rows = (int)Math.Floor((Bounds.Height - Hole) / Pitch) + 1;
        var offsetX = (Bounds.Width - (columns - 1) * Pitch) / 2;
        var offsetY = (Bounds.Height - (rows - 1) * Pitch) / 2;
        var r = Hole / 2;

        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var centre = new Point(offsetX + column * Pitch, offsetY + row * Pitch);
            context.DrawEllipse(Lip, null, centre + new Vector(0, 0.6), r, r);
            context.DrawEllipse(HoleFill, null, centre, r, r);
        }
    }
}

/// <summary>Brushed aluminium: fine horizontal grain over the panel's own gradient.
/// Seeded, so the grain is identical on every launch and in every preview.</summary>
public sealed class BrushedMetal : Control
{
    public override void Render(DrawingContext context)
    {
        var random = new Random(1962);
        for (double y = 0; y < Bounds.Height; y += 1)
        {
            var alpha = (byte)random.Next(0, 22);
            var light = random.Next(2) == 0;
            var pen = new Pen(new SolidColorBrush(light ? Color.FromArgb(alpha, 255, 255, 255)
                : Color.FromArgb((byte)(alpha / 2), 0, 0, 0)), 1);
            context.DrawLine(pen, new Point(0, y + 0.5), new Point(Bounds.Width, y + 0.5));
        }
    }
}
