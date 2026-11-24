namespace Masterwork.ModulePacker.Translation;

/// <summary>Which kind of string file a translation pair is: module restext or app resx.</summary>
public enum TranslationFileKind
{
    /// <summary>A module's <c>.restext</c> file: MWS markup (<c>**</c>, <c>_</c>, <c>//</c>), no HTML.</summary>
    Restext,

    /// <summary>An app <c>.resx</c> file: values may carry HTML tags, which must survive translation.</summary>
    Resx,
}

/// <summary>One check's finding on one string. <see cref="Check"/> is a stable id the runner can match on.</summary>
public sealed record TranslationFinding(string Check, string Detail);

/// <summary>The checks' result for one English/French string pair.</summary>
/// <param name="Failures">Hard failures: the string can't ship as it is.</param>
/// <param name="Flags">Soft findings passed to the judge, which may accept them.</param>
public sealed record StringVerdict(IReadOnlyList<TranslationFinding> Failures, IReadOnlyList<TranslationFinding> Flags);
