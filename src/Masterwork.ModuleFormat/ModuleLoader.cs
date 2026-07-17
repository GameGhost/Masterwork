using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.RepresentationModel;

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

        var (restextText, restextOverrideText) = ReadRestextWithOverride(directoryPath);

        var (passagesDir, overrideDir) = ResolvePassageDirs(directoryPath);

        var passageYamls = Directory.EnumerateFiles(passagesDir, "*.mws.yaml").Select(File.ReadAllText);
        var overridePassageYamls = overrideDir is not null
            ? Directory.EnumerateFiles(overrideDir, "*.mws.yaml").Select(File.ReadAllText)
            : null;

        var layoutsDir = Path.Combine(directoryPath, "layouts");
        var layoutChromeYamls = Directory.Exists(layoutsDir)
            ? Directory.EnumerateFiles(layoutsDir, "*.mws.yaml").Select(File.ReadAllText)
            : null;

        var additionalVariablesDir = Path.Combine(directoryPath, "variables");
        var additionalVariableYamls = Directory.Exists(additionalVariablesDir)
            ? Directory.EnumerateFiles(additionalVariablesDir, "*.yaml").Select(File.ReadAllText)
            : null;

        var module = LoadFromSources(passageYamls, variablesYaml, restextText, overridePassageYamls, restextOverrideText,
            layoutChromeYamls, additionalVariableYamls);

        WarnAboutStrayYamlFiles(passagesDir, module.Warnings);
        if (overrideDir is not null)
        {
            WarnAboutStrayYamlFiles(overrideDir, module.Warnings);
        }
        if (Directory.Exists(layoutsDir))
        {
            WarnAboutStrayYamlFiles(layoutsDir, module.Warnings);
        }

        return module;
    }

    // A ".yaml" file in passages/, passages-override/, or layouts/ that doesn't end in the
    // required ".mws.yaml" suffix is invisible to the "*.mws.yaml" glob used above — the passage
    // or layout it defines silently never loads, and anything targeting its passage_id fails at
    // playtime with no indication why (see Masterwork.Engine.PassageNotFoundException). A missing
    // extension (e.g. "Entry.yaml" instead of "Entry.mws.yaml") is an easy authoring slip, so this
    // surfaces it as a warning here instead of leaving it a silent no-op.
    private static void WarnAboutStrayYamlFiles(string dir, ModuleWarnings warnings)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.yaml"))
        {
            if (!file.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("stray_yaml_file",
                    $"'{Path.GetFileName(file)}' does not end in '.mws.yaml' and was not loaded.");
            }
        }
    }

    // Finds the module's base restext file — preferring en-US.restext, falling back to whichever
    // single {culture}.restext is present — and, alongside it, an optional
    // {culture}.overrides.restext with the same add/override-by-key semantics as passage overrides.
    private static (string? Base, string? Override) ReadRestextWithOverride(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return (null, null);
        }

        var restextFiles = Directory.EnumerateFiles(directoryPath, "*.restext")
            .Where(f => !f.EndsWith(".overrides.restext", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var basePath = restextFiles.FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), "en-US.restext", StringComparison.OrdinalIgnoreCase))
            ?? restextFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

        if (basePath is null)
        {
            return (null, null);
        }

        var culture = Path.GetFileNameWithoutExtension(basePath);
        var overridePath = Path.Combine(directoryPath, $"{culture}.overrides.restext");

        return (File.ReadAllText(basePath), File.Exists(overridePath) ? File.ReadAllText(overridePath) : null);
    }

    /// <inheritdoc/>
    public LoadedModule LoadFromSources(
        IEnumerable<string> passageYamls, string? variablesYaml = null, string? restextText = null,
        IEnumerable<string>? overridePassageYamls = null, string? restextOverrideText = null,
        IEnumerable<string>? layoutChromeYamls = null, IEnumerable<string>? additionalVariableYamls = null)
    {
        var warnings = new ModuleWarnings();

        var variables = variablesYaml is null
            ? new Dictionary<string, VarDef>()
            : new Dictionary<string, VarDef>(_variableManifest.Parse(variablesYaml, warnings));

        // Hand-authored variable files load after the extractor-owned _variables.yaml: a matching
        // name is replaced, a new one is added — same add/override-by-key semantics as passage
        // and layout-chrome overrides.
        if (additionalVariableYamls is not null)
        {
            foreach (var yaml in additionalVariableYamls)
            {
                foreach (var (name, def) in _variableManifest.Parse(yaml, warnings))
                {
                    variables[name] = def;
                }
            }
        }

        IReadOnlyDictionary<string, string> locale = restextText is null
            ? new Dictionary<string, string>()
            : _restextFile.Parse(restextText);

        if (restextOverrideText is not null)
        {
            // Same add/override-by-key semantics as passage overrides: a key present in the
            // override text replaces the base value; a new key is simply added.
            var mergedLocale = new Dictionary<string, string>(locale, StringComparer.Ordinal);
            foreach (var (key, value) in _restextFile.Parse(restextOverrideText))
            {
                mergedLocale[key] = value;
            }

            locale = mergedLocale;
        }

        var passages = new Dictionary<string, MwsPassageDoc>();
        foreach (var yaml in passageYamls)
        {
            var raw = _passageParser.ParsePassage(yaml, warnings);
            var resolved = _restextResolver.Resolve(raw, locale, warnings);
            passages[resolved.PassageId] = resolved;
        }

        // Hand-authored overrides load after the base passages: new passage_ids are added,
        // matching ones replace the base version entirely (see IModuleLoader.LoadFromSources).
        if (overridePassageYamls is not null)
        {
            foreach (var yaml in overridePassageYamls)
            {
                var raw = _passageParser.ParsePassage(yaml, warnings);
                var resolved = _restextResolver.Resolve(raw, locale, warnings);
                passages[resolved.PassageId] = resolved;
            }
        }

        var layoutChrome = new Dictionary<string, LayoutChromeDoc>();
        if (layoutChromeYamls is not null)
        {
            foreach (var yaml in layoutChromeYamls)
            {
                var raw = _passageParser.ParseLayoutChrome(yaml, warnings);
                var resolved = _restextResolver.ResolveLayoutChrome(raw, locale, warnings);
                layoutChrome[resolved.LayoutId] = resolved;
            }
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
            LayoutChrome = layoutChrome,
        };
    }

    // Resolves which subdirectory holds base passages and which (if any) holds hand-authored
    // overrides. Consults manifest.yaml's optional "passages"/"passages_override" fields (relative
    // to directoryPath) if present; otherwise falls back to the "passages"/"passages-override"
    // folder-name convention. If no "passages" subfolder exists at all, falls back further to the
    // legacy flat layout (passages directly at directoryPath), so older extractor output — which
    // has no passages/ subfolder — still loads unchanged.
    private static (string PassagesDir, string? OverrideDir) ResolvePassageDirs(string directoryPath)
    {
        string? passagesField = null;
        string? overrideField = null;

        var manifestPath = Path.Combine(directoryPath, "manifest.yaml");
        if (File.Exists(manifestPath))
        {
            (passagesField, overrideField) = ReadPassagePathFields(File.ReadAllText(manifestPath));
        }

        var passagesDir = Path.Combine(directoryPath, passagesField ?? "passages");
        if (!Directory.Exists(passagesDir))
        {
            passagesDir = directoryPath;
        }

        var overrideDir = Path.Combine(directoryPath, overrideField ?? "passages-override");
        return (passagesDir, Directory.Exists(overrideDir) ? overrideDir : null);
    }

    // A minimal, tolerant peek at manifest.yaml for just the two path fields this loader needs.
    // Deliberately does not go through ManifestParser/ModuleManifest's stricter schema — most
    // manifest fields (localized title, thumbnail, info, etc.) aren't consumed by this load path
    // at all, so a strict parse would fail on parts of the manifest this loader never touches.
    private static (string? PassagesDir, string? OverrideDir) ReadPassagePathFields(string manifestYaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(manifestYaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return (null, null);
        }

        string? Get(string key) =>
            root.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode s
                ? s.Value
                : null;

        return (Get("passages"), Get("passages_override"));
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

        var layoutChrome = new Dictionary<string, LayoutChromeDoc>(dependency.LayoutChrome);
        foreach (var (id, chrome) in module.LayoutChrome)
        {
            layoutChrome[id] = chrome; // module's own chrome wins on collision
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
            LayoutChrome = layoutChrome,
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
                case LinkNode n:
                    if (n.Target is not null)
                    {
                        CheckTarget(passageId, n.Target, passages, warnings);
                    }

                    CheckNodeListReferences(passageId, n.OnClick, passages, warnings);
                    break;
                case GotoNode g:
                    CheckTarget(passageId, g.Target, passages, warnings);
                    break;
                case IncludePassageNode ip:
                    CheckTarget(passageId, ip.Target, passages, warnings);
                    break;
                case PopupNode p:
                    if (p.Target is not null)
                    {
                        CheckTarget(passageId, p.Target, passages, warnings);
                    }

                    CheckNodeListReferences(passageId, p.OnClose, passages, warnings);
                    CheckNodeListReferences(passageId, p.Content, passages, warnings);
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

    // "app::gameover" is a reserved sentinel the engine (Masterwork.Engine.GameSession) recognizes
    // directly — it never resolves to a passage at all, so it must never be flagged as an
    // unresolved reference here. Duplicated as a literal rather than shared via a project
    // reference, matching how "${module::entrypoint}" is already duplicated across GameSession.cs
    // and PassageRenderer.cs.
    private const string AppGameOverTarget = "app::gameover";

    private void CheckTarget(string fromPassageId, string target,
        IReadOnlyDictionary<string, MwsPassageDoc> passages, ModuleWarnings warnings)
    {
        if (target.StartsWith("${", StringComparison.Ordinal) || target == AppGameOverTarget)
        {
            return; // dynamic, or a reserved app-level sentinel — can't/shouldn't validate statically
        }

        if (!passages.ContainsKey(target))
        {
            warnings.Add("unresolved_passage_ref", $"{fromPassageId}: target '{target}' does not exist");
            _logger.LogWarning("Unresolved passage reference: {FromPassageId} -> '{Target}'", fromPassageId, target);
        }
    }
}
