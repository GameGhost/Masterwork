using Masterwork.App.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Masterwork.App.Theme.MyFathersWork;

/// <summary>The "My Father's Work" flavor.</summary>
public sealed class MyFathersWorkTheme : IThemeModule
{
    public WhiteLabelConfig Config { get; } = new()
    {
        // Must match ApplicationTitle in WhiteLabel/MyFathersWork.props.
        AppName = "My Father's Work",
        ShortName = "My Father's Work",
        GameName = "My Father's Work",
        ThemeColor = "#0d1420",
        CatalogUrl = "https://raw.githubusercontent.com/GameGhost/Masterwork-Modules/main/.catalog/catalog.json",
        // Entry paths start with the release tag (v0.4.0/cost-of-disease.mwm), so one catalog can
        // point at packages from several releases — an unchanged module keeps its older tag.
        PackageBaseUrl = "https://github.com/GameGhost/Masterwork-Modules/releases/download",
        // The Masterwork content-signing certificate that Masterwork-Modules signs its releases with.
        PublisherThumbprint = "15B0433E3B600F11CB5A8F3B6222C9F907CF140F7024839A3703A783C3327C59",
    };

    public void ConfigureServices(IServiceCollection services)
    {
        const string audio = ThemeModules.AssetBasePath + "/audio";
        services.AddSingleton<IMainMenuTheme>(new MainMenuTheme(
            typeof(MainMenuScene),
            musicUrl: $"{audio}/main-menu-theme.ogg",
            transitionSfxUrl: $"{audio}/shell-transition.ogg",
            clickSfxUrl: $"{audio}/click.ogg"));
    }
}
