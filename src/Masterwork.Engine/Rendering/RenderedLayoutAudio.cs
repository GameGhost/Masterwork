namespace Masterwork.Engine.Rendering;

/// <summary>
/// Resolved <c>audio:</c> defaults for a <see cref="RenderedLayoutChrome"/> — each field has already
/// gone through expression evaluation (see <see cref="Masterwork.ModuleFormat.LayoutChromeAudio"/>
/// for the absent/empty/value semantics every field preserves), but not yet resolved against the
/// module-level SFX-bucket fallback; that three-way check happens at whichever firing site actually
/// needs it (passage on_display, popup open/Okay/Cancel), not here.
/// </summary>
public sealed record RenderedLayoutAudio
{
    /// <summary>On-display SFX default, or <see langword="null"/> to fall through to the module's <c>transition</c> default.</summary>
    public string? OnDisplay { get; init; }

    /// <summary>Milliseconds to delay <see cref="OnDisplay"/>.</summary>
    public int? OnDisplayDelayMs { get; init; }

    /// <summary>Popup-open SFX default, or <see langword="null"/> to fall through to the module's <c>popup_open</c> default.</summary>
    public string? Open { get; init; }

    /// <summary>Milliseconds to delay <see cref="Open"/>.</summary>
    public int? OpenDelayMs { get; init; }

    /// <summary>Popup-close SFX default (shared by Okay and Cancel), or <see langword="null"/> to fall through to the module's <c>popup_close</c> default.</summary>
    public string? Close { get; init; }

    /// <summary>Milliseconds to delay <see cref="Close"/>.</summary>
    public int? CloseDelayMs { get; init; }
}
