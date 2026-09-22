namespace SirWordALot.Core;

/// <summary>Seeds a brand-new library so the first run has something to study.
///
/// The words are a curated vocabulary set rather than the first entries of the
/// dictionary alphabetically, which would be "aardvark, aback, abacus". Each one
/// is looked up in the bundled offline dictionary for its Turkish meaning; words
/// the dictionary does not know are still added, just without one.</summary>
public static class StarterLibrary
{
    public const string Tag = "starter";

    /// <summary>Words worth knowing, grouped roughly by the register they belong to.</summary>
    private static readonly (string Word, string PartOfSpeech, string Definition)[] Words =
    [
        ("eminence", "noun", "A position of superiority or fame."),
        ("laconic", "adjective", "Using very few words."),
        ("obdurate", "adjective", "Stubbornly refusing to change an opinion."),
        ("pellucid", "adjective", "Translucently clear; easy to understand."),
        ("quixotic", "adjective", "Idealistic to the point of being impractical."),
        ("ersatz", "adjective", "An inferior substitute for the real thing."),
        ("candour", "noun", "Openness and honesty of expression."),
        ("prudent", "adjective", "Acting with care and thought for the future."),
        ("tenacious", "adjective", "Holding firmly; not readily letting go."),
        ("meticulous", "adjective", "Showing great attention to detail."),
        ("ambiguous", "adjective", "Open to more than one interpretation."),
        ("arduous", "adjective", "Involving strenuous effort; difficult."),
        ("candid", "adjective", "Truthful and straightforward."),
        ("diligent", "adjective", "Showing steady, careful effort."),
        ("eloquent", "adjective", "Fluent and persuasive in speech or writing."),
        ("frugal", "adjective", "Sparing with money or food."),
        ("gregarious", "adjective", "Fond of company; sociable."),
        ("impartial", "adjective", "Treating all rivals or disputants equally."),
        ("lucid", "adjective", "Expressed clearly; easy to understand."),
        ("mundane", "adjective", "Lacking interest or excitement; ordinary."),
        ("nuance", "noun", "A subtle difference in meaning or expression."),
        ("obsolete", "adjective", "No longer produced or used; out of date."),
        ("plausible", "adjective", "Seeming reasonable or probable."),
        ("reticent", "adjective", "Not revealing one's thoughts readily."),
        ("scrutiny", "noun", "Critical observation or examination."),
        ("substantiate", "verb", "To provide evidence for; to prove."),
        ("tangible", "adjective", "Perceptible by touch; clear and definite."),
        ("ubiquitous", "adjective", "Present or found everywhere."),
        ("vindicate", "verb", "To clear of blame or suspicion."),
        ("wary", "adjective", "Feeling or showing caution about danger."),
        ("zealous", "adjective", "Showing great energy for a cause."),
        ("abate", "verb", "To become less intense or widespread."),
        ("benevolent", "adjective", "Well meaning and kindly."),
        ("coherent", "adjective", "Logical and consistent."),
        ("deference", "noun", "Polite submission and respect."),
        ("enigma", "noun", "A person or thing that is mysterious."),
        ("fastidious", "adjective", "Very attentive to accuracy and detail."),
        ("garrulous", "adjective", "Excessively talkative."),
        ("hackneyed", "adjective", "Overused and therefore unoriginal."),
        ("implicit", "adjective", "Implied though not plainly expressed."),
        ("juxtapose", "verb", "To place close together for contrasting effect."),
        ("keen", "adjective", "Having or showing eagerness or enthusiasm."),
        ("latent", "adjective", "Existing but not yet developed or visible."),
        ("magnanimous", "adjective", "Generous or forgiving, especially to a rival."),
        ("novel", "adjective", "New and not resembling something formerly known."),
        ("obscure", "adjective", "Not discovered or known about; uncertain."),
        ("paradox", "noun", "A statement that contradicts itself yet may be true."),
        ("quell", "verb", "To put an end to, typically by force."),
        ("resilient", "adjective", "Able to recover quickly from difficulties."),
        ("stoic", "adjective", "Enduring hardship without showing feeling."),
        ("transient", "adjective", "Lasting only for a short time."),
        ("undermine", "verb", "To damage or weaken, especially gradually."),
        ("venerable", "adjective", "Accorded great respect because of age or wisdom."),
        ("whimsical", "adjective", "Playfully quaint or fanciful."),
        ("astute", "adjective", "Having sharp judgement; shrewd."),
        ("brevity", "noun", "Concise and exact use of words."),
        ("cogent", "adjective", "Clear, logical and convincing."),
        ("dubious", "adjective", "Hesitating or doubting; not to be relied upon."),
        ("ephemeral", "adjective", "Lasting for a very short time."),
        ("futile", "adjective", "Incapable of producing any useful result."),
    ];

    /// <summary>Adds the starter words when the library is empty. Returns how many
    /// were added, and zero for a library that already holds something — this must
    /// never surprise a returning user with words they did not choose.</summary>
    public static int SeedIfEmpty(Library library, OfflineDictionary? dictionary = null)
    {
        if (library.Count > 0) return 0;
        return Seed(library, dictionary ?? OfflineDictionary.Instance);
    }

    public static int Seed(Library library, OfflineDictionary dictionary)
    {
        var added = 0;

        foreach (var (word, partOfSpeech, definition) in Words)
        {
            if (library.FindEntry(word) is not null) continue;

            var id = library.AddEntry(word, "starter");
            var translations = dictionary.Translate(word);

            library.UpdateEntry(library.GetEntry(id)! with
            {
                PartOfSpeech = partOfSpeech,
                Definition = definition,
                Translation = translations.Count > 0 ? string.Join(", ", translations) : null,
            });

            library.TagEntry(id, Tag);
            added++;
        }

        return added;
    }

    public static int WordCount => Words.Length;
}
