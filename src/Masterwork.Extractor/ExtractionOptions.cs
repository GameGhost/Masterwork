namespace Masterwork.Extractor;

public enum BreaksMode { Omit, Emit, EmitCommented }

public class ExtractionOptions
{
    public string InputDir { get; set; } = "";  // file path or directory path
    public string PassagesOutDir { get; set; } = "";
    // Where _variables.yaml is written. Defaults to PassagesOutDir when not set via --variables-out.
    public string? VariablesOutDir { get; set; }
    // Where en-US.restext is written. Defaults to PassagesOutDir when not set via --restext-out.
    public string? RestextOutDir { get; set; }
    public string? ModuleId { get; set; }
    public string? ModuleTitle { get; set; }
    public string? SpriteMapPath { get; set; }
    public bool IncludeDebug { get; set; }
    public bool DryRun { get; set; }
    public bool SeedAnalysis { get; set; }
    // Controls how break/paragraph_break nodes that are trailing or between non-rendered nodes are emitted.
    public BreaksMode Breaks { get; set; } = BreaksMode.Omit;
    // Tags that mark passages to exclude from restext string extraction (e.g. "notext").
    public HashSet<string> RestextExcludeTags { get; set; } = new(StringComparer.Ordinal);
    // Specific passage IDs to exclude from restext string extraction.
    public HashSet<string> RestextExcludeIds { get; set; } = new(StringComparer.Ordinal);
}
