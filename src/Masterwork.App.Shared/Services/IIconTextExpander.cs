using Microsoft.AspNetCore.Components;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Expands <c>{icon:slug}</c> refs in a display-text template into HTML-encoded markup with an
/// inline <c>&lt;img&gt;</c> per ref. The engine deliberately leaves <c>{icon:slug}</c> unevaluated
/// in any template-expanded string (see <c>VariableStore.ExpandTemplate</c>) so the App can resolve
/// it against the active module's assets — this is the one place that happens. Every renderer that
/// displays a template-expanded string (<c>text.value</c>, <c>section.title</c>, ...) should route
/// it through here rather than rendering the raw string, or an <c>{icon:...}</c> ref left in that
/// field just shows up as literal text.
/// </summary>
public interface IIconTextExpander
{
    /// <summary>Expands every <c>{icon:slug}</c> ref in <paramref name="value"/>; everything else is HTML-encoded as plain text. Returns empty markup for <see langword="null"/> or empty input.</summary>
    Task<MarkupString> ExpandAsync(string? value);
}
