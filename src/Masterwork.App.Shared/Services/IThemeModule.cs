using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// A white-label flavor's runtime half: its <see cref="WhiteLabelConfig"/> and the services only it
/// knows how to supply (<see cref="IMainMenuTheme"/> and the like). Implemented once per theme
/// project, with a public parameterless constructor.
/// </summary>
/// <remarks>
/// Hosts never name a concrete theme. The flavor's MSBuild props stamp the host assembly with a
/// <see cref="ThemeModuleAttribute"/> and <see cref="ThemeModules.AddThemeModule"/> reads it back at
/// startup, so the same host source builds any flavor.
///
/// Theme assets are served from <see cref="ThemeModules.AssetBasePath"/> whichever flavor is built,
/// so markup and URLs that point into the theme never carry a flavor name.
/// </remarks>
public interface IThemeModule
{
    /// <summary>This flavor's identity, content source, and pinned publisher.</summary>
    WhiteLabelConfig Config { get; }

    /// <summary>Registers this flavor's theme services. <see cref="Config"/> is already registered.</summary>
    void ConfigureServices(IServiceCollection services);
}

/// <summary>Names the <see cref="IThemeModule"/> a host assembly was built with.</summary>
/// <remarks>
/// Written by the flavor's MSBuild props as a literal <c>typeof(...)</c>, never by hand. Holding the
/// type rather than its name keeps the theme assembly referenced (the compiler would otherwise drop
/// a reference no code uses, leaving nothing to discover) and tells the trimmer to keep the
/// constructor that's only ever called through reflection.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ThemeModuleAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type moduleType) : Attribute
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ModuleType { get; } = moduleType;
}

/// <summary>Finds and registers the flavor a host was built with.</summary>
public static class ThemeModules
{
    /// <summary>
    /// Where every flavor's theme static assets are served, relative to the app base. Set by each
    /// theme project's <c>StaticWebAssetBasePath</c>. Every theme provides <c>theme.css</c>, a
    /// square 512px <c>logo.png</c> and a 192px <c>logo-192.png</c>; anything else (<c>audio/…</c>)
    /// is up to the theme.
    /// </summary>
    public const string AssetBasePath = "_content/theme";

    /// <summary>Instantiates the one theme module <paramref name="hostAssembly"/> was built with.</summary>
    /// <exception cref="InvalidOperationException">The assembly names no theme module, more than one, or one that isn't an <see cref="IThemeModule"/>.</exception>
    public static IThemeModule Load(Assembly hostAssembly) =>
        Load(hostAssembly.GetCustomAttributes<ThemeModuleAttribute>().ToList(), hostAssembly.GetName().Name);

    /// <inheritdoc cref="Load(Assembly)"/>
    public static IThemeModule Load(IReadOnlyList<ThemeModuleAttribute> attributes, string? hostName)
    {
        if (attributes.Count != 1)
        {
            throw new InvalidOperationException(attributes.Count == 0
                ? $"{hostName} was built without a theme. Its project must import the white-label props, which select a flavor via WhiteLabelFlavor."
                : $"{hostName} was built with {attributes.Count} themes; exactly one is allowed.");
        }

        var type = attributes[0].ModuleType;
        if (!typeof(IThemeModule).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"{type.FullName} is named as {hostName}'s theme but does not implement {nameof(IThemeModule)}.");
        }

        return (IThemeModule)Activator.CreateInstance(type)!;
    }

    /// <summary>
    /// Loads <paramref name="hostAssembly"/>'s theme module, registers its <see cref="WhiteLabelConfig"/>
    /// as a singleton, and lets it register its own services.
    /// </summary>
    public static IThemeModule AddThemeModule(this IServiceCollection services, Assembly hostAssembly)
    {
        var module = Load(hostAssembly);
        services.AddSingleton(module.Config);
        module.ConfigureServices(services);
        return module;
    }
}
