using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SirWordALot.Core;

namespace SirWordALot.Shell;

/// <summary>A contribution grid: one column per week, one cell per day, tinted from
/// the surface towards the theme's accent as the count rises.</summary>
public sealed class Heatmap(IReadOnlyList<HeatmapDay> days) : Control
{
    private const double Cell = 11, Gap = 3;

    protected override Size MeasureOverride(Size availableSize)
    {
        var weeks = (int)Math.Ceiling(days.Count / 7.0);
        return new Size(weeks * (Cell + Gap), 7 * (Cell + Gap));
    }

    public override void Render(DrawingContext context)
    {
        if (days.Count == 0) return;
        var busiest = Math.Max(1, days.Max(d => d.Count));

        for (var i = 0; i < days.Count; i++)
        {
            var week = i / 7;
            var weekday = i % 7;
            var level = days[i].Count == 0
                ? 0
                : Math.Clamp((int)Math.Ceiling(days[i].Count / (double)busiest * 4), 1, 4);

            // Braun rounds each cell into a small key; Classic keeps square pixels.
            context.FillRectangle(AppTheme.Current.HeatLevels[level],
                new Rect(week * (Cell + Gap), weekday * (Cell + Gap), Cell, Cell),
                AppTheme.IsBraun ? 2.5f : 0);
        }
    }
}

/// <summary>Correct-rate by interval bucket, drawn as a polyline with markers.</summary>
public sealed class RetentionCurve(IReadOnlyList<RetentionPoint> points) : Control
{
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 420 : availableSize.Width, 120);

    public override void Render(DrawingContext context)
    {
        if (points.Count < 2 || Bounds.Width <= 0) return;

        var pen = new Pen(Ui.Accent, 1.5);
        var axis = new Pen(Ui.Line, 1);
        var height = Bounds.Height - 18;
        var step = Bounds.Width / (points.Count - 1);

        context.DrawLine(axis, new Point(0, height), new Point(Bounds.Width, height));

        Point At(int i) => new(i * step, height - points[i].Rate * height);

        for (var i = 1; i < points.Count; i++) context.DrawLine(pen, At(i - 1), At(i));
        for (var i = 0; i < points.Count; i++)
            context.DrawEllipse(points[i].Samples == 0 ? Ui.Line : Ui.Accent, null, At(i), 2.5, 2.5);
    }
}
