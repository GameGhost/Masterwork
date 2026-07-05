using Masterwork.Engine;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Holds the current <see cref="GameSession"/> for the duration of one app session (one browser
/// tab, or one MAUI app instance), so it survives navigation between <c>SessionSetup</c> and
/// <c>Play</c>. Registered as a scoped DI service — in both the WebAssembly host and MAUI's
/// BlazorWebView, a DI scope lives for the whole app session, so this behaves like a singleton in
/// practice without needing to be one.
/// </summary>
public sealed class GameSessionState
{
    /// <summary>The currently loaded module, or <see langword="null"/> if no session has started yet.</summary>
    public LoadedModule? Module { get; private set; }

    /// <summary>The active session, or <see langword="null"/> if no session has started yet.</summary>
    public GameSession? Session { get; private set; }

    /// <summary>Starts tracking a newly created session.</summary>
    public void Start(LoadedModule module, GameSession session)
    {
        Module = module;
        Session = session;
    }
}
