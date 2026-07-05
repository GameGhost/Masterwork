namespace Masterwork.ModuleFormat;

/// <summary>A paragraph separator (a larger visual gap than <see cref="BreakNode"/>). The <c>type: paragraph_break</c> node.</summary>
public sealed record ParagraphBreakNode : Node
{
    /// <inheritdoc/>
    public override string Type => "paragraph_break";
}
