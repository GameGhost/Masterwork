namespace Masterwork.ModuleFormat;

/// <summary>
/// The kind of value an <see cref="InputNode"/> or <see cref="PromptNode"/> collects from the
/// player. There is no safe fallback for an unrecognized value here — an unrecognized value would
/// pick the wrong input widget for the player, so parsing it is a hard module load error rather
/// than a warning.
/// </summary>
public enum InputValueType
{
    String,
    Number,
}
