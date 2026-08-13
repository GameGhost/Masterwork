namespace Masterwork.ModuleFormat;

/// <summary>
/// The <c>audio:</c> block on a <see cref="PopupNode"/> — background-music override while this
/// popup is open, plus open/okay/cancel SFX overrides. <see cref="Okay"/>/<see cref="Cancel"/> are
/// independently overridable even though the module-level default only has one shared
/// <c>popup_close</c> bucket for both — see <c>docs/mws-format-latest.md</c> §6 (<c>popup</c>).
/// </summary>
public sealed record PopupAudio
{
    /// <summary>Background-track override while this popup is open (topmost), or <c>${expr}</c>. Same absent/empty semantics as <see cref="PassageAudio.Music"/>.</summary>
    public string? Music { get; init; }

    /// <summary>SFX fired when this popup transitions closed→open, or <c>${expr}</c>. Overrides the module's <c>popup_open</c> default.</summary>
    public string? Open { get; init; }

    /// <summary>Milliseconds to delay <see cref="Open"/>. Default <c>0</c>.</summary>
    public int? OpenDelayMs { get; init; }

    /// <summary>SFX fired specifically when Okay is clicked, or <c>${expr}</c>. Overrides the module's <c>popup_close</c> default.</summary>
    public string? Okay { get; init; }

    /// <summary>Milliseconds to delay <see cref="Okay"/>. Default <c>0</c>.</summary>
    public int? OkayDelayMs { get; init; }

    /// <summary>SFX fired specifically when Cancel is clicked, or <c>${expr}</c>. Overrides the module's <c>popup_close</c> default — independently of <see cref="Okay"/>.</summary>
    public string? Cancel { get; init; }

    /// <summary>Milliseconds to delay <see cref="Cancel"/>. Default <c>0</c>.</summary>
    public int? CancelDelayMs { get; init; }
}
