using Microsoft.Extensions.Logging;

namespace Masterwork.ModuleFormat;

/// <summary>
/// Threaded through YAML parsing (passage files and <c>_variables.yaml</c>) to attribute warnings
/// to their source. <see cref="Warnings"/> is never null internally, even if the caller didn't
/// supply one, so call sites never need a null-conditional just to record a warning.
/// </summary>
internal sealed class YamlParseContext(ModuleWarnings? warnings, string source = "(unknown source)", ILogger? logger = null)
{
    /// <summary>The warnings collector this context reports into.</summary>
    public ModuleWarnings Warnings { get; } = warnings ?? new ModuleWarnings();

    /// <summary>The current source label (e.g. a passage ID, or "_variables.yaml") used to prefix warning messages.</summary>
    public string Source { get; set; } = source;

    /// <summary>Records a warning, prefixed with the current <see cref="Source"/>, and mirrors it to the logger (if any) so it's visible without inspecting <see cref="Warnings"/>.</summary>
    public void Warn(string kind, string message)
    {
        Warnings.Add(kind, $"{Source}: {message}");
        logger?.LogWarning("[{Kind}] {Source}: {Message}", kind, Source, message);
    }
}
