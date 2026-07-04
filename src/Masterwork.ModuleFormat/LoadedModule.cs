namespace Masterwork.ModuleFormat;

public sealed class LoadedModule
{
    public required IReadOnlyDictionary<string, MwsPassageDoc> Passages { get; init; }
    public required IReadOnlyDictionary<string, VarDef> Variables { get; init; }
    public required IReadOnlyDictionary<string, string> Locale { get; init; }
    public required ModuleWarnings Warnings { get; init; }

    // The passage tagged `Begins-Here` (case-insensitive), or null if none is tagged.
    public string? StartPassageId { get; init; }
}

public static class ModuleLoader
{
    public static LoadedModule LoadFromDirectory(string directoryPath)
    {
        var variablesPath = Path.Combine(directoryPath, "_variables.yaml");
        var variablesYaml = File.Exists(variablesPath) ? File.ReadAllText(variablesPath) : null;

        var restextPath = Path.Combine(directoryPath, "en-US.restext");
        var restextText = File.Exists(restextPath) ? File.ReadAllText(restextPath) : null;

        var passageYamls = Directory.EnumerateFiles(directoryPath, "*.mws.yaml").Select(File.ReadAllText);

        return LoadFromSources(passageYamls, variablesYaml, restextText);
    }

    // Filesystem-free load path for tests: build a module directly from in-memory YAML/restext text.
    public static LoadedModule LoadFromSources(
        IEnumerable<string> passageYamls, string? variablesYaml = null, string? restextText = null)
    {
        var warnings = new ModuleWarnings();

        var variables = variablesYaml is null
            ? new Dictionary<string, VarDef>()
            : VariableManifest.Parse(variablesYaml);

        var locale = restextText is null
            ? new Dictionary<string, string>()
            : RestextFile.Parse(restextText);

        var passages = new Dictionary<string, MwsPassageDoc>();
        foreach (var yaml in passageYamls)
        {
            var raw = PassageYamlParser.ParsePassage(yaml);
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
            CheckNodeListReferences(passage.PassageId, passage.Nodes, passages, warnings);
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
                    if (p.OnClose is not null) CheckTarget(passageId, p.OnClose, passages, warnings);
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
                        CheckNodeListReferences(passageId, branch.Then, passages, warnings);
                    if (c.Else is not null) CheckNodeListReferences(passageId, c.Else, passages, warnings);
                    break;
                case SwitchNode sw:
                    foreach (var sc in sw.Cases)
                        CheckNodeListReferences(passageId, sc.Nodes, passages, warnings);
                    if (sw.Default is not null) CheckNodeListReferences(passageId, sw.Default, passages, warnings);
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
        if (target.StartsWith("${", StringComparison.Ordinal)) return; // dynamic — can't validate statically
        if (!passages.ContainsKey(target))
            warnings.Add("unresolved_passage_ref", $"{fromPassageId}: target '{target}' does not exist");
    }
}
