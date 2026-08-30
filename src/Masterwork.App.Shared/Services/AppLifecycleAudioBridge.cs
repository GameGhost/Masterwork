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
///
/// De-dupes on the current state before ever touching <see cref="IAudioPlayer"/> — real bug found
/// via a player report: typing into a text-entry passage on Windows made the WebView progressively
/// laggier and then fully unresponsive. Root cause was <c>Window.Activated</c> (WinUI3) firing
/// repeatedly during text input — a known BlazorWebView/WebView2 quirk where focus churn between
/// the embedded WebView2 child window and the host window surfaces as spurious activation-state
/// transitions — with nothing here previously stopping a redundant "still backgrounded"/"still
/// foregrounded" signal from spawning yet another fire-and-forget JS interop call
/// (<c>PauseBgmForBackgroundAsync</c>/<c>ResumeBgmFromBackgroundAsync</c>, each a real
/// <c>audio.pause()</c>/<c>audio.play()</c> on a live element). At event-storm frequency those
/// calls piled up faster than the interop queue and the browser's media pipeline could drain them.
/// <c>wwwroot/audio.js</c>'s own <c>backgroundPaused</c> flag already no-ops a redundant call once
/// it arrives — this guard stops it one layer earlier, before it ever crosses into JS at all, since
/// the flood itself (not just the redundant work each call eventually did) was the actual problem.
/// </summary>
public static class AppLifecycleAudioBridge
{
    private static IAudioPlayer? _audioPlayer;
    private static bool _isBackgrounded;

    /// <summary>Registers the current session's instance — called once from <c>MainLayout.razor</c>'s <c>OnInitialized</c>.</summary>
    public static void Register(IAudioPlayer audioPlayer) => _audioPlayer = audioPlayer;

    /// <summary>The app itself has lost foreground focus — pause background music. No-op if nothing has registered yet, or if already backgrounded.</summary>
    public static void OnBackgrounded()
    {
        if (_isBackgrounded)
        {
            return;
        }

        _isBackgrounded = true;
        _ = _audioPlayer?.PauseBgmForBackgroundAsync();
    }

    /// <summary>The app has regained foreground focus — resume background music. No-op if nothing has registered yet, or if not currently backgrounded.</summary>
    public static void OnForegrounded()
    {
        if (!_isBackgrounded)
        {
            return;
        }

        _isBackgrounded = false;
        _ = _audioPlayer?.ResumeBgmFromBackgroundAsync();
    }
}
