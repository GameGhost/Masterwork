using System.Text;
using System.Text.RegularExpressions;
using Masterwork.ModuleFormat;
using VarDef = Masterwork.ModuleFormat.VarDef;
using Masterwork.Extractor.Visitors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Masterwork.Extractor;

public partial class CradleExtractor
{
    // Matches "varName op value" switch conditions, e.g. "players == 2", "costC == "Biology"", "players <= 5"
    [GeneratedRegex(@"^(\w+)\s*(==|!=|<=|>=|<|>)\s*(.+)$")]
    private static partial Regex SwitchCondRegex();
    private readonly ExtractionOptions _opts;
    private readonly SpriteMapper _spriteMapper;
    private readonly ExtractionReport _report;

    // passage index → (name, tags[], sourceFile)
    private readonly Dictionary<int, (string Name, string[] Tags, string SourceFile)> _registry = [];
    // passage index → Main method syntax
    private readonly Dictionary<int, MethodDeclarationSyntax> _mainMethods = [];
    // passage index → (fragment index → method syntax)
    private readonly Dictionary<int, Dictionary<int, MethodDeclarationSyntax>> _fragmentMethods = [];
    // All discovered variables: name → VarDef
    private readonly Dictionary<string, VarDef> _variables = [];
    // Variables whose type/default came from VarDefs field declarations (authoritative — not overridden by usage inference)
    private readonly HashSet<string> _varDefsVars = [];
    // Source file paths that are complete (class + VarDefs included) vs. partial (method-only)
    private readonly HashSet<string> _completeFiles = [];

    public CradleExtractor(ExtractionOptions opts, SpriteMapper spriteMapper, ExtractionReport report)
    {
        _opts = opts;
        _spriteMapper = spriteMapper;
        _report = report;
    }

    public List<MwsPassage> Extract(IEnumerable<string> sourceFiles)
    {
        var trees = sourceFiles.Select(f =>
        {
            var content = File.ReadAllText(f);
            var (prepared, isComplete) = PrepareSource(content);
            if (isComplete)
            {
                _completeFiles.Add(f);
            }

            return CSharpSyntaxTree.ParseText(prepared, path: f);
        }).ToList();

        Pass1_DiscoverVariables(trees);
        Pass2_BuildPassageRegistry(trees);
        Pass3_ExtractPassageBodies(trees);

        _report.VariablesDiscovered = _variables.Count;

        var passages = BuildPassages();
        AssignSeedKeys(passages);
        _report.PassagesExtracted = passages.Count;
        return passages;
    }

    // ── Seed key assignment ────────────────────────────────────────────────

    // Assigns stable seed_key values to every VarRandom node in every passage.
    // Keys are scoped per passage: "PassageId_N" (N is 0-based, DFS order).
    // Does not overwrite already-set keys, so hand-authored overrides are preserved.
    private static void AssignSeedKeys(List<MwsPassage> passages)
    {
        foreach (var passage in passages)
        {
            int counter = 0;
            AssignSeedKeysInNodes(passage.Nodes, passage.PassageId, ref counter);
        }
    }

    private static void AssignSeedKeysInNodes(List<MwsNode> nodes, string passageId, ref int counter)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case EffectNode effect when effect.VarRandom is not null:
                    foreach (var kv in effect.VarRandom)
                    {
                        kv.Value.SeedKey ??= $"{passageId}_{counter++}";
                    }

                    break;
                case LetNode let when let.Random is not null:
                    let.Random.SeedKey ??= $"{passageId}_{counter++}";
                    break;
                case ConditionalNode cond:
                    foreach (var branch in cond.Branches)
                    {
                        AssignSeedKeysInNodes(branch.Nodes, passageId, ref counter);
                    }

                    break;
                case SwitchNode sw:
                    foreach (var cas in sw.Cases)
                    {
                        AssignSeedKeysInNodes(cas.Nodes, passageId, ref counter);
                    }

