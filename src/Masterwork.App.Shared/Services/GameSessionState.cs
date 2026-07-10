using Masterwork.Engine;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Holds the current <see cref="GameSession"/> for the duration of one app session (one browser
/// tab, or one MAUI app instance), so it survives navigation between the app shell and
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

    /// <summary>The installed-module id the active session belongs to (see <see cref="IModuleStore"/>) — needed to key its autosave (<see cref="SaveIds.Autosave"/>).</summary>
    public string? ModuleId { get; private set; }

    /// <summary>The installed module's version at the time this session started — recorded into every save (see <see cref="SaveEntry.ModuleVersion"/>) for the Continue List's compatibility check.</summary>
    public string? ModuleVersion { get; private set; }

    /// <summary>
    /// The module-content language chosen when this session started (see <see cref="Masterwork.ModuleFormat.ModuleLocales"/>).
    /// Fixed for the lifetime of the session — no mid-session language switching yet — and carried
    /// into every save for this session so resuming loads the same language back (<see cref="SaveEntry.Language"/>).
    /// </summary>
    public string? Language { get; private set; }

    /// <summary>
    /// The active module's raw asset bytes (keyed by in-package path, e.g. <c>"assets/icons/village.png"</c>),
    /// for <see cref="IAssetResolver"/>'s bundle-local tier. Empty if no session has started yet or
    /// the module has no assets.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> Assets { get; private set; } = new Dictionary<string, byte[]>();

    /// <summary>The active module's stylesheet text, if it has one — see <see cref="LoadedModuleContent.StyleCss"/>.</summary>
    public string? StyleCss { get; private set; }

    /// <summary>Starts tracking a newly created or restored session.</summary>
    public void Start(string moduleId, string moduleVersion, string? language, LoadedModuleContent content, GameSession session)
    {
        ModuleId = moduleId;
        ModuleVersion = moduleVersion;
        Language = language;
        Module = content.Module;
        Assets = content.Assets;
        StyleCss = content.StyleCss;
        Session = session;
    }

    /// <summary>Clears the active session (e.g. after "Abandon Game" returns to the Main App Menu).</summary>
    public void Clear()
    {
        ModuleId = null;
        ModuleVersion = null;
        Language = null;
        Module = null;
        Assets = new Dictionary<string, byte[]>();
        StyleCss = null;
        Session = null;
    }
}
