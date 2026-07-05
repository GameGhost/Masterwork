namespace Masterwork.ModuleFormat;

/// <summary>
/// A session variable's declared type and default value, from <c>_variables.yaml</c> (or the
/// fixed typing rule the engine applies to standard variables — see
/// <see cref="Masterwork.ModuleFormat.VariableManifest"/>).
/// </summary>
public class VarDef
{
    /// <summary>The variable name.</summary>
    public string Name { get; set; } = "";

    /// <summary>One of <c>int</c>, <c>string</c>, or <c>array</c>.</summary>
    public string VarType { get; set; } = "string";

    /// <summary>The variable's default value, typed per <see cref="VarType"/>.</summary>
    public object? Default { get; set; }

    /// <summary><see langword="true"/> for engine-provided variables (nameA-E, townname, playerCount, ...).</summary>
    public bool IsStandard { get; set; }
}
