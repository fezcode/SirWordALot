using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class StatsView : UserControl, IDisposable
{
    private readonly ShellContext _context;
    private readonly ContentControl _body = new();

    public Overview Overview { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public StatsView(ShellContext context)
    {
        _context = context;
        Content = _body;
        context.Library.Changed += Refresh;
        Refresh();
    }

    public void Dispose() => _context.Library.Changed -= Refresh;

    public void Refresh()
    {
        var now = DateTime.UtcNow;
        Overview = _context.Stats.Summary(now);

        var breakdown = _context.Stats.Breakdown();
        var heatmapDays = _context.Stats.Heatmap(now);
        var curve = _context.Stats.RetentionCurve();
        var goal = _context.Settings.DailyGoal;

        var tiles = Ui.Columns("*,12,*,12,*,12,*,12,*,12,*",
            Ui.StatTile("01", $"{Overview.TotalWords}", "WORDS HELD"),
            new Border(),
            Ui.StatTile("02", $"{Overview.DueToday}", "DUE TODAY"),
            new Border(),
            Ui.StatTile("03", $"{Overview.Mastered}", "MASTERED"),
            new Border(),
            Ui.StatTile("04", $"{Overview.StreakDays}", "DAY STREAK"),
            new Border(),
            Ui.StatTile("05", $"{Overview.Retention:P0}", "RETENTION"),
            new Border(),
            Ui.StatTile("06", $"{Overview.ReviewsToday}/{goal}", "TODAY"));

        var heatmap = Ui.Stack(10,
            Ui.Columns("*,Auto",
                Ui.Label("REVIEW ACTIVITY  ·  53 WEEKS"),
                Ui.Label($"{heatmapDays.Sum(d => d.Count)} REVIEWS")),
            new ScrollViewer
            {
                Content = new Heatmap(heatmapDays),
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            });

        var bars = Ui.Stack(9, Ui.Label("MASTERY BREAKDOWN"));
        var largest = Math.Max(1, breakdown.Values.DefaultIfEmpty(0).Max());

        foreach (var (bucket, count) in breakdown.OrderBy(p => p.Key))
        {
            var track = new Grid { Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
            track.Children.Add(new Border { Height = 14, Background = Ui.RowRule });
            track.Children.Add(new Border
            {
                Height = 14,
                Background = Ui.Accent,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(1, count / (double)largest * 340),
            });

            bars.Children.Add(Ui.Columns("130,Auto,*",
                Ui.Label(bucket.ToString().ToUpperInvariant()),
                track,
                Ui.Text($"   {count}", 12, Ui.Muted, mono: true)));
        }

        var axis = Ui.Row(0, curve.Select(p => (Control)Ui.Label($"{p.UpperDays}D    ")).ToArray());

        var retention = Ui.Stack(10,
            Ui.Label("RETENTION BY INTERVAL"),
            new RetentionCurve(curve),
            axis);

        var header = Ui.Heading("DEPARTMENT RECORD", "The work, measured.",
            "Every number here comes from your own answers.");
        header.Margin = new Thickness(0, 0, 0, 24);

        _body.Content = Ui.Scroll(Ui.Pad(Ui.Stack(28,
            header, tiles, Ui.Rule(), heatmap, Ui.Rule(), bars, Ui.Rule(), retention), 32));
    }
}
