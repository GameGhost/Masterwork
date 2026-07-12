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
    // Path to a manually curated Key=Value restext file. Values that match extracted Common strings
    // use the curated key instead of an auto-generated Common_NNN one (see RestextCollector).
    public string? CommonRestextPath { get; set; }
    public string? ModuleId { get; set; }
    public string? ModuleTitle { get; set; }
    public string? SpriteMapPath { get; set; }
    // Path to a JSON map of passage name -> { layout, progress }: layout overrides InferLayout's
    // tag-based result; progress becomes a synthetic `_ProgressRound` assign wherever the source
    // has a matching PassageTracker.instance.CheckProgress(passageName, ...) call. See ProgressMapper.
    public string? ProgressMapPath { get; set; }
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
