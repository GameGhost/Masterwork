namespace Masterwork.ModuleFormat;

/// <summary>Multi-way dispatch on a single variable. The <c>type: switch</c> node.</summary>
public sealed record SwitchNode : Node
{
    /// <inheritdoc/>
    public override string Type => "switch";

    /// <summary>The variable name being matched against.</summary>
    public required string On { get; init; }

    /// <summary>Cases evaluated in order; the first one that matches is rendered.</summary>
    public required IReadOnlyList<SwitchCase> Cases { get; init; }

    /// <summary>Fallback nodes rendered when no case matches, if present.</summary>
    public IReadOnlyList<Node>? Default { get; init; }
}
