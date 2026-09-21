using System.Text;

namespace SirWordALot.Core;

/// <summary>Timestamped JSON snapshots with a fixed retention count.</summary>
public sealed class Backups(string folder)
{
    private const string Prefix = "sir-word-a-lot-";
    private const string Extension = ".json";

    public string Folder => folder;

    public string Write(Library library, DateTime nowUtc)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Prefix}{nowUtc:yyyyMMdd-HHmmss}{Extension}");
        WriteAtomic(path, Portability.ExportJson(library));
        return path;
    }

    public IReadOnlyList<string> List() =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, $"{Prefix}*{Extension}")
                       .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    public int Prune(int keep = 20)
    {
        var stale = List().Skip(Math.Max(0, keep)).ToList();
        foreach (var path in stale) File.Delete(path);
        return stale.Count;
    }

    /// <summary>Replaces the library wholesale. A pre-restore snapshot is the
    /// caller's responsibility; the Settings view writes one first.</summary>
    public ImportReport Restore(Library library, string path)
    {
        foreach (var entry in library.AllEntries(includeArchived: true)) library.DeleteEntry(entry.Id);
        foreach (var sentence in library.AllSentences()) library.DeleteSentence(sentence.Id);
        foreach (var deck in library.Decks()) library.DeleteDeck(deck.Id);

        return Portability.ImportJson(library, File.ReadAllText(path), MergePolicy.TakeTheirs);
    }

    /// <summary>Writes through a temporary file in the same directory, then renames,
    /// so a crash mid-write never leaves a truncated file at the target path.</summary>
    public static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.tmp");

        File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }
}
