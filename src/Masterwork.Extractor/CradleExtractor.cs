using System.Text;
using System.Diagnostics.CodeAnalysis;
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
    private readonly ProgressMapper _progressMapper;
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

    public CradleExtractor(ExtractionOptions opts, SpriteMapper spriteMapper, ExtractionReport report,
        ProgressMapper? progressMapper = null)
    {
        _opts = opts;
        _spriteMapper = spriteMapper;
        _report = report;
        _progressMapper = progressMapper ?? ProgressMapper.Empty();
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
                case SetupNotificationNode sn when sn.Random is not null:
                    sn.Random.SeedKey ??= $"{passageId}_{counter++}";
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

    // Vars.X names that are real `Vars.X` accesses in the Cradle source but never surface as an
    // actual variable reference anywhere in the EXTRACTED output — PassageBodyVisitor.ProcessAssignment
    // fully absorbs each one into a different node type at extraction time (see its own remarks), so
    // an engine-tracked session variable for it would never be read OR written by anything the player
    // actually plays through. `_SetupImage` is the one occurrence: always converted to a popup
    // header ImageNode, never appears in a `{_SetupImage}` template or an `if:` condition.
    private static readonly HashSet<string> ExtractorOnlySignalVars = new(StringComparer.Ordinal)
    {
        "_SetupImage",
    };

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
                        if (string.IsNullOrEmpty(varName) || ExtractorOnlySignalVars.Contains(varName))
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
                if (string.IsNullOrEmpty(varName) || varName == "Vars" || ExtractorOnlySignalVars.Contains(varName))
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
        // Two passes: the first builds every passage's own node tree (visit, stitch, consolidate)
        // and, along the way, collects every OTHER passage name ever referenced as an
        // include_passage target anywhere in the file — needed BEFORE the second pass decides
        // whether a GIVEN passage is safe to title-hoist, since a passage can be include_passage'd
        // from one processed either earlier or later in _registry's own iteration order.
        var built = new List<(int Idx, string Name, string[] Tags, string SourceFile, int? MainMethodLine, List<MwsNode> Nodes)>();
        var includePassageTargets = new HashSet<string>(StringComparer.Ordinal);
        var dynamicTargetVars = new HashSet<string>(StringComparer.Ordinal);
        var literalVarAssigns = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

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

            var visitor = new PassageBodyVisitor(name, _spriteMapper, _report, _variables, isCompleteFile, _progressMapper);
            var nodes = mainMethod.Body is not null
                ? visitor.VisitBlock(mainMethod.Body)
                : [];

            // Stitch fragment methods into expand_link nodes.
            // Pass the full _fragmentMethods table so cross-passage fragments can be resolved
            // (e.g. passage35_Fragment_3 called from passage32 — Cradle counter artifact).
            var localFrags = _fragmentMethods.TryGetValue(idx, out var lf) ? lf : [];
            StitchFragments(name, nodes, localFrags, _fragmentMethods, _spriteMapper, _report, _variables, _progressMapper);

            // Consolidate text, breaks, switches; then normalize VarRandom types
            nodes = ConsolidateTextNodes(nodes);

            CollectIncludePassageTargets(nodes, includePassageTargets, dynamicTargetVars, literalVarAssigns);

            built.Add((idx, name, tags, sourceFile, mainMethodLine >= 1 ? mainMethodLine : null, nodes));
        }

        // Resolve dynamic (`${varname}`) include_passage targets transitively: a variable that's
        // ever used as such a target AND is ever assigned a literal string matching a real passage
        // name means that passage is an include target too (see CollectIncludePassageTargets' own
        // remarks for the real occurrence this covers).
        var allPassageNames = new HashSet<string>(built.Select(b => b.Name), StringComparer.Ordinal);
        foreach (var varName in dynamicTargetVars)
        {
            if (!literalVarAssigns.TryGetValue(varName, out var literals))
            {
                continue;
            }

            foreach (var literal in literals)
            {
                if (allPassageNames.Contains(literal))
                {
                    includePassageTargets.Add(literal);
                }
            }
        }

        var passages = new List<MwsPassage>();
        foreach (var (idx, name, tags, sourceFile, mainMethodLine, builtNodes) in built)
        {
            var nodes = builtNodes;

            // Heading eligibility is decided from the tag-based category (hub/narration/introduction)
            // even when --progress-map overrides the final layout value to something more specific
            // (e.g. "hub_early") — the override changes which chrome/CSS applies, not whether this is
            // fundamentally a hub-family passage with a leading title block to hoist.
            var inferredLayout = InferLayout(tags);
            var layout = _progressMapper.TryGetLayoutOverride(name) ?? inferredLayout;

            // A passage that's ever the target of an include_passage node has its OWN nodes spliced
            // verbatim into the including passage's body at render time — it never renders its own
            // title, and title-hoisting here would silently delete content the includER still
            // needs (a node consumed into THIS passage's title is a node the includER never sees).
            // Real occurrence: Fear of the Unknown's letter1a/journal1a/date1a, each reused via
            // include_passage across several "randomized in-world document" passages.
            var (headingTitle, headingSubtitle, nodesAfterHeading) = includePassageTargets.Contains(name)
                ? (null, null, nodes)
                : TryHoistHeadingTitleSubtitle(nodes, inferredLayout);
            if (headingTitle is not null)
            {
                nodes = nodesAfterHeading;
            }

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
                Title = headingTitle ?? name,
                Subtitle = headingSubtitle,
                Tags = tags,
                Layout = layout,
                Nodes = nodes,
                Debug = isDebug,
                SourceFile = sourceFile,
                MainMethodSourceLine = mainMethodLine,
                IsIncludeTarget = includePassageTargets.Contains(name),
            });
        }

        return passages;
    }

    // Recursively collects every IncludePassageNode.Target found anywhere in `nodes` into `targets`
    // — used by BuildPassages' first pass to know, before its second pass decides whether ANY given
    // passage is safe to title-hoist, which passage names are ever spliced verbatim into another
    // passage's own body via include_passage. Mirrors the same container-node case list used
    // elsewhere for this kind of whole-tree walk (e.g. Program.cs's own CollectFromNodes, BreakFilter's
    // RecurseContainers) — every node type that can hold child nodes at this stage of the pipeline.
    //
    // A dynamic (`${varname}`-shaped, bare-identifier) target isn't a literal passage name, so it
    // can't be added to `targets` directly — instead its variable name is recorded into
    // `dynamicTargetVars`. BuildPassages resolves those afterward by cross-referencing
    // `literalVarAssigns` (also collected here, from every plain `Vars.X = "SomeLiteral";` assign
    // anywhere in the file — EffectNode.VarSets at this pre-serialization stage still holds the raw
    // .NET string, not yet MWS-expr-formatted) against every actual passage name: a variable that's
    // EVER used as a dynamic include_passage target AND is EVER assigned a literal string matching a
    // real passage name means that passage is, transitively, an include target too, even though no
    // single include_passage node names it directly. Real occurrence: Fear of the Unknown's
    // AsylumHub sets `quest1 = "CountQuestion4"` (a plain string literal assign); AsylumTest1
    // later does `include_passage: target: '${quest1}'` — two different passages, connected only
    // through the shared global `quest1` variable, so neither passage's own node tree alone reveals
    // that CountQuestion4 is an include target. Without this, CountQuestion4's leading bold text
    // ("Are you mentally ill?") was free to be hoisted into its own `title:` field — never spliced by
    // include_passage (which only ever copies `Nodes`, see PassageRenderer's IncludePassageNode case)
    // — silently deleting the question's own text from every render that included it.
    //
    // A more complex dynamic target (a ternary, a property access, etc.) isn't a bare identifier and
    // is simply skipped here — same as a fully-unresolvable target always was; this only ever ADDS
    // protection against title-hoisting, so an unrecognized shape just falls back to the prior,
    // narrower behavior rather than risking a false positive.
    private static void CollectIncludePassageTargets(
        List<MwsNode> nodes,
        HashSet<string> targets,
        HashSet<string> dynamicTargetVars,
        Dictionary<string, HashSet<string>> literalVarAssigns)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case IncludePassageNode inc when !inc.Target.StartsWith("${", StringComparison.Ordinal):
                    targets.Add(inc.Target);
                    break;
                case IncludePassageNode { Target: var t } when DynamicTargetVarPattern().Match(t) is { Success: true } m:
                    dynamicTargetVars.Add(m.Groups[1].Value);
                    break;
                case EffectNode effect:
                    if (effect.VarSets is not null)
                    {
                        foreach (var (varName, val) in effect.VarSets)
                        {
                            if (val is string literal)
                            {
                                (literalVarAssigns.TryGetValue(varName, out var set) ? set : literalVarAssigns[varName] = []).Add(literal);
                            }
                        }
                    }

                    // A "choose-one" VarRandom (arr.shuffled(key)[0]) resolves to exactly one of its
                    // own literal Values at runtime — same protection need as a plain literal assign,
                    // just picked randomly among several candidates instead of fixed. Real occurrence:
                    // Cost of Disease's HuntNorth/HuntWest/HuntEast/HuntSouth each set their own
                    // nextPsg var to `["Wight", "Moon Presence"].shuffled(key)[0]`, then include_passage
                    // it dynamically - every one of "Wight"/"Moon Presence"/etc. needs the same
                    // protection a single-literal assign would get.
                    // A "choose-one" VarRandom (arr.shuffled(key)[0]) resolves to exactly one of its
                    // own literal Values at runtime — same protection need as a plain literal assign,
                    // just picked randomly among several candidates instead of fixed. Real occurrence:
                    // Cost of Disease's HuntNorth/HuntWest/HuntEast/HuntSouth each set their own
                    // nextPsg var to `["Wight", "Moon Presence"].shuffled(key)[0]`, then include_passage
                    // it dynamically - every one of "Wight"/"Moon Presence"/etc. needs the same
                    // protection a single-literal assign would get.
                    if (effect.VarRandom is not null)
                    {
                        foreach (var (varName, vr) in effect.VarRandom)
                        {
                            foreach (var val in vr.Values)
                            {
                                if (val is string literal)
                                {
                                    (literalVarAssigns.TryGetValue(varName, out var set) ? set : literalVarAssigns[varName] = []).Add(literal);
                                }
                            }
                        }
                    }

                    break;
                case ConditionalNode cond:
                    foreach (var b in cond.Branches)
                    {
                        CollectIncludePassageTargets(b.Nodes, targets, dynamicTargetVars, literalVarAssigns);
                    }

                    break;
                case SwitchNode sw:
                    foreach (var c in sw.Cases)
                    {
                        CollectIncludePassageTargets(c.Nodes, targets, dynamicTargetVars, literalVarAssigns);
                    }

                    break;
                case SectionBodyNode sec:
                    CollectIncludePassageTargets(sec.Nodes, targets, dynamicTargetVars, literalVarAssigns);
                    break;
                case SetupBlockNode setup:
                    CollectIncludePassageTargets(setup.Nodes, targets, dynamicTargetVars, literalVarAssigns);
                    break;
                case LinkNode link:
                    CollectIncludePassageTargets(link.Nodes, targets, dynamicTargetVars, literalVarAssigns);
                    break;
                case ExpandLinkNode expand:
                    CollectIncludePassageTargets(expand.ExpandNodes, targets, dynamicTargetVars, literalVarAssigns);
                    break;
                case ForeachNode fe:
                    CollectIncludePassageTargets(fe.Nodes, targets, dynamicTargetVars, literalVarAssigns);
                    break;
            }
        }
    }

    [GeneratedRegex(@"^\$\{([A-Za-z_][A-Za-z0-9_]*)\}$")]
    private static partial Regex DynamicTargetVarPattern();

    private static void StitchFragments(
        string passageName,
        List<MwsNode> nodes,
        Dictionary<int, MethodDeclarationSyntax> localFrags,
        Dictionary<int, Dictionary<int, MethodDeclarationSyntax>> allFrags,
        SpriteMapper spriteMapper,
        ExtractionReport report,
        Dictionary<string, VarDef>? variables = null,
        ProgressMapper? progressMapper = null)
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
                        var fragVisitor = new PassageBodyVisitor(passageName, spriteMapper, report, variables, fragIsComplete, progressMapper);
                        var fragNodes = fragMethod.Body is not null
                            ? fragVisitor.VisitBlock(fragMethod.Body)
                            : [];
                        expand.ExpandNodes.Clear();
                        expand.ExpandNodes.AddRange(fragNodes);
                        // Recurse into the stitched content — it may contain nested fragments
                        StitchFragments(passageName, expand.ExpandNodes, localFrags, allFrags, spriteMapper, report, variables, progressMapper);
                        // Navigation terminals: GotoNode or CheckProgressNode at the end
                        // → convert the expand-link to a plain navigation LinkNode.
                        // CheckProgress always records state, so force state_affecting = true
                        // regardless of the enchant command (None vs Replace).
                        string? termTarget = null;
                        bool termStateAffecting = expand.StateAffecting;
                        if (expand.ExpandNodes.Count > 0)
                        {
                            // Trailing decorative breaks (e.g. a stray yield return lineBreak();
                            // after an unconditional ChangeView/CheckProgress call — see Cost of
                            // Disease's Scoring passage) don't stop this from being a navigation
                            // terminal: nothing ever renders after an unconditional GotoNode/
                            // CheckProgressNode anyway. Trim them so the checks below see the
                            // real terminal node, once trimming would actually reveal one.
                            var lastRealIndex = expand.ExpandNodes.Count - 1;
                            while (lastRealIndex >= 0 && expand.ExpandNodes[lastRealIndex] is BreakNode or ParagraphBreakNode)
                            {
                                lastRealIndex--;
                            }
                            if (lastRealIndex >= 0 && lastRealIndex < expand.ExpandNodes.Count - 1 &&
                                expand.ExpandNodes[lastRealIndex] is GotoNode or CheckProgressNode)
                            {
                                expand.ExpandNodes.RemoveRange(lastRealIndex + 1, expand.ExpandNodes.Count - lastRealIndex - 1);
                            }

                            if (expand.ExpandNodes[^1] is GotoNode termGoto)
                            {
                                termTarget = termGoto.Target;
                            }
                            else if (expand.ExpandNodes[^1] is CheckProgressNode cpTerm &&
                                     !string.IsNullOrEmpty(cpTerm.TargetPassage))
                            {
                                // --progress-map: when this CheckProgress's source passage has curated
                                // end-of-round popup text, the reference app shows an acknowledgement
                                // popup here (PassageTracker.CheckProgress -> ViewEndOfRound.SetEndOfRound),
                                // not a silent click-through — leave `expand` as an ExpandLinkNode (swap
                                // in a marker node instead of collapsing to LinkNode below) so
                                // V2Serializer.TransformPopup renders it as a layout: end_of_round popup.
                                var (eorBody, eorBody2) = progressMapper?.TryGetEndOfRoundText(cpTerm.CurrentPassage)
                                    ?? (null, null);
                                if (eorBody is not null)
                                {
                                    progressMapper!.TryGetProgressValue(cpTerm.CurrentPassage, out var progressValue);
                                    expand.ExpandNodes.RemoveAt(expand.ExpandNodes.Count - 1);
                                    // The _ProgressRound assign PassageBodyVisitor prepended right before
                                    // the CheckProgressNode moves into the popup's onclose instead of
                                    // sitting in its content — drop it here, V2Serializer re-adds it.
                                    if (expand.ExpandNodes is [.., EffectNode { VarSets.Count: 1 } lastEffect] &&
                                        lastEffect.VarSets!.ContainsKey("_ProgressRound"))
                                    {
                                        expand.ExpandNodes.RemoveAt(expand.ExpandNodes.Count - 1);
                                    }

                                    // Anything still left (e.g. a guarded assignment computing a
                                    // dynamic target — see Liberal2/OncloseNodes' own doc comment)
                                    // must run as onclose, not passage-render-time content.
                                    var oncloseNodes = new List<MwsNode>(expand.ExpandNodes);
                                    expand.ExpandNodes.Clear();
                                    expand.ExpandNodes.Add(new EndOfRoundMarkerNode
                                    {
                                        NextPassage = cpTerm.TargetPassage,
                                        ProgressValue = progressValue ?? 0,
                                        Body = eorBody,
                                        Body2 = eorBody2,
                                        OncloseNodes = oncloseNodes,
                                    });
                                }
                                else
                                {
                                    termTarget = cpTerm.TargetPassage;
                                    termStateAffecting = true;
                                }
                            }
                            else if (expand.ExpandNodes[^1] is ConditionalNode condTerm &&
                                     TryCollapseCheckProgressConditional(condTerm, out var ternaryTarget, out var repCp))
                            {
                                // Same shape as the single-CheckProgressNode case above, but each branch
                                // routes to a different target passage (e.g. Vars.peeps == 1 ? "NoUni3b" :
                                // "Scoring") while every branch reports the SAME current passage — collapse
                                // to one checkpoint whose target is a ternary expression instead of a
                                // conditional wrapping duplicate progress-assign/checkpoint content (see
                                // TryCollapseCheckProgressConditional for the exhaustiveness/uniformity
                                // requirements this relies on).
                                var (eorBody, eorBody2) = progressMapper?.TryGetEndOfRoundText(repCp!.CurrentPassage)
                                    ?? (null, null);
                                if (eorBody is not null)
                                {
                                    progressMapper!.TryGetProgressValue(repCp!.CurrentPassage, out var progressValue);
                                    expand.ExpandNodes.RemoveAt(expand.ExpandNodes.Count - 1);
                                    var oncloseNodes = new List<MwsNode>(expand.ExpandNodes);
                                    expand.ExpandNodes.Clear();
                                    expand.ExpandNodes.Add(new EndOfRoundMarkerNode
                                    {
                                        NextPassage = ternaryTarget!,
                                        ProgressValue = progressValue ?? 0,
                                        Body = eorBody,
                                        Body2 = eorBody2,
                                        OncloseNodes = oncloseNodes,
                                    });
                                }
                                else
                                {
                                    termTarget = ternaryTarget;
                                    termStateAffecting = true;
                                }
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
                    StitchFragments(passageName, expand.ExpandNodes, localFrags, allFrags, spriteMapper, report, variables, progressMapper);
                }
            }
            // Recurse into container nodes
            else if (nodes[i] is ConditionalNode cond)
            {
                foreach (var branch in cond.Branches)
                {
                    StitchFragments(passageName, branch.Nodes, localFrags, allFrags, spriteMapper, report, variables, progressMapper);
                }
            }
            else if (nodes[i] is SwitchNode sw)
            {
                foreach (var c in sw.Cases)
                {
                    StitchFragments(passageName, c.Nodes, localFrags, allFrags, spriteMapper, report, variables, progressMapper);
                }
            }
            else if (nodes[i] is ForeachNode fe)
            {
                StitchFragments(passageName, fe.Nodes, localFrags, allFrags, spriteMapper, report, variables, progressMapper);
            }
            else if (nodes[i] is SectionBodyNode section)
            {
                StitchFragments(passageName, section.Nodes, localFrags, allFrags, spriteMapper, report, variables, progressMapper);
            }
            else if (nodes[i] is SetupBlockNode setup)
            {
                StitchFragments(passageName, setup.Nodes, localFrags, allFrags, spriteMapper, report, variables, progressMapper);
            }
        }
    }

    // Recognizes an if/elseif/.../else chain whose every branch does nothing but call
    // PassageTracker.instance.CheckProgress(current, target) for the SAME current passage — e.g.
    // Cost of Disease's NoUni3 hub: `if (peeps == 1) CheckProgress("NoUni3", "NoUni3b"); else
    // CheckProgress("NoUni3", "Scoring");`. Since the current-passage argument (and therefore the
    // progress value / end-of-round text) is identical across branches, the whole conditional
    // collapses to a single checkpoint whose target is a ternary expression over the branches'
    // individual target passages, rather than a conditional wrapping duplicate progress-assign/
    // checkpoint content per branch (which V2Serializer has no representation for — CheckProgressNode
    // is only ever consumed as a StitchFragments terminal, never serialized directly).
    // Requires: an exhaustive chain (has an else branch), every branch's Nodes ending in a
    // CheckProgressNode with a non-empty TargetPassage, all branches reporting the same
    // CurrentPassage, and nothing left in any branch besides that terminal CheckProgressNode and
    // its optional preceding `_ProgressRound` assign — anything else per-branch is a different,
    // unhandled shape and this bails out (false) rather than risk dropping real content.
    private static bool TryCollapseCheckProgressConditional(
        ConditionalNode cond, out string? targetExpr, out CheckProgressNode? representative)
    {
        targetExpr = null;
        representative = null;

        if (!cond.Branches.Any(b => b.Else == true))
        {
            return false;
        }

        var arms = new List<(string? Condition, string Target)>();
        string? commonCurrentPassage = null;

        foreach (var branch in cond.Branches)
        {
            if (branch.Nodes.Count == 0 || branch.Nodes[^1] is not CheckProgressNode cp ||
                string.IsNullOrEmpty(cp.TargetPassage) || cp.TargetPassage.StartsWith("${", StringComparison.Ordinal))
            {
                // The ternary this method builds quotes each branch's target as a passage-name
                // string literal — correct for a plain literal target, but wrong for a branch
                // whose own target is already a computed expression (nesting an unevaluated
                // "${...}" string inside the outer ternary's string literal). No known real
                // occurrence combines both patterns; bail rather than mis-serialize if one ever does.
                return false;
            }

            var bodyEnd = branch.Nodes.Count - 1;
            if (bodyEnd > 0 && branch.Nodes[bodyEnd - 1] is EffectNode { VarSets.Count: 1 } eff &&
                eff.VarSets!.ContainsKey("_ProgressRound"))
            {
                bodyEnd--;
            }
            if (bodyEnd > 0)
            {
                return false;
            }

            commonCurrentPassage ??= cp.CurrentPassage;
            if (!string.Equals(commonCurrentPassage, cp.CurrentPassage, StringComparison.Ordinal))
            {
                return false;
            }

            representative ??= cp;
            arms.Add((branch.Else == true ? null : branch.Condition, cp.TargetPassage));
        }

        // Ternary-building below assumes the else arm is last regardless of the source's own
        // branch order (ConditionalBranch doesn't guarantee it).
        var elseIdx = arms.FindIndex(a => a.Condition is null);
        if (elseIdx != arms.Count - 1)
        {
            var elseArm = arms[elseIdx];
            arms.RemoveAt(elseIdx);
            arms.Add(elseArm);
        }

        targetExpr = "${" + BuildTernaryChain(arms) + "}";
        return true;
    }

    // internal: also used by V2Serializer.TransformPopup to collapse a SetupNotificationNode
    // nested inside one or more ConditionalNodes into a single node with a computed target.
    internal static string BuildTernaryChain(List<(string? Condition, string Target)> arms)
    {
        // A target that's already a ${...}-wrapped dynamic expression (e.g. one arm hoisted a
        // random choice into its own let — see V2Serializer.CollapseSetupNotificationConditionals'
        // own remarks) unwraps to a bare fragment for the ternary instead of being quoted as a
        // literal passage-id string — quoting it would nest an unevaluated "${...}" string inside
        // the outer ternary's own string literal, never actually evaluated. TryCollapseCheckProgress
        // Conditional's own callers never pass an already-dynamic target today (it bails out first —
        // see its own guard), so this only changes behavior for genuinely dynamic arms.
        string TargetExpr(string s) =>
            s.StartsWith("${", StringComparison.Ordinal) && s.EndsWith('}')
                ? s[2..^1]
                : $"\"{MwsExprHelper.EscapeStr(s)}\"";
        string Build(int i) =>
            arms[i].Condition is null
                ? TargetExpr(arms[i].Target)
                : $"{arms[i].Condition} ? {TargetExpr(arms[i].Target)} : {Build(i + 1)}";
        return Build(0);
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
        // Cradle tags: "ck" → hub; original scripts also use "HUB"
        if (tags.Any(t => t.Equals("ck", StringComparison.OrdinalIgnoreCase) ||
                          t.Equals("hub", StringComparison.OrdinalIgnoreCase)))
        {
            return "hub";
        }

        // "ck2" is NOT the special-event signal, despite looking plausible next to "ck"→hub — every
        // real ViewSpecialEvent.instance.ShowEventPopup() call site in Cost of Disease has an empty
        // tags array, and neither "ck2"-tagged passage (AngryMobStorybook, TipsnTricks) contains
        // that call. A "ck2"-tagged passage is just an ordinary narration passage; the real overlay
        // trigger is handled at the call site itself — see PassageBodyVisitor.IsShowEventPopupCall.

        // Cradle tag "INTRO" marks a generation-opening passage — visually distinct in the
        // reference app from ordinary narration (see masterwork-plan notes on layout survey).
        if (tags.Any(t => t.Equals("INTRO", StringComparison.OrdinalIgnoreCase)))
        {
            return "introduction";
        }

        return "narration";
    }

    // ── Heading (title/subtitle) hoisting ─────────────────────────────────────
    // For hub/narration/introduction passages, a single leading bold-styled block is treated as
    // the passage's own heading (title/subtitle) rather than ordinary body text — matches how the
    // reference app renders these as a distinct header. Two shapes both count as a legitimate
    // title+subtitle heading:
    //   1. One bold line with a "Title - Subtitle" split (e.g. Fever1's "YELLOW FEVER - Early
    //      Years", HunterConfrontation's "The Grand Contest - April, 1902") or a
    //      "GENERATION {roman}: Subtitle" colon split unique to the Generation-heading shape below
    //      (e.g. Fear of the Unknown's "GENERATION I: Fear of the Unknown", A Time of War's
    //      "GENERATION I: Taking Sides") — general colon-splitting is deliberately NOT supported,
    //      only this specific Generation-prefixed shape (see SplitHeadingLine's remarks).
    //   2. Two bold text() calls separated by a lineBreak() *inside the same open styleScope*
    //      (e.g. Scenario5Start: "GENERATION I:" / lineBreak() / "Yellow Fever", all inside one
    //      `using (styleScope("bold", true))` block).
    // Only the source's FIRST bold styleScope block is ever considered. A bold block that starts
    // a NEW, separate styleScope after a break is never folded in as a subtitle — post-
    // consolidation it looks identical to shape 2 in the node list, but `WithinStyleScope` on the
    // intervening break (set at AST-walk time in PassageBodyVisitor, before that distinction is
    // lost) tells them apart. Checked against every real occurrence in Cost of Disease: a bold
    // block starting a new scope after a break is always a separate, unrelated sentence (an
    // instruction like "Carefully hand this storybook to X...", a question, a second prompt),
    // never a genuine continuation of the heading — see Gen1-CreepyTrackRes.mws.yaml for a worked
    // example of the bug this avoids.
    //
    // Generation-label swap: in both shapes, when the extracted title is exactly "GENERATION
    // {roman}" (I/II/III), the reference app displays it as a small subtitle beneath the actual
    // descriptive title — not the other way around, even though it appears FIRST in the source
    // text. SwapIfGenerationLabel applies this after each shape's own split logic determines the
    // (title, subtitle) pair.
    [GeneratedRegex(@"^(.*?)\s+-\s+(.*)$")]
    private static partial Regex HeadingDashSplit();

    [GeneratedRegex(@"^(GENERATION\s+I{1,3})\s*:\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenerationColonSplit();

    [GeneratedRegex(@"^GENERATION\s+I{1,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex GenerationLabelPattern();

    private static (string? Title, string? Subtitle, List<MwsNode> Remaining) TryHoistHeadingTitleSubtitle(
        List<MwsNode> nodes, string layout)
    {
        if (layout != "hub" && layout != "narration" && layout != "introduction")
        {
            return (null, null, nodes);
        }

        // Skip a leading run of heading-inert nodes (assigns, breaks, a nested all-inert
        // conditional/switch, or — same "renders as a separate overlay, not passage body text"
        // reasoning as EndOfGenerationNode's own IsHeadingInert case — an auto-display popup like
        // InputPromptNode) before looking for the leading bold heading. Without this, the flat
        // shape-1/shape-2 check below only ever looked at nodes[0] itself, so anything heading-inert
        // sitting in front of the real heading defeated the hoist entirely — unlike the
        // ConditionalNode/SwitchNode-sole-candidate path further down, which already tolerates
        // inert siblings on either side. The skipped prefix is preserved exactly as-is (not
        // trimmed/removed) in the returned Remaining list; only the identified heading node(s)
        // themselves are consumed. Real occurrence: Fear of the Unknown's Player1Stats..
        // Player5Stats, each starting with "if (!X_submitted) { input-prompt popup }" (collapsed to
        // a bare InputPromptNode) before their own "**Agility Confirmed**"-style bold heading.
        var prefixEnd = 0;
        while (prefixEnd < nodes.Count && IsHeadingInert(nodes[prefixEnd]))
        {
            prefixEnd++;
        }
        var headingPrefix = nodes[..prefixEnd];
        var headingSuffix = nodes[prefixEnd..];

        // A candidate heading TextNode CAN depend on a preceding `let` (non-empty Lets) — hoisting
        // it into `title` does NOT strand the `let`, because the `let` node itself is never removed
        // from the body; only the TextNode that consumed it is (see headingSuffix.Skip(1) below —
        // headingPrefix, which is where a preceding let sits, is preserved as-is). Confirmed against
        // the engine: PassageRenderer.Render runs RenderNodes(passage.Nodes, ctx) — the ENTIRE body,
        // let included — to completion BEFORE evaluating Title: ExpandOrNull(passage.Title,
        // ctx.Store); VariableStore.SetLetVariable writes into the same ctx.Store that title
        // expansion reads from, and ClearLetScope() has no call sites anywhere in engine code (only
        // a unit test), so a let set anywhere during body rendering stays visible for the rest of
        // that render. And ExpressionEvaluator's `Expr.StringLiteral s => StoryValue.Of(ExpandTemplate(s.Value,
        // ctx))` means a `{letname}` placeholder embedded inside one ternary arm's own string
        // literal gets its own recursive resolution once that arm is selected — so a ternary title
        // built from per-branch let-dependent headings only ever evaluates the ONE arm whose
        // condition matches, which is always the same condition that gated whether that branch's
        // `let` executed in the body. A previous version of this comment argued the opposite and
        // added a `Lets`-emptiness guard here to work around it — that was based on a wrong
        // assumption about *when* title evaluates (assumed interleaved with body position; it's
        // actually strictly after), and the real bug behind the original HuntSuccessCheck failure
        // was AsTernaryArm's own (now-fixed) nested-ternary-as-literal-string bug, not this. Real
        // occurrence this un-blocks: Fear of the Unknown's AsylumTreatment, `let _rnd_0 =
        // rand_between(...); let _rnd_1 = [...].shuffled(...)[0]; **Asylum Admittance Log {_rnd_0}{_rnd_1}**`.
        //
        // MaxHeadingLength (50) mirrors the original Unity app's own title/body-text discriminator
        // exactly, not a guessed threshold: TwineTMProPlayer.RefreshText() promotes a leading bold
        // run to a separate title UI element, but only provisionally — if the accumulated title
        // text exceeds 50 characters it's demoted back into ordinary body text (title UI cleared).
        // Position alone (bold run starting right after the leading content) isn't a safe title
        // signal by itself: Cradle authors reuse the same `styleScope("bold", true)` for player-
        // facing physical-component instructions ("Carefully hand this Storybook device to the
        // player with the {crest} token...") and question prompts, which are visually bolded for
        // emphasis but were never meant as a page heading — the original app's own 50-char cutoff
        // is what actually tells these apart from real short titles ("Monument to Progress",
        // "Rebuilding"). Length is measured via EstimateRenderedLength (see its own remarks) rather
        // than the raw template's own character count — a `{varname}` placeholder's LITERAL length
        // is a poor proxy for a story variable's short runtime value, and this is NOT always
        // over-length-by-a-wide-margin-regardless the way the original version of this comment
        // assumed: an auto-generated let name like `{_rnd_AsylumTreatment_0}` (24 chars, embedding
        // the full passage name) is far longer than the 1-4 digit number it actually holds at play
        // time. Real occurrence: A Time of War's Amessyes/DuelResolution1 and Fear of the Unknown's
        // AsylumQuestion5/7/9 (genuinely over-length, correctly rejected) vs. AsylumTreatment
        // (falsely over-length by raw count alone, correctly accepted once estimated instead).
        const int MaxHeadingLength = 50;

        if (headingSuffix is [TextNode { Style: "bold", Template.Length: > 0 } first, ..]
            && EstimateRenderedLength(first.Template) <= MaxHeadingLength)
        {
            // Shape 2: title + subtitle as two bold lines joined by a break that stayed inside the
            // same styleScope. A third bold line right after (even scope-internal) disqualifies the
            // hoist — that's more than a simple two-line heading and is left for the body to render.
            // The combined length of both lines is what's checked against MaxHeadingLength,
            // mirroring the single accumulated titleString the original app builds from both —
            // first.Template alone already passed the pattern's own <= MaxHeadingLength check above,
            // but that's not sufficient once a second line is added on top.
            if (headingSuffix is [_, BreakNode { WithinStyleScope: true } or ParagraphBreakNode { WithinStyleScope: true },
                    TextNode { Style: "bold", Template.Length: > 0 } second, .. var rest]
                && rest is not [BreakNode, TextNode { Style: "bold" }, ..]
                && EstimateRenderedLength(first.Template) + EstimateRenderedLength(second.Template) <= MaxHeadingLength)
            {
                var (shape2Title, shape2Subtitle) = SwapIfGenerationLabel(TrimHeadingText(first.Template), TrimHeadingText(second.Template));
                return (shape2Title, shape2Subtitle, [.. headingPrefix, .. rest]);
            }

            // A bold run doesn't necessarily end where ConsolidateTextNodes happens to stop merging
            // consecutive TextNodes — Cradle can follow a leading text() call with an if/elseif
            // chain (consolidated to a ConditionalNode/SwitchNode) selecting between several bold
            // text() calls, ALL still inside the SAME open styleScope, with no lineBreak in between
            // (real occurrence: A Time of War's TownHallS1 — `text("Carefully hand this Storybook
            // device to Player "); Vars.th++; if (th==1) text(nameA); else if (th==2) text(nameB);
            // ...`). Confirmed against the decompiled original Unity app
            // (TwineTMProPlayer.RefreshText): title-text accumulation continues for every StoryText
            // output while the bold style-group stays open, regardless of source-level branching —
            // it only stops when that style-group actually closes (a break, or non-bold output). A
            // length check on first.Template ALONE is meaningless here since the real accumulated
            // text is longer by however many characters the winning branch adds — rather than guess
            // at that length (or which branch fires), bail out of hoisting entirely at this position
            // whenever more bold content could still directly follow without an intervening break.
            // Skip past any leading run of heading-inert, non-break nodes first (TownHallS1's own
            // `Vars.th++;` assign sits BETWEEN the leading text and the switch, producing no output
            // at all in the real app, so it doesn't end the open style scope either) — the check is
            // against the first node that would ACTUALLY still be visible, not literally
            // headingSuffix[1].
            var afterFirstIdx = 1;
            while (afterFirstIdx < headingSuffix.Count
                && headingSuffix[afterFirstIdx] is not (BreakNode or ParagraphBreakNode)
                && IsHeadingInert(headingSuffix[afterFirstIdx]))
            {
                afterFirstIdx++;
            }

            // Not every bold continuation is unbounded, though — Cost of Disease's DetEffectRandom
            // follows "The Effects of Immortality " with an OPTIONAL short static suffix
            // ("- Early Years"/"- Middle Years"/nothing, chosen by `round`), and even the longest of
            // those combined with the leading text stays well under MaxHeadingLength. Disqualifying
            // whenever ANY bold continuation exists at all (regardless of whether it's bounded)
            // would wrongly strip that title too — treating it exactly like TownHallS1's `{nameA}`-
            // style continuation, whose length genuinely can't be known until a name is chosen at
            // play time. MaxPossibleBoldContinuationLength distinguishes the two: it returns a real
            // upper bound when every reachable branch is static literal text, or null when any
            // reachable branch embeds a `{var}` placeholder (an unknowable runtime length) — only
            // the null/over-length case disqualifies; a provably-short continuation is left for the
            // body to render (shape 1 below still only consumes `first` itself either way, so this
            // doesn't need to actually splice the continuation into the title).
            if (afterFirstIdx < headingSuffix.Count && headingSuffix[afterFirstIdx] is not (BreakNode or ParagraphBreakNode))
            {
                var continuationLength = MaxPossibleBoldContinuationLength(headingSuffix[afterFirstIdx]);
                if (continuationLength is null || EstimateRenderedLength(first.Template) + continuationLength.Value > MaxHeadingLength)
                {
                    return (null, null, nodes);
                }
            }

            // Shape 1: "Title - Subtitle" or "GENERATION {roman}: Subtitle" splits on the first
            // match; otherwise the whole line becomes the title with no subtitle. Whatever follows
            // (including a break directly after this node) is left exactly as-is in the remaining
            // body — not trimmed or merged further.
            var (title, subtitle) = SplitHeadingLine(first.Template);
            return (title, subtitle, [.. headingPrefix, .. headingSuffix.Skip(1)]);
        }

        // A leading bold span can also arrive already inline-merged into a NON-uniformly-styled
        // TextNode, when ConsolidateTextNodes merges a closing bold styleScope directly into
        // whatever plain text immediately follows with no separating break — CanJoinGroup merges
        // regardless of style match (see its own remarks); changing that merge behavior itself would
        // have far broader blast radius than title-hoisting alone (it's what makes "Turn to **the
        // Cost of Disease** section" a single sensible node elsewhere in the corpus), so this is
        // handled narrowly here instead. Real occurrence: Fear of the Unknown's FPYesHub —
        // `styleScope("bold"){ text("Destiny Awaits") } text("Your choice has been made.")`, no
        // break in between, produces ONE TextNode with Style: null (mixed) and Template:
        // "**Destiny Awaits**Your choice has been made.". Only a LEADING span counts (`^\*\*...\*\*`,
        // non-greedy) — a bold span elsewhere mid-sentence isn't a heading candidate; this only
        // matches when the bold span starts at position 0 of the merged template.
        //
        // The REMAINDER within this same merged template — the plain text immediately after the
        // bold span, still part of the identical TextNode — must also be short (combined with the
        // bold span, against MaxHeadingLength), not just the bold span alone: A Time of War's
        // OptiontoKillYesPattern merges a short bold callout ("**Gain 1 Body,**") directly into a
        // long, multi-sentence paragraph ("Lose 1 and Gain 1VP. Then they must return a piece to
        // Lost.") with no break — an inline emphasis at the START of ordinary prose, not a heading,
        // even though it syntactically matches "leading bold span". Unlike
        // MaxPossibleBoldContinuationLength's job (bounding more BOLD content that might extend the
        // title), an unbounded-length PLAIN remainder within the same node is a much stronger signal
        // this was never a heading to begin with, so it's checked directly here rather than reused.
        if (headingSuffix is [TextNode { Style: not "bold", Template: { } mixedTemplate } mixedFirst, .. var mixedRest]
            && System.Text.RegularExpressions.Regex.Match(mixedTemplate, @"^\*\*(.+?)\*\*") is { Success: true } leadingBoldMatch
            && leadingBoldMatch.Groups[1].Value.Length > 0
            && EstimateRenderedLength(leadingBoldMatch.Groups[1].Value) <= MaxHeadingLength)
        {
            var leadingText = leadingBoldMatch.Groups[1].Value;
            var remainderTemplate = mixedTemplate[leadingBoldMatch.Length..];

            var mixedAfterIdx = 0;
            while (mixedAfterIdx < mixedRest.Count
                && mixedRest[mixedAfterIdx] is not (BreakNode or ParagraphBreakNode)
                && IsHeadingInert(mixedRest[mixedAfterIdx]))
            {
                mixedAfterIdx++;
            }

            var mixedCombinedLength = EstimateRenderedLength(leadingText) + EstimateRenderedLength(remainderTemplate);
            var continuationOk = mixedCombinedLength <= MaxHeadingLength;
            if (continuationOk && mixedAfterIdx < mixedRest.Count && mixedRest[mixedAfterIdx] is not (BreakNode or ParagraphBreakNode))
            {
                var continuationLength = MaxPossibleBoldContinuationLength(mixedRest[mixedAfterIdx]);
                continuationOk = continuationLength is not null
                    && mixedCombinedLength + continuationLength.Value <= MaxHeadingLength;
            }

            if (continuationOk)
            {
                List<MwsNode> remainderNodes = remainderTemplate.Length > 0
                    ? [new TextNode { Template = remainderTemplate, Style = mixedFirst.Style, Lets = mixedFirst.Lets, SourceLine = mixedFirst.SourceLine }]
                    : [];
                var (mixedTitle, mixedSubtitle) = SplitHeadingLine(leadingText);
                return (mixedTitle, mixedSubtitle, [.. headingPrefix, .. remainderNodes, .. mixedRest]);
            }
        }

        // A heading's LEADING fragment can also come from a single-branch (no else) conditional
        // whose own content independently hoists a short title (typically a computed/let-derived
        // value), immediately followed — no break — by more static bold text continuing the SAME
        // visual heading. Real occurrence: Fear of the Unknown's Player1Statsfin — `if (warriorA) {
        // let _rpl = warriorA.replace("_1", ""); **{_rpl}** }` (no else) followed directly by
        // `** Complete**` with no break. The recursive probe into the conditional's own branch is
        // safe even though it may end up unused (see WithBranchNodes/ReplaceNode's own remarks on
        // why every recursive hoist attempt is non-mutating until a caller commits to the result).
        // Deliberately narrow: only a bare (no dash/colon subtitle split) fragment title, only a
        // single following bold TextNode (not a further nested conditional/switch) — this is the
        // one reported shape, not a general "arbitrary leading + arbitrary trailing" combinator.
        if (headingSuffix is [ConditionalNode { Branches: [{ Else: not true } leadingBranch] } leadingCond, .. var afterCondRest])
        {
            var (fragmentTitle, fragmentSubtitle, leadingCondRemaining) = TryHoistHeadingTitleSubtitle(leadingBranch.Nodes, layout);
            if (fragmentTitle is not null && fragmentSubtitle is null && EstimateRenderedLength(fragmentTitle) <= MaxHeadingLength)
            {
                var afterCondIdx = 0;
                while (afterCondIdx < afterCondRest.Count
                    && afterCondRest[afterCondIdx] is not (BreakNode or ParagraphBreakNode)
                    && IsHeadingInert(afterCondRest[afterCondIdx]))
                {
                    afterCondIdx++;
                }

                if (afterCondIdx < afterCondRest.Count
                    && afterCondRest[afterCondIdx] is TextNode { Style: "bold", Template: { } continuationTemplate }
                    && EstimateRenderedLength(fragmentTitle) + EstimateRenderedLength(continuationTemplate) <= MaxHeadingLength)
                {
                    var newLeadingCond = WithBranchNodes(leadingCond, 0, leadingCondRemaining);
                    var combinedTitle = fragmentTitle + continuationTemplate;

                    // The fragment only actually renders when the guard condition is true — e.g.
                    // Fear of the Unknown's Player1Statsfin: `if (warriorA) { let _rpl = ...; **{_rpl}**
                    // }`, guarded on the SAME `warriorA` that the leading InputPromptNode (skipped as
                    // a heading-inert prefix above) is still WAITING on before the player's first
                    // visit. On that first visit `warriorA` is unset, this branch never executes, and
                    // a flat concatenation would produce a title referencing a `let` that was never
                    // bound — a StoryEvalException, not just a wrong title. Wrapping as a ternary
                    // (matching the SAME guard condition the body itself uses) makes the title track
                    // whichever the body actually decided: the combined text when the fragment
                    // rendered, or the trailing continuation alone when it didn't. AsTernaryArm/
                    // BuildTernaryChain already handle splicing a nested-expression arm correctly
                    // (see their own remarks) — reused here rather than duplicated.
                    var guardCondition = leadingBranch.Condition;
                    var finalTitle = guardCondition is null
                        ? combinedTitle
                        : "{" + BuildTernaryChain(
                            [(guardCondition, AsTernaryArm(combinedTitle)), (null, AsTernaryArm(continuationTemplate.TrimStart()))]) + "}";

                    return (finalTitle, null,
                        [.. headingPrefix, newLeadingCond, .. afterCondRest[..afterCondIdx], .. afterCondRest[(afterCondIdx + 1)..]]);
                }
            }
        }

        // Cradle idiom for a whole optional passage: "if (someFlag) { <real content> } else {
        // goto SomewhereElse; }" — the guard makes the ENTIRE body conditional, so a plain flat-list
        // check above never sees the leading bold heading; it's one level down, inside the branch
        // that actually renders. Only promote when EXACTLY ONE branch/case has heading-shaped
        // content of its own (recursing through this same function, so a multiply-nested guard
        // chain is handled too) — if the other branch(es) ALSO start with their own bold heading,
        // or none do, there's no single unambiguous title to hoist and this is left untouched, same
        // as today. A branch whose only content is a popup-triggering link (no leading bold text)
        // naturally fails the recursive check the same way it already fails the flat-list one above
        // — no separate "ignore popups" special case needed. Real-world occurrence: A Time of War's
        // TSBarracksPenalty ("if (barracks == "yes") { **Lack of Service Penalty** ... } else {
        // goto SeedGUNS; }").
        //
        // The heading-bearing conditional/switch doesn't have to be the ONLY top-level node — it
        // only has to be the FIRST non-inert one (headingSuffix[0], same anchor position shape-1
        // uses for a flat leading TextNode). Anything AFTER it — inert or not — is passed through
        // unchanged as ordinary body content, exactly like shape-1's own `headingSuffix.Skip(1)`
        // never re-checks what follows the title it already found. Real occurrence: A Time of War's
        // MonuRes and BenevolenceBonus, each `if (cond) { **Heading A** ... } else { **Heading B**
        // ... }` followed immediately (no wrapping conditional) by more unconditional body text, a
        // switch, and/or a setup popup shared by both outcomes — the OLD version of this check
        // required the WHOLE top-level node list to be inert-or-candidate, so any of that trailing
        // real content disqualified the hoist entirely, even though it has nothing to do with which
        // heading fired.
        //
        // ConditionalNode only: an else-less leading conditional is deliberately EXCLUDED here unless
        // everything after it is heading-inert too — an else-less `if` is exactly the shape a SEPARATE
        // sibling `if` might need to complete (see the guard-chain case below, e.g. ParadoxEvent:
        // `if (timemistake < 8) { **Monument to Progress** ... }` immediately followed by a separate
        // `if (timemistake == 8) { **Rebuilding** ... }`) — hoisting from JUST the first one here
        // would silently ignore the second, competing heading. A conditional WITH an else branch is
        // self-contained (both outcomes already covered, nothing else could complete it) and safe
        // regardless of what real content follows; an else-less one is only safe in the OLD
        // "sole candidate" sense, i.e. when there's no real competing content after it anyway.
        // SwitchNode has no equivalent risk — Cradle's ConsolidateSwitches pass always merges an
        // if/elseif chain on one variable into a single SwitchNode before this runs, so there's never
        // a second sibling SwitchNode on the same variable to combine with; a missing `default:` case
        // just means the value space is exhausted by the listed cases by construction (e.g.
        // `rand_between(1, 2, ...)` can only ever be 1 or 2), which BuildTernaryArmsFromSwitch already
        // covers via its own synthetic empty-fallback arm (same trick as the guard chain's).
        if (headingSuffix is [ConditionalNode leadCond, .. var afterLeadCondRest]
            && (leadCond.Branches.Any(b => b.Else == true) || afterLeadCondRest.All(IsHeadingInert)))
        {
            if (TryHoistFromOneBranch([.. leadCond.Branches.Select(b => b.Nodes)], layout,
                    out var condTitle, out var condSubtitle, out var condRemainingByIndex))
            {
                return (condTitle, condSubtitle,
                    [.. headingPrefix, WithBranchesNodes(leadCond, condRemainingByIndex), .. afterLeadCondRest]);
            }

            var condArms = BuildTernaryArmsFromConditional(leadCond);
            if (condArms is not null && TryBuildTernaryHeading(condArms, layout,
                    out var chainTitle, out var chainSubtitle, out var chainRemaining))
            {
                return (chainTitle, chainSubtitle,
                    [.. headingPrefix, WithAllBranchNodes(leadCond, chainRemaining), .. afterLeadCondRest]);
            }
        }

        if (headingSuffix is [SwitchNode leadSw, .. var afterSwRest])
        {
            if (TryHoistFromOneBranch([.. leadSw.Cases.Select(c => c.Nodes)], layout,
                    out var swTitle, out var swSubtitle, out var swRemainingByIndex))
            {
                return (swTitle, swSubtitle,
                    [.. headingPrefix, WithCasesNodes(leadSw, swRemainingByIndex), .. afterSwRest]);
            }

            var swArms = BuildTernaryArmsFromSwitch(leadSw);
            var swHasDefault = leadSw.Cases.Any(c => c.Default == true);
            if (TryBuildTernaryHeading(swArms, layout,
                    out var swChainTitle, out var swChainSubtitle, out var swChainRemaining,
                    appendEmptyFallbackArm: !swHasDefault))
            {
                return (swChainTitle, swChainSubtitle,
                    [.. headingPrefix, WithAllCaseNodes(leadSw, swChainRemaining), .. afterSwRest]);
            }
        }

        // Guard chain: multiple SEPARATE top-level `if (cond) { ... }` conditionals (no `else` on
        // any of them — not one if/elseif/else chain, which is BuildTernaryArmsFromConditional's own
        // case above), each independently carrying its own heading, sitting side by side with only
        // heading-inert siblings (if any) around them. Unlike the if/elseif/else and switch/default
        // cases, there's no explicit catch-all branch to anchor the ternary's unconditional trailing
        // arm on — an empty-string fallback covers whatever isn't matched by any guard, since the
        // conditions collectively look complete/non-overlapping by construction (each guards a
        // distinct value of the same variable) but that isn't something this can prove statically.
        // Real occurrence: A Time of War's ParadoxEvent — `if (timemistake < 8) { **Monument to
        // Progress** ... }` immediately followed by a separate `if (timemistake == 8) { **Rebuilding**
        // ... }`.
        if (TryFindAllHeadingCandidateConditionals(nodes, out var guardCandidates) &&
            TryBuildGuardChainHeading(guardCandidates, layout,
                out var guardTitle, out var guardSubtitle, out var guardRemaining))
        {
            var guardNodes = nodes;
            for (int i = 0; i < guardCandidates.Count; i++)
            {
                guardNodes = ReplaceNode(guardNodes, guardCandidates[i], WithBranchNodes(guardCandidates[i], 0, guardRemaining[i]));
            }

            return (guardTitle, guardSubtitle, guardNodes);
        }

        return (null, null, nodes);
    }

    // Collects every top-level ConditionalNode in `nodes` that is a plain, else-less guard (exactly
    // one branch, Else != true) and not heading-inert — i.e. every candidate for the guard-chain
    // heading case above. Any other non-inert top-level node (a bare heading TextNode, a conditional
    // WITH an else, a switch, etc.) makes the whole shape ambiguous and bails with an empty list,
    // same conservative philosophy used throughout this function. Requires at least two candidates —
    // exactly one (with nothing else non-inert around it) would already have been handled by the
    // leading-conditional shape above, which tolerates a sole else-less candidate whenever whatever
    // follows it is entirely heading-inert too (see its own remarks on why an else-less leading
    // conditional is otherwise deliberately left for here instead).
    private static bool TryFindAllHeadingCandidateConditionals(List<MwsNode> nodes, out List<ConditionalNode> candidates)
    {
        candidates = [];
        foreach (var node in nodes)
        {
            if (IsHeadingInert(node))
            {
                continue;
            }

            if (node is not ConditionalNode { Branches: [{ Else: not true }] } cond)
            {
                candidates = [];
                return false;
            }

            candidates.Add(cond);
        }

        return candidates.Count >= 2;
    }

    // Same shape as TryBuildTernaryHeading (requires every candidate to independently hoist its own
    // heading, and a uniform title-only/title+subtitle shape across all of them), but appends a
    // synthetic, unconditional "" (empty string) fallback arm instead of requiring — and consuming —
    // a real else/default branch, since guard-chain candidates never have one. See the call site's
    // own remarks for why an empty fallback is an acceptable trade here.
    private static bool TryBuildGuardChainHeading(
        List<ConditionalNode> candidates, string layout,
        out string? title, out string? subtitle, out List<List<MwsNode>> remainingPerCandidate)
    {
        title = null;
        subtitle = null;
        remainingPerCandidate = [];

        var hoisted = new List<(string? Condition, string Title, string? Subtitle, List<MwsNode> Remaining)>();
        foreach (var candidate in candidates)
        {
            var branch = candidate.Branches[0];
            var (t, s, r) = TryHoistHeadingTitleSubtitle(branch.Nodes, layout);
            if (t is null)
            {
                return false;
            }

            hoisted.Add((branch.Condition, t, s, r));
        }

        var withSubtitle = hoisted.Count(h => h.Subtitle is not null);
        if (withSubtitle != 0 && withSubtitle != hoisted.Count)
        {
            return false;
        }

        var titleArms = hoisted.Select(h => (h.Condition, Title: AsTernaryArm(h.Title))).ToList();
        titleArms.Add((null, ""));
        title = "{" + BuildTernaryChain(titleArms) + "}";

        if (withSubtitle > 0)
        {
            var subtitleArms = hoisted.Select(h => (h.Condition, Title: AsTernaryArm(h.Subtitle!))).ToList();
            subtitleArms.Add((null, ""));
            subtitle = "{" + BuildTernaryChain(subtitleArms) + "}";
        }

        remainingPerCandidate = [.. hoisted.Select(h => h.Remaining)];
        return true;
    }

    // A hoisted title/subtitle can itself already be a `{...}`-wrapped expression rather than plain
    // text — when a candidate's own heading resolved through a NESTED ternary/guard-chain hoist
    // (e.g. Cost of Disease's AllMWRewards: 5 top-level `if (tempcomp == nameX)` guards, each
    // wrapping its own `switch (typeX)` whose cases all independently hoist the same "**Reward**"
    // heading via the existing switch/default ternary path — so each guard's own hoisted title is
    // already a full `{typeX == ... ? ... : ...}` string, not a literal). BuildTernaryChain's own
    // `${...}`-unwrap rule (built for target/goto arms) already does exactly the splice this needs;
    // re-wrapping a `{...}` string as `${...}` reuses it for free instead of letting BuildTernaryChain
    // quote the nested expression as literal text containing literal brace/quote characters — a real
    // bug caught by AllMWRewards ending up with a restext value of the un-evaluated literal string
    // `{typeA == "creature" ? "Reward" : ...}` instead of the intended splice.
    private static string AsTernaryArm(string hoistedText) =>
        hoistedText.Length >= 2 && hoistedText[0] == '{' && hoistedText[^1] == '}'
            ? "$" + hoistedText
            : hoistedText;

    // True when `node` can never itself carry a heading — its only possible effect is a state
    // change (assign, random draw, let), a break (no text of its own), an auto-display popup with
    // no label (EndOfGenerationNode/InputPromptNode/SetupBlockNode — a separate overlay, not
    // passage body text, same reasoning BreakFilter's own IsNonRendered already applies), a pure
    // redirect (GotoNode/GotoMenuNode — never produces output; a render that fires one is never
    // shown to the player at all, since GameSession.RenderChainFrom discards every intermediate
    // PassageRenderResult, title included, and only returns the goto chain's final landing passage
    // — so treating it as heading-inert can never surface a wrong title, only skip past dead-end
    // branches to find the real one, e.g. Fear of the Unknown's AsylumMeet: `if (Av > 1) { goto
    // AsylumComplete; } if (Bv > 1) { goto ... } ... if (Ev > 1) { goto ... } else { **Retrieval of
    // Property** ... }`), a progress-tracking checkpoint (CheckProgressNode), a click-through link
    // (LinkNode/ExpandLinkNode — a link's own label is UI/navigation text, never itself a plausible
    // heading candidate the way BreakFilter's own comment already assumes: "a branch whose only
    // content is a popup-triggering link... naturally fails [to hoist a title]... no leading bold
    // text"; this just extends the same reasoning to a link sitting as a top-level SIBLING next to
    // the real heading-bearing conditional/switch, not just as a branch's own sole content — real
    // occurrence: A Time of War's SeedResolution and Fear of the Unknown's PEWitch2, both `switch/
    // conditional (real headings) ... ExpandLinkNode ("Click to continue...") ...`, deliberately NOT
    // mirrored in BreakFilter.IsNonRendered, which correctly keeps treating a link as real rendered
    // content for break-trimming purposes — a link is never a plausible title, but it is a real,
    // visible break-relevant element, so the two mechanisms answer different questions here), or an
    // include_passage whose own spliced-in content isn't visible to this pass (IncludePassageNode —
    // its target's Nodes are only known at render time via a dynamic `${var}` expression in every
    // reported occurrence, so there's nothing to inspect statically; empirically, every direction/
    // fragment passage actually included this way — Cost of Disease's HuntNorth/HuntWest/etc. — opens
    // with plain, non-heading text, so treating the include as a non-competing prefix rather than
    // refusing to hoist at all is the better trade-off. Real occurrence: Cost of Disease's HuntNight1/
    // HuntNight2, `include_passage(${direction}); **{huntreward1}.**...`), or a nested conditional/
    // switch whose every branch/case consists ENTIRELY of such nodes. See the call sites' remarks
    // (SeedGUNS) for why this needs to look past nodes like this instead of requiring the
    // heading-bearing conditional/switch to be the ONLY top-level node.
    private static bool IsHeadingInert(MwsNode node) => node switch
    {
        EffectNode or LetNode or CheckpointNode or BreakNode or ParagraphBreakNode
            or EndOfGenerationNode or InputPromptNode or SetupBlockNode
            or GotoNode or GotoMenuNode or CheckProgressNode
            or LinkNode or ExpandLinkNode or IncludePassageNode => true,
        ConditionalNode cond => cond.Branches.All(b => b.Nodes.All(IsHeadingInert)),
        SwitchNode sw => sw.Cases.All(c => c.Nodes.All(IsHeadingInert)),
        _ => false,
    };

    // Estimates the RENDERED length of `template` for the MaxHeadingLength check — a `{varname}`
    // placeholder's own literal character count is a poor proxy for what actually displays at play
    // time, especially for auto-generated let-bound names ({_rnd_PassageName_N}/
    // {_rpl_PassageName_N}), which embed the full passage name and can run 20+ characters even
    // though the substituted value is typically short (a 1-4 digit number, a single letter, a short
    // computed string). Each placeholder is counted as a small fixed estimate instead of its own
    // literal length. Real occurrence: Fear of the Unknown's AsylumTreatment — the raw template
    // "Asylum Admittance Log {_rnd_AsylumTreatment_0}{_rnd_AsylumTreatment_1}" is 73 characters
    // (over the 50 cutoff), but the actual rendered text ("Asylum Admittance Log 2453D" or similar)
    // is well under it. Deliberately NOT applied inside MaxPossibleBoldContinuationLength's own "any
    // placeholder = unbounded" rule for TRAILING bold continuations (see its own remarks) — that's a
    // separate, intentionally conservative judgment call about content that hasn't been confirmed
    // short, not about measuring a candidate that's already been identified as the heading itself.
    private const int EstimatedPlaceholderLength = 4;

    private static int EstimateRenderedLength(string template) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{[^{}]+\}", new string('#', EstimatedPlaceholderLength)).Length;

    // Upper bound on how much MORE bold-styled text `node` could put on screen at runtime — used to
    // detect (and, when safe, tolerate) a bold heading run that continues past the first TextNode
    // without an intervening break (see the shape-1 call site's own remarks). Returns:
    //   - 0 for anything that can't produce bold text at all (heading-inert nodes, non-bold text).
    //   - The literal length for static bold text with no `{var}` placeholder (TownHallS1's
    //     Vars.nameA/B/etc.: unknowable until a name is actually chosen at play time, so bold text
    //     containing a placeholder is treated as unbounded, not measured).
    //   - null ("unbounded") if either the text itself is placeholder-bearing, or a
    //     ConditionalNode/SwitchNode has any branch/case that is (checking every branch, not just
    //     one, since only one will actually fire but which one isn't known statically — the max
    //     across branches is what could actually happen).
    private static int? MaxPossibleBoldContinuationLength(MwsNode node) => node switch
    {
        TextNode { Style: "bold", Template: { } t } => t.Contains('{') ? null : t.Length,
        ConditionalNode cond => MaxOrUnbounded(cond.Branches.Select(b => SumBoldContinuationLength(b.Nodes))),
        SwitchNode sw => MaxOrUnbounded(sw.Cases.Select(c => SumBoldContinuationLength(c.Nodes))),
        _ => 0,
    };

    private static int? SumBoldContinuationLength(List<MwsNode> nodes)
    {
        var total = 0;
        foreach (var node in nodes)
        {
            var length = MaxPossibleBoldContinuationLength(node);
            if (length is null)
            {
                return null;
            }

            total += length.Value;
        }

        return total;
    }

    private static int? MaxOrUnbounded(IEnumerable<int?> lengths)
    {
        var max = 0;
        foreach (var length in lengths)
        {
            if (length is null)
            {
                return null;
            }

            max = Math.Max(max, length.Value);
        }

        return max;
    }

    // Tries each branch/case's own node list against TryHoistHeadingTitleSubtitle. Succeeds when
    // every branch that yields a title agrees on the EXACT SAME (title, subtitle) — the common case
    // is exactly one branch succeeding and the rest being heading-inert (e.g. a real branch vs. a
    // `goto`-only one), but several branches independently producing the identical heading is just
    // as unambiguous: the player sees the same text no matter which branch actually fired, so there's
    // nothing to disambiguate with a ternary. Real occurrence: A Time of War's BarracksSimple1/2/3 —
    // two outer branches (one for `bldg1 == "Laborer's Union"`, one for `!bldg1`) each independently
    // wrap the identical nested "if warwinner == ... { **Service Required** ... } else { goto }"
    // structure; a THIRD outer branch (the catch-all else) is just a `goto`. Two branches succeeding
    // with the SAME title isn't ambiguous, only differing titles are (still left untouched — that's
    // what TryBuildTernaryHeading is for). Only branches that actually produced a title get an entry
    // in `remainingByIndex`; branches that didn't (heading-inert ones, e.g. a bare `goto`) keep their
    // original Nodes untouched by the caller.
    private static bool TryHoistFromOneBranch(List<List<MwsNode>> branchNodeLists, string layout,
        out string? title, out string? subtitle, out Dictionary<int, List<MwsNode>> remainingByIndex)
    {
        title = null;
        subtitle = null;
        remainingByIndex = [];

        for (int i = 0; i < branchNodeLists.Count; i++)
        {
            var (t, s, r) = TryHoistHeadingTitleSubtitle(branchNodeLists[i], layout);
            if (t is null)
            {
                continue;
            }

            if (title is null)
            {
                (title, subtitle) = (t, s);
            }
            else if (t != title || s != subtitle)
            {
                title = null;
                subtitle = null;
                remainingByIndex = [];
                return false;
            }

            remainingByIndex[i] = r;
        }

        return title is not null;
    }

    // Builds a shallow clone of `cond` with ONLY branch `branchIdx`'s Nodes replaced — never
    // mutates `cond` itself. See ReplaceNode's own remarks for why this matters: a recursive hoist
    // call used to PROBE whether a branch has its own title (TryHoistFromOneBranch's per-branch
    // loop, or a nested call one level further down) must not corrupt the shared tree just because
    // it succeeded — the ENCLOSING caller might still reject the result (e.g. because a SECOND
    // branch also independently succeeded, making the overall hoist ambiguous), and by then the
    // damage would already be done. Real occurrence: A Time of War's BarracksSimple1 — two outer
    // branches each wrap their own inner conditional whose `then` starts with bold "Service
    // Required"; both inner hoists succeed during probing, so the outer "exactly one" check
    // correctly refuses to use either — but the in-place-mutation version had already deleted the
    // "Service Required" TextNode from both, silently losing real content despite hoisting nothing.
    private static ConditionalNode WithBranchNodes(ConditionalNode cond, int branchIdx, List<MwsNode> newNodes) =>
        new()
        {
            SourceLine = cond.SourceLine,
            Branches = [.. cond.Branches.Select((b, i) => i == branchIdx
                ? new ConditionalBranch { Condition = b.Condition, Else = b.Else, Nodes = newNodes }
                : b)],
        };

    // Same as WithBranchNodes but replaces every branch's Nodes at once — used by the ternary-chain
    // path, which (unlike the sole-candidate path) commits every branch's own remaining nodes in one
    // shot rather than just the single matched branch.
    private static ConditionalNode WithAllBranchNodes(ConditionalNode cond, List<List<MwsNode>> newNodesPerBranch) =>
        new()
        {
            SourceLine = cond.SourceLine,
            Branches = [.. cond.Branches.Select((b, i) =>
                new ConditionalBranch { Condition = b.Condition, Else = b.Else, Nodes = newNodesPerBranch[i] })],
        };

    // SwitchNode counterparts of WithBranchNodes/WithAllBranchNodes — see their own remarks.
    private static SwitchNode WithCaseNodes(SwitchNode sw, int caseIdx, List<MwsNode> newNodes) =>
        new()
        {
            SourceLine = sw.SourceLine,
            On = sw.On,
            Cases = [.. sw.Cases.Select((c, i) => i == caseIdx
                ? new SwitchCase { Match = c.Match, Default = c.Default, Nodes = newNodes }
                : c)],
        };

    private static SwitchNode WithAllCaseNodes(SwitchNode sw, List<List<MwsNode>> newNodesPerCase) =>
        new()
        {
            SourceLine = sw.SourceLine,
            On = sw.On,
            Cases = [.. sw.Cases.Select((c, i) =>
                new SwitchCase { Match = c.Match, Default = c.Default, Nodes = newNodesPerCase[i] })],
        };

    // Multi-index counterparts of WithBranchNodes/WithCaseNodes — replaces every branch/case whose
    // index appears in `newNodesByIndex`, leaving every other branch/case's Nodes untouched. Used by
    // TryHoistFromOneBranch's "multiple branches agree on the same title" case (see its own remarks),
    // where more than one branch may need its own heading text removed at once.
    private static ConditionalNode WithBranchesNodes(ConditionalNode cond, Dictionary<int, List<MwsNode>> newNodesByIndex) =>
        new()
        {
            SourceLine = cond.SourceLine,
            Branches = [.. cond.Branches.Select((b, i) => newNodesByIndex.TryGetValue(i, out var newNodes)
                ? new ConditionalBranch { Condition = b.Condition, Else = b.Else, Nodes = newNodes }
                : b)],
        };

    private static SwitchNode WithCasesNodes(SwitchNode sw, Dictionary<int, List<MwsNode>> newNodesByIndex) =>
        new()
        {
            SourceLine = sw.SourceLine,
            On = sw.On,
            Cases = [.. sw.Cases.Select((c, i) => newNodesByIndex.TryGetValue(i, out var newNodes)
                ? new SwitchCase { Match = c.Match, Default = c.Default, Nodes = newNodes }
                : c)],
        };

    // Replaces the occurrence of `oldNode` in `nodes` (found by reference, not value equality — two
    // structurally-identical-but-distinct node objects must not be confused) with `newNode`. Used
    // together with WithBranchNodes/WithCaseNodes to substitute a cloned, hoist-modified
    // ConditionalNode/SwitchNode into the returned node list without ever mutating the original tree.
    private static List<MwsNode> ReplaceNode(List<MwsNode> nodes, MwsNode oldNode, MwsNode newNode) =>
        [.. nodes.Select(n => ReferenceEquals(n, oldNode) ? newNode : n)];

    // Tries to collapse EVERY branch/case's own heading into ONE ternary-chained title/subtitle —
    // e.g. Cradle's "switch (gunsbonus) { case 1: **Knowledge Bonus** ...; case 2: **Ingredient
    // Bonus** ...; case 3: **Wealth Bonus** ...; default: **Knowledge Bonus** ...; }" idiom, where
    // the SAME switch determines both the passage's displayed content AND its title, per branch.
    // Unlike TryHoistFromOneBranch (fires when exactly one branch has heading content and the rest
    // are skip-only), this requires EVERY branch to have its own heading — a mix (some branches
    // have a heading, some don't) is genuinely ambiguous and left untouched, same conservative
    // philosophy as everywhere else in this function. Also requires a uniform shape across every
    // branch (all-with-subtitle or all-without) — a per-branch mix of shape 1 (title only) and
    // shape 2 (title + subtitle) headings would need per-arm subtitle logic MWS's flat `subtitle:`
    // field can't express, so that's left untouched too. `{...}` template placeholders already
    // evaluate a full expression, ternaries included (see docs/mws-format-latest.md §4 and
    // ExpressionEvaluator.ExpandTemplate) — this just builds that expression the same way
    // BuildTernaryChain already does for `target`/`goto`, wrapped in `{}` rather than `${}` since
    // title/subtitle splice expressions into an otherwise-literal string rather than being a
    // whole-field expression themselves. Real-world occurrence: A Time of War's SeedGUNS.
    //
    // `appendEmptyFallbackArm` covers a SwitchNode with no `default:` case whose declared cases still
    // each carry their own heading (e.g. Cost of Disease's Diseases1 — `switch (disease1) { case 1:
    // **A Year of Sickness** ...; case 2: **Rest and Time** ...; }`, no default since
    // `rand_between(1, 2, ...)` can only ever be 1 or 2) — same "declared cases exhaust the value
    // space by construction, but that isn't provable statically" trade TryBuildGuardChainHeading
    // already makes for else-less guard chains, via the same unconditional "" fallback arm appended
    // AFTER the uniformity check (so it never needs its own hoisted title, and never counts against
    // the with-subtitle-or-without uniformity requirement above it). Every `arms` entry itself still
    // must independently hoist a real title either way — this only widens what a MISSING arm (the
    // implicit "value matched nothing") is allowed to fall back to.
    private static bool TryBuildTernaryHeading(
        List<(string? Condition, List<MwsNode> Nodes)> arms, string layout,
        out string? title, out string? subtitle, out List<List<MwsNode>> remainingPerArm,
        bool appendEmptyFallbackArm = false)
    {
        title = null;
        subtitle = null;
        remainingPerArm = [];

        if (arms.Count < 2)
        {
            return false;
        }

        var hoisted = new List<(string? Condition, string Title, string? Subtitle, List<MwsNode> Remaining)>();
        foreach (var (armCondition, armNodes) in arms)
        {
            var (t, s, r) = TryHoistHeadingTitleSubtitle(armNodes, layout);
            if (t is null)
            {
                return false;
            }

            hoisted.Add((armCondition, t, s, r));
        }

        var withSubtitle = hoisted.Count(h => h.Subtitle is not null);
        if (withSubtitle != 0 && withSubtitle != hoisted.Count)
        {
            return false;
        }

        // See AsTernaryArm's own remarks — a hoisted arm's title/subtitle can itself already be a
        // `{...}`-wrapped expression from a nested ternary/guard-chain hoist, which must be spliced
        // in rather than quoted as literal text.
        var titleArms = hoisted.Select(h => (h.Condition, Title: AsTernaryArm(h.Title))).ToList();
        if (appendEmptyFallbackArm)
        {
            titleArms.Add((null, ""));
        }

        title = "{" + BuildTernaryChain(titleArms) + "}";

        if (withSubtitle == 0)
        {
            subtitle = null;
        }
        else
        {
            var subtitleArms = hoisted.Select(h => (h.Condition, Title: AsTernaryArm(h.Subtitle!))).ToList();
            if (appendEmptyFallbackArm)
            {
                subtitleArms.Add((null, ""));
            }

            subtitle = "{" + BuildTernaryChain(subtitleArms) + "}";
        }

        remainingPerArm = [.. hoisted.Select(h => h.Remaining)];
        return true;
    }

    // Builds ternary arms from a ConditionalNode's branches for TryBuildTernaryHeading — null only
    // when there's no `else` branch to anchor the ternary's unconditional trailing arm on
    // (BuildTernaryChain requires the last arm to have a null Condition; without a real else,
    // there's no branch whose heading can safely serve as the "none of the above" fallback title).
    private static List<(string? Condition, List<MwsNode> Nodes)>? BuildTernaryArmsFromConditional(ConditionalNode cond)
    {
        if (!cond.Branches.Any(b => b.Else == true))
        {
            return null;
        }

        var arms = cond.Branches
            .Select(b => (Condition: b.Else == true ? null : b.Condition, b.Nodes))
            .ToList();
        var elseIdx = arms.FindIndex(a => a.Condition is null);
        if (elseIdx != arms.Count - 1)
        {
            var fallback = arms[elseIdx];
            arms.RemoveAt(elseIdx);
            arms.Add(fallback);
        }

        return arms;
    }

    // Builds ternary arms from a SwitchNode's cases for TryBuildTernaryHeading — one arm per declared
    // case, regardless of whether there's a `default:` case. Unlike BuildTernaryArmsFromConditional,
    // never returns null for a missing default — the caller passes appendEmptyFallbackArm: true to
    // TryBuildTernaryHeading in that situation instead (see its own remarks for why that's safe here
    // specifically, when it wouldn't be for a bare else-less conditional).
    private static List<(string? Condition, List<MwsNode> Nodes)> BuildTernaryArmsFromSwitch(SwitchNode sw)
    {
        var arms = sw.Cases
            .Select(c => (Condition: c.Default == true ? null : BuildSwitchCaseCondition(sw.On, c.Match), c.Nodes))
            .ToList();
        var defaultIdx = arms.FindIndex(a => a.Condition is null);
        if (defaultIdx >= 0 && defaultIdx != arms.Count - 1)
        {
            var fallback = arms[defaultIdx];
            arms.RemoveAt(defaultIdx);
            arms.Add(fallback);
        }

        return arms;
    }

    private static readonly string[] SwitchCasePatternPrefixes = ["==", "!=", "<=", ">=", "<", ">"];

    // Rebuilds an equivalent boolean condition string from a SwitchCase's `match` value — the
    // inverse of BuildMatchValue (which strips an "==" op down to a bare literal, but keeps any
    // other comparison operator as a raw "<=5"-style pattern string glued onto the value; see its
    // own remarks). A compound (List<object>) match — from an OR'd chain like "on == "A" || on ==
    // "B"" — becomes an equivalent "||"-joined condition. Internal (not private): also called from
    // V2Serializer's SwitchNode overload of TryGetSetupTargetArms.
    internal static string BuildSwitchCaseCondition(string on, object? match) => match switch
    {
        List<object> list => string.Join(" || ", list.Select(v => BuildSwitchCaseCondition(on, v))),
        int n => $"{on} == {n}",
        string s when SwitchCasePatternPrefixes.Any(s.StartsWith) => $"{on}{s}",
        string s => $"{on} == \"{MwsExprHelper.EscapeStr(s)}\"",
        _ => $"{on} == {match}",
    };

    private static (string Title, string? Subtitle) SplitHeadingLine(string text)
    {
        var trimmed = text.Trim();

        // "GENERATION {roman}: Subtitle" — a colon-separated single line, unique to the Generation
        // heading shape. Deliberately narrower than a general "any colon splits" rule: A Time of
        // War's ForewordScen2 ("A Time of War : A Memoir Across Three Generations") also has a
        // colon in its single bold heading line but isn't a Generation-label heading and should
        // stay unsplit, same as today. Group 1 is the Generation label by construction, so this
        // always returns in the swapped (descriptive title, Generation label) order directly.
        var genColon = GenerationColonSplit().Match(trimmed);
        if (genColon.Success)
        {
            return (TrimHeadingText(genColon.Groups[2].Value), TrimHeadingText(genColon.Groups[1].Value));
        }

        var m = HeadingDashSplit().Match(trimmed);
        if (m.Success)
        {
            var titlePart = TrimHeadingText(m.Groups[1].Value);
            var subtitlePart = TrimHeadingText(m.Groups[2].Value);
            if (titlePart.Length > 0 && subtitlePart.Length > 0)
            {
                return SwapIfGenerationLabel(titlePart, subtitlePart);
            }
        }
        return (TrimHeadingText(trimmed), null);
    }

    // The reference app shows a bare "GENERATION {roman}" heading part as a small subtitle beneath
    // the actual descriptive title, regardless of which order the source text puts them in — swap
    // whenever the already-split title is exactly a Generation label.
    private static (string Title, string? Subtitle) SwapIfGenerationLabel(string title, string? subtitle) =>
        subtitle is not null && GenerationLabelPattern().IsMatch(title) ? (subtitle, title) : (title, subtitle);

    // Strips whitespace and stray ':' characters (e.g. a bold "GENERATION I:" line preceding a
    // subtitle) from extracted title/subtitle text. Runs whitespace-trim again after stripping
    // colons in case removing them exposes new leading/trailing whitespace.
    private static string TrimHeadingText(string text) => text.Trim().Trim(':').Trim();

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
        return MergeInterstitialAssigns(MergeComplementaryConditionalTextFragments(
            HoistAndMergeSwitchLets(ConsolidateSwitches(ConsolidateBreaks(result)))));
    }

    // Scans for [TextNode+][single-branch text-only ConditionalNode]{2}[TextNode+] runs where the
    // two conditionals' conditions are a provably exhaustive, non-overlapping numeric-range
    // partition of the same variable (e.g. Cradle's `if (Vars.players <= 3) {...} if (Vars.players
    // >= 4) {...}`, used to pick alternate wording for a clause in the middle of one sentence) and
    // merges them into a single if/else-if ConditionalNode, folding the surrounding lead-in/lead-out
    // text into each branch — producing two complete sentences instead of a prefix/conditional/
    // conditional/suffix sequence that reads as a broken fragment when rendered as separate nodes.
    // Deliberately narrow: exactly two branches, same variable, complementary ranges, text-only
    // branch content, non-empty text on both sides. A survey of every occurrence of "text, 2+
    // adjacent bare single-branch conditionals, text" in Cost of Disease found real cases that must
    // NOT be merged this way — e.g. DevEventCure/Gen1Creepy-ConcealExpose's `wolves == "evil"` /
    // `hunters == "evil"` (different variables, not proven mutually exclusive — both could be true,
    // in which case an if/else-if merge would silently drop one clause instead of showing both as
    // the source does) and END-UniGood's three independent boolean flags (additive, not a two-way
    // split). Anything outside the narrow "same var, complementary numeric range" shape is left
    // alone rather than risk that. See EquitableValues.mws.yaml / UniEvent2-Failure.mws.yaml for the
    // real occurrences this fixes.
    private static List<MwsNode> MergeComplementaryConditionalTextFragments(List<MwsNode> nodes)
    {
        var result = new List<MwsNode>(nodes.Count);
        int i = 0;
        while (i < nodes.Count)
        {
            if (nodes[i] is not TextNode)
            {
                result.Add(nodes[i++]);
                continue;
            }

            int prefixStart = i;
            int prefixEnd = i;
            while (prefixEnd < nodes.Count && nodes[prefixEnd] is TextNode)
            {
                prefixEnd++;
            }

            if (TryMergeConditionalTextFragment(nodes, prefixStart, prefixEnd, out var merged, out var consumedEnd))
            {
                result.Add(merged);
                i = consumedEnd;
                continue;
            }

            for (int k = prefixStart; k < prefixEnd; k++)
            {
                result.Add(nodes[k]);
            }

            i = prefixEnd;
        }
        return result;
    }

    private static bool TryMergeConditionalTextFragment(
        List<MwsNode> nodes, int prefixStart, int prefixEnd, out ConditionalNode merged, out int consumedEnd)
    {
        merged = null!;
        consumedEnd = prefixEnd;

        if (prefixEnd + 1 >= nodes.Count ||
            !IsSingleBranchTextOnlyConditional(nodes[prefixEnd], out var branchA, out var condA) ||
            !IsSingleBranchTextOnlyConditional(nodes[prefixEnd + 1], out var branchB, out var condB) ||
            (prefixEnd + 2 < nodes.Count && nodes[prefixEnd + 2] is ConditionalNode) ||
            !AreComplementaryNumericRanges(condA, condB))
        {
            return false;
        }

        int suffixStart = prefixEnd + 2;
        int suffixEnd = suffixStart;
        while (suffixEnd < nodes.Count && nodes[suffixEnd] is TextNode)
        {
            suffixEnd++;
        }

        // Require text on both sides — a conditional pair with nothing following isn't a
        // "fragment in the middle," it's an ordinary trailing optional clause.
        if (suffixEnd == suffixStart)
        {
            return false;
        }

        var prefixTexts = nodes[prefixStart..prefixEnd].Cast<TextNode>().ToList();
        var suffixTexts = nodes[suffixStart..suffixEnd].Cast<TextNode>().ToList();

        var mergedBranchA = new ConditionalBranch
        {
            Condition = condA,
            Nodes = [MergeTextNodesForFragment(prefixTexts.Concat(branchA!.Nodes.Cast<TextNode>()).Concat(suffixTexts))],
        };
        var mergedBranchB = new ConditionalBranch
        {
            Condition = condB,
            Nodes = [MergeTextNodesForFragment(prefixTexts.Concat(branchB!.Nodes.Cast<TextNode>()).Concat(suffixTexts))],
        };

        merged = new ConditionalNode
        {
            Branches = [mergedBranchA, mergedBranchB],
            SourceLine = prefixTexts[0].SourceLine,
        };
        consumedEnd = suffixEnd;
        return true;
    }

    private static bool IsSingleBranchTextOnlyConditional(
        MwsNode node, out ConditionalBranch? branch, out string? condition)
    {
        branch = null;
        condition = null;
        if (node is not ConditionalNode { Branches: [var b] } || b.Else == true || b.Condition is null ||
            b.Nodes.Count == 0 || !b.Nodes.All(n => n is TextNode))
        {
            return false;
        }

        branch = b;
        condition = b.Condition;
        return true;
    }

    // True when condA/condB are simple comparisons ("var OP N") on the same variable whose implied
    // integer ranges are complementary and non-overlapping — i.e. together they cover every integer,
    // each exactly once (e.g. "players <= 3" / "players >= 4", or "players < 4" / "players > 3").
    private static bool AreComplementaryNumericRanges(string? condA, string? condB)
    {
        if (condA is null || condB is null) return false;
        var a = SwitchCondRegex().Match(condA);
        var b = SwitchCondRegex().Match(condB);
        if (!a.Success || !b.Success) return false;
        if (a.Groups[1].Value != b.Groups[1].Value) return false;
        if (!int.TryParse(a.Groups[3].Value, out var nA) || !int.TryParse(b.Groups[3].Value, out var nB)) return false;

        var (loA, hiA) = ToInclusiveRange(a.Groups[2].Value, nA);
        var (loB, hiB) = ToInclusiveRange(b.Groups[2].Value, nB);
        if (loA is null && hiB is null)
        {
            return hiA is not null && loB is not null && hiA + 1 == loB;
        }
        if (loB is null && hiA is null)
        {
            return hiB is not null && loA is not null && hiB + 1 == loA;
        }
        return false;
    }

    // Converts a "var OP N" comparison into an inclusive [lo, hi] integer range (null = unbounded).
    private static (int? Lo, int? Hi) ToInclusiveRange(string op, int n) => op switch
    {
        "<" => (null, n - 1),
        "<=" => (null, n),
        ">" => (n + 1, null),
        ">=" => (n, null),
        _ => (null, null),
    };

    private static TextNode MergeTextNodesForFragment(IEnumerable<TextNode> texts)
    {
        var list = texts.ToList();
        var allRuns = new List<TextRun>();
        foreach (var t in list)
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
        var mergedTemplate = BuildTemplate(allRuns, dominantStyle).Replace("****", "").Replace("__", "");
        return new TextNode
        {
            Template = mergedTemplate,
            Style = dominantStyle,
            SourceLine = list.FirstOrDefault()?.SourceLine,
        };
    }

    // Scans for [TextNode+][Interstitial+][TextNode+] runs and, when hoisting the interstitials
    // is safe, emits them first and merges all texts into one node. An "interstitial" is a node
    // that produces no inline text of its own and can be freely relocated: a pure-assign EffectNode
    // (safe as long as no pre-node text references the assigned variables), or a setup-image
    // ImageNode (Vars._SetupImage — routed to the enclosing popup's header by SplitPopupHeaderNodes
    // regardless of where in the content list it sits, so it never needed to interrupt the sentence
    // it was written in the middle of; see PassageBodyVisitor's ProcessAssignment _SetupImage case).
    // Runs after ConsolidateSwitches so conditional blocks have already been promoted above the
    // text/interstitial sequence.
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

            // Span of following interstitials
            int interstitialEnd = preEnd;
            while (interstitialEnd < nodes.Count && IsInterstitialHoistable(nodes[interstitialEnd]))
            {
                interstitialEnd++;
            }

            // Only merge when interstitials are followed by at least one more text node
            if (interstitialEnd == preEnd || interstitialEnd >= nodes.Count || nodes[interstitialEnd] is not TextNode)
            {
                for (int k = i; k < preEnd; k++)
                {
                    result.Add(nodes[k]);
                }

                i = preEnd;
                continue;
            }

            // Safety check: none of the pre-interstitial texts may reference variables the
            // interstitials assign (setup-image ImageNodes assign nothing, so this is trivially
            // satisfied whenever the span is image-only).
            var interstitials = nodes[preEnd..interstitialEnd].ToList();
            var assignedVars = interstitials.OfType<EffectNode>().SelectMany(a => a.VarSets!.Keys).ToHashSet(StringComparer.Ordinal);
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
            int postEnd = interstitialEnd;
            while (postEnd < nodes.Count && nodes[postEnd] is TextNode)
            {
                postEnd++;
            }

            // Emit: interstitials, then merged text (pre + post)
            result.AddRange(interstitials);
            var allRuns = new List<TextRun>();
            foreach (var t in preTexts.Concat(nodes[interstitialEnd..postEnd].Cast<TextNode>()))
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
                SourceLine = preTexts.FirstOrDefault()?.SourceLine ?? interstitials.FirstOrDefault()?.SourceLine,
            });
            i = postEnd;
        }
        return result;
    }

    private static bool IsInterstitialHoistable(MwsNode node) =>
        IsPureAssignEffect(node) || node is ImageNode { Style: "setup-image" };

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
            // The special-event overlay marker (see PassageBodyVisitor.IsShowEventPopupCall) is a
            // synthesized structural node, not real inline reading text — it must never merge with
            // a neighboring TextNode (e.g. a bold heading immediately before it, with no break in
            // between, exactly S5Special1a's own real shape). Returning false here both refuses to
            // join an existing group (flushing it as its own node, unmerged) and skips starting a
            // new group of its own, so text that follows it doesn't get pulled in either.
            if (t.Style == "special-event")
            {
                return false;
            }

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
                var withinStyleScope = firstBreak.WithinStyleScope;
                int count = 1;
                i++;
                while (i < nodes.Count && nodes[i] is BreakNode nextBreak)
                {
                    withinStyleScope &= nextBreak.WithinStyleScope;
                    count++;
                    i++;
                }
                result.Add(count >= 2
                    ? new ParagraphBreakNode { SourceLine = sourceLine, WithinStyleScope = withinStyleScope }
                    : new BreakNode { SourceLine = sourceLine, WithinStyleScope = withinStyleScope });
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
                if (rawVal.Contains(' ') || !IsLiteralMatchValue(rawVal))
                {
                    // rawVal isn't a quoted string or integer literal — it's a bare identifier, i.e.
                    // a reference to another variable (e.g. `Vars.trig == Vars.players`). switch's
                    // `match:` field is always a static literal to compare `on` against, never a
                    // dynamic expression, so silently coercing the bareword into a literal string
                    // (as BuildMatchValue used to) produces a case that can never match the intended
                    // variable's value — see Cost of Disease's NewMHub, where `trig == players`
                    // became `match: 'players'` and fell through to `match: 2` instead. Bail on the
                    // whole conversion; the original if/elseif/else chain already evaluates this
                    // correctly in order.
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

        // Reject compound values like "2 || x == 3", and anything that isn't actually a literal
        // (e.g. `Vars.trig == Vars.players` — a bare identifier here is a variable reference, not a
        // literal to match against — see TryConvertCompoundConditionalToSwitch's identical check for
        // why silently coercing it into a literal string is wrong).
        var rawVal = m.Groups[3].Value.Trim();
        bool isQuoted = rawVal.StartsWith('"') && rawVal.EndsWith('"');
        if ((!isQuoted && rawVal.Contains(' ')) || !IsLiteralMatchValue(rawVal))
        {
            return null;
        }

        return m.Groups[1].Value;
    }

    // A switch/case `match:` value is always a static literal — a bare (unquoted, non-numeric)
    // identifier is a reference to another variable, never a literal, in this expression language.
    private static bool IsLiteralMatchValue(string rawVal) =>
        (rawVal.StartsWith('"') && rawVal.EndsWith('"')) || int.TryParse(rawVal, out _);

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
                case SetupNotificationNode sn when sn.Random is not null:
                    sn.Random = NormalizeVarRandom(sn.Random);
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
