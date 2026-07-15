namespace Masterwork.Extractor;

// Extractor-internal marker node for PassageTracker.instance.CheckProgress(...) calls that have a
// matching --progress-map entry with end-of-round body text. Produced by CradleExtractor.StitchFragments
// (replacing the terminal CheckProgressNode and the _ProgressRound assign that would otherwise sit in
// the expand-link's own content); consumed by V2Serializer.TransformPopup, which turns the enclosing
// expand-link into a layout: end_of_round popup instead of a bare navigation link — matching the
// reference app's ViewEndOfRound acknowledgement popup (PassageTracker.CheckProgress -> ViewEndOfRound.
// SetEndOfRound), which the source's CheckProgress call site alone doesn't otherwise represent.
internal sealed class EndOfRoundMarkerNode : MwsNode
{
    public override string Type => "__end_of_round__";
    public required string NextPassage { get; init; }
    public int ProgressValue { get; init; }
    public string? Body { get; init; }
    public string? Body2 { get; init; }

    // Nodes that sat before the terminal CheckProgress call in the source — typically a guarded
    // assignment that computes a dynamic NextPassage (e.g. Fear of the Unknown's Liberal2:
    // Vars.Liberal2nextpsg set via macros1.either() just before the call). These must run as
    // `onclose`, right before `target` resolves, not as passage-render-time `content` — content
    // runs against a sandboxed store clone the moment the popup renders, well before Okay is
    // clicked, which is too early for logic whose only purpose is feeding the target expression.
    public List<MwsNode> OncloseNodes { get; init; } = [];

    public override Dictionary<string, object?> ToDict() =>
        throw new InvalidOperationException("EndOfRoundMarkerNode must be consumed by V2Serializer before serialization.");
}
