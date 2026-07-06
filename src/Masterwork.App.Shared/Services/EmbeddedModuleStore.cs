using Masterwork.App.Shared.SampleData;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IModuleStore"/>
public sealed class EmbeddedModuleStore(IModuleLoader loader) : IModuleStore
{
    /// <summary>The demo module's stable id — used as a <see cref="SaveEntry.ModuleId"/> and for <see cref="SaveIds.Autosave"/>.</summary>
    public const string DemoModuleId = "masterwork.demo";

    /// <summary>Bumped whenever <see cref="SampleModule"/>'s content changes in a way that could break an existing save.</summary>
    public const string DemoModuleVersion = "1.0.0";

    private static readonly InstalledModule DemoModule = new(
        ModuleId: DemoModuleId,
        Version: DemoModuleVersion,
        Title: "Masterwork Demo",
        Description: "A small hand-authored scenario showing off the engine's node types — an evolving hub, " +
                     "random/shuffled events, module variables, and an ending. Ships with the app and can't be removed.",
        IsBuiltIn: true);

    /// <inheritdoc/>
    public Task<IReadOnlyList<InstalledModule>> ListAsync() =>
        Task.FromResult<IReadOnlyList<InstalledModule>>([DemoModule]);

    /// <inheritdoc/>
    public Task<LoadedModule> LoadAsync(string moduleId)
    {
        if (moduleId != DemoModuleId)
        {
            throw new InvalidOperationException($"Unknown module id '{moduleId}' — only the built-in demo module is available until Milestone B's upload/download pipeline exists.");
        }

        return Task.FromResult(loader.LoadFromSources(SampleModule.PassageYamls, SampleModule.VariablesYaml));
    }
}
