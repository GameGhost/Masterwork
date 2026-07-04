using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Masterwork.ModuleFormat;

// Resolves `restext://Key` references against a loaded locale dictionary. Runs once at module
// load time, before any expression is parsed or cached, since expressions can themselves contain
// restext:// references inside string literals (e.g. `if: warwinner == "restext://Common_026"`).
//
// Two resolution modes:
//   - Display fields (text.value, navigation.label, section.title, ...): substituted as-is.
//   - Expression fields (let/assign.expr, conditional.if, navigation.target when dynamic, ...):
//     the resolved value is spliced into a double-quoted string literal, so embedded `"` must be
//     escaped as `\"` to keep the expression syntactically valid.
public static partial class RestextResolver
{
    public static MwsPassageDoc Resolve(MwsPassageDoc passage, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings = null) =>
        passage with
        {
            Title = ResolveDisplay(passage.Title, locale, warnings),
            Location = passage.Location is null ? null : passage.Location with
            {
                Name = ResolveDisplay(passage.Location.Name, locale, warnings),
            },
            Nodes = ResolveNodeList(passage.Nodes, locale, warnings),
        };

    private static List<V3Node> ResolveNodeList(IReadOnlyList<V3Node> nodes, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) =>
        [.. nodes.Select(n => ResolveNode(n, locale, warnings))];

    private static V3Node ResolveNode(V3Node node, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) => node switch
    {
        V3TextNode t => t with { Value = ResolveDisplay(t.Value, locale, warnings)! },
        V3SectionNode s => s with
        {
            Title = ResolveDisplay(s.Title, locale, warnings),
            Content = ResolveNodeList(s.Content, locale, warnings),
        },
        V3LetNode l => l with { Expr = ResolveExpr(l.Expr, locale, warnings) },
        V3AssignNode a => a with { Expr = ResolveExpr(a.Expr, locale, warnings) },
        V3NavigationNode n => n with
        {
            Label = ResolveDisplay(n.Label, locale, warnings)!,
            Target = ResolveExpr(n.Target, locale, warnings),
            OnClick = ResolveNodeList(n.OnClick, locale, warnings),
        },
        V3PopupNode p => p with
        {
            Label = ResolveDisplay(p.Label, locale, warnings),
            Content = ResolveNodeList(p.Content, locale, warnings),
            OnClose = ResolveExpr(p.OnClose, locale, warnings),
        },
        V3InputNode i => i with
        {
            Label = ResolveDisplay(i.Label, locale, warnings)!,
            Text = ResolveDisplay(i.Text, locale, warnings)!,
            OnSubmit = ResolveExpr(i.OnSubmit, locale, warnings),
        },
        V3PromptNode pr => pr with { Text = ResolveDisplay(pr.Text, locale, warnings)! },
        V3GotoNode g => g with { Target = ResolveExpr(g.Target, locale, warnings) },
        V3IncludePassageNode ip => ip with { Target = ResolveExpr(ip.Target, locale, warnings) },
        V3ConditionalNode c => c with
        {
            Conditions = c.Conditions.Select(b => b with
            {
                If = ResolveExpr(b.If, locale, warnings),
                Then = ResolveNodeList(b.Then, locale, warnings),
            }).ToList(),
            Else = c.Else is null ? null : ResolveNodeList(c.Else, locale, warnings),
        },
        V3SwitchNode sw => sw with
        {
            Cases = sw.Cases.Select(cs => cs with
            {
                Match = ResolveMatch(cs.Match, locale, warnings),
                Nodes = ResolveNodeList(cs.Nodes, locale, warnings),
            }).ToList(),
            Default = sw.Default is null ? null : ResolveNodeList(sw.Default, locale, warnings),
        },
        V3ForEachNode f => f with { Do = ResolveNodeList(f.Do, locale, warnings) },
        V3CheckpointNode cp => cp with
        {
            Display = ResolveDisplay(cp.Display, locale, warnings),
            Diagnostic = ResolveDisplay(cp.Diagnostic, locale, warnings),
        },
        _ => node,
    };

    private static object ResolveMatch(object match, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) => match switch
    {
        string s => ResolveExpr(s, locale, warnings),
        List<object> list => list.Select(v => ResolveMatch(v, locale, warnings)).ToList(),
        _ => match,
    };

    [return: NotNullIfNotNull(nameof(value))]
    private static string? ResolveDisplay(string? value, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) =>
        value is null ? null : RestextRefRegex().Replace(value, m => Lookup(m.Groups[1].Value, locale, warnings));

    [return: NotNullIfNotNull(nameof(value))]
    private static string? ResolveExpr(string? value, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) =>
        value is null ? null : RestextRefRegex().Replace(value, m => EscapeForExpr(Lookup(m.Groups[1].Value, locale, warnings)));

    private static string Lookup(string key, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings)
    {
        if (locale.TryGetValue(key, out var v)) return v;
        warnings?.Add("missing_restext_key", $"restext key not found: {key}");
        return $"restext://{key}";
    }

    private static string EscapeForExpr(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    [GeneratedRegex(@"restext://([A-Za-z][A-Za-z0-9_]*)")]
    private static partial Regex RestextRefRegex();
}
