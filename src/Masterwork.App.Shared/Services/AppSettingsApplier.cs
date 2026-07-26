using System.Globalization;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Pushes an <see cref="AppSettings"/> value onto the running app — <see cref="IAudioPlayer"/> for
/// volume/mute, the <c>--mws-text-scale</c> CSS variable (via <c>wwwroot/appSettings.js</c>) for
/// text size, and thread culture for the UI language (see <see cref="ApplyCulture"/>). Shared by
/// <see cref="Masterwork.App.Shared.Layout.MainLayout"/> (applies the last-saved settings once at
/// startup) and <see cref="Masterwork.App.Shared.Chrome.OptionsDialog"/> (applies a freshly-edited
/// draft on Apply) so there's exactly one place this logic lives.
/// </summary>
public static class AppSettingsApplier
{
    private static string? s_lastAppliedUiLocale;

    /// <summary>
    /// Sets thread culture (all four of <see cref="CultureInfo.CurrentCulture"/>/<see cref="CultureInfo.CurrentUICulture"/>/
    /// <see cref="CultureInfo.DefaultThreadCurrentCulture"/>/<see cref="CultureInfo.DefaultThreadCurrentUICulture"/>) to
    /// <paramref name="uiLocale"/> — required by the generated <c>AppStrings</c> class's
    /// <c>GenerateCode</c> mode, which reads <see cref="CultureInfo.CurrentUICulture"/> to pick
    /// which baked-in language table to use (Aigamo.ResXGenerator satellite-assembly resource
    /// loading was unreliable in the browser WASM host — this codegen mode sidesteps it entirely).
    /// Synchronous and side-effect-free otherwise, so it can run before the host is even built —
    /// see the per-host <c>Program.cs</c>/<c>MauiProgram.cs</c> startup-culture calls, which must
    /// run before the very first render or the main view paints once in English regardless.
    /// </summary>
    public static void ApplyCulture(string uiLocale)
    {
        s_lastAppliedUiLocale = uiLocale;
        ReassertCulture();
    }

    /// <summary>
    /// Re-applies <see cref="ApplyCulture"/>'s last-set locale to whichever thread calls this. WASM
    /// is single-threaded, so this is a no-op-equivalent there, but MAUI's <c>BlazorWebView</c> can
    /// dispatch different render passes onto different underlying OS threads — and
    /// <see cref="CultureInfo.CurrentCulture"/>/<see cref="CultureInfo.CurrentUICulture"/> are
    /// genuinely per-thread, not per-SynchronizationContext, so a change made on the thread that
    /// handled an Options "Apply" click doesn't reliably reach whichever thread later renders the
    /// page (this is what broke live language switching on Windows/Android — see
    /// masterwork-plan-rev19.md).
    ///
    /// Called via <c>@{ ReassertCulture(); }</c> at the top of every page and every independently-
    /// re-rendering chrome component (<c>OptionsDialog</c>, <c>PauseBar</c>) —
    /// <strong>not</strong> just <see cref="Layout.MainLayout"/>. A Razor
    /// component's <c>BuildRenderTree</c> only re-runs when that specific component instance
    /// re-renders; a layout does <em>not</em> get pulled back into the render batch just because a
    /// page nested inside its <c>@Body</c> re-renders from its own internal state change (e.g.
    /// toggling a dialog open) — only page *navigation* does that, by giving the layout a
    /// genuinely new <c>Body</c> value. So a call placed only in <c>MainLayout</c> only fires on
    /// the first paint and on navigation, never on a page's own internal re-renders — which is
    /// exactly where the inconsistent-language symptom showed up (opening a dialog without
    /// navigating away first).
    /// </summary>
    public static void ReassertCulture()
    {
        if (s_lastAppliedUiLocale is null)
        {
            return;
        }

        var culture = CultureInfo.GetCultureInfo(s_lastAppliedUiLocale);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static async Task ApplyAsync(AppSettings settings, IAudioPlayer audioPlayer, IJSRuntime js)
    {
        await audioPlayer.SetBgmVolumeAsync(settings.BgmVolume);
        await audioPlayer.SetBgmMutedAsync(settings.BgmMuted);
        await audioPlayer.SetSfxVolumeAsync(settings.SfxVolume);
        await audioPlayer.SetSfxMutedAsync(settings.SfxMuted);
        ApplyCulture(settings.UiLocale);

        var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/appSettings.js");
        await module.InvokeVoidAsync("applyTextScale", settings.TextSizeStep);
    }
}
