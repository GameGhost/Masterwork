using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Pushes an <see cref="AppSettings"/> value onto the running app — <see cref="IAudioPlayer"/> for
/// volume/mute, and the <c>--mws-text-scale</c> CSS variable (via <c>wwwroot/appSettings.js</c>) for
/// text size. Shared by <see cref="Masterwork.App.Shared.Layout.MainLayout"/> (applies the
/// last-saved settings once at startup) and <see cref="Masterwork.App.Shared.Chrome.OptionsDialog"/>
/// (applies a freshly-edited draft on Apply) so there's exactly one place this logic lives.
/// </summary>
public static class AppSettingsApplier
{
    public static async Task ApplyAsync(AppSettings settings, IAudioPlayer audioPlayer, IJSRuntime js)
    {
        await audioPlayer.SetBgmVolumeAsync(settings.BgmVolume);
        await audioPlayer.SetBgmMutedAsync(settings.BgmMuted);
        await audioPlayer.SetSfxVolumeAsync(settings.SfxVolume);
        await audioPlayer.SetSfxMutedAsync(settings.SfxMuted);

        var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/appSettings.js");
        await module.InvokeVoidAsync("applyTextScale", settings.TextSizeStep);
    }
}
