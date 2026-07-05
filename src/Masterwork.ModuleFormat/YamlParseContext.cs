namespace Masterwork.ModuleFormat;

// Threaded through YAML parsing (passage files and _variables.yaml) to attribute warnings to
// their source. Warnings is never null internally, even if the caller didn't supply one, so call
// sites never need a null-conditional just to record a warning.
internal sealed class YamlParseContext(ModuleWarnings? warnings, string source = "(unknown source)")
{
    public ModuleWarnings Warnings { get; } = warnings ?? new ModuleWarnings();
    public string Source { get; set; } = source;

    public void Warn(string kind, string message) => Warnings.Add(kind, $"{Source}: {message}");
}
