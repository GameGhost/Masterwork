using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IAudioPlayer"/>
public sealed class JsAudioPlayer(IJSRuntime js) : IAudioPlayer, IAsyncDisposable
{
    private IJSObjectReference? _module;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/audio.js");

    /// <inheritdoc/>
    public async Task PlayBgmAsync(string? url)
    {
        if (url is null)
        {
            return;
        }

        var module = await ModuleAsync();
        await module.InvokeVoidAsync("playBgm", url);
    }

    /// <inheritdoc/>
    public async Task StopBgmAsync()
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("stopBgm");
    }

    /// <inheritdoc/>
    public async Task PlaySfxAsync(string? url)
    {
        if (url is null)
        {
            return;
        }

        var module = await ModuleAsync();
        await module.InvokeVoidAsync("playSfx", url);
    }

    /// <inheritdoc/>
    public async Task SetAmbientOnlyAsync(bool ambientOnly)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("setAmbientOnly", ambientOnly);
    }

    /// <inheritdoc/>
    public async Task SetBgmVolumeAsync(double volume)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("setBgmVolume", volume);
    }

    /// <inheritdoc/>
    public async Task SetBgmMutedAsync(bool muted)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("setBgmMuted", muted);
    }

    /// <inheritdoc/>
    public async Task SetSfxVolumeAsync(double volume)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("setSfxVolume", volume);
    }

    /// <inheritdoc/>
    public async Task SetSfxMutedAsync(bool muted)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("setSfxMuted", muted);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}
