using Masterwork.App.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Masterwork.App.Theme.Test;

/// <summary>The bare test flavor.</summary>
public sealed class TestTheme : IThemeModule
{
    public WhiteLabelConfig Config { get; } = new()
    {
        // Must match ApplicationTitle in WhiteLabel/Test.props.
        AppName = "Test Flavor",
        ShortName = "Test Flavor",
        GameName = "Test Game",
        ThemeColor = "#202020",
        // .invalid never resolves, so a test-flavor build can't reach real content by accident.
        CatalogUrl = "https://content.example.invalid/catalog.json",
        PackageBaseUrl = "https://content.example.invalid/packages",
        PublisherThumbprint = null,
    };

    public void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IMainMenuTheme>(new MainMenuTheme(typeof(TestScene)));
}
