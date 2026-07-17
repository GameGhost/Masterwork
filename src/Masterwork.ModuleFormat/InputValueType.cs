namespace Masterwork.ModuleFormat;

/// <summary>
/// The kind of value an <see cref="InputNode"/> collects from the player. Not declared on the node
/// itself — derived at render time from the <see cref="VarKind"/> of the variable bound by
/// <see cref="InputNode.Var"/>.
/// </summary>
public enum InputValueType
{
    String,
    Number,
    Boolean,
}
