namespace Masterwork.App.Shared.Services;

/// <summary>
/// The decorative parts of a screen whose whole structure (not just an image or color) can vary
/// per theme — currently just the main menu's background scene. Registered by whichever host
/// project (<c>Masterwork.App</c>, <c>Masterwork.App.Web.Client</c>) wires up the active theme,
/// since those are the only projects that reference both this interface (via
/// <c>Masterwork.App.Shared</c>) and a concrete theme's own component types — the theme project
/// itself never references <c>Masterwork.App.Shared</c>, avoiding a circular project reference.
/// A different, structurally unrelated future theme (a different layer count, no "manor"/"trees"
/// concept at all, etc.) just registers a different <see cref="MainMenuTheme"/> pointing at its own
/// component — <see cref="Masterwork.App.Shared.Pages.MainMenu"/> itself never changes.
/// </summary>
public interface IMainMenuTheme
{
    /// <summary>
    /// A component type rendered in place of the main menu's background scene layer stack, via
    /// <c>&lt;DynamicComponent Type="Theme.SceneComponentType" /&gt;</c>. Takes no parameters —
    /// purely decorative, <c>aria-hidden</c> from the host page's own perspective.
    /// </summary>
    Type SceneComponentType { get; }

    /// <summary>
    /// Background-music URL for the app's pre-module screens (main menu, help, etc.), or
    /// <see langword="null"/> for silence. A code-level property, not a YAML surface — theme audio
    /// applies only to app-shell views, deliberately outside the per-module <c>audio:</c> resolution
    /// stack (see <c>docs/mws-format-latest.md</c> §6 "Audio"). Starts playing when the app shell is
    /// shown and stops the instant a module loads to play, resuming on return to the main menu.
    /// </summary>
    string? MusicUrl { get; }

    /// <summary>SFX for app-shell screen transitions, or <see langword="null"/> for none. Same app-shell-only scope as <see cref="MusicUrl"/>.</summary>
    string? TransitionSfxUrl { get; }
}

/// <summary>Plain <see cref="IMainMenuTheme"/> implementation — a host project just instantiates
/// this with the active theme's own component type, no per-theme C# class needed.</summary>
public sealed class MainMenuTheme(Type sceneComponentType, string? musicUrl = null, string? transitionSfxUrl = null) : IMainMenuTheme
{
    public Type SceneComponentType { get; } = sceneComponentType;
    public string? MusicUrl { get; } = musicUrl;
    public string? TransitionSfxUrl { get; } = transitionSfxUrl;
}
