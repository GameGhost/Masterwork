namespace Masterwork.ModuleFormat;

/// <summary>
/// Parses a single <c>.mws.yaml</c> passage file (MWS v0.3) into an <see cref="MwsPassageDoc"/> tree.
/// </summary>
public interface IPassageYamlParser
{
    /// <summary>
    /// Parses a passage document from raw YAML text.
    /// </summary>
    /// <param name="yamlText">The full contents of a <c>.mws.yaml</c> file.</param>
    /// <param name="warnings">
    /// Collector for non-fatal issues (unmatched fields, wrong-shaped fields, unknown node types,
    /// stale format versions). Pass <see langword="null"/> to discard warnings.
    /// </param>
    /// <exception cref="MwsParseException">
    /// The document is empty, or a required field is missing/malformed with no safe fallback.
    /// </exception>
    MwsPassageDoc ParsePassage(string yamlText, ModuleWarnings? warnings = null);
}
