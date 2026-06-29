namespace Masterwork.Extractor;

public class ExtractionOptions
{
    public string InputDir { get; set; } = "";  // file path or directory path
    public string OutputDir { get; set; } = "";
    public string? ModuleId { get; set; }
    public string? ModuleTitle { get; set; }
    public string? SpriteMapPath { get; set; }
    public bool IncludeDebug { get; set; }
    public bool DryRun { get; set; }
    public bool SeedAnalysis { get; set; }
    // Directory of hand-authored override YAML files; matched by filename + passage_id.
    public string? OverridesDir { get; set; }
}
