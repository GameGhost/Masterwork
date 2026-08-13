namespace Masterwork.Engine.Rendering;

/// <summary>
/// Resolved <c>audio:</c> overrides for a <see cref="RenderedPopup"/> — each field has already gone
/// through expression evaluation (see <see cref="Masterwork.ModuleFormat.PopupAudio"/> for the
/// absent/empty/value semantics every field preserves), but not yet resolved against module-level
/// SFX-bucket fallbacks; that flat 2-level check happens at whichever firing site actually needs it
/// (popup open/Okay/Cancel), not here. Kept as one nested record — rather than seven more flat
/// properties on <see cref="RenderedPopup"/> — partly to mirror the YAML's own nesting, and partly
/// because <see cref="RenderedPopup.Okay"/>/<see cref="RenderedPopup.Cancel"/> already exist as
/// button-label strings; flat <c>Okay</c>/<c>Cancel</c> SFX properties would collide with those.
/// </summary>
public sealed record RenderedPopupAudio
{
    /// <summary>Background-track override while this popup is open, or <see langword="null"/> to inherit from the enclosing tier. Present-but-empty means explicit silence.</summary>
    public string? Music { get; init; }

    /// <summary>Open-transition SFX override, or <see langword="null"/> to use the module's <c>popup_open</c> default.</summary>
    public string? Open { get; init; }

    /// <summary>Milliseconds to delay <see cref="Open"/>.</summary>
    public int? OpenDelayMs { get; init; }

    /// <summary>Okay-click SFX override, or <see langword="null"/> to use the module's <c>popup_close</c> default.</summary>
    public string? Okay { get; init; }

    /// <summary>Milliseconds to delay <see cref="Okay"/>.</summary>
    public int? OkayDelayMs { get; init; }

    /// <summary>Cancel-click SFX override, or <see langword="null"/> to use the module's <c>popup_close</c> default.</summary>
    public string? Cancel { get; init; }

    /// <summary>Milliseconds to delay <see cref="Cancel"/>.</summary>
    public int? CancelDelayMs { get; init; }
}
