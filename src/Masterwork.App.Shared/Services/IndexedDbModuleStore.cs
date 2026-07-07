using Masterwork.ModuleFormat;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IModuleStore"/>
/// <remarks>Uploaded packages are backed by IndexedDB (<c>wwwroot/moduleStore.js</c>) rather than <c>localStorage</c>, since real <c>.mwm</c> files with real assets are multi-megabyte. Registered for the web heads only.</remarks>
public sealed class IndexedDbModuleStore(IJSRuntime js, IModuleLoader loader) : IModuleStore
{
    private sealed record UploadedModuleMeta(string Id, string Title, string Version, string? Description);

    private async Task<IJSObjectReference> ModuleAsync() =>
        await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/moduleStore.js");

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstalledModule>> ListAsync()
    {
        var module = await ModuleAsync();
        var uploaded = await module.InvokeAsync<UploadedModuleMeta[]>("listModules");

        var result = new List<InstalledModule> { BuiltInModules.Demo };
        result.AddRange(uploaded.Select(m => new InstalledModule(m.Id, m.Version, m.Title, m.Description ?? "", IsBuiltIn: false)));
        return result;
    }

    /// <inheritdoc/>
    public async Task<LoadedModule> LoadAsync(string moduleId)
    {
        if (moduleId == BuiltInModules.DemoModuleId)
        {
            return BuiltInModules.LoadDemo(loader);
        }

        var module = await ModuleAsync();
        var bytes = await module.InvokeAsync<byte[]?>("getModuleBytes", moduleId)
            ?? throw new InvalidOperationException($"Module '{moduleId}' is not installed.");

        var contents = ModulePackage.ReadFromBytes(bytes);
        return loader.LoadFromSources(contents.PassageYamls, contents.VariablesYaml, contents.RestextText);
    }

    /// <inheritdoc/>
    public async Task<InstalledModule> InstallAsync(byte[] mwmBytes)
    {
        var contents = ModulePackage.ReadFromBytes(mwmBytes);
        if (contents.ManifestYaml is null)
        {
            throw new InvalidOperationException("This .mwm file has no manifest.yaml and can't be installed.");
        }

        var manifest = new ManifestParser().Parse(contents.ManifestYaml);
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("putModule", manifest.Id, manifest.Title, manifest.Version, manifest.Description, mwmBytes);

        return new InstalledModule(manifest.Id, manifest.Version, manifest.Title, manifest.Description ?? "", IsBuiltIn: false);
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetPackageBytesAsync(string moduleId)
    {
        if (moduleId == BuiltInModules.DemoModuleId)
        {
            return null;
        }

        var module = await ModuleAsync();
        return await module.InvokeAsync<byte[]?>("getModuleBytes", moduleId);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string moduleId)
    {
        if (moduleId == BuiltInModules.DemoModuleId)
        {
            throw new InvalidOperationException("The built-in demo module can't be removed.");
        }

        var module = await ModuleAsync();
        await module.InvokeVoidAsync("deleteModule", moduleId);
    }
}
