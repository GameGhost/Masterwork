namespace Masterwork.ModuleFormat;

/// <summary>The <c>audio:</c> block on a <see cref="LinkNode"/> — a click-sound override.</summary>
public sealed record LinkAudio
{
    /// <summary>SFX fired on click, or <c>${expr}</c>. Overrides the module's <c>click</c> default. Same absent/empty semantics as <see cref="PassageAudio.Music"/>.</summary>
    public string? Click { get; init; }

    /// <summary>Milliseconds to delay <see cref="Click"/>. Default <c>0</c>.</summary>
    public int? ClickDelayMs { get; init; }
}
