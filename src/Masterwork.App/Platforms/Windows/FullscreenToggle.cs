using Microsoft.JSInterop;
using Microsoft.UI.Windowing;

namespace Masterwork.App.Platforms.Windows;

/// <summary>
/// F11 fullscreen toggle, invoked from the web content via JS (see <c>wwwroot/fullscreenToggle.js</c>)
/// rather than a native WinUI <c>KeyboardAccelerator</c> — that approach (tried first) only fired
/// while a native window element had focus, not while focus was inside the <c>BlazorWebView</c> (the
/// overwhelmingly common case), which meant F11 could enter fullscreen but then never exit it. It
/// also showed WinUI's own "F11" keyboard-accelerator hint tooltip, which could get stuck on-screen
/// with no way to dismiss it. A page-level <c>keydown</c> listener has neither problem — it fires
/// regardless of focus within the page, and draws no native UI of its own.
/// </summary>
public static class FullscreenToggle
{
    private static AppWindow? _appWindow;

    /// <summary>Captures the app's single window, set once from <c>MauiProgram</c>'s <c>OnWindowCreated</c>.</summary>
    public static void Initialize(AppWindow appWindow) => _appWindow = appWindow;

    [JSInvokable]
    public static void Toggle()
    {
        if (_appWindow is null)
        {
            return;
        }

        _appWindow.SetPresenter(_appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Default
            : AppWindowPresenterKind.FullScreen);
    }
}
