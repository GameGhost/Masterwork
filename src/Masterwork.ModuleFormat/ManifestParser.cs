using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <inheritdoc cref="IManifestParser"/>
public sealed class ManifestParser : IManifestParser
{
    private readonly ILogger<ManifestParser> _logger;

    /// <summary>Creates a parser that discards log output.</summary>
    public ManifestParser() : this(NullLogger<ManifestParser>.Instance)
    {
    }

    /// <summary>Creates a parser that logs through <paramref name="logger"/>.</summary>
    public ManifestParser(ILogger<ManifestParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public ModuleManifest Parse(string yamlText, ModuleWarnings? warnings = null, string? preferredLocale = null)
    {
        var ctx = new YamlParseContext(warnings, "manifest.yaml", _logger);

        var stream = new YamlStream();
        stream.Load(new StringReader(yamlText));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;

        var dependencies = new List<ModuleDependency>();
        if (root.GetSequence("dependencies", ctx) is { } seq)
        {
            foreach (var child in seq.Children)
            {
                if (child is not YamlMappingNode depMap)
                {
                    ctx.Warn("wrong_field_type", $"dependencies entry: expected a mapping but found a {YamlNodeExtensions.DescribeKind(child)}; skipping it");
                    continue;
                }

                dependencies.Add(new ModuleDependency
                {
                    Id = depMap.GetRequiredString("id", ctx),
                    Version = depMap.GetString("version", ctx),
                });
                depMap.WarnUnmatchedFields(ctx, "dependencies entry", "id", "version");
            }
        }

        ModuleThumbnail? thumbnail = null;
        if (root.GetMapping("thumbnail", ctx) is { } thumbMap)
        {
            thumbnail = new ModuleThumbnail
            {
                Image = thumbMap.GetString("image", ctx),
                BorderInactive = thumbMap.GetString("border-inactive", ctx),
                BorderActive = thumbMap.GetString("border-active", ctx),
            };
            thumbMap.WarnUnmatchedFields(ctx, "thumbnail", "image", "border-inactive", "border-active");
        }

        ModuleInfo? info = null;
        if (root.GetMapping("info", ctx) is { } infoMap)
        {
            info = new ModuleInfo
            {
                PlayersMin = infoMap.GetInt("players-min", ctx),
                PlayersMax = infoMap.GetInt("players-max", ctx),
                Playtime = GetOptionalLocalizedString(infoMap, "playtime", ctx, preferredLocale),
            };
            infoMap.WarnUnmatchedFields(ctx, "info", "players-min", "players-max", "playtime");
        }

        ModuleAudioManifest? audio = null;
        if (root.GetMapping("audio", ctx) is { } audioMap)
        {
            ModuleMusicManifest? music = null;
            if (audioMap.GetMapping("music", ctx) is { } musicMap)
            {
                var order = musicMap.GetString("order", ctx) ?? "sequence";
                if (order is not ("sequence" or "shuffle"))
                {
                    ctx.Warn("invalid_enum_value", $"audio.music.order has unrecognized value '{order}'; falling back to 'sequence'");
                    order = "sequence";
                }

                music = new ModuleMusicManifest
                {
                    DefaultTracks = musicMap.GetStringList("default_tracks", ctx),
                    Order = order,
                };
                musicMap.WarnUnmatchedFields(ctx, "audio.music", "default_tracks", "order");
            }

            ModuleSfxManifest? sfx = null;
            if (audioMap.GetMapping("sfx", ctx) is { } sfxMap)
            {
                sfx = new ModuleSfxManifest
                {
                    Transition = sfxMap.GetStringList("transition", ctx),
                    PopupOpen = sfxMap.GetStringList("popup_open", ctx),
                    PopupClose = sfxMap.GetStringList("popup_close", ctx),
                    Click = sfxMap.GetStringList("click", ctx),
                };
                sfxMap.WarnUnmatchedFields(ctx, "audio.sfx", "transition", "popup_open", "popup_close", "click");
            }

            audio = new ModuleAudioManifest { Music = music, Sfx = sfx };
            audioMap.WarnUnmatchedFields(ctx, "audio", "music", "sfx");
        }

        var manifest = new ModuleManifest
        {
            Id = root.GetRequiredString("id", ctx),
            Title = GetRequiredLocalizedString(root, "title", ctx, preferredLocale),
            Version = root.GetRequiredString("version", ctx),
            ModuleType = root.GetString("type", ctx) ?? "original_scenario",
            Description = GetOptionalLocalizedString(root, "description", ctx, preferredLocale),
            Dependencies = dependencies,
            Languages = root.GetStringList("languages", ctx),
            Thumbnail = thumbnail,
            Info = info,
            Audio = audio,
            Entry = root.GetString("entry", ctx),
            PassagesPath = root.GetString("passages", ctx) ?? "passages",
            PassagesOverridePath = root.GetString("passages_override", ctx) ?? "passages-override",
            StylePath = root.GetString("style", ctx) ?? "assets/style.css",
        };

        root.WarnUnmatchedFields(ctx, "manifest.yaml",
            "id", "title", "version", "type", "description", "dependencies",
            "languages", "thumbnail", "info", "audio", "entry", "passages", "passages_override", "style");

        _logger.LogDebug("Parsed manifest '{Id}' v{Version} ({DependencyCount} dependencies)", manifest.Id, manifest.Version, dependencies.Count);
        return manifest;
    }

    // `title`/`description`/`info.playtime` may be a plain scalar (simple single-language modules)
    // or a localized list (`- en-US: 'The Cost of Disease'`, one or more locale:value entries per
    // list item). Resolves to preferredLocale, falling back to ModuleLocales.Default, then to
    // whichever locale is actually present.
    private static string? ResolveLocalizedOrPlainString(YamlNode node, string key, YamlParseContext ctx, string? preferredLocale)
    {
        if (node is YamlScalarNode s)
        {
            return s.Value;
        }

        if (node is not YamlSequenceNode seq)
        {
            return null; // caller decides how to report the wrong shape
        }

        var localized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in seq.Children)
        {
            if (child is not YamlMappingNode entry)
            {
                ctx.Warn("wrong_field_type", $"field '{key}' entry: expected a mapping but found a {YamlNodeExtensions.DescribeKind(child)}; skipping it");
                continue;
            }

            foreach (var (k, v) in entry.Children)
            {
                if (k is YamlScalarNode { Value: { } locale } && v is YamlScalarNode { Value: { } text })
                {
                    localized[locale] = text;
                }
            }
        }

        if (localized.Count == 0)
        {
            return null;
        }

        if (preferredLocale is not null && localized.TryGetValue(preferredLocale, out var preferred))
        {
            return preferred;
        }

        return localized.TryGetValue(ModuleLocales.Default, out var fallback) ? fallback : localized.Values.First();
    }

    private static string GetRequiredLocalizedString(YamlMappingNode map, string key, YamlParseContext ctx, string? preferredLocale)
    {
        var node = map.TryGet(key);
        if (node is null)
        {
            throw new MwsParseException($"{ctx.Source}: missing required field '{key}'");
        }

        var value = ResolveLocalizedOrPlainString(node, key, ctx, preferredLocale);
        if (value is null)
        {
            throw new MwsParseException($"{ctx.Source}: field '{key}' must be a text value or a localized list but found a {YamlNodeExtensions.DescribeKind(node)}");
        }

        return value;
    }

    private static string? GetOptionalLocalizedString(YamlMappingNode map, string key, YamlParseContext ctx, string? preferredLocale)
    {
        var node = map.TryGet(key);
        if (node is null)
        {
            return null;
        }

        var value = ResolveLocalizedOrPlainString(node, key, ctx, preferredLocale);
        if (value is null)
        {
            ctx.Warn("wrong_field_type", $"field '{key}' expected a text value or a localized list but found a {YamlNodeExtensions.DescribeKind(node)}; ignoring it");
        }

        return value;
    }
}
