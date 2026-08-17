namespace Masterwork.ModuleFormat;

/// <summary>
/// The <c>audio:</c> block on a <c>layouts/{id}.yaml</c> file — a third, SFX-only resolution tier
/// (node override → layout default → module default; see <c>docs/mws-format-latest.md</c> §6),
/// consulted only when a passage/popup using this layout doesn't itself override a given SFX
/// moment. Deliberately has no <c>Music</c> field — background-music resolution stays
/// passage/module/theme only (see <see cref="Masterwork.Engine.Audio.AudioResolver"/>); a shared
/// layout isn't a music decision. <see cref="Close"/> covers both Okay and Cancel with one shared
/// field, matching how the module-tier <c>popup_close</c> bucket already covers both.
/// </summary>
public sealed record LayoutChromeAudio
{
    /// <summary>On-display SFX default for any passage using this layout, or <c>${expr}</c>. Only consulted if the passage/its module have no override of their own.</summary>
    public string? OnDisplay { get; init; }

    /// <summary>Milliseconds to delay <see cref="OnDisplay"/>.</summary>
    public int? OnDisplayDelayMs { get; init; }

    /// <summary>Popup-open SFX default for any popup using this layout, or <c>${expr}</c>. Only consulted if the popup/its module have no override of their own.</summary>
    public string? Open { get; init; }

    /// <summary>Milliseconds to delay <see cref="Open"/>.</summary>
    public int? OpenDelayMs { get; init; }

    /// <summary>Popup-close SFX default (shared by Okay and Cancel) for any popup using this layout, or <c>${expr}</c>. Only consulted if the popup/its module have no override of their own.</summary>
    public string? Close { get; init; }

    /// <summary>Milliseconds to delay <see cref="Close"/>.</summary>
    public int? CloseDelayMs { get; init; }
}
