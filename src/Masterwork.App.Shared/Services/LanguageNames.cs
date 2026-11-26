using System.Globalization;

namespace Masterwork.App.Shared.Services;

/// <summary>Names for language codes as shown in language pickers.</summary>
public static class LanguageNames
{
    /// <summary>
    /// <paramref name="code"/>'s name in its own language, capitalized, e.g. <c>"Français (Canada)"</c>
    /// for <c>fr-CA</c>. Returns the code itself when the platform doesn't know the culture.
    /// </summary>
    public static string Native(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            var name = culture.NativeName;

            // A culture the platform has no data for comes back named after its own code, e.g. "xx (YY)".
            var language = code.Split('-')[0];
            if (string.IsNullOrWhiteSpace(name) || culture.Equals(CultureInfo.InvariantCulture)
                || string.Equals(name, language, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(language + " (", StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }

            return culture.TextInfo.ToUpper(name[0]) + name[1..];
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }
}
