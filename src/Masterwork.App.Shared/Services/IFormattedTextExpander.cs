using Microsoft.AspNetCore.Components;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Expands a template-expanded display-text string into HTML: <c>{icon:slug}</c> refs become an
/// inline <c>&lt;img&gt;</c>, <c>**bold**</c>/<c>_italic_</c> spans become <c>&lt;strong&gt;</c>/
/// <c>&lt;em&gt;</c>, everything else is HTML-encoded as plain text. The engine deliberately leaves
/// both forms unevaluated in any template-expanded string (see <c>VariableStore.ExpandTemplate</c>)
/// so the App is the one place they become real markup — every renderer that displays a
/// template-expanded string (<c>text.value</c>, <c>section.title</c>, ...) should route it through
/// here rather than rendering the raw string, or the syntax just shows up as literal text.
/// </summary>
public interface IFormattedTextExpander
{
    /// <summary>Expands every <c>{icon:slug}</c> ref and <c>**bold**</c>/<c>_italic_</c> span in <paramref name="value"/>; everything else is HTML-encoded as plain text. Returns empty markup for <see langword="null"/> or empty input.</summary>
    Task<MarkupString> ExpandAsync(string? value);
}
