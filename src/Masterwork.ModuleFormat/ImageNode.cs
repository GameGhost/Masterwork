namespace Masterwork.ModuleFormat;

/// <summary>Standalone image asset. The <c>type: image</c> node.</summary>
public sealed record ImageNode : Node
{
    /// <inheritdoc/>
    public override string Type => "image";

    /// <summary>Asset URI for the image.</summary>
    public required string Asset { get; init; }

    /// <summary>Size hint preserved as-is from the source's <c>&lt;size=N&gt;</c> tag, if any.</summary>
    public string? Size { get; init; }

    /// <summary>Horizontal alignment, if specified. Falls back to <see langword="null"/> on an unrecognized value.</summary>
    public Alignment? Align { get; init; }

    /// <summary>Formatted title/alt text for the image, if any (string formatting — see the format spec).</summary>
    public string? Title { get; init; }

    /// <summary>Open, module-extensible visual style vocabulary, styled entirely by module CSS.</summary>
    public string? Style { get; init; }
}
