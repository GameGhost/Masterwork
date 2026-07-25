namespace Masterwork.App.Shared.Services;

/// <summary>
/// Native, out-of-WebView file picking for uploading a module package — the workaround for a
/// still-open WebView2 platform bug (MicrosoftEdge/WebView2Feedback#3551) where the HTML
/// <c>&lt;input type="file"&gt;</c> element Blazor's own <c>InputFile</c> component renders (used
/// directly on the Web/WASM heads, where there's no WebView2 involved) crashes the whole process
/// with a native <c>STATUS_BREAKPOINT</c> on Windows: WebView2's internal handling of the OS file-open
/// dialog runs a nested message loop with a 30-second re-entrancy timer, and — specifically when a
/// debugger is attached — deliberately raises <c>__debugbreak()</c> once that timer elapses, or
/// sooner depending on internal WebView2 state. Confirmed by Microsoft's own WebView2 team as a
/// WebView2 bug, not a MAUI/Blazor/.NET one, and (as of this writing) still unresolved after 14+
/// months. <see cref="NullModuleFilePicker"/> is the default (Web/WASM heads keep using
/// <c>InputFile</c>, which doesn't hit this bug — there's no WebView2 there); <c>MauiModuleFilePicker</c>
/// (<c>Masterwork.App</c>) overrides it with the real <see cref="Microsoft.Maui.Storage.FilePicker"/>.
/// </summary>
public interface IModuleFilePicker
{
    /// <summary>
    /// <see langword="true"/> on heads that should use <see cref="PickAsync"/> instead of rendering
    /// an <c>InputFile</c> element.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Prompts for a single file and returns its full contents, or <see langword="null"/> if the
    /// picker was cancelled. Never called when <see cref="IsAvailable"/> is <see langword="false"/>.
    /// </summary>
    Task<byte[]?> PickAsync();
}
