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

    public override Dictionary<string, object?> ToDict() =>
        throw new InvalidOperationException("EndOfRoundMarkerNode must be consumed by V2Serializer before serialization.");
}
