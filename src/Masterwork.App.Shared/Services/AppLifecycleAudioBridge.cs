namespace Masterwork.App.Shared.Services;

/// <summary>
/// Lets MAUI's native app-lifecycle events (Android's <c>OnPause</c>/<c>OnResume</c>, Windows'
/// <c>Window.Activated</c>, iOS's <c>OnResignActivation</c>/<c>OnActivated</c> once that target
/// exists — all registered in <c>MauiProgram</c>'s <c>ConfigureLifecycleEvents</c>) reach the
/// currently-active Blazor circuit's own <see cref="IAudioPlayer"/>, the same direction and same
/// static-holder pattern as <see cref="HardwareBackButtonBridge"/> — see its own remarks for why
/// this is safe only because MAUI is one process, one window, one Blazor circuit at a time, and
/// must never be copied to Blazor Server.
///
/// Not used by <c>Masterwork.App.Web.Client</c> (the pure web build) at all — a browser tab has no
/// native "app lifecycle," so <c>wwwroot/audio.js</c>'s own <c>document.visibilitychange</c>
/// listener calls the same underlying JS pause/resume functions directly, with no C# involved.
/// </summary>
public static class AppLifecycleAudioBridge
{
    private static IAudioPlayer? _audioPlayer;

    /// <summary>Registers the current session's instance — called once from <c>MainLayout.razor</c>'s <c>OnInitialized</c>.</summary>
    public static void Register(IAudioPlayer audioPlayer) => _audioPlayer = audioPlayer;

    /// <summary>The app itself has lost foreground focus — pause background music. No-op if nothing has registered yet.</summary>
    public static void OnBackgrounded() => _ = _audioPlayer?.PauseBgmForBackgroundAsync();

    /// <summary>The app has regained foreground focus — resume background music. No-op if nothing has registered yet.</summary>
    public static void OnForegrounded() => _ = _audioPlayer?.ResumeBgmFromBackgroundAsync();
}
