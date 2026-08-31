using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Resolves a module's manifest-declared asset-pack dependencies against an
/// <see cref="IAssetPackStore"/> into the pieces <see cref="IModuleLoader.LoadFromSources"/> needs —
/// shared by <c>FileModuleStore</c> and <see cref="IndexedDbModuleStore"/> so this orchestration
/// isn't duplicated per platform.
/// </summary>
public static class ModuleDependencyResolver
{
    /// <param name="DependencyRestexts">Feed directly into <c>LoadFromSources</c>'s parameter of the same name.</param>
    /// <param name="LayoutChromeYamls">Dependency-only layout chrome, in dependency-declaration order — append the module's own layout YAML *after* these before passing the combined list to <c>LoadFromSources</c>, so the module wins on a matching <c>layout_id</c>.</param>
    /// <param name="AdditionalVariableYamls">Dependency-only variable YAML, in dependency-declaration order — same append-after convention, folded into <c>LoadFromSources</c>'s <c>additionalVariableYamls</c> parameter.</param>
    /// <param name="MissingDependencyWarnings">
    /// One entry per declared dependency that couldn't be resolved (not installed, or has no
    /// declared version). Add these to the loaded module's own <see cref="ModuleWarnings"/> after
    /// <c>LoadFromSources</c> returns rather than failing the whole module load.
    /// </param>
    public sealed record Result(
        IReadOnlyList<DependencyRestext> DependencyRestexts,
        IReadOnlyList<string> LayoutChromeYamls,
        IReadOnlyList<string> AdditionalVariableYamls,
        IReadOnlyList<string> MissingDependencyWarnings
    );

    /// <param name="dependencies">A module's manifest-declared <c>dependencies:</c> list, in declaration order.</param>
    /// <param name="assetPackStore">Where to look up each dependency's installed content.</param>
    /// <param name="moduleResolvedLocale">
    /// The exact locale token the dependent module itself resolved to — each dependency's restext is
    /// looked up under this same token. An asset pack shipping more locales than the module exposes
    /// is simply never reached from here.
    /// </param>
    public static async Task<Result> ResolveAsync(
        IReadOnlyList<ModuleDependency> dependencies, IAssetPackStore assetPackStore, string? moduleResolvedLocale)
    {
        var dependencyRestexts = new List<DependencyRestext>();
        var layoutYamls = new List<string>();
        var variableYamls = new List<string>();
        var missingWarnings = new List<string>();

        foreach (var dependency in dependencies)
        {
            if (dependency.Version is null)
            {
                missingWarnings.Add($"Dependency '{dependency.Id}' has no declared version and can't be resolved.");
                continue;
            }

            var content = await assetPackStore.LoadContentAsync(dependency.Id, dependency.Version);
            if (content is null)
            {
                missingWarnings.Add($"Dependency '{dependency.Id}' v{dependency.Version} is not installed — shared content from it won't be available.");
                continue;
            }

            var assetPackDefaultLocale = content.ManifestYaml is null
                ? ModuleLocales.Default
                : new AssetPackManifestParser().Parse(content.ManifestYaml).DefaultLocale;

            var selectedRestext = moduleResolvedLocale is not null ? content.RestextByLocale.GetValueOrDefault(moduleResolvedLocale) : null;
            var defaultRestext = content.RestextByLocale.GetValueOrDefault(assetPackDefaultLocale);
            if (selectedRestext is not null || defaultRestext is not null)
            {
                dependencyRestexts.Add(new DependencyRestext(selectedRestext, defaultRestext));
            }

            layoutYamls.AddRange(content.LayoutYamls);
            if (content.VariablesYaml is not null)
            {
                variableYamls.Add(content.VariablesYaml);
            }
        }

        return new Result(dependencyRestexts, layoutYamls, variableYamls, missingWarnings);
    }
}
