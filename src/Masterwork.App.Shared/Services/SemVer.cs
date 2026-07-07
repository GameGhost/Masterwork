namespace Masterwork.App.Shared.Services;

/// <summary>How an uploaded module's version compares to whatever's already installed under the same id.</summary>
public enum ModuleVersionComparison
{
    /// <summary>Same version string, and the package bytes are identical — nothing would actually change.</summary>
    SameContent,

    /// <summary>Same version string, but the package bytes differ — likely a rebuild without a version bump.</summary>
    SameVersionDifferentContent,

    /// <summary>The uploaded version is newer.</summary>
    Upgrade,

    /// <summary>The uploaded version is older.</summary>
    Downgrade,
}

/// <summary>
/// A small, lenient semver-ish comparer for module version strings — good enough to order
/// "1.2.3"-shaped strings without pulling in a full semver package. Non-numeric or missing
/// components compare as 0, so odd version strings degrade to string equality rather than throwing.
/// </summary>
public static class SemVer
{
    /// <summary>-1 if <paramref name="a"/> is older than <paramref name="b"/>, 0 if equal, 1 if newer.</summary>
    public static int Compare(string a, string b)
    {
        var partsA = ParseParts(a);
        var partsB = ParseParts(b);

        for (var i = 0; i < Math.Max(partsA.Length, partsB.Length); i++)
        {
            var pa = i < partsA.Length ? partsA[i] : 0;
            var pb = i < partsB.Length ? partsB[i] : 0;
            var cmp = pa.CompareTo(pb);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return 0;
    }

    private static long[] ParseParts(string version) =>
        [.. version.Split('.', '-', '+').Select(p => long.TryParse(p, out var n) ? n : 0)];
}
