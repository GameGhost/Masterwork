using Masterwork.ModuleFormat;

namespace Masterwork.Engine.Rendering;

/// <summary>A rendered player-clickable input form.</summary>
public sealed record RenderedInput : RenderedAction
{
    /// <summary>Formatted label for the button/link that opens the form.</summary>
    public required string Label { get; init; }

    /// <summary>One of <c>link</c> or <c>button</c>.</summary>
    public string? Style { get; init; }

    /// <summary>Formatted instruction text shown inside the form.</summary>
    public required string Text { get; init; }

    /// <summary>The kind of value collected.</summary>
    public required InputValueType InputType { get; init; }

    /// <summary>Session variable that receives the submitted value.</summary>
    public required string Var { get; init; }

    /// <summary>Target passage_id, or <c>"${expr}"</c>, navigated to after submission.</summary>
    public required string OnSubmit { get; init; }
}
