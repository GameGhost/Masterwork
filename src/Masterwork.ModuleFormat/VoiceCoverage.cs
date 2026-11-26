using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <summary>A passage (or layout) with at least one <c>audio_track</c> playing <c>audio://vo/</c> narration.</summary>
/// <param name="Where">The source file and passage id, for messages.</param>
/// <param name="Slugs">The narration slugs it references, in order, without duplicates.</param>
public sealed record NarratedPassage(string Where, IReadOnlyList<string> Slugs);

/// <summary>A file under a package's <c>assets/audio/vo/</c>.</summary>
/// <param name="RelativePath">The path within the package, with forward slashes.</param>
/// <param name="Language">The voice language folder, or <see langword="null"/> when the file isn't in a track folder.</param>
/// <param name="TrackId">The track folder, or <see langword="null"/> when the file isn't in a track folder.</param>
/// <param name="Slug">The narration slug the file answers (<c>audio://vo/&lt;slug&gt;</c>).</param>
public sealed record VoiceFile(string RelativePath, string? Language, string? TrackId, string Slug);

/// <summary>How many of a module's narrated passages one voice track has every file for.</summary>
public sealed record TrackCoverage(VoiceTrack Track, int Covered, int Total, IReadOnlyList<string> Uncovered)
{
    /// <summary>For example "28 of 31 narrated passages".</summary>
    public string Summary => $"{Covered} of {Total} narrated passages";
}

/// <summary>
/// Pack-time checks of narration files against the <c>audio_track</c> nodes that play them: files
/// nothing plays, and how much of a module each voice track covers. Coverage is reported, never
/// stored: a gap just hides that <c>audio_track</c> at play time.
/// </summary>
public static partial class VoiceCoverage
{

    [GeneratedRegex(@"audio://vo/([A-Za-z0-9_\-./]+)")]
    private static partial Regex NarrationReference();

    /// <summary>Every passage and layout in <paramref name="moduleDirectory"/> that plays narration.</summary>
    public static IReadOnlyList<NarratedPassage> CollectNarration(string moduleDirectory)
    {
        var narrated = new List<NarratedPassage>();
        foreach (var doc in ModuleSourceDocuments.Enumerate(moduleDirectory))
        {
            var slugs = new List<string>();
            Walk(doc.Root, slugs);
            if (slugs.Count > 0)
            {
                narrated.Add(new NarratedPassage(doc.Where, slugs));
            }
        }

        return narrated;
    }

    /// <summary>Every file under <paramref name="packageDirectory"/>'s <c>assets/audio/vo/</c>.</summary>
    public static IReadOnlyList<VoiceFile> ListVoiceFiles(string packageDirectory)
    {
        var voDirectory = Path.Combine(packageDirectory, VoiceTrack.NarrationRoot);
        if (!Directory.Exists(voDirectory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(voDirectory, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(packageDirectory, f).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .Select(ToVoiceFile),
        ];
    }

    /// <summary>
    /// One message per narration file nothing can play: outside a track folder, in a folder for a
    /// track <paramref name="declaredTracks"/> doesn't list, or for a slug no <c>audio_track</c> references.
    /// The last check is skipped when <paramref name="narration"/> is <see langword="null"/> (the module isn't known).
    /// </summary>
    public static IReadOnlyList<string> Warnings(
        IReadOnlyList<VoiceTrack> declaredTracks, IReadOnlyList<VoiceFile> files, IReadOnlyList<NarratedPassage>? narration)
    {
        var referenced = narration?.SelectMany(p => p.Slugs).ToHashSet(StringComparer.Ordinal);
        var warnings = new List<string>();
        foreach (var file in files)
        {
            if (file.Language is null || file.TrackId is null)
            {
                warnings.Add($"{file.RelativePath} is not in a voice track folder ({VoiceTrack.NarrationRoot}<language>/<track>/), so nothing plays it.");
            }
            else if (!declaredTracks.Any(t => t.Language == file.Language && t.Id == file.TrackId))
            {
                warnings.Add($"{file.RelativePath} is in the folder of track {file.Language}/{file.TrackId}, which voices: doesn't declare.");
            }
            else if (referenced is not null && !referenced.Contains(file.Slug))
            {
                warnings.Add($"{file.RelativePath}: no audio_track references audio://vo/{file.Slug}.");
            }
        }

        return warnings;
    }

    /// <summary>Each of <paramref name="tracks"/>' coverage of <paramref name="narration"/>: a passage counts when the track has a file for every slug it plays.</summary>
    public static IReadOnlyList<TrackCoverage> Coverage(
        IReadOnlyList<VoiceTrack> tracks, IReadOnlyList<VoiceFile> files, IReadOnlyList<NarratedPassage> narration)
    {
        return
        [
            .. tracks.Select(track =>
            {
                var present = files.Where(f => f.Language == track.Language && f.TrackId == track.Id)
                    .Select(f => f.Slug).ToHashSet(StringComparer.Ordinal);
                var uncovered = narration.Where(p => !p.Slugs.All(present.Contains)).Select(p => p.Where).ToList();
                return new TrackCoverage(track, narration.Count - uncovered.Count, narration.Count, uncovered);
            }),
        ];
    }

    private static VoiceFile ToVoiceFile(string relativePath)
    {
        var withinVo = relativePath[VoiceTrack.NarrationRoot.Length..];
        var withoutExtension = Path.ChangeExtension(withinVo, null).Replace('\\', '/');
        var parts = withoutExtension.Split('/', 3);
        return parts.Length == 3
            ? new VoiceFile(relativePath, parts[0], parts[1], parts[2])
            : new VoiceFile(relativePath, null, null, withoutExtension);
    }

    private static void Walk(YamlNode node, List<string> slugs)
    {
        switch (node)
        {
            case YamlMappingNode map:
                if (map.TryGet("type") is YamlScalarNode { Value: "audio_track" } && map.TryGet("asset") is YamlScalarNode { Value: { } asset })
                {
                    foreach (Match match in NarrationReference().Matches(asset))
                    {
                        if (!slugs.Contains(match.Groups[1].Value))
                        {
                            slugs.Add(match.Groups[1].Value);
                        }
                    }
                }

                foreach (var value in map.Children.Values)
                {
                    Walk(value, slugs);
                }

                break;
            case YamlSequenceNode seq:
                foreach (var child in seq.Children)
                {
                    Walk(child, slugs);
                }

                break;
        }
    }
}
