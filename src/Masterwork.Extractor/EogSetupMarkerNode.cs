namespace Masterwork.Extractor;

using Masterwork.ModuleFormat;

// Extractor-internal marker node for ViewEndOfGeneration.S_OnSetSpecialSetup calls.
// Produced by PassageBodyVisitor; consumed by V2Serializer.TransformPopup.
// When found in a popup's expand nodes, transforms the popup to layout: end_of_generation.
internal sealed class EogSetupMarkerNode : MwsNode
{
    public override string Type => "__eog_setup__";
    public string? Title { get; init; }
    public int CompletedRound { get; init; }
    public string? BodyText { get; init; }
    // Literal passageName argument (null when computed via conditional structure).
    public string? PassageName { get; init; }
    // Conditional nodes computing passageName when it's not a literal.
    // Each leaf LetNode has Var = "passageName".
    public List<MwsNode>? PassageNameNodes { get; init; }

    public override Dictionary<string, object?> ToDict() =>
        throw new InvalidOperationException("EogSetupMarkerNode must be consumed by V2Serializer before serialization.");
}
