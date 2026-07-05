namespace Masterwork.ModuleFormat;

/// <summary>
/// Parses <c>_variables.yaml</c> into a flat name-&gt;<see cref="VarDef"/> map.
/// </summary>
public interface IVariableManifest
{
    /// <summary>Parses a variable manifest from raw YAML text.</summary>
    /// <param name="yamlText">The full contents of a <c>_variables.yaml</c> file.</param>
    /// <param name="warnings">Collector for unmatched/wrong-shaped field warnings. Pass <see langword="null"/> to discard them.</param>
    IReadOnlyDictionary<string, VarDef> Parse(string yamlText, ModuleWarnings? warnings = null);
}
