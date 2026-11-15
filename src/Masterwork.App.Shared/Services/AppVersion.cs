using System.Reflection;

namespace Masterwork.App.Shared.Services;

/// <summary>The app's version, as shown to players.</summary>
public static class AppVersion
{
    /// <summary>
    /// The build's version (e.g. <c>0.3.2</c>), without the source-revision suffix the SDK appends to
    /// the informational version.
    /// </summary>
    public static string Display { get; } = Read();

    private static string Read()
    {
        var assembly = typeof(AppVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
        {
            return assembly.GetName().Version?.ToString(3) ?? "";
        }

        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
