using Masterwork.App.Shared.SampleData;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// The app's permanently-embedded modules — currently just the demo module, ships with the app
/// binary rather than living in an <see cref="IModuleStore"/> implementation's own uploaded-module
/// storage, and can't be deleted via Manage Modules. Shared by every per-host <see cref="IModuleStore"/>
/// implementation so each one only has to add its own uploaded-module storage on top of this.
/// </summary>
public static class BuiltInModules
{
    /// <summary>The demo module's stable id — used as a <see cref="SaveEntry.ModuleId"/> and for <see cref="SaveIds.Autosave"/>.</summary>
    public const string DemoModuleId = "masterwork.demo";

    /// <summary>Bumped whenever <see cref="SampleModule"/>'s content changes in a way that could break an existing save.</summary>
    public const string DemoModuleVersion = "1.0.0";

    /// <summary>The demo module's <see cref="InstalledModule"/> entry. Its passage text is inline, not <c>restext://</c>-referenced, so it's implicitly single-language.</summary>
    public static readonly InstalledModule Demo = new(
        ModuleId: DemoModuleId,
        Version: DemoModuleVersion,
        Title: "Masterwork Demo",
        Description: "A small hand-authored scenario showing off the engine's node types — an evolving hub, " +
                     "random/shuffled events, module variables, and an ending. Ships with the app and can't be removed.",
        IsBuiltIn: true,
        AvailableLanguages: [ModuleLocales.Default]);

    /// <summary>Loads the demo module's full content. Single-language, so there's no <paramref name="locale"/> to select between — the parameter exists only to keep this call-compatible with <see cref="IModuleStore.LoadAsync"/>. Has no assets or stylesheet of its own.</summary>
    public static LoadedModuleContent LoadDemo(IModuleLoader loader, string? locale = null) =>
        new(loader.LoadFromSources(SampleModule.PassageYamls, SampleModule.VariablesYaml), new Dictionary<string, byte[]>(), null);
}
