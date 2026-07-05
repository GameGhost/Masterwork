namespace Masterwork.ModuleFormat;

/// <summary>
/// Assembles a <see cref="LoadedModule"/> from either a directory of extractor output or
/// in-memory YAML/restext text (the latter used by tests, to avoid filesystem round-trips).
/// </summary>
public static class ModuleLoader
{
    /// <summary>
    /// Loads a module from an extractor output directory: every <c>*.mws.yaml</c> passage file,
    /// plus <c>_variables.yaml</c> and <c>en-US.restext</c> if present.
    /// </summary>
    /// <param name="directoryPath">Path to the extractor output directory.</param>
    public static LoadedModule LoadFromDirectory(string directoryPath)
    {
        var variablesPath = Path.Combine(directoryPath, "_variables.yaml");
        var variablesYaml = File.Exists(variablesPath) ? File.ReadAllText(variablesPath) : null;

        var restextPath = Path.Combine(directoryPath, "en-US.restext");
        var restextText = File.Exists(restextPath) ? File.ReadAllText(restextPath) : null;

        var passageYamls = Directory.EnumerateFiles(directoryPath, "*.mws.yaml").Select(File.ReadAllText);

        return LoadFromSources(passageYamls, variablesYaml, restextText);
    }

    /// <summary>
    /// Builds a module directly from in-memory YAML/restext text — the filesystem-free load path
    /// used by tests.
    /// </summary>
    /// <param name="passageYamls">Raw <c>.mws.yaml</c> text, one entry per passage.</param>
    /// <param name="variablesYaml">Raw <c>_variables.yaml</c> text, if any.</param>
    /// <param name="restextText">Raw <c>en-US.restext</c> text, if any.</param>
    public static LoadedModule LoadFromSources(
        IEnumerable<string> passageYamls, string? variablesYaml = null, string? restextText = null)
    {
        var warnings = new ModuleWarnings();

        var variables = variablesYaml is null
            ? new Dictionary<string, VarDef>()
            : VariableManifest.Parse(variablesYaml, warnings);

        var locale = restextText is null
            ? new Dictionary<string, string>()
            : RestextFile.Parse(restextText);

        var passages = new Dictionary<string, MwsPassageDoc>();
        foreach (var yaml in passageYamls)
        {
            var raw = PassageYamlParser.ParsePassage(yaml, warnings);
            var resolved = RestextResolver.Resolve(raw, locale, warnings);
            passages[resolved.PassageId] = resolved;
        }

        var startPassageId = passages.Values
            .FirstOrDefault(p => p.Tags.Any(t => string.Equals(t, "Begins-Here", System.StringComparison.OrdinalIgnoreCase)))
            ?.PassageId;

        ValidatePassageReferences(passages, warnings);

        return new LoadedModule
        {
            Passages = passages,
            Variables = variables,
            Locale = locale,
            Warnings = warnings,
            StartPassageId = startPassageId,
        };
    }

    // Verifies that every statically-known navigation/goto/include_passage target resolves to a
    // known passage. Dynamic targets ("${expr}") are skipped — they can't be checked until runtime.
    private static void ValidatePassageReferences(IReadOnlyDictionary<string, MwsPassageDoc> passages, ModuleWarnings warnings)
    {
        foreach (var passage in passages.Values)
        {
            CheckNodeListReferences(passage.PassageId, passage.Nodes, passages, warnings);
        }
    }

    private static void CheckNodeListReferences(string passageId, IReadOnlyList<Node> nodes,
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

    private static void CheckTarget(string fromPassageId, string target,
        IReadOnlyDictionary<string, MwsPassageDoc> passages, ModuleWarnings warnings)
    {
        if (target.StartsWith("${", StringComparison.Ordinal))
        {
            return; // dynamic — can't validate statically
        }

        if (!passages.ContainsKey(target))
        {
            warnings.Add("unresolved_passage_ref", $"{fromPassageId}: target '{target}' does not exist");
        }
    }
}
