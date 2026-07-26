namespace Masterwork.App.Shared.Services;

/// <summary>
/// A simple app-level back-stack of visited routes, fed by <c>MainLayout</c>'s subscription to
/// <see cref="Microsoft.AspNetCore.Components.NavigationManager.LocationChanged"/>. Web doesn't need
/// this at all — the browser's own back/forward buttons already get real history for free via
/// <c>NavigationManager</c>'s built-in History API integration. It exists purely so MAUI's Android
/// hardware back button (which isn't wired to any browser-style history by default — see
/// <c>MainPage.xaml.cs</c>'s <c>OnBackButtonPressed</c>) has something to navigate to.
/// </summary>
/// <remarks>
/// <c>/play</c> is deliberately never pushed as a destination to go back <em>to</em> — the whole
/// point of <c>Play.razor</c>'s own <c>RegisterLocationChangingHandler</c> is that leaving it is a
/// deliberate decision (quit), never an implicit "back" hop, and once that decision is made the
/// session is cleared, so there'd be nothing valid to resume by returning to that route anyway. The
/// route <em>before</em> <c>/play</c> was entered is still tracked normally, so a back-press once
/// the player has actually left play returns to wherever they were browsing beforehand (e.g. Start
/// New Game), same as ordinary back navigation anywhere else in the app.
/// </remarks>
public sealed class AppNavigationHistory
{
    private readonly Stack<string> _history = new();
    private string? _current;

    /// <summary>Records a navigation to <paramref name="relativeUri"/> (e.g. <c>"new-game"</c>, no leading slash — see <see cref="Microsoft.AspNetCore.Components.NavigationManager.ToBaseRelativePath"/>).</summary>
    public void Track(string relativeUri)
    {
        if (relativeUri == _current)
        {
            return;
        }

        if (_current is not null && !IsPlayRoute(_current))
        {
            _history.Push(_current);
        }

        _current = relativeUri;
    }

    /// <summary>Pops the most recently tracked route, or returns <see langword="false"/> if there's nowhere to go back to (the caller should fall through to whatever the platform's own default back behavior is — typically exiting the app).</summary>
    public bool TryGoBack(out string? previousRelativeUri)
    {
        if (_history.Count == 0)
        {
            previousRelativeUri = null;
            return false;
        }

        previousRelativeUri = _history.Pop();
        return true;
    }

    private static bool IsPlayRoute(string relativeUri) =>
        relativeUri.TrimStart('/').Equals("play", StringComparison.OrdinalIgnoreCase);
}
