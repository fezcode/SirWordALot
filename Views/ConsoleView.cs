using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class ConsoleView : UserControl
{
    private readonly ConsoleCommands _commands;
    private readonly StackPanel _output = Ui.Stack(2);
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input = new() { Watermark = "Type help to begin" };
    private readonly List<string> _history = [];
    private int _historyIndex;

    public ConsoleView(ShellContext context)
    {
        _commands = new ConsoleCommands(context.Library, context.Search, context.Stats, context.Settings);
        _input.Classes.Add("terminal");

        Write("SIR WORD-A-LOT [VERSION 0.1.0]");
        Write("(c) Fezcode. Every word is valued.");
        Write("");
        Write("Secure session established.");
        Write("");

        _scroll = new ScrollViewer
        {
            Content = _output,
            Padding = new Thickness(22, 18),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var prompt = Ui.Columns("Auto,*",
            Ui.Text("swl:~ $ ", 12.5, Ui.Phosphor, mono: true),
            _input);
        prompt.Margin = new Thickness(22, 0, 22, 14);

        var footer = new Border
        {
            Height = 30,
            Background = Brush.Parse("#0C2429"),
            Padding = new Thickness(22, 0),
            Child = Ui.Columns("*,Auto",
                Ui.Label("SWL  /  DEPARTMENT SHELL", Brush.Parse("#5E8F8B")),
                Ui.Label("LIVE", Ui.Phosphor)),
        };

        Content = new Border
        {
            Background = Ui.Dark,
            Child = Ui.Rows("*,Auto,Auto", _scroll, prompt, footer),
        };

        _input.KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Submit();
                e.Handled = true;
                break;
            case Key.Up when _history.Count > 0:
                _historyIndex = Math.Max(0, _historyIndex - 1);
                _input.Text = _history[_historyIndex];
                e.Handled = true;
                break;
            case Key.Down when _history.Count > 0:
                _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
                _input.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
                e.Handled = true;
                break;
        }
    }

    private void Submit()
    {
        var line = _input.Text ?? "";
        _input.Text = "";

        if (line.Trim().Length > 0)
        {
            _history.Add(line);
            _historyIndex = _history.Count;
        }

        Write($"swl:~ $ {line}", Brush.Parse("#6FA69C"));

        foreach (var output in _commands.Run(line))
        {
            if (output == ConsoleCommands.ClearSignal) { _output.Children.Clear(); return; }
            Write(output);
        }

        Write("");
        _scroll.ScrollToEnd();
    }

    private void Write(string text, IBrush? color = null) =>
        _output.Children.Add(Ui.Text(text, 12.5, color ?? Ui.Phosphor, mono: true));
}
