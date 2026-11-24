namespace Masterwork.Translation;

/// <summary>One check's finding on one string. <see cref="Check"/> is a stable id the runner can match on.</summary>
public sealed record TranslationFinding(string Check, string Detail);

/// <summary>The checks' result for one source/target string pair.</summary>
/// <param name="Failures">Hard failures: the string can't ship as it is.</param>
/// <param name="Flags">Soft findings passed to the judge, which may accept them.</param>
public sealed record StringVerdict(IReadOnlyList<TranslationFinding> Failures, IReadOnlyList<TranslationFinding> Flags);
