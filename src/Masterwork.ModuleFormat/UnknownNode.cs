namespace Masterwork.ModuleFormat;

/// <summary>
/// Fallback for an unrecognized <c>type:</c> value. The parser logs an "unknown_node_type"
/// warning whenever this is produced rather than failing the whole module load, since a single
/// unrecognized node is usually recoverable (stale extractor output, a typo, or a future node
/// type this build doesn't know about yet).
/// </summary>
public sealed record UnknownNode : Node
{
    private readonly string _type;

    /// <summary>Creates an unknown-node placeholder carrying the original, unrecognized type string.</summary>
    /// <param name="type">The raw <c>type:</c> value that didn't match any known node type.</param>
    public UnknownNode(string type)
    {
        _type = type;
    }

    /// <inheritdoc/>
    public override string Type => _type;
}
