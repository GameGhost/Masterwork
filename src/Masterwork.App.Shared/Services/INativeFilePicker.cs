namespace Masterwork.App.Shared.Services;

/// <summary>
/// Which file the picker is being opened for — the two matter differently per platform (extension
/// filter, Android MIME types, dialog title) even though they share the exact same native-picker
/// machinery and Windows-specific fix (see <see cref="INativeFilePicker"/>'s own remarks).
/// </summary>
public enum NativeFileKind
{
    /// <summary>A <c>.mwm</c> module package — see <c>StartNewGame.razor</c>'s upload flow.</summary>
    ModulePackage,

    /// <summary>An exported <c>.mwsave</c> save file — see <c>ContinueList.razor</c>'s import flow.</summary>
    SaveFile,
}

/// <summary>
/// Native, out-of-WebView file picking — the workaround for a still-open WebView2 platform bug
/// (MicrosoftEdge/WebView2Feedback#3551) where the HTML <c>&lt;input type="file"&gt;</c> element
/// Blazor's own <c>InputFile</c> component renders (used directly on the Web/WASM heads, where
/// there's no WebView2 involved) crashes the whole process with a native <c>STATUS_BREAKPOINT</c> on
/// Windows: WebView2's internal handling of the OS file-open dialog runs a nested message loop with a
/// 30-second re-entrancy timer, and — specifically when a debugger is attached — deliberately raises
/// <c>__debugbreak()</c> once that timer elapses, or sooner depending on internal WebView2 state.
/// Confirmed by Microsoft's own WebView2 team as a WebView2 bug, not a MAUI/Blazor/.NET one, and (as
/// of this writing) still unresolved after 14+ months. <see cref="NullNativeFilePicker"/> is the
/// default (Web/WASM heads keep using <c>InputFile</c>, which doesn't hit this bug — there's no
/// WebView2 there); <c>MauiNativeFilePicker</c> (<c>Masterwork.App</c>) overrides it with the real
/// native picker.
///
/// Originally module-package-only (<c>IModuleFilePicker</c>) — generalized to <see cref="NativeFileKind"/>
/// once <c>ContinueList.razor</c>'s save-import flow needed the exact same out-of-WebView picking (and,
/// on Windows, the exact same <c>WindowId</c>-association fix — see <c>MauiNativeFilePicker</c>'s own
/// remarks) for a second file type rather than duplicating the whole mechanism.
/// </summary>
public interface INativeFilePicker
{
    /// <summary>
    /// <see langword="true"/> on heads that should use <see cref="PickAsync"/> instead of rendering
    /// an <c>InputFile</c> element.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Prompts for a single file of the given kind and returns its full contents, or
    /// <see langword="null"/> if the picker was cancelled. Never called when <see cref="IsAvailable"/>
    /// is <see langword="false"/>.
    /// </summary>
    Task<byte[]?> PickAsync(NativeFileKind kind);
}
