namespace Masterwork.ModuleFormat;

/// <summary>
/// Parses an <c>en-US.restext</c> locale file: one <c>Key=Value</c> per line, single-line values
/// only. <c>#</c>-prefixed lines are comments; blank lines are ignored.
/// </summary>
public interface IRestextFile
{
    /// <summary>Parses restext text into a key/value dictionary.</summary>
    /// <param name="text">The full contents of an <c>en-US.restext</c> file.</param>
    IReadOnlyDictionary<string, string> Parse(string text);
}
