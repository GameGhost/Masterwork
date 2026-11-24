using System.Text.RegularExpressions;

namespace Masterwork.ModulePacker.Translation;

/// <summary>
/// The language-dependent parts of the checks for one target locale. A locale with its own entry
/// gets that entry; any other gets neutral defaults from its language subtag, so a new language can
/// be checked with no code change and given its own rules later.
/// </summary>
/// <param name="Code">The locale code, such as <c>fr-CA</c>.</param>
/// <param name="Typography">Normalizes the locale's punctuation spacing; null when the locale has no rules.</param>
/// <param name="SeparatesWords">False for scripts written without spaces between words (Japanese, Chinese, Thai), where a placeholder running into the next word is correct.</param>
/// <param name="LatinScript">False when the locale is written in another script, which turns on the <c>latin-script</c> flag.</param>
/// <param name="LengthRatioLow">The shortest expected translation, as a fraction of the source's length.</param>
/// <param name="LengthRatioHigh">The longest expected translation, as a multiple of the source's length.</param>
public sealed partial record TranslationLocale(
    string Code,
    Func<string, TranslationFileKind, TypographyResult>? Typography,
    bool SeparatesWords,
    bool LatinScript,
    double LengthRatioLow,
    double LengthRatioHigh)
{
    private static readonly Dictionary<string, TranslationLocale> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fr-CA"] = new("fr-CA", FrenchTypography.Normalize, SeparatesWords: true, LatinScript: true, 0.7, 2.0),
    };

    private static readonly HashSet<string> WithoutWordSpaces = new(StringComparer.OrdinalIgnoreCase) { "ja", "km", "lo", "my", "th", "zh" };

    private static readonly HashSet<string> NonLatin = new(StringComparer.OrdinalIgnoreCase)
    {
        "am", "ar", "be", "bg", "bn", "el", "fa", "gu", "he", "hi", "hy", "ja", "ka", "kk", "km", "kn",
        "ko", "lo", "mk", "ml", "mn", "mr", "my", "ne", "pa", "ru", "si", "ta", "te", "th", "uk", "ur", "zh",
    };

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex CodePattern();

    /// <summary>The rules for <paramref name="code"/>.</summary>
    /// <exception cref="FormatException"><paramref name="code"/> isn't shaped like a locale code.</exception>
    public static TranslationLocale For(string code)
    {
        if (!CodePattern().IsMatch(code))
        {
            throw new FormatException($"'{code}' isn't a locale code such as fr-CA.");
        }

        if (Known.TryGetValue(code, out var known))
        {
            return known;
        }

        var language = code.Split('-')[0];
        var separatesWords = !WithoutWordSpaces.Contains(language);
        return separatesWords
            ? new(code, null, SeparatesWords: true, LatinScript: !NonLatin.Contains(language), 0.5, 2.5)
            : new(code, null, SeparatesWords: false, LatinScript: false, 0.15, 1.2);
    }

    /// <summary>
    /// Reads the locale from a string file's name: <c>fr-CA.restext</c>, <c>fr-CA.overrides.restext</c>,
    /// <c>AppStrings.fr-CA.resx</c>. Takes the last dot-separated part shaped like a locale code.
    /// </summary>
    public static string? CodeFromFileName(string path) =>
        Path.GetFileNameWithoutExtension(path).Split('.').LastOrDefault(CodePattern().IsMatch);
}