                    break;
                case SectionBodyNode sec:
                    AssignSeedKeysInNodes(sec.Nodes, passageId, ref counter);
                    break;
                case SetupBlockNode setup:
                    AssignSeedKeysInNodes(setup.Nodes, passageId, ref counter);
                    break;
                case LinkNode link when link.Nodes.Count > 0:
                    AssignSeedKeysInNodes(link.Nodes, passageId, ref counter);
                    break;
                case ExpandLinkNode expand:
                    AssignSeedKeysInNodes(expand.ExpandNodes, passageId, ref counter);
                    break;
                case ForeachNode fe:
                    AssignSeedKeysInNodes(fe.Nodes, passageId, ref counter);
                    break;
            }
        }
    }

    // ── Pass 1: Variable discovery ─────────────────────────────────────────

    private void Pass1_DiscoverVariables(List<SyntaxTree> trees)
    {
        // Phase A: VarDefs inner class field declarations in complete files. This is the only
        // place a real default can come from — StoryVar isn't statically typed, so the initializer
        // is really just "help ensure there's a default" (per the original Cradle author's intent),
        // not a type declaration. It informs TYPE here; usage-based inference in Phase C then
        // confirms/refines type from every assignment, not just this one.
        //   public StoryVar @name = 0     → int;  default kept only if != 0
        //   public StoryVar @name = ""    → string; default kept only if != ""
        //   public StoryVar @name = true  → bool; default kept only if true
        //   public StoryVar @name         → string, no default (refinable by Phase C)
        foreach (var tree in trees)
        {
            if (!_completeFiles.Contains(tree.FilePath))
            {
                continue;
            }

            var root = tree.GetCompilationUnitRoot();
            foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (cls.Identifier.Text != "VarDefs")
                {
                    continue;
                }

                foreach (var field in cls.Members.OfType<FieldDeclarationSyntax>())
                {
                    if (field.Declaration.Type.ToString() != "StoryVar")
                    {
                        continue;
                    }

                    foreach (var declarator in field.Declaration.Variables)
                    {
                        var varName = declarator.Identifier.Text.TrimStart('@');
                        if (string.IsNullOrEmpty(varName))
                        {
                            continue;
                        }

                        var (varType, defaultVal) = InferFromVarDefsInitializer(declarator.Initializer);

                        _variables[varName] = new VarDef
                        {
                            Name = varName,
                            VarType = varType,
                            Default = defaultVal,
                        };
                        // Only mark as authoritative when an explicit initializer is present.
                        // Vars declared as "public StoryVar @x;" (no initializer) remain
                        // refinable by usage-based inference in Phase C.
                        if (declarator.Initializer is not null)
                        {
                            _varDefsVars.Add(varName);
                        }
                    }
                }
            }
        }

        // Phase B: scan this.Vars.X accesses — adds any variable not already known from VarDefs.
        foreach (var tree in trees)
        {
            var root = tree.GetCompilationUnitRoot();
            foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                // this.Vars.X  →  MemberAccess( MemberAccess(this, Vars), X )
                // Vars.X       →  MemberAccess( Identifier(Vars), X )           [complete files]
                bool isVarsAccess =
                    (access.Expression is MemberAccessExpressionSyntax inner &&
                     inner.Name.Identifier.Text == "Vars") ||
                    (access.Expression is IdentifierNameSyntax idName &&
                     idName.Identifier.Text == "Vars");

                if (!isVarsAccess)
                {
                    continue;
                }

                var varName = access.Name.Identifier.Text;
                if (string.IsNullOrEmpty(varName) || varName == "Vars")
                {
                    continue;
                }

                if (!_variables.ContainsKey(varName))
                {
                    _variables[varName] = new VarDef
                    {
                        Name = varName,
                        VarType = InferTypeFromContext(access),
                    };
                }
            }
        }

        // Phase C: confirm/refine types from every assignment RHS for variables not locked by an
        // explicit VarDefs initializer. Accumulates every distinct inference (not just the first)
        // so a genuine type conflict — not a first-assignment fluke — is what triggers hoisting.
        // No default-value capture here: a "first assignment in source order" is an arbitrary
        // location in a ~30k-line file, unrelated to actual game-start state, so it's not a
        // trustworthy default source — see Phase A above for the one place a real default lives.
        var phaseC = new Dictionary<string, List<VarKind>>();
        foreach (var tree in trees)
        {
            var root = tree.GetCompilationUnitRoot();
            foreach (var assign in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assign.Left is not MemberAccessExpressionSyntax leftAccess)
                {
                    continue;
                }

                bool isVarsLeft =
                    (leftAccess.Expression is MemberAccessExpressionSyntax innerLeft2 &&
                     innerLeft2.Name.Identifier.Text == "Vars") ||
                    (leftAccess.Expression is IdentifierNameSyntax leftId &&
                     leftId.Identifier.Text == "Vars");
                if (!isVarsLeft)
                {
                    continue;
                }

                var varName = leftAccess.Name.Identifier.Text;
                if (!_variables.ContainsKey(varName) || _varDefsVars.Contains(varName))
                {
                    continue;
                }

                var inferredType = InferTypeFromRhs(assign.Right);
                if (inferredType is not null)
                {
                    if (!phaseC.TryGetValue(varName, out var typeList))
                    {
                        phaseC[varName] = typeList = [];
                    }

                    typeList.Add(inferredType.Value);
                }
            }
        }
        // Apply inferred types; warn when the same variable receives conflicting inferences.
        foreach (var (varName, types) in phaseC)
        {
            if (!_variables.TryGetValue(varName, out var def))
            {
                continue;
            }

            var distinct = types.Distinct().ToList();
            if (distinct.Count == 1)
            {
                def.VarType = distinct[0];
            }
            else
            {
                var chosen = PickBestType(distinct);
                _report.AddWarning("[variables]",
                    $"Variable '{varName}' has conflicting assignment types: {string.Join(", ", distinct)}. Using '{chosen}'.");
                def.VarType = chosen;
            }
        }
    }

    // Reads a VarDefs field's initializer, if any, for its type — and, only when the literal
    // differs from that type's canonical zero value, its default too. No initializer means
    // "string, no default" (StoryVar's own uninitialized-field behavior), refinable by Phase C.
    private static (VarKind Type, object? Default) InferFromVarDefsInitializer(EqualsValueClauseSyntax? initializer)
    {
        if (initializer?.Value is not LiteralExpressionSyntax lit)
        {
            return (VarKind.String, null);
        }

        if (lit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            var value = Convert.ToInt64(lit.Token.Value ?? 0L);
            return (VarKind.Integer, value != 0 ? value : null);
        }

        if (lit.IsKind(SyntaxKind.TrueLiteralExpression) || lit.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            var value = lit.IsKind(SyntaxKind.TrueLiteralExpression);
            return (VarKind.Boolean, value ? true : null);
        }

        var text = lit.Token.ValueText;
        return (VarKind.String, !string.IsNullOrEmpty(text) ? text : null);
    }

    private static VarKind PickBestType(IEnumerable<VarKind> types)
    {
        var set = new HashSet<VarKind>(types);
        if (set.Count == 1)
        {
            return set.First();
        }

        // bool < int < string: pick the widest scalar type that can represent every observed
        // value (StoryValue.AsInt()/AsString() already hoist bool→int→string at runtime).
        if (set.IsSubsetOf((VarKind[])[VarKind.Boolean, VarKind.Integer, VarKind.String]))
        {
            return set.Contains(VarKind.String) ? VarKind.String : VarKind.Integer;
        }

        // Array/record mixed with a scalar type (or with each other) isn't a coercion the engine
        // supports — string is the safest fallback declaration.
        return VarKind.String;
    }

    private static VarKind InferTypeFromContext(MemberAccessExpressionSyntax access)
    {
        // Look at the parent — if it's int.Parse(this.Vars.X) the var is probably int
        var parent = access.Parent;
        if (parent is ArgumentSyntax arg && arg.Parent?.Parent is InvocationExpressionSyntax inv)
        {
            var methodName = (inv.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
            if (methodName == "Parse")
            {
                return VarKind.Integer;
            }
        }
        return VarKind.String;
    }

    private static VarKind? InferTypeFromRhs(ExpressionSyntax rhs)
    {
        while (rhs is ParenthesizedExpressionSyntax paren)
        {
            rhs = paren.Expression;
        }

        if (rhs is LiteralExpressionSyntax lit2)
        {
            if (lit2.IsKind(SyntaxKind.NumericLiteralExpression))
            {
                return VarKind.Integer;
            }

            if (lit2.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return VarKind.String;
            }

            if (lit2.IsKind(SyntaxKind.TrueLiteralExpression) || lit2.IsKind(SyntaxKind.FalseLiteralExpression))
            {
                return VarKind.Boolean;
            }
        }
        if (rhs is CastExpressionSyntax cast && cast.Type.ToString() == "int")
        {
            return VarKind.Integer;
        }

        if (rhs is InvocationExpressionSyntax inv2)
        {
            var methodName = (inv2.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text
                ?? (inv2.Expression as IdentifierNameSyntax)?.Identifier.Text;
            if (methodName == "a" || methodName == "shuffled")
            {
                // Element type isn't recoverable from the call alone; arrays are untyped at
                // runtime anyway (VarKind's array split is documentation-only), so this is a
                // harmless default rather than a real element-type claim.
                return VarKind.StringArray;
            }

            if (methodName is "num" or "random" or "PassageValueNumber" or "Range" or "Parse")
            {
                return VarKind.Integer;
            }
            // either(x, y, ...) produces int when all args are numeric literals, string when all are string literals
            if (methodName == "either")
            {
                var args = inv2.ArgumentList.Arguments;
                if (args.Count > 0 && args.All(a =>
                        a.Expression is LiteralExpressionSyntax eLit &&
                        eLit.IsKind(SyntaxKind.NumericLiteralExpression)))
                {
                    return VarKind.Integer;
                }

                if (args.Count > 0 && args.All(a =>
                        a.Expression is LiteralExpressionSyntax eLit &&
                        eLit.IsKind(SyntaxKind.StringLiteralExpression)))
                {
                    return VarKind.String;
                }
            }
        }
        if (rhs is ArrayCreationExpressionSyntax)
        {
            return VarKind.StringArray;
        }

        return null;
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
                if (!TryParsePassageMethod(name, out int idx, out string kind))
                {
                    continue;
                }

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
        if (!name.StartsWith("passage"))
        {
            return false;
        }

        var rest = name["passage".Length..];
        var underscore = rest.IndexOf('_');
        if (underscore < 0)
        {
            return false;
        }

        if (!int.TryParse(rest[..underscore], out idx))
        {
            return false;
        }

        kind = rest[(underscore + 1)..];
        return true;
    }

    private void ExtractRegistration(int idx, MethodDeclarationSyntax initMethod)
    {
        if (initMethod.Body is null)
        {
            return;
        }

        // base.Passages["Name"] = new StoryPassage("Name", new string[] { "tag1", ... }, delegate)
        foreach (var assign in initMethod.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assign.Right is not ObjectCreationExpressionSyntax ctor)
            {
                continue;
            }

            var ctorArgs = ctor.ArgumentList?.Arguments;
            if (ctorArgs is null || ctorArgs.Value.Count < 2)
            {
                continue;
            }

            var passageName = GetStringArgument(ctorArgs.Value[0].Expression);
            if (passageName is null)
            {
                continue;
            }

            var tags = ExtractStringArray(ctorArgs.Value[1].Expression);
            var sourceFile = initMethod.SyntaxTree.FilePath;
            _registry[idx] = (passageName, tags, sourceFile);
            return;
        }
    }

    private static string? GetStringArgument(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return lit.Token.ValueText;
        }

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

        foreach (var (idx, (name, tags, sourceFile)) in _registry.OrderBy(kv => kv.Key))
        {
            if (!_mainMethods.TryGetValue(idx, out var mainMethod))
            {
                _report.AddWarning(name, "No Main method found for this passage index");
                continue;
            }

            // 1-based line in the original file.
            // Wrapped files: Roslyn 0-based line - 1 (accounts for 2 prepended wrapper lines).
            // Complete files: Roslyn 0-based line + 1 (direct 0-to-1-based conversion).
            var line0 = mainMethod.GetLocation().GetLineSpan().StartLinePosition.Line;
            var isCompleteFile = _completeFiles.Contains(mainMethod.SyntaxTree.FilePath);
            var mainMethodLine = isCompleteFile ? line0 + 1 : line0 - 1;

            var visitor = new PassageBodyVisitor(name, _spriteMapper, _report, _variables, isCompleteFile);
            var nodes = mainMethod.Body is not null
                ? visitor.VisitBlock(mainMethod.Body)
                : [];

            // Stitch fragment methods into expand_link nodes.
            // Pass the full _fragmentMethods table so cross-passage fragments can be resolved
            // (e.g. passage35_Fragment_3 called from passage32 — Cradle counter artifact).
            var localFrags = _fragmentMethods.TryGetValue(idx, out var lf) ? lf : [];
            StitchFragments(name, nodes, localFrags, _fragmentMethods, _spriteMapper, _report, _variables);

            // Consolidate text, breaks, switches; then normalize VarRandom types
            nodes = ConsolidateTextNodes(nodes);
            var safeName = name.Replace(" ", "_").Replace("-", "_");
            var rndSeq = FindNextRndSeq(nodes, safeName);
            nodes = HoistAssignAndSwitchPlayerNames(nodes, safeName, ref rndSeq);
            NormalizeAllVarRandoms(nodes);
            // Strip decorative breaks from logic-only goto passages (no text, ends in goto)
            if (!HasTextOutput(nodes) && nodes.Any(n => n is GotoNode))
            {
                nodes = nodes.Where(n => n is not BreakNode and not ParagraphBreakNode).ToList();
            }

            // Filter debug passages if requested
            var isDebug = tags.Contains("devpage") || HasDevpageGuard(nodes);
            if (isDebug && !_opts.IncludeDebug)
            {
                _report.AddInfo(name, "Excluded debug passage");
                continue;
            }

            passages.Add(new MwsPassage
            {
                PassageIndex = idx,
                PassageId = name,
                Title = name,
                Tags = tags,
                Layout = InferLayout(tags),
                Nodes = nodes,
                Debug = isDebug,
                SourceFile = sourceFile,
                MainMethodSourceLine = mainMethodLine >= 1 ? mainMethodLine : null,
            });
        }

        return passages;
    }

    private static void StitchFragments(
        string passageName,
        List<MwsNode> nodes,
        Dictionary<int, MethodDeclarationSyntax> localFrags,
        Dictionary<int, Dictionary<int, MethodDeclarationSyntax>> allFrags,
        SpriteMapper spriteMapper,
        ExtractionReport report,
        IReadOnlyDictionary<string, VarDef>? variables = null)
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
                    // Look up the fragment method: local first, then cross-passage fallback.
                    // Cross-passage refs occur when Cradle's global fragment counter produces
                    // a method name like passage35_Fragment_3 called from passage32_Main.
                    var fragIdx = ParseFragmentIndex(unk.OriginalCode);
                    MethodDeclarationSyntax? fragMethod = null;
                    if (fragIdx.HasValue)
                    {
                        if (!localFrags.TryGetValue(fragIdx.Value, out fragMethod))
                        {
                            var crossPassageIdx = ParseFragmentPassageIndex(unk.OriginalCode);
                            if (crossPassageIdx.HasValue &&
                                allFrags.TryGetValue(crossPassageIdx.Value, out var crossFrags))
                            {
                                crossFrags.TryGetValue(fragIdx.Value, out fragMethod);
                            }
                        }
                    }

                    if (fragMethod is not null)
                    {
                        var fragIsComplete = fragMethod.SyntaxTree.GetRoot() is CompilationUnitSyntax cu2 &&
                            cu2.Members.OfType<ClassDeclarationSyntax>().Any();
                        var fragVisitor = new PassageBodyVisitor(passageName, spriteMapper, report, variables, fragIsComplete);
                        var fragNodes = fragMethod.Body is not null
                            ? fragVisitor.VisitBlock(fragMethod.Body)
                            : [];
                        expand.ExpandNodes.Clear();
                        expand.ExpandNodes.AddRange(fragNodes);
                        // Recurse into the stitched content — it may contain nested fragments
                        StitchFragments(passageName, expand.ExpandNodes, localFrags, allFrags, spriteMapper, report, variables);
                        // Navigation terminals: GotoNode or CheckProgressNode at the end
                        // → convert the expand-link to a plain navigation LinkNode.
                        // CheckProgress always records state, so force state_affecting = true
                        // regardless of the enchant command (None vs Replace).
                        string? termTarget = null;
                        bool termStateAffecting = expand.StateAffecting;
                        if (expand.ExpandNodes.Count > 0)
                        {
                            if (expand.ExpandNodes[^1] is GotoNode termGoto)
                            {
                                termTarget = termGoto.Target;
                            }
                            else if (expand.ExpandNodes[^1] is CheckProgressNode cpTerm &&
                                     !string.IsNullOrEmpty(cpTerm.TargetPassage))
                            {
                                termTarget = cpTerm.TargetPassage;
                                termStateAffecting = true;
                            }
                        }
                        if (termTarget is not null)
                        {
                            nodes[i] = new LinkNode
                            {
                                Label = expand.Label,
                                Target = termTarget,
                                StateAffecting = termStateAffecting,
                                Nodes = expand.ExpandNodes.GetRange(0, expand.ExpandNodes.Count - 1),
                                SourceLine = expand.SourceLine,
                            };
                        }
                    }
                    else
                    {
                        report.AddWarning(passageName,
                            $"Fragment not stitched: {unk.OriginalCode}",
                            sourceLine: unk.SourceLine);
                    }
                }
                else
                {
                    // Not a stub — still recurse in case there are nested ExpandLinkNodes
                    StitchFragments(passageName, expand.ExpandNodes, localFrags, allFrags, spriteMapper, report, variables);
                }
            }
            // Recurse into container nodes
            else if (nodes[i] is ConditionalNode cond)
            {
                foreach (var branch in cond.Branches)
                {
                    StitchFragments(passageName, branch.Nodes, localFrags, allFrags, spriteMapper, report, variables);
                }
            }
            else if (nodes[i] is SwitchNode sw)
            {
                foreach (var c in sw.Cases)
                {
                    StitchFragments(passageName, c.Nodes, localFrags, allFrags, spriteMapper, report, variables);
                }
            }
            else if (nodes[i] is ForeachNode fe)
            {
                StitchFragments(passageName, fe.Nodes, localFrags, allFrags, spriteMapper, report, variables);
            }
            else if (nodes[i] is SectionBodyNode section)
            {
                StitchFragments(passageName, section.Nodes, localFrags, allFrags, spriteMapper, report, variables);
            }
            else if (nodes[i] is SetupBlockNode setup)
            {
                StitchFragments(passageName, setup.Nodes, localFrags, allFrags, spriteMapper, report, variables);
            }
        }
    }

    private static int? ParseFragmentIndex(string refCode)
    {
        // Supports both "this.passageN_Fragment_M" and "passageN_Fragment_M" (complete-file format)
        var m = System.Text.RegularExpressions.Regex.Match(
            refCode, @"(?:this\.)?passage\d+_Fragment_(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var fragIdx))
        {
            return fragIdx;
        }

        return null;
    }

    // Extracts the passage index N from "passageN_Fragment_M" for cross-passage lookup.
    private static int? ParseFragmentPassageIndex(string refCode)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            refCode, @"(?:this\.)?passage(\d+)_Fragment_\d+");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var idx))
        {
            return idx;
        }

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
                    var c = branch.Condition;
                    if (c is null || !c.Contains("devpage"))
                    {
                        continue;
                    }
                    // "!devpage" (normalized from "devpage == 0 || devpage == """) is the
                    // normal-user guard (show setup on first HUB visit) — not a debug gate.
                    // Only flag as debug when devpage is checked truthy (e.g. != 0, == 1).
                    if (c.Contains("!devpage") || c.Contains("devpage == 0") || c.Contains("devpage == \"\""))
                    {
                        continue;
                    }

                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasTextOutput(List<MwsNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode: case SectionHeadingNode: return true;
                case ConditionalNode cond:
                    if (cond.Branches.Any(b => HasTextOutput(b.Nodes)))
                    {
                        return true;
                    }

                    break;
                case SwitchNode sw:
                    if (sw.Cases.Any(c => HasTextOutput(c.Nodes)))
                    {
                        return true;
                    }

                    break;
                case SectionBodyNode sec:
                    if (HasTextOutput(sec.Nodes))
                    {
                        return true;
                    }

                    break;
                case SetupBlockNode sb:
                    if (HasTextOutput(sb.Nodes))
                    {
                        return true;
                    }

                    break;
                case ForeachNode fe:
                    if (HasTextOutput(fe.Nodes))
                    {
                        return true;
                    }

                    break;
            }
        }
        return false;
    }

    private static string InferLayout(string[] tags)
    {
        // Cradle tags: "ck" → hub, "ck2" → event; original scripts also use "HUB"
        if (tags.Any(t => t.Equals("ck", StringComparison.OrdinalIgnoreCase) ||
                          t.Equals("hub", StringComparison.OrdinalIgnoreCase)))
        {
            return "hub";
        }

        if (tags.Any(t => t.Equals("ck2", StringComparison.OrdinalIgnoreCase)))
        {
            return "event";
        }

        return "narration";
    }

    // ── Text consolidation post-pass ─────────────────────────────────────────
    // Merges consecutive text/icon TextNodes into a single template string.
    // _rnd_* EffectNodes become LetNodes emitted before the merged TextNode.
    // Promotable ConditionalNodes (all-effect branches) become preamble nodes.
    // Runs a second ConsolidateBreaks pass: [break, break+] → paragraph_break.
    // Recurses into all container node types.

    private static List<MwsNode> ConsolidateTextNodes(List<MwsNode> nodes)
    {
        nodes = HoistConditionalLets(nodes);
        var result = new List<MwsNode>();
        var group = new List<MwsNode>();

        void FlushGroup()
        {
            if (group.Count == 0)
            {
                return;
            }

            var preambleNodes = new List<MwsNode>();
            var letNodes = new List<LetNode>();
            var letVarNames = new List<string>();
            var allRuns = new List<TextRun>();
            int? firstLine = null;

            foreach (var n in group)
            {
                if (n is TextNode t)
                {
                    firstLine ??= t.SourceLine;
                    if (t.Template is not null)
                    {
                        allRuns.Add(new TextRun { Text = t.Template, Style = t.Style });
                    }
                    else
                    {
                        allRuns.AddRange(t.Runs);
                    }
                }
                else if (IsRndOnlyEffect(n))
                {
                    var e = (EffectNode)n;
                    firstLine ??= e.SourceLine;
                    foreach (var kv in e.VarRandom!)
                    {
                        letNodes.Add(new LetNode { Var = kv.Key, Random = kv.Value, SourceLine = e.SourceLine });
                    }

                    letVarNames.AddRange(e.VarRandom.Keys);
                }
                else if (n is LetNode ln)
                {
                    firstLine ??= ln.SourceLine;
                    letNodes.Add(ln);
                    letVarNames.Add(ln.Var);
                }
                else
                {
                    // Promotable conditional — emitted before the merged TextNode
                    preambleNodes.Add(n);
                }
            }

            result.AddRange(preambleNodes);
            result.AddRange(letNodes);

            if (allRuns.Count > 0)
            {
                var dominantStyle = ComputeDominantStyle(allRuns);
                result.Add(new TextNode
                {
                    Template = BuildTemplate(allRuns, dominantStyle),
                    Style = dominantStyle,
                    Lets = letVarNames.Count > 0 ? letVarNames : null,
                    SourceLine = firstLine,
                });
            }

            group.Clear();
        }

        foreach (var node in nodes)
        {
            if (CanJoinGroup(node, group))
            {
                group.Add(node);
            }
            else
            {
                FlushGroup();
                result.Add(RecurseContainers(node));
            }
        }
        FlushGroup();
        return MergeInterstitialAssigns(HoistAndMergeSwitchLets(ConsolidateSwitches(ConsolidateBreaks(result))));
    }

    // Scans for [TextNode+][PureAssignEffect+][TextNode+] runs and, when hoisting the assigns
    // is safe (the pre-assign texts don't reference the assigned variables), emits the assigns
    // first and merges all texts into one node. Runs after ConsolidateSwitches so conditional
    // blocks have already been promoted above the text/assign sequence.
    private static List<MwsNode> MergeInterstitialAssigns(List<MwsNode> nodes)
    {
        var result = new List<MwsNode>(nodes.Count);
        int i = 0;
        while (i < nodes.Count)
        {
            if (nodes[i] is not TextNode) { result.Add(nodes[i++]); continue; }

            // Span of leading text nodes
            int preEnd = i;
            while (preEnd < nodes.Count && nodes[preEnd] is TextNode)
            {
                preEnd++;
            }

            // Span of following pure assigns
            int assignEnd = preEnd;
            while (assignEnd < nodes.Count && IsPureAssignEffect(nodes[assignEnd]))
            {
                assignEnd++;
            }

            // Only merge when assigns are followed by at least one more text node
            if (assignEnd == preEnd || assignEnd >= nodes.Count || nodes[assignEnd] is not TextNode)
            {
                for (int k = i; k < preEnd; k++)
                {
                    result.Add(nodes[k]);
                }

                i = preEnd;
                continue;
            }

            // Safety check: none of the pre-assign texts may reference the assigned variables
            var assigns = nodes[preEnd..assignEnd].Cast<EffectNode>().ToList();
            var assignedVars = assigns.SelectMany(a => a.VarSets!.Keys).ToHashSet(StringComparer.Ordinal);
            var preTexts = nodes[i..preEnd].Cast<TextNode>().ToList();

            if (preTexts.Any(t => TextNodeReferencesAny(t, assignedVars)))
            {
                for (int k = i; k < preEnd; k++)
                {
                    result.Add(nodes[k]);
                }

                i = preEnd;
                continue;
            }

            // Gather post-text run
            int postEnd = assignEnd;
            while (postEnd < nodes.Count && nodes[postEnd] is TextNode)
            {
                postEnd++;
            }

            // Emit: assigns, then merged text (pre + post)
            result.AddRange(assigns);
            var allRuns = new List<TextRun>();
            foreach (var t in preTexts.Concat(nodes[assignEnd..postEnd].Cast<TextNode>()))
            {
                if (t.Template is not null)
                {
                    allRuns.Add(new TextRun { Text = t.Template, Style = t.Style });
                }
                else
                {
                    allRuns.AddRange(t.Runs);
                }
            }
            var dominantStyle = ComputeDominantStyle(allRuns);
            var mergedTemplate = BuildTemplate(allRuns, dominantStyle);
            // Collapse adjacent identical markdown markers from seams between pre-built template strings
            mergedTemplate = mergedTemplate.Replace("****", "").Replace("__", "");
            result.Add(new TextNode
            {
                Template = mergedTemplate,
                Style = dominantStyle,
                SourceLine = preTexts.FirstOrDefault()?.SourceLine ?? assigns.FirstOrDefault()?.SourceLine,
            });
            i = postEnd;
        }
        return result;
    }

    // When every branch of a ConditionalNode contains exactly [LetNode(Random), TextNode({var})],
    // the conditional is "homogeneous random" — all branches produce the same kind of value and
    // the only difference is the random range. Rename all let vars to a single canonical name,
    // strip the TextNodes from branches (making the conditional promotable), and inject a synthetic
    // TextNode({canonical}) immediately after the conditional so text consolidation merges it with
    // the surrounding text fragments.
    private static List<MwsNode> HoistConditionalLets(List<MwsNode> nodes)
    {
        if (!nodes.Any(n => n is ConditionalNode c && IsHoistableConditionalLets(c)))
        {
            return nodes;
        }

        var result = new List<MwsNode>(nodes.Count + 2);
        foreach (var node in nodes)
        {
            if (node is ConditionalNode cond && IsHoistableConditionalLets(cond))
            {
                var firstLet = cond.Branches[0].Nodes.OfType<LetNode>().First();
                var firstTxt = cond.Branches[0].Nodes.OfType<TextNode>().First();
                var canonical = firstLet.Var;
                var style = firstTxt.Style ?? firstTxt.Runs?.FirstOrDefault()?.Style;
                foreach (var branch in cond.Branches)
                {
                    branch.Nodes.OfType<LetNode>().First().Var = canonical;
                    branch.Nodes.RemoveAll(n => n is TextNode);
                }
                result.Add(cond);
                result.Add(new TextNode { Template = $"{{{canonical}}}", Style = style });
            }
            else
            {
                result.Add(node);
            }
        }
        return result;
    }

    private static bool IsHoistableConditionalLets(ConditionalNode cond)
    {
        if (cond.Branches.Count < 2)
        {
            return false;
        }

        foreach (var branch in cond.Branches)
        {
            if (branch.Nodes.Count != 2)
            {
                return false;
            }
            // Accept both [LetNode, TextNode] and [TextNode, LetNode] orderings
            var let = branch.Nodes.OfType<LetNode>().FirstOrDefault();
            var txt = branch.Nodes.OfType<TextNode>().FirstOrDefault();
            if (let is null || let.Random is null)
            {
                return false;
            }

            if (txt is null)
            {
                return false;
            }

            var expected = $"{{{let.Var}}}";
            bool match = txt.Template == expected
                || (txt.Template is null && txt.Runs is { Count: 1 }
                    && txt.Runs[0].Text == expected && txt.Runs[0].AssetRef is null);
            if (!match)
            {
                return false;
            }
        }
        return true;
    }

    // When every case of a SwitchNode contains exactly [LetNode(Compute|Random), TextNode({var})]
    // (or with bold/italic wrapping like **{var}**), the switch is hoistable: normalize all case
    // let vars to the first case's canonical name, strip the TextNodes from cases (making the switch
    // promotable), and merge the surrounding text nodes into one with the variable reference inlined.
    // Runs after ConsolidateSwitches so switch nodes already exist.
    private static List<MwsNode> HoistAndMergeSwitchLets(List<MwsNode> nodes)
    {
        if (!nodes.Any(n => n is SwitchNode sw && IsHoistableSwitchLets(sw)))
        {
            return nodes;
        }

        var result = new List<MwsNode>(nodes.Count + 1);
        int i = 0;
        while (i < nodes.Count)
        {
            if (nodes[i] is not SwitchNode sw || !IsHoistableSwitchLets(sw))
            {
                result.Add(nodes[i++]);
                continue;
            }

            // Determine canonical name and inline style from the first case.
            var firstLet = sw.Cases[0].Nodes.OfType<LetNode>().First();
            var firstTxt = sw.Cases[0].Nodes.OfType<TextNode>().First();
            var canonical = firstLet.Var;
            var inlineStyle = GetSingleVarInlineStyle(firstTxt, canonical);

            // Normalize all cases: rename lets to canonical, strip text nodes.
            foreach (var c in sw.Cases)
            {
                foreach (var let in c.Nodes.OfType<LetNode>())
                {
                    let.Var = canonical;
                }

                c.Nodes.RemoveAll(n => n is TextNode);
            }

            // Pop any preceding text nodes from result (they'll be absorbed into the merged text).
            var preRuns = new List<TextRun>();
            while (result.Count > 0 && result[^1] is TextNode preT)
            {
                result.RemoveAt(result.Count - 1);
                if (preT.Template is not null)
                {
                    preRuns.Insert(0, new TextRun { Text = preT.Template, Style = preT.Style });
                }
                else
                {
                    preRuns.InsertRange(0, preT.Runs);
                }
            }

            // Consume following text nodes.
            i++;
            var postRuns = new List<TextRun>();
            while (i < nodes.Count && nodes[i] is TextNode postT)
            {
                i++;
                if (postT.Template is not null)
                {
                    postRuns.Add(new TextRun { Text = postT.Template, Style = postT.Style });
                }
                else
                {
                    postRuns.AddRange(postT.Runs);
                }
            }

            // Emit: hoisted switch (now all-LetNode cases), then merged text with {canonical} inlined.
            result.Add(sw);
            var allRuns = new List<TextRun>(preRuns)
            {
                new() { Text = $"{{{canonical}}}", Style = inlineStyle },
            };
            allRuns.AddRange(postRuns);
            if (allRuns.Count > 0)
            {
                var dominant = ComputeDominantStyle(allRuns);
                var template = BuildTemplate(allRuns, dominant);
                template = template.Replace("****", "").Replace("__", "");
                result.Add(new TextNode
                {
                    Template = template,
                    Style = dominant,
                    Lets = [canonical],
                });
            }
        }
        return result;
    }

    private static bool IsHoistableSwitchLets(SwitchNode sw)
    {
        if (sw.Cases.Count < 2)
        {
            return false;
        }

        foreach (var c in sw.Cases)
        {
            if (c.Nodes.Count != 2)
            {
                return false;
            }

            var let = c.Nodes.OfType<LetNode>().FirstOrDefault();
            var txt = c.Nodes.OfType<TextNode>().FirstOrDefault();
            if (let is null || (let.Compute is null && let.Random is null))
            {
                return false;
            }

            if (txt is null)
            {
                return false;
            }

            if (!IsSingleVarTemplate(txt, let.Var))
            {
                return false;
            }
        }
        return true;
    }

    // Returns true when the text node's content is exactly {varName}, **{varName}**, or _{varName}_.
    private static bool IsSingleVarTemplate(TextNode txt, string varName)
    {
        var expected = $"{{{varName}}}";
        if (txt.Template is not null)
        {
            return txt.Template == expected
                || txt.Template == $"**{expected}**"
                || txt.Template == $"_{expected}_";
        }

        if (txt.Runs is { Count: 1 })
        {
            var r = txt.Runs[0];
            return r.AssetRef is null && (r.Text == expected
                || r.Text == $"**{expected}**"
                || r.Text == $"_{expected}_");
        }
        return false;
    }

    // Returns the inline style implied by the single-var template (bold, italic, or null).
    private static string? GetSingleVarInlineStyle(TextNode txt, string varName)
    {
        var tmpl = txt.Template ?? txt.Runs?.FirstOrDefault()?.Text;
        if (tmpl == $"**{{{varName}}}**")
        {
            return "bold";
        }

        if (tmpl == $"_{{{varName}}}_")
        {
            return "italic";
        }

        return null;
    }

    // If a TextNode's content is exactly {var}, **{var}**, or _{var}_, returns the var name.
    private static string? TryExtractSingleVarRef(TextNode txt)
    {
        var tmpl = txt.Template ?? txt.Runs?.FirstOrDefault()?.Text;
        if (tmpl is null)
        {
            return null;
        }

        string inner;
        if (tmpl.StartsWith("**{") && tmpl.EndsWith("}**"))
        {
            inner = tmpl[3..^3];
        }
        else if (tmpl.StartsWith("_{") && tmpl.EndsWith("}_"))
        {
            inner = tmpl[2..^2];
        }
        else if (tmpl.StartsWith("{") && tmpl.EndsWith("}"))
        {
            inner = tmpl[1..^1];
        }
        else
        {
            return null;
        }

        return inner.Length > 0 && !inner.Contains('{') && !inner.Contains('}') ? inner : null;
    }

    // True when all non-default cases have exactly [TextNode(**{varRef}**)] and the optional
    // default has exactly [GotoNode]. Requires at least 2 non-default cases.
    private static bool IsHoistableVarNameSwitch(SwitchNode sw)
    {
        var matchCases = sw.Cases.Where(c => c.Default != true).ToList();
        if (matchCases.Count < 2)
        {
            return false;
        }

        foreach (var c in matchCases)
        {
            if (c.Nodes.Count != 1 || c.Nodes[0] is not TextNode txt)
            {
                return false;
            }

            if (TryExtractSingleVarRef(txt) is null)
            {
                return false;
            }
        }
        var defaultCase = sw.Cases.FirstOrDefault(c => c.Default == true);
        if (defaultCase is not null)
        {
            if (defaultCase.Nodes.Count != 1 || defaultCase.Nodes[0] is not GotoNode)
            {
                return false;
            }
        }
        return true;
    }

    private static HashSet<string> GetAllModifiedVars(EffectNode e)
    {
        var vars = new HashSet<string>(StringComparer.Ordinal);
        if (e.VarSets is not null)
        {
            vars.UnionWith(e.VarSets.Keys);
        }

        if (e.VarMath is not null)
        {
            vars.UnionWith(e.VarMath.Keys);
        }

        if (e.VarRandom is not null)
        {
            vars.UnionWith(e.VarRandom.Keys);
        }

        if (e.VarPush is not null)
        {
            vars.UnionWith(e.VarPush.Keys);
        }

        if (e.VarPop is not null)
        {
            vars.Add(e.VarPop);
        }

        if (e.VarSort is not null)
        {
            vars.UnionWith(e.VarSort.Keys);
        }

        if (e.VarRemove is not null)
        {
            vars.UnionWith(e.VarRemove.Keys);
        }

        return vars;
    }

    // Scans nodes recursively for _rnd_{safeName}_N let vars/effects and returns next safe seq.
    private static int FindNextRndSeq(List<MwsNode> nodes, string safeName)
    {
        var prefix = $"_rnd_{safeName}_";
        int max = -1;
        ScanRndVarSeqs(nodes, prefix, ref max);
        return max + 1;
    }

    private static void ScanRndVarSeqs(List<MwsNode> nodes, string prefix, ref int max)
    {
        foreach (var node in nodes)
        {
            if (node is LetNode let && let.Var.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(let.Var[prefix.Length..], out var n))
            {
                max = Math.Max(max, n);
            }
            else if (node is EffectNode eff && eff.VarRandom is not null)
            {
                foreach (var key in eff.VarRandom.Keys)
                {
                    if (key.StartsWith(prefix, StringComparison.Ordinal)
                        && int.TryParse(key[prefix.Length..], out var n2))
                    {
                        max = Math.Max(max, n2);
                    }
                }
            }
            IEnumerable<MwsNode> children = node switch
            {
                SwitchNode sw2 => sw2.Cases.SelectMany(c => c.Nodes),
                ConditionalNode cond => cond.Branches.SelectMany(b => b.Nodes),
                SectionBodyNode sec => sec.Nodes,
                SetupBlockNode setup => setup.Nodes,
                ForeachNode fe => fe.Nodes,
                ExpandLinkNode exp => exp.ExpandNodes,
                LinkNode link => link.Nodes,
                _ => [],
            };
            ScanRndVarSeqs(children.ToList(), prefix, ref max);
        }
    }

    // Detects [TextNode(A), EffectNode(→V), SwitchNode(IsHoistableVarNameSwitch)] where A
    // doesn't reference V. Reorders to [EffectNode, SwitchNode(cases→LetNodes), merged TextNode].
    private static List<MwsNode> HoistAssignAndSwitchPlayerNames(
        List<MwsNode> nodes, string safeName, ref int rndSeq)
    {
        if (!nodes.Any(n => n is SwitchNode sw && IsHoistableVarNameSwitch(sw)))
        {
            return nodes;
        }

        var result = new List<MwsNode>(nodes.Count + 1);
        int i = 0;
        while (i < nodes.Count)
        {
            if (i + 2 < nodes.Count
                && nodes[i] is TextNode preTxt
                && nodes[i + 1] is EffectNode effect
                && nodes[i + 2] is SwitchNode sw
                && IsHoistableVarNameSwitch(sw)
                && !TextNodeReferencesAny(preTxt, GetAllModifiedVars(effect)))
            {
                var canonical = $"_rnd_{safeName}_{rndSeq++}";

                // Capture inline style from first case before mutating.
                // Prefer inline marker style (**{var}**), fall back to node-level Style field.
                var firstMatchCase = sw.Cases.First(c => c.Default != true);
                var firstCaseTxt = (TextNode)firstMatchCase.Nodes[0];
                var inlineStyle = GetSingleVarInlineStyle(firstCaseTxt, TryExtractSingleVarRef(firstCaseTxt)!)
                    ?? firstCaseTxt.Style;

                // Replace TextNode in each non-default case with LetNode(canonical, varRef)
                foreach (var c in sw.Cases.Where(c => c.Default != true))
                {
                    var caseTxt = (TextNode)c.Nodes[0];
                    c.Nodes[0] = new LetNode
                    {
                        Var = canonical,
                        Compute = TryExtractSingleVarRef(caseTxt)!,
                        SourceLine = caseTxt.SourceLine,
                    };
                }

                // Build merged template: pre-text runs + {canonical}.
                // The pre-text naturally ends with a trailing space (from the Cradle source
                // having separate text() calls for the prefix and the variable), so no leading
                // space is needed on the canonical run.
                var preRuns = preTxt.Template is not null
                    ? [new TextRun { Text = preTxt.Template, Style = preTxt.Style }]
                    : preTxt.Runs.ToList();
                var allRuns = new List<TextRun>(preRuns)
                {
                    new() { Text = $"{{{canonical}}}", Style = inlineStyle },
                };
                var dominant = ComputeDominantStyle(allRuns);
                var template = BuildTemplate(allRuns, dominant);
                template = template.Replace("****", "").Replace("__", "");

                result.Add(effect);
                result.Add(sw);
                result.Add(new TextNode
                {
                    Template = template,
                    Style = dominant,
                    Lets = [canonical],
                    SourceLine = preTxt.SourceLine,
                });
                i += 3;
                continue;
            }
            result.Add(nodes[i++]);
        }
        return result;
    }

    // Pure-text TextNodes always start or extend a group.
    // Icon-only TextNodes only extend an existing group (never start one).
    // _rnd_*-only EffectNodes, direct LetNodes, and promotable ConditionalNodes only extend an existing group.
    private static bool CanJoinGroup(MwsNode node, List<MwsNode> group)
    {
        if (node is TextNode t)
        {
            if (t.Template is not null)
            {
                return true;
            }

            if (t.Runs.All(r => r.AssetRef is null))
            {
                return true;
            }

            return group.Count > 0; // icon-only: only extends existing group
        }
        if (group.Count == 0)
        {
            return false;
        }

        if (node is LetNode)
        {
            return true;
        }

        return IsRndOnlyEffect(node) || IsPromotableConditional(node);
    }

    private static bool IsPureAssignEffect(MwsNode node) =>
        node is EffectNode e
        && e.VarSets is { Count: > 0 }
        && e.VarMath is null or { Count: 0 }
        && e.VarRandom is null or { Count: 0 }
        && e.VarPush is null or { Count: 0 }
        && e.VarPop is null
        && e.VarSort is null or { Count: 0 }
        && e.VarRemove is null or { Count: 0 };

    private static bool TextNodeReferencesAny(TextNode t, IEnumerable<string> varNames)
    {
        foreach (var varName in varNames)
        {
            var token = $"{{{varName}}}";
            if (t.Template?.Contains(token) == true)
            {
                return true;
            }

            if (t.Runs is not null && t.Runs.Any(r => r.Text?.Contains(token) == true))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsRndOnlyEffect(MwsNode node)
    {
        if (node is not EffectNode e)
        {
            return false;
        }

        if (e.VarSets is { Count: > 0 })
        {
            return false;
        }

        if (e.VarMath is { Count: > 0 })
        {
            return false;
        }

        if (e.VarRandom is null || e.VarRandom.Count == 0)
        {
            return false;
        }

        return e.VarRandom.Keys.All(k => k.StartsWith("_rnd_"));
    }

    private static bool IsPromotableConditional(MwsNode node)
    {
        if (node is not ConditionalNode cond)
        {
            return false;
        }
        // Vacuously-empty branches are not promotable (they have no effect to move)
        if (cond.Branches.All(b => b.Nodes.Count == 0))
        {
            return false;
        }

        return cond.Branches.All(b => b.Nodes.All(n => n is EffectNode or LetNode));
    }

    private static MwsNode RecurseContainers(MwsNode node)
    {
        switch (node)
        {
            case ConditionalNode cond:
                foreach (var b in cond.Branches)
                {
                    b.Nodes = ConsolidateTextNodes(b.Nodes);
                }

                break;
            case SwitchNode sw:
                foreach (var c in sw.Cases)
                {
                    c.Nodes = ConsolidateTextNodes(c.Nodes);
                }

                break;
            case SectionBodyNode section:
                section.Nodes = ConsolidateTextNodes(section.Nodes);
                break;
            case SetupBlockNode setup:
                setup.Nodes = ConsolidateTextNodes(setup.Nodes);
                break;
            case LinkNode link when link.Nodes.Count > 0:
                link.Nodes = ConsolidateTextNodes(link.Nodes);
                break;
            case ExpandLinkNode expand:
                expand.ExpandNodes = ConsolidateTextNodes(expand.ExpandNodes);
                break;
            case ForeachNode fe:
                fe.Nodes = ConsolidateTextNodes(fe.Nodes);
                break;
        }
        return node;
    }

    private static List<MwsNode> ConsolidateBreaks(List<MwsNode> nodes)
    {
        var result = new List<MwsNode>();
        int i = 0;
        while (i < nodes.Count)
        {
            if (nodes[i] is BreakNode firstBreak)
            {
                var sourceLine = firstBreak.SourceLine;
                int count = 1;
                i++;
                while (i < nodes.Count && nodes[i] is BreakNode) { count++; i++; }
                result.Add(count >= 2
                    ? new ParagraphBreakNode { SourceLine = sourceLine }
                    : new BreakNode { SourceLine = sourceLine });
            }
            else
            {
                result.Add(nodes[i++]);
            }
        }
        return result;
    }

    // ── Switch consolidation ──────────────────────────────────────────────────
    // Collapses 2+ consecutive ConditionalNodes that all test the same variable
    // (with simple "var op value" conditions) into a single SwitchNode.
    // An else branch on the final conditional becomes the default case.

    private static List<MwsNode> ConsolidateSwitches(List<MwsNode> nodes)
    {
        var result = new List<MwsNode>();
        int i = 0;
        while (i < nodes.Count)
        {
            if (nodes[i] is not ConditionalNode firstCond)
            {
                result.Add(nodes[i++]);
                continue;
            }

            // Try consecutive simple-condition switch (e.g. "var == value" across multiple ConditionalNodes).
            if (TryExtractSwitchVar(firstCond) is { } switchVar)
            {
                var run = new List<ConditionalNode> { firstCond };
                while (i + run.Count < nodes.Count &&
                       !HasElseBranch(run[^1]) &&
                       nodes[i + run.Count] is ConditionalNode next &&
                       TryExtractSwitchVar(next) == switchVar)
                {
                    run.Add(next);
                }

                if (run.Count >= 2)
                {
                    result.Add(BuildSwitchNode(switchVar, run));
                    i += run.Count;
                    continue;
                }
            }

            // Try a single ConditionalNode whose branches use compound "var == a || var == b" conditions.
            if (TryConvertCompoundConditionalToSwitch(firstCond) is { } sw)
            {
                result.Add(sw);
                i++;
                continue;
            }

            result.Add(nodes[i++]);
        }
        return result;
    }

    // Converts a single ConditionalNode into a SwitchNode when all branch conditions use
    // simple equality ("var == a" or compound "var == a || var == b") on the same variable.
    // Only equality is accepted: comparison operators (>, <, >=, <=) are not safe to
    // auto-convert because the engine's switch semantics may differ from if-else ordering.
    // Single-condition branches (no ||) require 3+ total branches to avoid converting
    // a plain if/else into a switch.
    private static SwitchNode? TryConvertCompoundConditionalToSwitch(ConditionalNode cond)
    {
        if (cond.Branches.Count < 2)
        {
            return null;
        }

        bool allowSimpleConditions = cond.Branches.Count >= 3;
        string? switchVar = null;
        var cases = new List<SwitchCase>();

        foreach (var branch in cond.Branches)
        {
            if (branch.Else == true)
            {
                cases.Add(new SwitchCase { Default = true, Nodes = branch.Nodes });
                continue;
            }
            if (branch.Condition is null)
            {
                return null;
            }

            var parts = branch.Condition.Split("||", StringSplitOptions.TrimEntries);
            if (parts.Length < 2 && !allowSimpleConditions)
            {
                return null;
            }

            var matchValues = new List<object>();
            foreach (var part in parts)
            {
                var m = SwitchCondRegex().Match(part);
                if (!m.Success)
                {
                    return null;
                }

                if (m.Groups[2].Value != "==")
                {
                    return null;
                }

                var varName = m.Groups[1].Value;
                var rawVal = m.Groups[3].Value.Trim();
                if (rawVal.Contains(' '))
                {
                    return null;
                }

                switchVar ??= varName;
                if (varName != switchVar)
                {
                    return null;
                }

                matchValues.Add(BuildMatchValue("==", rawVal));
            }
            var matchObj = matchValues.Count == 1 ? matchValues[0] : (object)matchValues;
            cases.Add(new SwitchCase { Match = matchObj, Nodes = branch.Nodes });
        }

        if (switchVar is null)
        {
            return null;
        }

        return new SwitchNode { On = switchVar, Cases = cases, SourceLine = cond.SourceLine };
    }

    // Returns the switch variable name if the conditional has exactly one "if" branch
    // (plus optional else) whose condition is a simple equality ("varName == value").
    // Only equality is accepted: comparison operators (>, <, >=, <=) may not be mutually
    // exclusive across consecutive if-blocks, so collapsing them to switch is unsafe.
    private static string? TryExtractSwitchVar(ConditionalNode cond)
    {
        if (cond.Branches.Count == 0 || cond.Branches.Count > 2)
        {
            return null;
        }

        var first = cond.Branches[0];
        if (first.Condition is null || first.Else == true)
        {
            return null;
        }

        if (cond.Branches.Count == 2 && cond.Branches[1].Else != true)
        {
            return null;
        }

        var m = SwitchCondRegex().Match(first.Condition);
        if (!m.Success)
        {
            return null;
        }

        if (m.Groups[2].Value != "==")
        {
            return null;
        }

        // Reject compound values like "2 || x == 3"
        var rawVal = m.Groups[3].Value.Trim();
        bool isQuoted = rawVal.StartsWith('"') && rawVal.EndsWith('"');
        if (!isQuoted && rawVal.Contains(' '))
        {
            return null;
        }

        return m.Groups[1].Value;
    }

    private static bool HasElseBranch(ConditionalNode cond) =>
        cond.Branches.Count > 0 && cond.Branches[^1].Else == true;

    private static SwitchNode BuildSwitchNode(string varName, List<ConditionalNode> run)
    {
        var cases = new List<SwitchCase>();
        for (int k = 0; k < run.Count; k++)
        {
            var cond = run[k];
            var first = cond.Branches[0];
            var m = SwitchCondRegex().Match(first.Condition!);
            cases.Add(new SwitchCase
            {
                Match = BuildMatchValue(m.Groups[2].Value, m.Groups[3].Value.Trim()),
                Nodes = first.Nodes,
            });
            if (k == run.Count - 1 && HasElseBranch(cond))
            {
                cases.Add(new SwitchCase { Default = true, Nodes = cond.Branches[^1].Nodes });
            }
        }
        return new SwitchNode { On = varName, Cases = cases, SourceLine = run[0].SourceLine };
    }

    private static object BuildMatchValue(string op, string rawVal)
    {
        if (op == "==")
        {
            if (rawVal.StartsWith('"') && rawVal.EndsWith('"'))
            {
                return rawVal[1..^1];
            }

            if (int.TryParse(rawVal, out var n))
            {
                return n;
            }

            return rawVal;
        }
        return $"{op}{rawVal}";
    }

    // ── VarRandom normalization ───────────────────────────────────────────────
    // Converts choose-one with a contiguous all-integer list to rand-between.
    // Recurses into all container types including SwitchNode.

    private static void NormalizeAllVarRandoms(List<MwsNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case EffectNode e when e.VarRandom is not null:
                    foreach (var key in e.VarRandom.Keys.ToList())
                    {
                        e.VarRandom[key] = NormalizeVarRandom(e.VarRandom[key]);
                    }

                    break;
                case LetNode let when let.Random is not null:
                    let.Random = NormalizeVarRandom(let.Random);
                    break;
                case ConditionalNode cond:
                    foreach (var b in cond.Branches)
                    {
                        NormalizeAllVarRandoms(b.Nodes);
                    }

                    break;
                case SwitchNode sw:
                    foreach (var c in sw.Cases)
                    {
                        NormalizeAllVarRandoms(c.Nodes);
                    }

                    break;
                case SectionBodyNode section:
                    NormalizeAllVarRandoms(section.Nodes);
                    break;
                case SetupBlockNode setup:
                    NormalizeAllVarRandoms(setup.Nodes);
                    break;
                case LinkNode link when link.Nodes.Count > 0:
                    NormalizeAllVarRandoms(link.Nodes);
                    break;
                case ExpandLinkNode expand:
                    NormalizeAllVarRandoms(expand.ExpandNodes);
                    break;
                case ForeachNode fe:
                    NormalizeAllVarRandoms(fe.Nodes);
                    break;
            }
        }
    }

    private static VarRandom NormalizeVarRandom(VarRandom vr)
    {
        if (vr.RandomType != "choose-one" || vr.Values.Count < 2)
        {
            return vr;
        }

        if (!IsContiguousIntegerList(vr.Values, out var min, out var max))
        {
            return vr;
        }

        return new VarRandom { RandomType = "rand-between", Min = min, Max = max };
    }

    private static bool IsContiguousIntegerList(List<object> values, out int min, out int max)
    {
        min = max = 0;
        var ints = new List<int>(values.Count);
        foreach (var v in values)
        {
            if (v is int i)
            {
                ints.Add(i);
            }
            else if (v is long l)
            {
                ints.Add((int)l);
            }
            else
            {
                return false;
            }
        }
        if (ints.Count < 2)
        {
            return false;
        }

        ints.Sort();
        min = ints[0]; max = ints[^1];
        for (int k = 1; k < ints.Count; k++)
        {
            if (ints[k] != ints[k - 1] + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static string? ComputeDominantStyle(List<TextRun> runs)
    {
        var significant = runs.Where(r => r.Text?.Trim().Length > 0).ToList();
        if (significant.Count == 0)
        {
            return null;
        }

        var first = significant[0].Style;
        return significant.All(r => r.Style == first) ? first : null;
    }

    // Merges consecutive runs sharing the same *effective* style into one buffer before wrapping —
    // same shape as MwsExprHelper.BuildValueFromRuns, plus the dominant-style skip (a run whose
    // style matches `dominantStyle` is left unwrapped here, since that style is instead hoisted
    // onto the merged TextNode's own Style field — see ComputeDominantStyle/UniformBoldScope test).
    private static string BuildTemplate(IEnumerable<TextRun> runs, string? dominantStyle)
    {
        var sb = new StringBuilder();
        string? currentStyle = null;
        var buffer = new StringBuilder();

        void FlushBuffer()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            var text = buffer.ToString();
            buffer.Clear();
            sb.Append(currentStyle switch
            {
                "bold" => MwsExprHelper.WrapEmphasis(text, "**"),
                "italic" => MwsExprHelper.WrapEmphasis(text, "_"),
                _ => text,
            });
        }

        foreach (var run in runs)
        {
            if (run.AssetRef is not null)
            {
                FlushBuffer();
                currentStyle = null;
                var slug = run.AssetRef.StartsWith("icon://") ? run.AssetRef["icon://".Length..] : run.AssetRef;
                sb.Append($"{{icon:{slug}}}");
                continue;
            }
            if (run.Text is null)
            {
                continue;
            }

            // Dominant style is already expressed at the node level — don't repeat it inline.
            var effective = run.Style == dominantStyle ? null : run.Style;
            if (effective != currentStyle)
            {
                FlushBuffer();
                currentStyle = effective;
            }

            buffer.Append(run.Text);
        }

        FlushBuffer();
        return MwsExprHelper.CollapseAdjacentSpaces(sb.ToString());
    }

    public Dictionary<string, VarDef> GetDiscoveredVariables() => _variables;

    // Returns the source ready for Roslyn and whether it was already a complete file.
    // Complete files (Cradle 2.0.2.0+) include class declaration and VarDefs — parse as-is.
    // Older partial files (method bodies only) are wrapped in a synthetic class declaration.
    private static (string source, bool isComplete) PrepareSource(string content)
    {
        if (content.Contains("public partial class @") || content.Contains("\npublic partial class "))
        {
            return (content, true);
        }

        return (WrapPartialClass(content), false);
    }

    private static string WrapPartialClass(string content) =>
        "using System; using System.Collections.Generic;\n" +
        "public partial class CradleStory {\n" +
        content +
        "\n}";
}
