using System.Text.Json;
using System.Text.Json.Serialization;

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
    };

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
        }

        return document;
    }

    /// <summary>Serializes a catalog for publishing. The bytes this returns are exactly what a detached signature must cover.</summary>
    public static byte[] Write(CatalogDocument document) => JsonSerializer.SerializeToUtf8Bytes(document, Options);
}
