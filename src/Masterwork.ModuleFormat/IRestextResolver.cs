namespace Masterwork.ModuleFormat;

/// <summary>
/// Resolves <c>restext://Key</c> references against a loaded locale dictionary.
/// </summary>
public interface IRestextResolver
{
    /// <summary>Returns a copy of <paramref name="passage"/> with every restext reference resolved.</summary>
    /// <param name="passage">The passage to resolve.</param>
    /// <param name="locale">Locale dictionary from <see cref="IRestextFile.Parse"/>.</param>
    /// <param name="warnings">Collector for missing-key warnings. Pass <see langword="null"/> to discard them.</param>
    MwsPassageDoc Resolve(MwsPassageDoc passage, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings = null);

    /// <summary>Returns a copy of <paramref name="chrome"/> with every restext reference resolved.</summary>
    /// <param name="chrome">The layout chrome document to resolve.</param>
    /// <param name="locale">Locale dictionary from <see cref="IRestextFile.Parse"/>.</param>
    /// <param name="warnings">Collector for missing-key warnings. Pass <see langword="null"/> to discard them.</param>
    LayoutChromeDoc ResolveLayoutChrome(LayoutChromeDoc chrome, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings = null);
}
