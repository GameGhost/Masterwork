using Microsoft.AspNetCore.Components;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// A single-process, single-window bridge letting MAUI's native back-button handling
/// (<c>MainPage.xaml.cs</c>'s <c>OnBackButtonPressed</c>) reach into the currently-active Blazor
/// circuit's own <see cref="NavigationManager"/> and <see cref="AppNavigationHistory"/> —
/// <c>BlazorWebView</c> doesn't publicly expose its own DI container to host/native code, so this is
/// the other direction: a Blazor component (<c>MainLayout.razor</c>, guaranteed to live for the whole
/// app session) registers its own injected instances here once, and native code calls back in.
/// Safe as a static holder <em>only</em> because this is a MAUI app — one process, one window, one
/// Blazor circuit ever active at a time. The same pattern would leak state across users on Blazor
/// Server and must never be copied there.
/// </summary>
public static class HardwareBackButtonBridge
{
    private static NavigationManager? _nav;
    private static AppNavigationHistory? _history;

    /// <summary>Registers the current session's instances — called once from <c>MainLayout.razor</c>'s <c>OnInitialized</c>.</summary>
    public static void Register(NavigationManager nav, AppNavigationHistory history)
    {
        _nav = nav;
        _history = history;
    }

    /// <summary>
    /// Attempts to navigate back to the previously-tracked route. Returns <see langword="false"/> if
    /// there's nothing to go back to (or nothing has registered yet), so the caller can fall through
    /// to the platform's own default back behavior — on Android, exiting the app.
    /// </summary>
    public static bool TryGoBack()
    {
        if (_nav is null || _history is null || !_history.TryGoBack(out var previousRelativeUri) || previousRelativeUri is null)
        {
            return false;
        }

        _nav.NavigateTo(previousRelativeUri);
        return true;
    }
}
