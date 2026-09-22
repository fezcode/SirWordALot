using SkiaSharp;

namespace SirWordALot.Measure;

/// <summary>Measures the rendered previews. This is what proves the HammerOS palette
/// and chrome geometry survive into the pixels, rather than only into the source.</summary>
internal static class Program
{
    private static int _failures;
    private static int _passes;

    private static readonly string[] Views =
        ["library", "sentences", "lookup", "quiz", "decks", "stats", "settings"];

    public static int Main(string[] args)
    {
        var folder = args.FirstOrDefault() ?? "dist/preview";

        if (!Directory.Exists(folder))
        {
            Console.WriteLine($"No preview folder at {folder}. Run tools/Preview first.");
            return 1;
        }

        foreach (var view in Views) MeasureChrome(Path.Combine(folder, $"{view}.png"), view);
        MeasureChrome(Path.Combine(folder, "entry.png"), "entry");
        MeasureIcon(Path.Combine(folder, "icon.png"));

        Console.WriteLine(_failures == 0
            ? $"All {_passes} measurements passed."
            : $"{_failures} of {_passes + _failures} measurements failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void MeasureChrome(string path, string view)
    {
        if (Load(path, view) is not { } bitmap) return;
        using (bitmap)
        {
            Expect($"{view} width", 1180, bitmap.Width);
            Expect($"{view} height", 760, bitmap.Height);

            // The 1px window border down the left edge.
            Expect($"{view} border", "#9CAEA0", Hex(bitmap, 0, 380));

            // Titlebar body: inside the border, left of the glyph, and again at
            // row 40 which must still be chrome because the bar is 43px tall.
            Expect($"{view} titlebar", "#E9EBDD", Hex(bitmap, 6, 20));
            Expect($"{view} titlebar depth", "#E9EBDD", Hex(bitmap, 320, 40));

            // Row 46 is past the titlebar and its 1px rule, so it is view surface.
            Expect($"{view} below titlebar", true, Hex(bitmap, 320, 46) != "#E9EBDD");

            // Sampled above the first nav row and below the last, so a selected
            // item's highlight cannot be mistaken for the sidebar itself.
            Expect($"{view} sidebar top", "#DFE4D6", Hex(bitmap, 8, 50));
            Expect($"{view} sidebar bottom", "#DFE4D6", Hex(bitmap, 8, 700));
            Expect($"{view} sidebar ends", true, Hex(bitmap, 200, 400) != "#DFE4D6");

            // Footer body, inside the 42px strip above the bottom border.
            Expect($"{view} footer", "#ECEDE0", Hex(bitmap, 600, 740));

            // A view that failed to render would leave the whole content area a flat
            // cream field, so require the surface to actually carry ink.
            Expect($"{view} draws content", true, DistinctColours(bitmap, 180, 50, 990, 660) > 12);

            // Exactly one sidebar entry may carry the selected tint. Button
            // backgrounds cross-fade, and a frozen transition once left six
            // highlighted at once in a headless render while the app was fine.
            Expect($"{view} highlights one nav row", 1, HighlightedNavRows(bitmap));
        }
    }

    private static void MeasureIcon(string path)
    {
        if (Load(path, "icon") is not { } bitmap) return;
        using (bitmap)
        {
            Expect("icon size", 256, bitmap.Width);
            Expect("icon border", "#9CAEA0", Hex(bitmap, 2, 128));
            Expect("icon field", "#ECEDE0", Hex(bitmap, 128, 20));
        }
    }

    private static SKBitmap? Load(string path, string label)
    {
        if (!File.Exists(path)) { Fail($"{label}: no image at {path}"); return null; }
        var bitmap = SKBitmap.Decode(path);
        if (bitmap is null) Fail($"{label}: could not decode {path}");
        return bitmap;
    }

    private static string Hex(SKBitmap bitmap, int x, int y)
    {
        var pixel = bitmap.GetPixel(x, y);
        return $"#{pixel.Red:X2}{pixel.Green:X2}{pixel.Blue:X2}";
    }

    /// <summary>Counts the sidebar bands painted with the selected tint, by walking
    /// down the nav column and tallying runs rather than assuming row positions.</summary>
    private static int HighlightedNavRows(SKBitmap bitmap)
    {
        const string Selected = "#CBD7C9";
        var runs = 0;
        var inRun = false;

        for (var y = 50; y < 710; y++)
        {
            var hit = Hex(bitmap, 120, y) == Selected;
            if (hit && !inRun) runs++;
            inRun = hit;
        }
        return runs;
    }

    /// <summary>How many distinct colours appear in a region. A blank view is one
    /// flat colour; a rendered one carries text, rules and controls.</summary>
    private static int DistinctColours(SKBitmap bitmap, int x, int y, int width, int height)
    {
        var seen = new HashSet<uint>();
        for (var dy = 0; dy < height; dy += 3)
            for (var dx = 0; dx < width; dx += 3)
            {
                var pixel = bitmap.GetPixel(x + dx, y + dy);
                seen.Add((uint)(pixel.Red << 16 | pixel.Green << 8 | pixel.Blue));
            }
        return seen.Count;
    }

    private static void Expect<T>(string name, T expected, T actual)
    {
        if (Equals(expected, actual)) { _passes++; return; }
        Fail($"{name}: expected {expected}, got {actual}");
    }

    private static void Fail(string message)
    {
        Console.WriteLine($"  FAIL  {message}");
        _failures++;
    }
}
