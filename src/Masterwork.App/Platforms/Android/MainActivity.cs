using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Masterwork.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // Android renders edge-to-edge by default on API 35+ (and this app opts in explicitly here so
    // behavior is identical on older OS versions too), which is why BlazorWebView was stretching
    // under the status bar and behind the gesture/button navigation bar. Rather than pad around it
    // in CSS — which would leave `vh`/`vw` inside the page measuring the *full* screen instead of the
    // visible area, forcing every template to account for the gutters itself — the content view
    // hosting BlazorWebView is padded by the system bar insets here, so the WebView's own laid-out
    // size already excludes the status bar and nav bar in both orientations. No changes needed on
    // the Blazor/CSS side.
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Android's native WebView defaults to MediaPlaybackRequiresUserGesture = true — a
        // *separate* gate from the JS-level Chromium autoplay policy audio.js's own
        // attachUnlockListener() targets (see its own remarks). That JS-side fix covers desktop
        // browsers (which don't expose this native setting at all) but can never satisfy this one:
        // it's enforced by the native WebView beneath the JS layer, before any script — including a
        // pointerdown-triggered AudioContext.resume() — gets a say. Real symptom this caused,
        // reported on-device: no theme music/transition SFX at app boot, mute toggles had no
        // audible effect, starting a module produced no bgm — while plain playSfx() clicks (which
        // are literal, direct taps, not a Blazor-lifecycle-triggered async call several interop hops
        // removed from any native gesture) kept working throughout, since those already satisfied
        // whatever gesture association this same native gate was checking for. Must be set before
        // the BlazorWebView's own handler ever creates its native Android.Webkit.WebView (i.e.
        // before base.OnCreate below, which is what actually inflates the view tree) — the mapper
        // customization itself is a global, idempotent registration on
        // BlazorWebViewHandler.BlazorWebViewMapper, safe to re-run if OnCreate ever runs again
        // (e.g. Activity recreation).
        BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("AllowAudioAutoplay", (handler, view) =>
        {
            if (handler.PlatformView is Android.Webkit.WebView webView)
            {
                webView.Settings.MediaPlaybackRequiresUserGesture = false;
            }
        });

        // Must precede base.OnCreate (which calls SetContentView internally) per Android's own
        // edge-to-edge guidance — calling it after destabilized MAUI's internal Fragment-based
        // window-content host and surfaced a known MAUI/Android bug where its NavigationRootManager
        // fragment container borrows an already-compiled resource id (observed: androidx.constraintlayout's
        // R.id.jumpToStart) instead of a freshly generated one, which can go missing from the newly
        // inflated view tree when Android restores Activity state after a backgrounded process is
        // killed, crashing with "No view found for id ... NavigationRootManager_ElementBasedFragment".
        WindowCompat.SetDecorFitsSystemWindows(Window!, false);

        // Discards the OS-level Bundle instead of restoring into it — sidesteps the same crash at its
        // root, since this app has its own save/autosave system entirely independent of Android's
        // Activity instance-state, so nothing meaningful is actually lost here.
        base.OnCreate(null);

        var contentView = Window!.DecorView.FindViewById(Android.Resource.Id.Content)!;
        ViewCompat.SetOnApplyWindowInsetsListener(contentView, new SystemBarsInsetPaddingListener());
    }

    private sealed class SystemBarsInsetPaddingListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(Android.Views.View? v, WindowInsetsCompat? insets)
        {
            if (v is null || insets is null)
            {
                return insets;
            }

            var systemBars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars())!;
            v.SetPadding(systemBars.Left, systemBars.Top, systemBars.Right, systemBars.Bottom);
            return WindowInsetsCompat.Consumed;
        }
    }
}
