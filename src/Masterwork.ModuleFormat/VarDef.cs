namespace Masterwork.ModuleFormat;

/// <summary>
/// A session or let variable's declared type. Mirrors <c>Masterwork.Engine.StoryValue</c>'s value
/// kinds (string/int/bool/record/array), with arrays split by element kind purely as
/// declaration-time documentation — <c>StoryValue.ArrayVal</c> is untyped/heterogeneous at runtime,
/// so this distinction isn't enforced by anything that reads it.
/// </summary>
public enum VarKind
{
    String,
    Integer,
    Boolean,
    Record,
    StringArray,
    IntArray,
    BooleanArray,
    RecordArray,
}

/// <summary>Converts between <see cref="VarKind"/> and its <c>_variables.yaml</c> text form.</summary>
public static class VarKindText
{
    /// <summary>Renders a <see cref="VarKind"/> as the lowercase token written to <c>_variables.yaml</c>.</summary>
    public static string ToYaml(this VarKind kind) => kind switch
    {
        VarKind.String => "string",
        VarKind.Integer => "int",
        VarKind.Boolean => "bool",
        VarKind.Record => "record",
        VarKind.StringArray => "string_array",
        VarKind.IntArray => "int_array",
        VarKind.BooleanArray => "bool_array",
        VarKind.RecordArray => "record_array",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Parses a <c>_variables.yaml</c> type token. Also accepts the legacy bare <c>"array"</c>
    /// token (pre-typed-array extractor output) as <see cref="VarKind.StringArray"/> — arrays are
    /// untyped at runtime, so this is a harmless default, not a real element-type claim.
    /// </summary>
    /// <exception cref="FormatException">The token isn't a recognized type.</exception>
    public static VarKind Parse(string text) => text switch
    {
        "string" => VarKind.String,
        "int" => VarKind.Integer,
        "bool" => VarKind.Boolean,
        "record" => VarKind.Record,
        "string_array" => VarKind.StringArray,
        "int_array" => VarKind.IntArray,
        "bool_array" => VarKind.BooleanArray,
        "record_array" => VarKind.RecordArray,
        "array" => VarKind.StringArray,
        _ => throw new FormatException($"Unknown variable type '{text}'"),
    };
}

/// <summary>
/// A session variable's declared type and (rarely) an explicit default, from <c>_variables.yaml</c>
/// (or the fixed typing rule the engine applies to legacy standard variables — see
/// <see cref="Masterwork.ModuleFormat.VariableManifest"/>).
/// </summary>
public class VarDef
{
    /// <summary>The variable name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The variable's declared type.</summary>
    public VarKind VarType { get; set; } = VarKind.String;

    /// <summary>
    /// Explicit default, typed per <see cref="VarType"/>. Present only when it differs from
    /// <see cref="VarType"/>'s canonical zero value (empty string, <c>0</c>, <c>false</c>, empty
    /// record, empty array) — see <c>VariableStore</c>'s default derivation, which uses the
    /// canonical zero whenever this is <see langword="null"/>. Always <see langword="null"/> for
    /// <see cref="VarKind.Record"/>/<see cref="VarKind.RecordArray"/>: Cradle's <c>VarDefs</c> has
    /// no record type, so there's no source a non-empty record default could come from.
    /// </summary>
    public object? Default { get; set; }

    /// <summary>
    /// <see langword="true"/> for variables read from a legacy <c>standard_variables:</c> list
    /// (nameA-E, townname, playerCount, ...). The extractor no longer generates this — see
    /// <see cref="Masterwork.ModuleFormat.VariableManifest"/>'s class remarks.
    /// </summary>
    public bool IsStandard { get; set; }
}
