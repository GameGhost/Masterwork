using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.ModuleFormat;

/// <inheritdoc cref="IModuleLoader"/>
public sealed class ModuleLoader : IModuleLoader
{
    private readonly IPassageYamlParser _passageParser;
    private readonly IVariableManifest _variableManifest;
    private readonly IRestextFile _restextFile;
    private readonly IRestextResolver _restextResolver;
    private readonly ILogger<ModuleLoader> _logger;

    /// <summary>Creates a loader wired to the default parser/resolver implementations, discarding log output.</summary>
    public ModuleLoader() : this(new PassageYamlParser(), new VariableManifest(), new RestextFile(), new RestextResolver(), NullLogger<ModuleLoader>.Instance)
    {
    }

    /// <summary>Creates a loader with explicit dependencies, e.g. for testing with mocks.</summary>
    public ModuleLoader(IPassageYamlParser passageParser, IVariableManifest variableManifest,
        IRestextFile restextFile, IRestextResolver restextResolver, ILogger<ModuleLoader>? logger = null)
    {
        _passageParser = passageParser;
        _variableManifest = variableManifest;
        _restextFile = restextFile;
        _restextResolver = restextResolver;
        _logger = logger ?? NullLogger<ModuleLoader>.Instance;
    }

    /// <inheritdoc/>
    public LoadedModule LoadFromDirectory(string directoryPath)
    {
        _logger.LogDebug("Loading module from directory '{DirectoryPath}'", directoryPath);

        var variablesPath = Path.Combine(directoryPath, "_variables.yaml");
        var variablesYaml = File.Exists(variablesPath) ? File.ReadAllText(variablesPath) : null;

        var restextPath = Path.Combine(directoryPath, "en-US.restext");
        var restextText = File.Exists(restextPath) ? File.ReadAllText(restextPath) : null;

        var passageYamls = Directory.EnumerateFiles(directoryPath, "*.mws.yaml").Select(File.ReadAllText);

        return LoadFromSources(passageYamls, variablesYaml, restextText);
    }

    /// <inheritdoc/>
    public LoadedModule LoadFromSources(
        IEnumerable<string> passageYamls, string? variablesYaml = null, string? restextText = null)
    {
        var warnings = new ModuleWarnings();

        var variables = variablesYaml is null
            ? new Dictionary<string, VarDef>()
            : _variableManifest.Parse(variablesYaml, warnings);

        var locale = restextText is null
            ? new Dictionary<string, string>()
            : _restextFile.Parse(restextText);

        var passages = new Dictionary<string, MwsPassageDoc>();
        foreach (var yaml in passageYamls)
        {
            var raw = _passageParser.ParsePassage(yaml, warnings);
            var resolved = _restextResolver.Resolve(raw, locale, warnings);
            passages[resolved.PassageId] = resolved;
        }

        var startPassageId = passages.Values
            .FirstOrDefault(p => p.Tags.Any(t => string.Equals(t, "Begins-Here", System.StringComparison.OrdinalIgnoreCase)))
            ?.PassageId;

        ValidatePassageReferences(passages, warnings);

        _logger.LogDebug("Loaded module: {PassageCount} passages, start passage {StartPassageId}, {WarningCount} warnings",
            passages.Count, startPassageId ?? "(none)", warnings.Items.Count);

        return new LoadedModule
        {
            Passages = passages,
            Variables = variables,
            Locale = locale,
            Warnings = warnings,
            StartPassageId = startPassageId,
        };
    }

    /// <inheritdoc/>
    public LoadedModule MergeDependency(LoadedModule module, LoadedModule dependency)
    {
        var passages = new Dictionary<string, MwsPassageDoc>(dependency.Passages);
        foreach (var (id, passage) in module.Passages)
        {
            passages[id] = passage; // module's own passages win on collision
        }

        var variables = new Dictionary<string, VarDef>(dependency.Variables);
        foreach (var (name, def) in module.Variables)
        {
            variables[name] = def; // module's own declarations win on collision
        }

        _logger.LogDebug(
            "Merged dependency into module: {DependencyPassageCount} dependency passages, {DependencyVarCount} dependency variables",
            dependency.Passages.Count, dependency.Variables.Count);

        return new LoadedModule
        {
            Passages = passages,
            Variables = variables,
            Locale = module.Locale,
            Warnings = module.Warnings,
            StartPassageId = module.StartPassageId,
        };
    }

    // Verifies that every statically-known navigation/goto/include_passage target resolves to a
    // known passage. Dynamic targets ("${expr}") are skipped — they can't be checked until runtime.
    private void ValidatePassageReferences(IReadOnlyDictionary<string, MwsPassageDoc> passages, ModuleWarnings warnings)
    {
        foreach (var passage in passages.Values)
        {
            CheckNodeListReferences(passage.PassageId, passage.Nodes, passages, warnings);
        }
    }

    private void CheckNodeListReferences(string passageId, IReadOnlyList<Node> nodes,
        IReadOnlyDictionary<string, MwsPassageDoc> passages, ModuleWarnings warnings)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case NavigationNode n:
                    CheckTarget(passageId, n.Target, passages, warnings);
                    CheckNodeListReferences(passageId, n.OnClick, passages, warnings);
                    break;
                case GotoNode g:
                    CheckTarget(passageId, g.Target, passages, warnings);
                    break;
                case IncludePassageNode ip:
                    CheckTarget(passageId, ip.Target, passages, warnings);
                    break;
                case PopupNode p:
                    if (p.OnClose is not null)
                    {
                        CheckTarget(passageId, p.OnClose, passages, warnings);
                    }

                    CheckNodeListReferences(passageId, p.Content, passages, warnings);
                    break;
                case InputNode i:
                    CheckTarget(passageId, i.OnSubmit, passages, warnings);
                    break;
                case SectionNode s:
                    CheckNodeListReferences(passageId, s.Content, passages, warnings);
                    break;
                case ConditionalNode c:
                    foreach (var branch in c.Conditions)
                    {
                        CheckNodeListReferences(passageId, branch.Then, passages, warnings);
                    }

                    if (c.Else is not null)
                    {
                        CheckNodeListReferences(passageId, c.Else, passages, warnings);
                    }

                    break;
                case SwitchNode sw:
                    foreach (var sc in sw.Cases)
                    {
                        CheckNodeListReferences(passageId, sc.Nodes, passages, warnings);
                    }

                    if (sw.Default is not null)
                    {
                        CheckNodeListReferences(passageId, sw.Default, passages, warnings);
                    }

                    break;
                case ForEachNode f:
                    CheckNodeListReferences(passageId, f.Do, passages, warnings);
                    break;
            }
        }
    }

    private void CheckTarget(string fromPassageId, string target,
        IReadOnlyDictionary<string, MwsPassageDoc> passages, ModuleWarnings warnings)
    {
        if (target.StartsWith("${", StringComparison.Ordinal))
        {
            return; // dynamic — can't validate statically
        }

        if (!passages.ContainsKey(target))
        {
            warnings.Add("unresolved_passage_ref", $"{fromPassageId}: target '{target}' does not exist");
            _logger.LogWarning("Unresolved passage reference: {FromPassageId} -> '{Target}'", fromPassageId, target);
        }
    }
}
