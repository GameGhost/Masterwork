namespace Masterwork.ModulePacker.Translation;

/// <summary>Which kind of string file a translation pair is: module restext or app resx.</summary>
public enum TranslationFileKind
{
    /// <summary>A module's <c>.restext</c> file: MWS markup (<c>**</c>, <c>_</c>, <c>//</c>), no HTML.</summary>
    Restext,

    /// <summary>An app <c>.resx</c> file: values may carry HTML tags, which must survive translation.</summary>
    Resx,
}
