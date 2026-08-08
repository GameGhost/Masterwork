using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.ModuleFormat;

/// <summary>
/// <inheritdoc cref="IRestextResolver"/> Runs once at module load time, before any expression is
/// parsed or cached, since expressions can themselves contain restext:// references inside string
/// literals (e.g. <c>if: warwinner == "restext://Common_026"</c>).
/// </summary>
/// <remarks>
/// Two resolution modes:
/// <list type="bullet">
/// <item>Display fields (<c>text.value</c>, <c>link.label</c>, <c>section.title</c>, ...): substituted as-is.</item>
/// <item>
/// Expression fields (<c>let</c>/<c>assign.expr</c>, <c>conditional.if</c>, <c>link.target</c>
/// when dynamic, ...): the resolved value is spliced into a double-quoted string literal, so
/// embedded <c>"</c> must be escaped as <c>\"</c> to keep the expression syntactically valid.
/// </item>
/// </list>
/// </remarks>
public sealed partial class RestextResolver : IRestextResolver
{
    private readonly ILogger<RestextResolver> _logger;

    /// <summary>Creates a resolver that discards log output.</summary>
    public RestextResolver() : this(NullLogger<RestextResolver>.Instance)
    {
    }

    /// <summary>Creates a resolver that logs through <paramref name="logger"/>.</summary>
    public RestextResolver(ILogger<RestextResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public MwsPassageDoc Resolve(MwsPassageDoc passage, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings = null)
    {
        _logger.LogDebug("Resolving restext references for passage '{PassageId}'", passage.PassageId);
        return passage with
        {
            Title = ResolveTitleOrSubtitle(passage.Title, locale, warnings),
            Subtitle = ResolveTitleOrSubtitle(passage.Subtitle, locale, warnings),
            Location = passage.Location is null ? null : passage.Location with
            {
                Name = ResolveDisplay(passage.Location.Name, locale, warnings),
            },
            Nodes = ResolveNodeList(passage.Nodes, locale, warnings),
        };
    }

    /// <inheritdoc/>
    public LayoutChromeDoc ResolveLayoutChrome(LayoutChromeDoc chrome, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings = null)
    {
        _logger.LogDebug("Resolving restext references for layout chrome '{LayoutId}'", chrome.LayoutId);
        return chrome with
        {
            Header = ResolveNodeList(chrome.Header, locale, warnings),
            Footer = ResolveNodeList(chrome.Footer, locale, warnings),
            BeforeContent = ResolveNodeList(chrome.BeforeContent, locale, warnings),
            AfterContent = ResolveNodeList(chrome.AfterContent, locale, warnings),
        };
    }

    private List<Node> ResolveNodeList(IReadOnlyList<Node> nodes, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) =>
        [.. nodes.Select(n => ResolveNode(n, locale, warnings))];

    private Node ResolveNode(Node node, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) => node switch
    {
        TextNode t => t with { Value = ResolveDisplay(t.Value, locale, warnings)! },
        ImageNode im => im with { Title = ResolveDisplay(im.Title, locale, warnings) },
        SectionNode s => s with
        {
            Title = ResolveDisplay(s.Title, locale, warnings),
            Content = ResolveNodeList(s.Content, locale, warnings),
        },
        LetNode l => l with { Expr = ResolveExpr(l.Expr, locale, warnings) },
        AssignNode a => a with { Expr = ResolveExpr(a.Expr, locale, warnings) },
        LinkNode n => n with
        {
            Label = ResolveDisplay(n.Label, locale, warnings)!,
            Target = ResolveExpr(n.Target, locale, warnings),
            OnClick = ResolveNodeList(n.OnClick, locale, warnings),
            SnapshotLabel = ResolveDisplay(n.SnapshotLabel, locale, warnings),
        },
        PopupNode p => p with
        {
            Label = ResolveDisplay(p.Label, locale, warnings),
            Header = ResolveNodeList(p.Header, locale, warnings),
            Content = ResolveNodeList(p.Content, locale, warnings),
            Okay = ResolveDisplay(p.Okay, locale, warnings),
            Cancel = ResolveDisplay(p.Cancel, locale, warnings),
            Target = ResolveExpr(p.Target, locale, warnings),
            OnClose = ResolveNodeList(p.OnClose, locale, warnings),
            SnapshotLabel = ResolveDisplay(p.SnapshotLabel, locale, warnings),
        },
        InputNode i => i with
        {
            Label = ResolveDisplay(i.Label, locale, warnings)!,
        },
        GotoNode g => g with
        {
            Target = ResolveExpr(g.Target, locale, warnings),
            SnapshotLabel = ResolveDisplay(g.SnapshotLabel, locale, warnings),
        },
        IncludePassageNode ip => ip with { Target = ResolveExpr(ip.Target, locale, warnings) },
        ConditionalNode c => c with
        {
            Conditions = c.Conditions.Select(b => b with
            {
                If = ResolveExpr(b.If, locale, warnings),
                Then = ResolveNodeList(b.Then, locale, warnings),
            }).ToList(),
            Else = c.Else is null ? null : ResolveNodeList(c.Else, locale, warnings),
        },
        SwitchNode sw => sw with
        {
            Cases = sw.Cases.Select(cs => cs with
            {
                Match = ResolveMatch(cs.Match, locale, warnings),
                Nodes = ResolveNodeList(cs.Nodes, locale, warnings),
            }).ToList(),
            Default = sw.Default is null ? null : ResolveNodeList(sw.Default, locale, warnings),
        },
        ForEachNode f => f with { Do = ResolveNodeList(f.Do, locale, warnings) },
        CheckpointNode cp => cp with
        {
            Display = ResolveDisplay(cp.Display, locale, warnings),
            Diagnostic = ResolveDisplay(cp.Diagnostic, locale, warnings),
        },
        _ => node,
    };

    private object ResolveMatch(object match, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) => match switch
    {
        string s => ResolveExpr(s, locale, warnings),
        List<object> list => list.Select(v => ResolveMatch(v, locale, warnings)).ToList(),
        _ => match,
    };

    [return: NotNullIfNotNull(nameof(value))]
    private string? ResolveDisplay(string? value, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) =>
        value is null ? null : RestextRefRegex().Replace(value, m => Lookup(m.Groups[1].Value, locale, warnings));

    // Title/subtitle are ordinarily plain display text or a single bare "restext://Key" (the whole
    // field) — never inside any quote context, so ResolveDisplay's unescaped splice is correct.
    // But CradleExtractor.TryBuildTernaryHeading can collapse several branches' own headings into
    // one computed title, e.g. '{gunsbonus == 1 ? "restext://Heading_A" : "restext://Heading_B"}' —
    // there, each restext:// reference sits inside a double-quoted string literal that's itself
    // part of a larger expression, so the looked-up text must be escaped (ResolveExpr) or a locale
    // value containing '"' would break the ternary's own string-literal syntax. A field wrapped
    // entirely in "{...}" is exactly (and only) this expression shape — RestextCollector.ExtractField
    // never produces a plain display title starting with "{" for any other reason (a bare "{var}"
    // placeholder never contains a restext:// reference in the first place, so which mode resolves
    // it is moot — see that method's own remarks).
    [return: NotNullIfNotNull(nameof(value))]
    private string? ResolveTitleOrSubtitle(string? value, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) =>
        value is not null && value.StartsWith('{') && value.EndsWith('}')
            ? ResolveExpr(value, locale, warnings)
            : ResolveDisplay(value, locale, warnings);

    [return: NotNullIfNotNull(nameof(value))]
    private string? ResolveExpr(string? value, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings) =>
        value is null ? null : RestextRefRegex().Replace(value, m => EscapeForExpr(Lookup(m.Groups[1].Value, locale, warnings)));

    private string Lookup(string key, IReadOnlyDictionary<string, string> locale, ModuleWarnings? warnings)
    {
        if (locale.TryGetValue(key, out var v))
        {
            return v;
        }

        warnings?.Add("missing_restext_key", $"restext key not found: {key}");
        _logger.LogWarning("Restext key not found: {Key}", key);
        return $"restext://{key}";
    }

    private static string EscapeForExpr(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // Keys may start with '_' — RestextCollector.SanitizeForRestextKey prepends '_' when a
    // passage_id doesn't start with a letter (e.g. "1sttime-Suspicion" → "_1sttime_Suspicion_001").
    [GeneratedRegex(@"restext://([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex RestextRefRegex();
}
