using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MasterWork.ModuleFormat;
using MasterWork.Extractor.Visitors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MasterWork.Extractor;

public class CradleExtractor
{
    private readonly ExtractionOptions _opts;
    private readonly SpriteMapper _spriteMapper;
    private readonly ExtractionReport _report;

    // passage index → (name, tags[])
    private readonly Dictionary<int, (string Name, string[] Tags)> _registry = [];
    // passage index → Main method syntax
    private readonly Dictionary<int, MethodDeclarationSyntax> _mainMethods = [];
    // passage index → (fragment index → method syntax)
    private readonly Dictionary<int, Dictionary<int, MethodDeclarationSyntax>> _fragmentMethods = [];
    // All discovered variables: name → VarDef
    private readonly Dictionary<string, VarDef> _variables = [];

    // Standard variables from GLOBALS.cs that all modules share
    private static readonly HashSet<string> StandardVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "nameA", "nameB", "nameC", "nameD", "nameE",
        "townname", "players", "playerCount", "currentPassage",
    };

    public CradleExtractor(ExtractionOptions opts, SpriteMapper spriteMapper, ExtractionReport report)
    {
        _opts = opts;
        _spriteMapper = spriteMapper;
        _report = report;
    }

    public List<MwsPassage> Extract(IEnumerable<string> sourceFiles)
    {
        var trees = sourceFiles.Select(f =>
            CSharpSyntaxTree.ParseText(WrapPartialClass(File.ReadAllText(f)), path: f)).ToList();

        Pass1_DiscoverVariables(trees);
        Pass2_BuildPassageRegistry(trees);
        Pass3_ExtractPassageBodies(trees);

        _report.VariablesDiscovered = _variables.Count;

        var passages = BuildPassages();
        _report.PassagesExtracted = passages.Count;
        return passages;
    }

    // ── Pass 1: Variable discovery ─────────────────────────────────────────

    private void Pass1_DiscoverVariables(List<SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            var root = tree.GetCompilationUnitRoot();
            foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                // this.Vars.X  →  MemberAccess( MemberAccess(this, Vars), X )
                if (access.Expression is MemberAccessExpressionSyntax inner &&
                    inner.Name.Identifier.Text == "Vars")
                {
                    var varName = access.Name.Identifier.Text;
                    if (string.IsNullOrEmpty(varName) || varName == "Vars") continue;

                    if (!_variables.ContainsKey(varName))
                    {
                        _variables[varName] = new VarDef
                        {
                            Name = varName,
                            VarType = InferTypeFromContext(access),
                            IsStandard = StandardVariables.Contains(varName),
                        };
                    }
                }
            }
        }

        // Refine types from assignment RHS
        foreach (var tree in trees)
        {
            var root = tree.GetCompilationUnitRoot();
            foreach (var assign in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assign.Left is not MemberAccessExpressionSyntax leftAccess) continue;
                if (leftAccess.Expression is not MemberAccessExpressionSyntax innerLeft ||
                    innerLeft.Name.Identifier.Text != "Vars") continue;

                var varName = leftAccess.Name.Identifier.Text;
                if (!_variables.TryGetValue(varName, out var def)) continue;

                var inferredType = InferTypeFromRhs(assign.Right);
                if (inferredType != "string" || def.VarType == "string")
                    def.VarType = inferredType;

                // Capture first assigned literal as default
                if (def.Default is null && assign.Right is LiteralExpressionSyntax lit)
                    def.Default = GetLiteralValue(lit);
            }
        }
    }

    private static string InferTypeFromContext(MemberAccessExpressionSyntax access)
    {
        // Look at the parent — if it's int.Parse(this.Vars.X) the var is probably int
        var parent = access.Parent;
        if (parent is ArgumentSyntax arg && arg.Parent?.Parent is InvocationExpressionSyntax inv)
        {
            var methodName = (inv.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
            if (methodName == "Parse") return "int";
        }
        return "string";
    }

    private static string InferTypeFromRhs(ExpressionSyntax rhs)
    {
        if (rhs is LiteralExpressionSyntax lit2)
        {
            if (lit2.IsKind(SyntaxKind.NumericLiteralExpression)) return "int";
            if (lit2.IsKind(SyntaxKind.StringLiteralExpression)) return "string";
        }
        if (rhs is InvocationExpressionSyntax inv2)
        {
            var methodName = (inv2.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text
                ?? (inv2.Expression as IdentifierNameSyntax)?.Identifier.Text;
            if (methodName == "a" || methodName == "shuffled") return "array";
            if (methodName == "num" || methodName == "random") return "int";
        }
        if (rhs is ArrayCreationExpressionSyntax) return "array";
        return "string";
    }

    private static object GetLiteralValue(LiteralExpressionSyntax lit)
    {
        if (lit.IsKind(SyntaxKind.NumericLiteralExpression)) return lit.Token.Value ?? 0;
        return lit.Token.ValueText;
    }

    // ── Pass 2: Passage registry ───────────────────────────────────────────

    private void Pass2_BuildPassageRegistry(List<SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            var root = tree.GetCompilationUnitRoot();
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var name = method.Identifier.Text;
                if (!TryParsePassageMethod(name, out int idx, out string kind)) continue;

                if (kind == "Init")
                {
                    ExtractRegistration(idx, method);
                }
                else if (kind == "Main")
                {
                    _mainMethods[idx] = method;
                }
                else if (kind.StartsWith("Fragment_") &&
                    int.TryParse(kind["Fragment_".Length..], out int fragIdx))
                {
                    if (!_fragmentMethods.TryGetValue(idx, out var frags))
                    {
                        frags = [];
                        _fragmentMethods[idx] = frags;
                    }
                    frags[fragIdx] = method;
                }
            }
        }
    }

    private static bool TryParsePassageMethod(string name, out int idx, out string kind)
    {
        idx = 0; kind = "";
        // passageN_Init, passageN_Main, passageN_Fragment_M
        if (!name.StartsWith("passage")) return false;
        var rest = name["passage".Length..];
        var underscore = rest.IndexOf('_');
        if (underscore < 0) return false;

        if (!int.TryParse(rest[..underscore], out idx)) return false;
        kind = rest[(underscore + 1)..];
        return true;
    }

    private void ExtractRegistration(int idx, MethodDeclarationSyntax initMethod)
    {
        if (initMethod.Body is null) return;

        // base.Passages["Name"] = new StoryPassage("Name", new string[] { "tag1", ... }, delegate)
        foreach (var assign in initMethod.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assign.Right is not ObjectCreationExpressionSyntax ctor) continue;
            var ctorArgs = ctor.ArgumentList?.Arguments;
            if (ctorArgs is null || ctorArgs.Value.Count < 2) continue;

            var passageName = GetStringArgument(ctorArgs.Value[0].Expression);
            if (passageName is null) continue;

            var tags = ExtractStringArray(ctorArgs.Value[1].Expression);
            _registry[idx] = (passageName, tags);
            return;
        }
    }

    private static string? GetStringArgument(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
            return lit.Token.ValueText;
        return null;
    }

    private static string[] ExtractStringArray(ExpressionSyntax expr)
    {
        // new string[] { "tag1", "tag2" }
        if (expr is ArrayCreationExpressionSyntax arr && arr.Initializer is not null)
        {
            return arr.Initializer.Expressions
                .OfType<LiteralExpressionSyntax>()
                .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
                .Select(l => l.Token.ValueText)
                .ToArray();
        }
        return [];
    }

    // ── Pass 3: Extract passage bodies ────────────────────────────────────

    private void Pass3_ExtractPassageBodies(List<SyntaxTree> trees)
    {
        // Resolved in BuildPassages; nothing to do here since bodies are already collected
    }

    // ── Build final passages ──────────────────────────────────────────────

    private List<MwsPassage> BuildPassages()
    {
        var passages = new List<MwsPassage>();

        foreach (var (idx, (name, tags)) in _registry.OrderBy(kv => kv.Key))
        {
            if (!_mainMethods.TryGetValue(idx, out var mainMethod))
            {
                _report.AddWarning(name, "No Main method found for this passage index");
                continue;
            }

            var visitor = new PassageBodyVisitor(name, _spriteMapper, _report);
            var nodes = mainMethod.Body is not null
                ? visitor.VisitBlock(mainMethod.Body)
                : [];

            // Stitch fragment methods into expand_link nodes
            if (_fragmentMethods.TryGetValue(idx, out var frags))
                StitchFragments(name, nodes, frags);

            // Filter debug passages if requested
            var isDebug = tags.Contains("devpage") || HasDevpageGuard(nodes);
            if (isDebug && !_opts.IncludeDebug)
            {
                _report.AddInfo(name, "Excluded debug passage");
                continue;
            }

            passages.Add(new MwsPassage
            {
                PassageId = name,
                Title = name,
                Tags = tags,
                Layout = InferLayout(tags),
                Nodes = nodes,
                Debug = isDebug,
            });
        }

        return passages;
    }

    private static void StitchFragments(
        string passageName,
        List<MwsNode> nodes,
        Dictionary<int, MethodDeclarationSyntax> frags)
    {
        // Walk the node tree and replace pending fragment stubs in ExpandLinkNodes
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is ExpandLinkNode expand)
            {
                // Check if the first expand_node is a pending fragment stub
                if (expand.ExpandNodes.Count == 1 &&
                    expand.ExpandNodes[0] is UnknownNode unk &&
                    unk.Note == "fragment:pending_stitch")
                {
                    // Try to find the fragment method index from the ref
                    var fragIdx = ParseFragmentIndex(unk.OriginalCode, passageName);
                    if (fragIdx.HasValue && frags.TryGetValue(fragIdx.Value, out var fragMethod))
                    {
                        // Re-visit fragment body
                        var fragVisitor = new PassageBodyVisitor(passageName, SpriteMapper.Empty(),
                            new ExtractionReport());
                        var fragNodes = fragMethod.Body is not null
                            ? fragVisitor.VisitBlock(fragMethod.Body)
                            : [];
                        expand.ExpandNodes.Clear();
                        expand.ExpandNodes.AddRange(fragNodes);
                    }
                }
            }
            // Recurse into container nodes
            else if (nodes[i] is ConditionalNode cond)
            {
                foreach (var branch in cond.Branches)
                    StitchFragments(passageName, branch.Nodes, frags);
            }
            else if (nodes[i] is SectionBodyNode section)
                StitchFragments(passageName, section.Nodes, frags);
            else if (nodes[i] is SetupBlockNode setup)
                StitchFragments(passageName, setup.Nodes, frags);
        }
    }

    private static int? ParseFragmentIndex(string refCode, string passageName)
    {
        // this.passageN_Fragment_M — extract M
        var pattern = $"this.passage\\d+_Fragment_(\\d+)";
        var m = System.Text.RegularExpressions.Regex.Match(refCode, pattern);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var fragIdx))
            return fragIdx;
        return null;
    }

    private static bool HasDevpageGuard(List<MwsNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is ConditionalNode cond)
            {
                foreach (var branch in cond.Branches)
                {
                    if (branch.Condition?.Contains("devpage") == true) return true;
                }
            }
        }
        return false;
    }

    private static string InferLayout(string[] tags)
    {
        // Cradle tags: "ck" → hub, "ck2" → event; original scripts also use "HUB"
        if (tags.Any(t => t.Equals("ck", StringComparison.OrdinalIgnoreCase) ||
                          t.Equals("hub", StringComparison.OrdinalIgnoreCase)))
            return "hub";
        if (tags.Any(t => t.Equals("ck2", StringComparison.OrdinalIgnoreCase)))
            return "event";
        return "narration";
    }

    public Dictionary<string, VarDef> GetDiscoveredVariables() => _variables;

    // Cradle scripts are partial class members with no class or namespace declaration.
    // Wrap them so Roslyn can parse method declarations correctly.
    private static string WrapPartialClass(string content) =>
        "using System; using System.Collections.Generic;\n" +
        "public partial class CradleStory {\n" +
        content +
        "\n}";
}
