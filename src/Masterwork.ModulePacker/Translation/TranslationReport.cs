using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Masterwork.ModulePacker.Translation;

/// <summary>The checks' result for one key of a translation pair.</summary>
/// <param name="English">Null when the key exists only in the French file.</param>
/// <param name="French">Null when the French file has no entry for the key.</param>
/// <param name="Fixed">True when the typography fixer rewrote this string's spacing.</param>
public sealed record StringReport(
    string Key,
    string? English,
    string? French,
    IReadOnlyList<TranslationFinding> Failures,
    IReadOnlyList<TranslationFinding> Flags,
    bool Fixed);

/// <summary>A problem with the French file itself rather than one string, such as a value broken across lines.</summary>
public sealed record FileProblem(int Line, string Message);

/// <summary>The checks' result for a whole English/French file pair.</summary>
public sealed record TranslationReport(
    TranslationFileKind Kind,
    string English,
    string French,
    IReadOnlyList<FileProblem> FileProblems,
    IReadOnlyList<StringReport> Strings)
{
    // Pair-level findings, alongside the per-string ones in TranslationChecks.
    public const string UnknownKey = "unknown-key";
    public const string Untranslated = "untranslated";

    /// <summary>True when nothing failed hard. Flags don't count.</summary>
    public bool Passed => FileProblems.Count == 0 && Strings.All(s => s.Failures.Count == 0);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The machine-readable form the translate-verify runner reads. Every key is listed, clean ones too.</summary>
    public string ToJson() => JsonSerializer.Serialize(new
    {
        Kind,
        English,
        French,
        Passed,
        Counts = new
        {
            Strings = Strings.Count,
            Failed = Strings.Count(s => s.Failures.Count > 0),
            Flagged = Strings.Count(s => s.Flags.Count > 0),
            Fixed = Strings.Count(s => s.Fixed),
        },
        FileProblems,
        Strings,
    }, JsonOptions);

    /// <summary>A readable summary: counts, then each failing string, then each flagged one.</summary>
    public string ToSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Checked {Strings.Count} strings: {English} -> {French}");
        foreach (var problem in FileProblems)
        {
            sb.AppendLine($"  line {problem.Line}: {problem.Message}");
        }

        var fixedCount = Strings.Count(s => s.Fixed);
        if (fixedCount > 0)
        {
            sb.AppendLine($"Fixed typography in {fixedCount} strings.");
        }

        AppendSection(sb, "Failures", s => s.Failures);
        AppendSection(sb, "Flags", s => s.Flags);
        sb.AppendLine(Passed ? "PASS" : "FAIL");
        return sb.ToString();
    }

    private void AppendSection(StringBuilder sb, string title, Func<StringReport, IReadOnlyList<TranslationFinding>> pick)
    {
        var hits = Strings.Where(s => pick(s).Count > 0).ToList();
        sb.AppendLine($"{title}: {hits.Count} strings");
        foreach (var s in hits)
        {
            foreach (var finding in pick(s))
            {
                sb.AppendLine($"  {s.Key}: {finding.Check}: {finding.Detail}");
            }
        }
    }
}
