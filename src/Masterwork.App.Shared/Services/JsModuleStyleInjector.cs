using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IModuleStyleInjector"/>
public sealed class JsModuleStyleInjector(IJSRuntime js) : IModuleStyleInjector
{
    private IJSObjectReference? _module;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/moduleStyle.js");

    /// <inheritdoc/>
    public async Task ApplyAsync(string? css)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("setModuleStyle", css);
    }
}
