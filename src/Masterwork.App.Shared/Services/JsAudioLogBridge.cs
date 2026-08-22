using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Static JS-callable bridge so <c>wwwroot/audio.js</c>'s own diagnostic trail (see its own
/// <c>logAudio</c>) lands in the same rolling file log as everything else, not just a devtools
/// console a player can't reach on a locked-down device — the original motivation was an Android
/// bug report where the file log (reachable only via <c>adb</c>) never told us anything useful
/// because the JS-side trail that actually explained the bug never left the browser console.
///
/// A STATIC <c>[JSInvokable]</c> method, not an instance one behind a <c>DotNetObjectReference</c>
/// — audio.js's own log calls fire from many different contexts (native DOM event listeners like
/// <c>pointerdown</c>/<c>visibilitychange</c>, media element events like <c>waiting</c>/<c>stalled</c>)
/// with no single C#-owned component instance to thread a reference through; a static method,
/// callable via <c>DotNet.invokeMethodAsync("Masterwork.App.Shared", "Log", ...)</c> from anywhere
/// in JS, needs none.
///
/// <see cref="Configure"/> is called once, by whichever host actually has a real file logger to
/// forward into (currently just <c>Masterwork.App</c>/MAUI — see its own <c>MauiProgram</c>).
/// Never configured on <c>Masterwork.App.Web.Client</c> (WASM): there's no filesystem there either,
/// so <see cref="Log"/> is a harmless no-op and the browser console stays the log, same as
/// <see cref="FileLoggerProvider"/>'s own documented split.
/// </summary>
public static class JsAudioLogBridge
{
    private static ILogger? _logger;

    // Log is only ever reached via JS reflection (DotNet.invokeMethodAsync), never called directly
    // from C# — with no static-analysis-visible caller, a trimmed Release build (Android's own
    // linker pass, distinct from Blazor WASM's own JSInvokable-aware trimming) could remove it as
    // dead code. DynamicDependency, anchored on Configure (which IS directly called, from
    // MauiProgram, so always survives), tells the trimmer to keep Log alongside it regardless.
    [DynamicDependency(nameof(Log))]
    public static void Configure(ILogger logger)
    {
        _logger = logger;
        _logger.LogInformation("JsAudioLogBridge configured — audio.js log entries should appear below with an [audio.js] prefix.");
    }

    [JSInvokable]
    public static void Log(string level, string message)
    {
        var logLevel = level switch
        {
            "warn" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => LogLevel.Debug,
        };
        _logger?.Log(logLevel, "[audio.js] {Message}", message);
    }
}
