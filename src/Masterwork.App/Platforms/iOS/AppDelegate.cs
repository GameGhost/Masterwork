using Foundation;

namespace Masterwork.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // TODO when this target is un-deferred: BlazorWebView needs to be kept inside the safe area
    // (notch/Dynamic Island, home indicator) the same way Platforms/Android/MainActivity.cs does for
    // Android's system bars — see "Mobile safe-area / system-bar insets" in ../../../CLAUDE.md for the
    // approach (native inset handling, not CSS, so vh/vw stay correct with no per-template changes).
}
