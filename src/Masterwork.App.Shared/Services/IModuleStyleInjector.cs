using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Applies (or clears) the currently-loaded module's own stylesheet to the page — the other half
/// of the app-chrome-is-structure-only / module-provides-styling split (see
/// <see cref="GameSessionState.StyleCss"/>, <see cref="ModuleManifest.StylePath"/>). One
/// implementation (<see cref="JsModuleStyleInjector"/>) works on every host, same reasoning as
/// <see cref="IAudioPlayer"/> — MAUI's <c>BlazorWebView</c> is a real WebView, so DOM manipulation
/// via <see cref="Microsoft.JSInterop.IJSRuntime"/> behaves the same there as in a browser.
/// </summary>
public interface IModuleStyleInjector
{
    /// <summary>
    /// Replaces the page's module-provided <c>&lt;style&gt;</c> element with <paramref name="css"/>,
    /// or removes it entirely if <paramref name="css"/> is <see langword="null"/> or empty (e.g. a
    /// module with no stylesheet, or after "Abandon Game" clears the session).
    /// </summary>
    Task ApplyAsync(string? css);
}
