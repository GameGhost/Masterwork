using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Masterwork.ModuleFormat;

/// <summary>Thrown when catalog JSON can't be read as a <see cref="CatalogDocument"/>.</summary>
public sealed class CatalogParseException(string message) : Exception(message);

/// <summary>
/// Reads and writes <c>catalog.json</c>. Snake_case on the wire, matching the field style the MWS
/// YAML files already use.
/// </summary>
public static class CatalogParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new Iso8601DateTimeConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitEmptyCollections } },

        // Writes apostrophes and em-dashes as themselves rather than ' and —. The default
        // encoder escapes anything that could be dangerous when JSON is pasted straight into HTML,
        // which a catalog never is — it's fetched and parsed, and the app renders these strings
        // through Blazor, which escapes on output. Every description in a real catalog contains at
        // least one of these, so the default turns the whole file into escape sequences. Quotes,
        // backslashes, and control characters are still escaped.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // An absent list and an empty one mean the same thing to every reader of a catalog, so only one
    // of them is worth writing. Done at serialization rather than by making the properties nullable:
    // in memory they stay non-null and always enumerable, so no caller has to null-check a list.
    private static void OmitEmptyCollections(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType == typeof(string)
                || !typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            property.ShouldSerialize = static (_, value) => value switch
            {
                null => false,
                System.Collections.ICollection collection => collection.Count > 0,
                System.Collections.IEnumerable enumerable => Any(enumerable),
                _ => true,
            };
        }
    }

    private static bool Any(System.Collections.IEnumerable enumerable)
    {
        var enumerator = enumerable.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Parses catalog JSON, rejecting anything structurally unusable. An unrecognized
    /// <c>format</c> is a hard failure rather than a warning (the treatment a manifest's own
    /// <c>format:</c> gets): a manifest comes from content this build already has in hand, whereas a
    /// catalog comes from a remote source that may be newer than the app, and guessing at entries
    /// whose meaning has changed is worse than refusing to read them.
    /// </summary>
    public static CatalogDocument Parse(byte[] json)
    {
        CatalogDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<CatalogDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new CatalogParseException($"Catalog is not valid JSON: {ex.Message}");
        }

        if (document is null)
        {
            throw new CatalogParseException("Catalog is empty.");
        }

        if (document.Format != CatalogFormatVersion.Current)
        {
            throw new CatalogParseException(
                $"Unsupported catalog format '{document.Format}' — this build understands '{CatalogFormatVersion.Current}'.");
        }

        foreach (var entry in document.Entries)
        {
            if (entry.Type is not ("module" or "assets"))
            {
                throw new CatalogParseException($"Catalog entry '{entry.Id}' has unknown type '{entry.Type}'.");
            }

            // A malformed hash would otherwise surface only after a full download, as an install
            // failure that looks like a corrupted transfer rather than a bad catalog.
            if (entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit))
            {
                throw new CatalogParseException($"Catalog entry '{entry.Id}' has a malformed sha256.");
            }

            // Rejected at parse time, not just at download time, so a catalog that tries to name a
            // host (or climb out of its own content base) is refused whole rather than per-entry
            // when someone happens to click it.
            if (!CatalogPaths.IsSafeRelative(entry.Path))
            {
                throw new CatalogParseException(
                    $"Catalog entry '{entry.Id}' has path '{entry.Path}' — entries must be relative to the source's content base.");
            }

            if (entry.Thumbnail is { } thumbnail)
            {
                if (!CatalogPaths.IsSafeRelative(thumbnail.Path))
                {
                    throw new CatalogParseException(
                        $"Catalog entry '{entry.Id}' has thumbnail path '{thumbnail.Path}' — thumbnails must be relative to the catalog's own directory.");
                }

                if (thumbnail.Hash.Length != 64 || !thumbnail.Hash.All(Uri.IsHexDigit))
                {
                    throw new CatalogParseException($"Catalog entry '{entry.Id}' has a malformed thumbnail hash.");
                }
            }
        }

        return document;
    }

    /// <summary>Serializes a catalog for publishing. The bytes this returns are exactly what a detached signature must cover.</summary>
    public static byte[] Write(CatalogDocument document) => JsonSerializer.SerializeToUtf8Bytes(document, Options);
}
