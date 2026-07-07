using NuGet.Versioning;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Module version comparison, backed by <see cref="NuGetVersion"/> (the NuGet.Versioning package)
/// rather than hand-rolled parsing — module version strings are exactly the kind of "mostly semver"
/// strings that package deliberately handles leniently (missing components, non-numeric
/// pre-release/build metadata, etc.).
/// </summary>
public static class SemVer
{
    /// <summary>-1 if <paramref name="a"/> is older than <paramref name="b"/>, 0 if equal, 1 if newer.</summary>
    public static int Compare(string a, string b) => Parse(a).CompareTo(Parse(b));

    /// <summary>True if the major or minor component differs between the two versions — a patch-only difference isn't considered "significant" on its own.</summary>
    public static bool MajorOrMinorDiffers(string a, string b)
    {
        var va = Parse(a);
        var vb = Parse(b);
        return va.Major != vb.Major || va.Minor != vb.Minor;
    }

    // Falls back to 0.0.0 for anything NuGetVersion can't parse, rather than throwing — a module
    // author's malformed version string shouldn't crash the upload flow, just compare as unknown.
    private static NuGetVersion Parse(string version) =>
        NuGetVersion.TryParse(version, out var parsed) ? parsed : new NuGetVersion(0, 0, 0);
}
