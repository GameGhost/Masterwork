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
    public ModuleManifest Parse(string yamlText, ModuleWarnings? warnings = null)
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

        var manifest = new ModuleManifest
        {
            Id = root.GetRequiredString("id", ctx),
            Title = root.GetRequiredString("title", ctx),
            Version = root.GetRequiredString("version", ctx),
            ModuleType = root.GetString("type", ctx) ?? "original_scenario",
            Description = root.GetString("description", ctx),
            Dependencies = dependencies,
        };

        root.WarnUnmatchedFields(ctx, "manifest.yaml", "id", "title", "version", "type", "description", "dependencies");

        _logger.LogDebug("Parsed manifest '{Id}' v{Version} ({DependencyCount} dependencies)", manifest.Id, manifest.Version, dependencies.Count);
        return manifest;
    }
}
