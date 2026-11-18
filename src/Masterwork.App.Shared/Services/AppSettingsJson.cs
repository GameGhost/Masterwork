using System.Text.Json;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// The one way <see cref="AppSettings"/> is turned into text and back, shared by every head's store.
///
/// It exists because the stores used to disagree. The web store serialized the whole record, so a new
/// setting persisted automatically; the native store wrote a hand-maintained list of fields, so a new
/// setting was silently dropped there unless someone remembered to add it. Three settings were missed
/// that way — hidden catalog listings, added sources, and trusted publishers — and each looked like it
/// worked, because a page holding settings in memory kept them until the next reload. Routing both
/// stores through here means a setting persists on every head as soon as it exists, and one test
/// covers all of them.
/// </summary>
public static class AppSettingsJson
{
    /// <summary>Serializes the full settings record.</summary>
    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings);

    /// <summary>
    /// Deserializes settings, falling back to defaults for anything unreadable. Fields missing from
    /// older saved JSON take their declared defaults, so adding a setting never breaks loading one
    /// saved before it existed.
    /// </summary>
    public static AppSettings Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return AppSettings.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Default;
        }
        catch (JsonException)
        {
            // A corrupt settings blob shouldn't stop the app starting; the player loses preferences
            // they can set again, not access to their content.
            return AppSettings.Default;
        }
    }
}
