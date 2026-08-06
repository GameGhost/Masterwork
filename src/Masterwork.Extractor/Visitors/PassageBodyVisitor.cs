using System.Text;
using System.Text.RegularExpressions;
using Masterwork.ModuleFormat;
using VarDef = Masterwork.ModuleFormat.VarDef;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Masterwork.Extractor.Visitors;

// Walks the statements of a Cradle passageN_Main() iterator method and
// produces a list of MwsNode objects representing the passage content.
public class PassageBodyVisitor
{
    private readonly string _passageName;
    private readonly SpriteMapper _spriteMapper;
    private readonly ProgressMapper _progressMapper;
    private readonly ExtractionReport _report;

    // Current accumulated text runs — flushed into a TextNode at the next non-text node
    private List<TextRun> _pendingRuns = [];
    // Style stack — tracks nested styleScope calls (bold, italic, etc.)
    private readonly Stack<string> _styleStack = new();
    // Source line of the statement currently being processed (1-based in original file)
    private int? _currentStatementLine;
    // Source line of the first text() call that started the current pending-runs buffer
    private int? _pendingTextStartLine;
    // Pending alignment value from a surrounding <align=...> tag (cleared at FlushText)
    private string? _pendingAlign;
    // Pending size value from a surrounding <size=N> tag (cleared at FlushText)
    private string? _pendingSize;
    // Source line of the <size=...> open tag that set _pendingSize
    private int? _pendingSizeLine;
    // Layout tags encountered that will not be preserved (e.g. <line-height>), warned at FlushText
    private List<(string Tag, int? Line)> _pendingUnpreservedTags = [];
    // Monotonically incrementing counter for unique inline-random variable names within a passage
    private int _varRandomSeq = 0;
    // Local string variables declared in this passage method (name → literal value)
    private readonly Dictionary<string, string> _localVars = new(StringComparer.Ordinal);
    // Local delegate variables that are captures of ViewEndOfGeneration.S_OnEndOfGeneration
    private readonly HashSet<string> _eogDelegates = new(StringComparer.Ordinal);
    // Local array variables: name → list of member variable names (for let array nodes)
    private readonly Dictionary<string, List<string>> _localArrayVars = new(StringComparer.Ordinal);
    // Local computed variables: name → aggregate expression string (for condition substitution)
    // e.g. "num" → "play.countif(\"=\" + max_play)" from a LINQ Count-where-Max pattern
    private readonly Dictionary<string, string> _localComputedVars = new(StringComparer.Ordinal);
    // Local List<int> variables: name → the Vars array variable they mirror (or null if unknown)
    private readonly Dictionary<string, string?> _localIntLists = new(StringComparer.Ordinal);
    // Local string variables whose values are computed via string-literal ternaries (passage names).
    // Key = local var name, Value = ConditionalNode list with LetNode leaves (Var = local name).
    // Consumed by BuildEogSetupMarker when the var is passed as passageName to S_OnSetSpecialSetup.
    private readonly Dictionary<string, List<MwsNode>> _localPassageConditionals = new(StringComparer.Ordinal);
    // Vars variables set to int arrays in this passage: varName → int values (for localIntList matching)
    private readonly Dictionary<string, List<int>> _passageIntArrayVars = new(StringComparer.Ordinal);

    // Known variable types — used to validate int.Parse() arithmetic stripping. Also the
    // registration target for synthetic variables this visitor invents itself (e.g. _ProgressRound
    // below) — mutable (not IReadOnlyDictionary) for exactly that, mirroring how
    // V2Serializer.TransformInputAction registers its own synthetic guard variable via the same
    // shared dictionary threaded through SerializationContext.Variables.
    private readonly Dictionary<string, VarDef>? _variables;

    // Line-number offset applied in GetLine: +1 for complete files (0-indexed Roslyn → 1-indexed),
    // -1 for partial/wrapped files (Roslyn sees 2 prepended wrapper lines, net offset is -1).
    private readonly int _lineOffset;

    public PassageBodyVisitor(string passageName, SpriteMapper spriteMapper, ExtractionReport report,
        Dictionary<string, VarDef>? variables = null, bool isCompleteFile = false,
        ProgressMapper? progressMapper = null)
    {
        _passageName = passageName;
        _spriteMapper = spriteMapper;
        _report = report;
        _variables = variables;
        _lineOffset = isCompleteFile ? 1 : -1;
        _progressMapper = progressMapper ?? ProgressMapper.Empty();
    }

    public List<MwsNode> VisitBlock(BlockSyntax block) =>
        VisitStatements(block.Statements);

    private List<MwsNode> VisitStatements(IEnumerable<StatementSyntax> statements)
    {
        var result = new List<MwsNode>();
        var list = statements.ToList();

        for (int i = 0; i < list.Count; i++)
        {
            var stmt = list[i];
            _currentStatementLine = GetLine(stmt);

            // Skip Cradle cleanup artifacts: StyleScope styleScope = null;
            if (IsCradleCleanupStatement(stmt))
            {
                continue;
            }

            // yield break — end of iterator
            if (stmt is YieldStatementSyntax { RawKind: (int)SyntaxKind.YieldBreakStatement })
            {
                break;
            }

            // yield return <expr>
            if (stmt is YieldStatementSyntax { RawKind: (int)SyntaxKind.YieldReturnStatement } ys)
            {
                var nodes = ProcessYieldExpression(ys.Expression!);
                TagNodes(nodes, _currentStatementLine);
                result.AddRange(FlushAndAdd(nodes));
                continue;
            }

            // using (base.styleScope(...)) { ... }
            if (stmt is UsingStatementSyntax us && IsStyleScopeUsing(us, out var scopeName, out var hookId))
            {
                // When the using body is a single statement (no braces), Roslyn red-node positions
                // are invalidated when that statement is placed into any synthetic collection.
                // Capture the inner line directly from the node before collection wrapping.
                var innerLine = us.Statement is BlockSyntax blk2 && blk2.Statements.Count > 0
                    ? GetLine(blk2.Statements[0])
                    : GetLine(us.Statement);
                IEnumerable<StatementSyntax> scopeStmts = us.Statement is BlockSyntax blk
                    ? blk.Statements
                    : SyntaxFactory.SingletonList(us.Statement);
                var savedLine = _currentStatementLine;
                var inner = VisitStyleScopeBlock(scopeName!, hookId, scopeStmts);
                _currentStatementLine = savedLine;
                TagNodes(inner, innerLine ?? _currentStatementLine);
                result.AddRange(FlushAndAdd(inner));
                continue;
            }

            // if / else if / else
            if (stmt is IfStatementSyntax ifs)
            {
                // Two-statement input prompt: if (ispopup) { OnGenerationBtn } + if (PassageValueNumber() >= 0) { store }
                if (i + 1 < list.Count && list[i + 1] is IfStatementSyntax nextIfs &&
                    TryDetectInputPrompt(ifs, nextIfs, out var twoIfInput))
                {
                    result.AddRange(FlushText());
                    twoIfInput!.SourceLine = _currentStatementLine;
                    result.Add(twoIfInput);
                    i++; // consume the store-if too
                    continue;
                }

                // If-else input prompt (legacy pattern)
                if (IsInputPromptIf(ifs, out var inputNode))
                {
                    result.AddRange(FlushText());
                    inputNode!.SourceLine = _currentStatementLine;
                    result.Add(inputNode!);
                    continue;
                }

                result.AddRange(FlushText());
                var condLine = _currentStatementLine; // capture before BuildConditional mutates _currentStatementLine
                var (hoisted, cond) = BuildConditional(ifs);
                if (hoisted.Count > 0)
                {
                    TagNodes(hoisted, condLine);
                    result.AddRange(hoisted);
                }

                cond.SourceLine = condLine;
                result.Add(cond);
                continue;
            }

            // Expression statements: assignments, Unity API calls
            if (stmt is ExpressionStatementSyntax es)
            {
                if (IsIgnorableAssignment(es))
                {
                    continue;
                }

                var nodes = ProcessExpressionStatement(es);
                TagNodes(nodes, _currentStatementLine);
                result.AddRange(FlushAndAdd(nodes));
                continue;
            }

            // Local variable declarations — track known patterns and emit nodes where needed
            if (stmt is LocalDeclarationStatementSyntax localDecl)
            {
                var localNodes = ProcessLocalDeclaration(localDecl);
                if (localNodes is not null)
                {
                    TagNodes(localNodes, _currentStatementLine);
                    result.AddRange(FlushAndAdd(localNodes));
                    continue;
                }
            }

            // Anything else — flag for review
            var code = stmt.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(code) && code != ";")
            {
                result.AddRange(FlushText());
                result.Add(new UnknownNode { OriginalCode = Truncate(code), SourceLine = _currentStatementLine });
                _report.AddUnhandled(_passageName, code, GetLine(stmt));
            }
        }

        result.AddRange(FlushText());
        return result;
    }

    private static void TagNodes(List<MwsNode> nodes, int? line)
    {
        foreach (var n in nodes)
        {
            if (n.SourceLine is null)
            {
                n.SourceLine = line;
            }
        }
    }

    // ── Style scope handling ───────────────────────────────────────────────

    private List<MwsNode> VisitStyleScopeBlock(string scopeName, string? hookId, IEnumerable<StatementSyntax> statements)
    {
        switch (scopeName)
        {
            case "bold":
            case "italic":
                _styleStack.Push(scopeName);
                var styled = VisitStatements(statements);
                _styleStack.Pop();
                return styled;

            case "hubTitle":
            case "heading":
            {
                _styleStack.Push(scopeName);
                var inner = VisitStatements(statements);
                _styleStack.Pop();
                return [new SectionHeadingNode { Text = BuildHeadingTemplate(inner) }];
            }

            case "hubDetails":
            {
                var inner = VisitStatements(statements);
                return [new SectionBodyNode { Nodes = inner }];
            }

            case "setupStyle":
            case "setupStyleEvnt":
            {
                var inner = VisitStatements(statements);
                return [new SetupBlockNode { Nodes = inner }];
            }

            case "hook":
                // enchantHook wrapper — the inner link will reference this hookId.
                // We just pass through the inner content; the link handler stitches the fragment.
                return VisitStatements(statements);

            default:
                _report.AddWarning(_passageName, $"Unknown styleScope: {scopeName}");
                return VisitStatements(statements);
        }
    }

    // ── yield return expression dispatch ──────────────────────────────────

    private List<MwsNode> ProcessYieldExpression(ExpressionSyntax expr)
    {
        if (expr is not InvocationExpressionSyntax inv)
        {
            return [Unknown(expr)];
        }

        var methodName = GetSimpleMethodName(inv);

        return methodName switch
        {
            "text" => ProcessTextInvocation(inv),
            "lineBreak" => [new BreakNode { WithinStyleScope = _styleStack.Count > 0 }],
            "link" => [ProcessLink(inv)],
            "passage" => ProcessPassageInclusionNodes(inv),
            "abort" => ProcessAbort(inv),
            _ => [Unknown(inv)],
        };
    }

    // ── text() ────────────────────────────────────────────────────────────

    private List<MwsNode> ProcessTextInvocation(InvocationExpressionSyntax inv)
    {
        var arg = inv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (arg is null)
        {
            return [];
        }

        var currentStyle = _styleStack.Count > 0 ? _styleStack.Peek() : null;
        if (currentStyle == "hubTitle")
        {
            currentStyle = null; // handled by scope
        }

        // string literal
        if (arg is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var raw = lit.Token.ValueText;

            // Standalone HTML-like layout tag (e.g. <align="center">, <size=200>, </align>)
            // — track state for the pending runs group; do not add a text run.
            if (_spriteMapper.IsStandaloneLayoutTag(raw))
            {
                ProcessLayoutTag(raw.Trim());
                return [];
            }

            // Embedded newlines are break points, not whitespace to collapse: split the
            // literal on them so each side flushes to its own TextNode with a BreakNode in
            // between (runs of 2+ consolidate into a paragraph break via the existing
            // ConsolidateBreaks pass, same as adjacent lineBreak() calls) instead of
            // surviving into a multi-line restext value that NormalizeRestextValue would
            // later flatten to a single space, losing the intended break structure.
            if (raw.Contains('\n'))
            {
                return ProcessTextLiteralWithBreaks(raw, currentStyle);
            }

            AddPlainTextRuns(raw, currentStyle);
            return [];
        }

        // this.Vars.X — variable interpolation
        if (IsVarAccess(arg, out var varName))
        {
            AddRun(new TextRun { Text = $"{{{varName}}}", Style = currentStyle });
            return [];
        }

        // this.Vars.X + N — arithmetic in text position (e.g. symp + 2)
        if (arg is BinaryExpressionSyntax binText && IsVarAccess(binText.Left, out var varNameArith) &&
            binText.Right is LiteralExpressionSyntax litArith)
        {
            var op = binText.OperatorToken.Text;
            AddRun(new TextRun { Text = $"{{{varNameArith}{op}{litArith.Token.ValueText}}}", Style = currentStyle });
            return [];
        }

        // macros1.either(...) in text position — inline random text
        if (arg is InvocationExpressionSyntax macroInv2)
        {
            var macroName = GetSimpleMethodName(macroInv2);
            if (macroName == "either")
            {
                var values = ExtractMacroArgs(macroInv2);
                // Flush pending text, emit effect + text run
                var flushNodes = FlushText();
                var tempVar = $"_rnd_{_passageName.Replace(" ", "_").Replace("-", "_")}_{_varRandomSeq++}";
                flushNodes.Add(new EffectNode
                {
                    VarRandom = new() { [tempVar] = new VarRandom { RandomType = "choose-one", Values = values } }
                });
                AddRun(new TextRun { Text = $"{{{tempVar}}}", Style = currentStyle });
                return flushNodes;
            }
            if (macroName == "random")
            {
                var macArgs = macroInv2.ArgumentList.Arguments;
                var min = macArgs.Count > 0 ? TryParseInt(macArgs[0].Expression) : null;
                var max = macArgs.Count > 1 ? TryParseInt(macArgs[1].Expression) : null;
                var tempVar2 = $"_rnd_{_passageName.Replace(" ", "_").Replace("-", "_")}_{_varRandomSeq++}";
                var flushNodes2 = FlushText();
                flushNodes2.Add(new EffectNode
                {
                    VarRandom = new() { [tempVar2] = new VarRandom { RandomType = "range", Min = min, Max = max } }
                });
                AddRun(new TextRun { Text = $"{{{tempVar2}}}", Style = currentStyle });
                return flushNodes2;
            }
        }

        // String concat chain: "prefix " + var + " suffix" → add runs for each leaf
        if (arg is BinaryExpressionSyntax concatText && concatText.OperatorToken.Text == "+"
            && IsTemplateConcatExpr(arg) && ContainsStringOrEither(arg))
        {
            return ProcessTextConcatPart(arg, currentStyle);
        }

        // intLiteral - int.Parse(random(min, max)) → fold to let with range random
        // int.Parse is a no-op since macros1.random already returns an int
        if (arg is BinaryExpressionSyntax subBin && subBin.OperatorToken.Text == "-"
            && subBin.Left is LiteralExpressionSyntax constLit
            && constLit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            var unwrappedRhs = UnwrapIntParse(subBin.Right);
            if (unwrappedRhs is InvocationExpressionSyntax randInv2 && GetSimpleMethodName(randInv2) == "random")
            {
                var ra = randInv2.ArgumentList.Arguments;
                var rMin = ra.Count > 0 ? TryParseInt(ra[0].Expression) : null;
                var rMax = ra.Count > 1 ? TryParseInt(ra[1].Expression) : null;
                if (rMin.HasValue && rMax.HasValue)
                {
                    var constVal = Convert.ToInt32(constLit.Token.Value);
                    var letVar = $"_rnd_{_passageName.Replace(" ", "_").Replace("-", "_")}_{_varRandomSeq++}";
                    var flushNodes3 = FlushText();
                    flushNodes3.Add(new LetNode
                    {
                        Var = letVar,
                        Random = new VarRandom { RandomType = "range", Min = constVal - rMax.Value, Max = constVal - rMin.Value },
                    });
                    AddRun(new TextRun { Text = $"{{{letVar}}}", Style = currentStyle });
                    return flushNodes3;
                }
            }
        }

        // this.Vars.X.ToString() — variable reference in text position
        if (arg is InvocationExpressionSyntax toStringInv
            && GetSimpleMethodName(toStringInv) == "ToString"
            && toStringInv.ArgumentList.Arguments.Count == 0
            && toStringInv.Expression is MemberAccessExpressionSyntax toStringMa
            && IsVarAccess(toStringMa.Expression, out var tsVar))
        {
            AddRun(new TextRun { Text = $"{{{tsVar}}}", Style = currentStyle });
            return [];
        }

        // localVar.Replace("X","Y").Replace(...)... chain — strip player-number suffixes
        if (TryUnwrapReplaceChain(arg, out var replaceBase, out var replacePairs) && replaceBase is not null)
        {
            string? sourceVar = null;
            if (replaceBase is IdentifierNameSyntax replId
                && _localVars.TryGetValue(replId.Identifier.Text, out var localVal)
                && localVal.StartsWith("{") && localVal.EndsWith("}"))
            {
                sourceVar = localVal[1..^1];
            }
            else if (IsVarAccess(replaceBase, out var directVar))
            {
                sourceVar = directVar;
            }

            if (sourceVar is not null)
            {
                var finds = replacePairs.Select(r => r.find).ToList();
                var withVal = replacePairs[0].with;
                var letVar = $"_rpl_{_passageName.Replace(" ", "_").Replace("-", "_")}_{_varRandomSeq++}";
                var letNode = new LetNode
                {
                    Var = letVar,
                    Replace = new VarReplace
                    {
                        Source = sourceVar,
                        Find = finds.Count == 1 ? (object)finds[0] : finds,
                        With = withVal,
                    },
                };
                var flushNodes = FlushText();
                flushNodes.Add(letNode);
                AddRun(new TextRun { Text = $"{{{letVar}}}", Style = currentStyle });
                return flushNodes;
            }
        }

        // Fallback — flag for review
        _report.AddWarning(_passageName, "text() with non-literal arg",
            arg.ToString(), GetLine(arg));
        AddRun(new TextRun { Text = $"{{?{Truncate(arg.ToString())}}}", Style = currentStyle });
        return [];
    }

    // Recursively walks a + chain in text() argument position, adding runs for each recognisable
    // leaf. either() calls flush pending text and emit a preceding EffectNode.
    private List<MwsNode> ProcessTextConcatPart(ExpressionSyntax expr, string? style)
    {
        expr = UnwrapParens(expr);

        if (expr is LiteralExpressionSyntax lit2)
        {
            var raw2 = lit2.Token.ValueText;
            if (raw2.Contains('\n'))
            {
                return ProcessTextLiteralWithBreaks(raw2, style);
            }

            AddPlainTextRuns(raw2, style);
            return [];
        }

        if (IsVarAccess(expr, out var v2))
        {
            AddRun(new TextRun { Text = $"{{{v2}}}", Style = style });
            return [];
        }

        if (expr is InvocationExpressionSyntax inv2 && GetSimpleMethodName(inv2) == "either")
        {
            var values2 = ExtractMacroArgs(inv2);
            var preNodes = FlushText();
            var tmpVar = $"_rnd_{_passageName.Replace(" ", "_").Replace("-", "_")}_{_varRandomSeq++}";
            preNodes.Add(new EffectNode
            {
                VarRandom = new() { [tmpVar] = new VarRandom { RandomType = "choose-one", Values = values2 } },
            });
            AddRun(new TextRun { Text = $"{{{tmpVar}}}", Style = style });
            return preNodes;
        }

        if (expr is BinaryExpressionSyntax bin2 && bin2.OperatorToken.Text == "+")
        {
            var left2 = ProcessTextConcatPart(bin2.Left, style);
            var right2 = ProcessTextConcatPart(bin2.Right, style);
            left2.AddRange(right2);
            return left2;
        }

        return [];
    }

    // ── link() ────────────────────────────────────────────────────────────

    private MwsNode ProcessLink(InvocationExpressionSyntax inv)
    {
        var args = inv.ArgumentList.Arguments;
        string label = "";
        if (args.Count > 0)
        {
            var labelExpr = args[0].Expression;
            label = GetStringValue(labelExpr) ?? TryBuildConcatString(labelExpr) ?? "";
        }
        var target = args.Count > 1 ? GetStringValue(args[1].Expression) : null;
        var hasCallback = args.Count > 2 && !IsNullLiteral(args[2].Expression);

        if (hasCallback)
        {
            // expand_link — fragment ref will be stitched later
            var fragmentRef = ExtractFragmentRef(args[2].Expression);
            var isReplace = IsReplaceEnchant(args[2].Expression);
            return new ExpandLinkNode
            {
                Label = label,
                StateAffecting = isReplace,
                // ExpandNodes populated in FragmentStitchPass
                ExpandNodes = [new UnknownNode
                {
                    OriginalCode = fragmentRef ?? args[2].Expression.ToString(),
                    Note = "fragment:pending_stitch"
                }],
                SourceLine = _currentStatementLine,
            };
        }

        return new LinkNode
        {
            Label = label,
            Target = target ?? "",
            StateAffecting = true,
            SourceLine = _currentStatementLine,
        };
    }

    // ── passage() ─────────────────────────────────────────────────────────

    private List<MwsNode> ProcessPassageInclusionNodes(InvocationExpressionSyntax inv)
    {
        var arg = inv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (arg is null)
        {
            return [Unknown(inv)];
        }

        var passageName = GetStringValue(arg);
        if (passageName is not null)
        {
            return [new IncludePassageNode { Target = passageName }];
        }

        // base.passage(this.Vars.X, ...) — dynamic inclusion via variable
        if (IsVarAccess(arg, out var targetVar))
        {
            return [new IncludePassageNode { Target = $"${{{targetVar}}}" }];
        }

        // base.passage(macros1.either([list of string passage IDs])) — random passage pick.
        // Emit: let var = choose-one(ids); include-passage target: '${var}'
        if (arg is InvocationExpressionSyntax macroInv && GetSimpleMethodName(macroInv) == "either")
        {
            var values = ExtractMacroArgs(macroInv);
            if (values.Count > 0 && values.All(v => v is string))
            {
                var tempVar = $"_rnd_{_passageName.Replace(" ", "_").Replace("-", "_")}_{_varRandomSeq++}";
                return
                [
                    new LetNode
                    {
                        Var = tempVar,
                        Random = new VarRandom { RandomType = "choose-one", Values = values },
                    },
                    new IncludePassageNode { Target = $"${{{tempVar}}}" },
                ];
            }
        }

        return [Unknown(inv)];
    }

    // ── abort() ───────────────────────────────────────────────────────────

    private List<MwsNode> ProcessAbort(InvocationExpressionSyntax inv)
    {
        // abort(goToPassage: "Name") or abort("Name") or abort(this.Vars.X)
        var arg = inv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (arg is null)
        {
            return [Unknown(inv)];
        }

        var target = GetStringValue(arg);
        if (target is not null)
        {
            return [new GotoNode { Target = target }];
        }

        // Variable target: abort(this.Vars.tempeffect)
        if (IsVarAccess(arg, out var varName))
        {
            return [new GotoNode { Target = $"${{{varName}}}" }];
        }

        // abort(either([id1, id2, ...])) — random goto
        if (arg is InvocationExpressionSyntax eitherInv && GetSimpleMethodName(eitherInv) == "either")
        {
            var values = ExtractMacroArgs(eitherInv);
            if (values.Count > 0 && values.All(v => v is string))
            {
                var tempVar = $"_rnd_{_passageName.Replace(" ", "_").Replace("-", "_")}_{_varRandomSeq++}";
                return
                [
                    new LetNode { Var = tempVar, Random = new VarRandom { RandomType = "choose-one", Values = values } },
                    new GotoNode { Target = $"${{{tempVar}}}" },
                ];
            }
        }

        return [Unknown(inv)];
    }

    // ── Expression statements (assignments, Unity API calls) ──────────────

    private List<MwsNode> ProcessExpressionStatement(ExpressionStatementSyntax es)
    {
        var expr = es.Expression;

        // this.Vars.X = value
        if (expr is AssignmentExpressionSyntax assign)
        {
            var effect = ProcessAssignment(assign, out var preamble);
            if (effect is not null)
            {
                return preamble is not null ? [..preamble, effect] : [effect];
            }
        }

        // ViewItemObtain.SetupPassagename = "X"
        if (expr is AssignmentExpressionSyntax assignSetup &&
            IsSetupPassagenameAssignment(assignSetup, out var nextPassage))
        {
            return [new SetupNotificationNode { NextPassage = nextPassage }];
        }

        // ViewController.instance.ChangeView(ViewController.instance.<view>) — every real
        // story-script call site passes .scoreEntry, .generations, or .mainMenu.
        if (expr is InvocationExpressionSyntax cvi && TryGetChangeViewTarget(cvi, out var viewName))
        {
            if (viewName == "scoreEntry")
            {
                return [new GotoNode { Target = "ScoreEntry" }];
            }

            if (viewName == "mainMenu")
            {
                return [new GotoMenuNode { Target = "main_menu" }];
            }

            // .generations (Fear of the Unknown's Privatized3) just switches which native UI panel
            // Unity displays — the hub view the player's already navigating to via ordinary MWS
            // links regardless. No MWS equivalent needed, same "pure view navigation" category as
            // EnableDisableContinueBtn/IsIgnorableCall above — dropped, not emitted as a node.
            if (viewName == "generations")
            {
                return [];
            }

            _report.AddWarning(_passageName, $"ChangeView(...{viewName}) has no recognized mapping", sourceLine: GetLine(cvi));
            return [Unknown(cvi)];
        }

        // PassageTracker.instance.CheckProgress(current, target)
        if (expr is InvocationExpressionSyntax cpInv && IsCheckProgress(cpInv, out var cp))
        {
            // --progress-map: prepend a synthetic _ProgressRound assign when the current passage has
            // a mapped value — kept BEFORE cp/the goto node(s) below (not after) so whichever of
            // those remains the list's own last element for StitchFragments' termination-detection
            // (expand.ExpandNodes[^1] is CheckProgressNode/GotoNode) — see the plan notes on why
            // ordering here isn't cosmetic.
            List<MwsNode> progressPrefix = [];
            if (_progressMapper.TryGetProgressValue(cp!.CurrentPassage, out var progressValue))
            {
                if (progressValue is int p)
                {
                    // Register the synthetic variable so it lands in _variables.yaml — otherwise a
                    // module referencing it (e.g. layout chrome) would hit an undeclared variable,
                    // since this assign is invented here, not discovered from any Vars.X reference in
                    // the source. Same TryAdd-into-shared-dictionary pattern as
                    // V2Serializer.TransformInputAction's synthetic guard variable.
                    _variables?.TryAdd("_ProgressRound", new VarDef { Name = "_ProgressRound", VarType = VarKind.Integer });
                    progressPrefix.Add(new EffectNode { VarSets = new() { ["_ProgressRound"] = p } });
                }
            }
            else if (_progressMapper.IsConfigured)
            {
                _report.AddWarning(_passageName,
                    $"CheckProgress(\"{cp.CurrentPassage}\", ...) has no entry in the progress map", sourceLine: GetLine(cpInv));
            }

            // If target wasn't a plain string literal, try progressively more general fallbacks.
            if (string.IsNullOrEmpty(cp.TargetPassage) && cpInv.ArgumentList.Arguments.Count > 1)
            {
                var targetExpr = cpInv.ArgumentList.Arguments[1].Expression;
                if (targetExpr is IdentifierNameSyntax cpTargetId)
                {
                    // Computed local var: check _localVars and _localPassageConditionals first.
                    // When resolved, return conditional GotoNodes instead of a CheckProgressNode so
                    // that the enclosing expand-link can be converted to navigation by V2Serializer.
                    var targetVar = cpTargetId.Identifier.Text;
                    if (_localVars.TryGetValue(targetVar, out var literalTarget) && !string.IsNullOrEmpty(literalTarget))
                    {
                        return [.. progressPrefix, new GotoNode { Target = literalTarget }];
                    }

                    if (_localPassageConditionals.TryGetValue(targetVar, out var psnNodes))
                    {
                        return [.. progressPrefix, .. BuildGotoNodesFromLetConditionals(psnNodes)];
                    }

                    // Not a known local var — it's a session variable (Vars.X in the original
                    // source, stripped to its bare name by SimplifyCondition's normalization),
                    // valid to reference directly as a target expression instead of leaving this
                    // CheckProgressNode with an unusable empty target. See Fear of the Unknown's
                    // Liberal2 (Vars.Liberal2nextpsg, conditionally assigned via macros1.either()
                    // just before this call) for the real occurrence motivating this — any nodes
                    // that computed the variable's value stay in progressPrefix/preceding sibling
                    // nodes, which StitchFragments' end-of-round-popup collapse now runs as
                    // `onclose` (before target resolves), not passage-render-time `content`.
                    cp.TargetPassage = $"${{{targetVar}}}";
                }
                else
                {
                    // Any other dynamic shape (ternary, boolean expression, arithmetic, ...) found
                    // directly in the argument — e.g. A Time of War's MonarchReign2: CheckProgress(
                    // "MonarchReign2", Vars.advisorcount >= Vars.commtrigger ? "CommemorativeStatueEvent"
                    // : "MonarchReign3"). Translate the raw C# text into an MWS expression the same
                    // way IsSetupPassagenameAssignment's computed-target fallback does.
                    cp.TargetPassage = $"${{{SimplifyCondition(targetExpr.ToString())}}}";
                }
            }
            return [.. progressPrefix, cp];
        }

        // PassageTracker.instance.SetLocationIndicatorIcon(...) — usually paired with
        // locationName/locationIcon assignments; emit as SetLocationNode stub
        if (expr is InvocationExpressionSyntax locInv && IsSetLocationCall(locInv))
        {
            _report.AddInfo(_passageName, $"set_location call detected: {locInv}", GetLine(locInv));
            return [new SetLocationNode()];
        }

        // ViewSpecialEvent.instance.ShowEventPopup() — triggers a transient fade-in/fade-out
        // banner overlay on top of whatever narration content is currently rendered. NOT signaled
        // by a passage tag ("ck2" looked plausible — Cradle tags "ck"→hub, "ck2"→? — but every
        // real ShowEventPopup() call site in Cost of Disease has an *empty* tags array, and neither
        // "ck2"-tagged passage in that source contains this call at all; the two are unrelated).
        // This is the actual, only reliable signal. Emitted at the exact call-site position, not
        // forced to the front of the passage — some call sites have real narrative content before
        // them (e.g. S5Special1a's own bold title + lineBreak precede it).
        if (expr is InvocationExpressionSyntax sev && IsShowEventPopupCall(sev))
        {
            return [new TextNode { Template = "Special Event", Style = "special-event" }];
        }

        // ViewGenerationEnding, ViewPopupPanel utility calls — skip
        if (expr is InvocationExpressionSyntax utilInv && IsIgnorableCall(utilInv))
        {
            return [];
        }

        // Noop comparison: "this.Vars.X op value;" with no assignment.
        // Cradle emits these for conditional blocks that existed in the original
        // Harlowe script but contained no yield statements.
        // Emit as an empty conditional so the structure is preserved for the editor.
        if (expr is BinaryExpressionSyntax cmpExpr &&
            IsComparisonExpression(cmpExpr) &&
            IsVarAccess(cmpExpr.Left, out _))
        {
            var condStr = SimplifyCondition(cmpExpr.ToString());
            return [new ConditionalNode
            {
                Branches = [new ConditionalBranch { Condition = condStr, Nodes = [] }]
            }];
        }

        // EOG delegate invocation: s_OnEndOfGeneration(arg, N) or direct ViewEndOfGeneration.S_OnEndOfGeneration(...)
        if (expr is InvocationExpressionSyntax eogInv && TryBuildEogNode(eogInv, out var eogNode))
        {
            return [eogNode!];
        }

        // ViewEndOfGeneration.S_OnEndOfGeneration?.Invoke(msg, N) — null-conditional form in complete-class files
        if (expr is ConditionalAccessExpressionSyntax condEog &&
            condEog.Expression.ToString().Contains("S_OnEndOfGeneration") &&
            condEog.WhenNotNull is InvocationExpressionSyntax condEogInv)
        {
            var condArgs = condEogInv.ArgumentList.Arguments;
            string? condMsg = null;
            if (condArgs.Count > 0)
            {
                var msgExpr = condArgs[0].Expression;
                if (msgExpr is LiteralExpressionSyntax condLit && condLit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    condMsg = condLit.Token.ValueText;
                }
                else if (msgExpr is IdentifierNameSyntax condId &&
                         _localVars.TryGetValue(condId.Identifier.Text, out var storedMsg))
                {
                    condMsg = storedMsg;
                }
            }
            int condGen = 0;
            if (condArgs.Count > 1 && condArgs[1].Expression is LiteralExpressionSyntax condGenLit)
            {
                int.TryParse(condGenLit.Token.ValueText, out condGen);
            }

            return [new EndOfGenerationNode { Generation = condGen, Message = BuildEogMessageTemplate(condMsg) }];
        }

        // ViewEndOfGeneration.S_OnSetSpecialSetup?.Invoke(title, completedRound, passageName, bodyText)
        if (expr is ConditionalAccessExpressionSyntax condSetup &&
            condSetup.Expression.ToString().Contains("S_OnSetSpecialSetup") &&
            condSetup.WhenNotNull is InvocationExpressionSyntax setupInv)
        {
            return [BuildEogSetupMarker(setupInv.ArgumentList.Arguments)];
        }

        // ViewEndOfRound.instance.SetEndOfRound(body, round, nextPassage, instruction)
        if (expr is InvocationExpressionSyntax setEorInv &&
            setEorInv.Expression is MemberAccessExpressionSyntax setEorMa &&
            setEorMa.Name.Identifier.Text == "SetEndOfRound" &&
            setEorMa.Expression.ToString().Contains("ViewEndOfRound"))
        {
            var eorArgs = setEorInv.ArgumentList.Arguments;
            var body = eorArgs.Count > 0 ? GetStringValue(eorArgs[0].Expression) : null;
            var round = eorArgs.Count > 1 ? TryParseInt(eorArgs[1].Expression) : null;
            var next = eorArgs.Count > 2 ? GetStringValue(eorArgs[2].Expression) : null;
            var instruction = eorArgs.Count > 3 ? GetStringValue(eorArgs[3].Expression) : null;
            return [new ModalNode { Chrome = "end_of_round", Body = body, Round = round, Next = next, Instruction = instruction }];
        }

        // Local string variable compound assignment: text += "..." or text += "str" + var
        // Updates the tracked local var so EOG/input nodes that read it get the appended value.
        if (expr is AssignmentExpressionSyntax localAppend &&
            localAppend.OperatorToken.IsKind(SyntaxKind.PlusEqualsToken) &&
            localAppend.Left is IdentifierNameSyntax localAppendId &&
            _localVars.TryGetValue(localAppendId.Identifier.Text, out var existingVal))
        {
            var rhs2 = localAppend.Right;
            string addition;
            if (rhs2 is LiteralExpressionSyntax appLit && appLit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                addition = _spriteMapper.StripLayoutTags(appLit.Token.ValueText) ?? appLit.Token.ValueText;
            }
            else if (IsTemplateConcatExpr(rhs2) && ContainsStringOrEither(rhs2))
            {
                var appendLets = new List<LetNode>();
                var appendTmpl = new StringBuilder();
                TryExtractTemplateConcat(rhs2, appendLets, appendTmpl);
                addition = appendTmpl.ToString();
            }
            else
            {
                addition = $"{{?{Truncate(rhs2.ToString())}}}";
            }

            _localVars[localAppendId.Identifier.Text] = existingVal + addition;
            return [];
        }

        // localList.Remove(arg) — local int list mutation mirroring a Vars array
        if (expr is InvocationExpressionSyntax listRemoveInv &&
            listRemoveInv.Expression is MemberAccessExpressionSyntax listRemoveMa &&
            listRemoveMa.Name.Identifier.Text == "Remove" &&
            listRemoveMa.Expression is IdentifierNameSyntax listRemoveId &&
            _localIntLists.TryGetValue(listRemoveId.Identifier.Text, out var listVarsRef) &&
            listRemoveInv.ArgumentList.Arguments.Count == 1)
        {
            var listArg = listRemoveInv.ArgumentList.Arguments[0].Expression;
            // Remove(intLiteral) — already covered by var_remove on the Vars array; suppress
            if (listArg is LiteralExpressionSyntax)
            {
                return [];
            }
            // Remove(Vars.X) — emit var_remove on the mirrored Vars array
            if (IsVarAccess(listArg, out var removeVar) && listVarsRef is not null)
            {
                return [new EffectNode { VarRemove = new() { [listVarsRef] = $"{{{removeVar}}}" } }];
            }
        }

        // ViewBiddingSystem.instance.OnShowBidding("passage", BiddingSystem.Mode)
        // Handled by V2Serializer as popup chrome — produce the marker node without
        // logging it as an unknown, since it will be consumed during serialization.
        if (expr is InvocationExpressionSyntax biddingInv &&
            biddingInv.Expression is MemberAccessExpressionSyntax biddingMa &&
            biddingMa.Name.Identifier.Text == "OnShowBidding" &&
            biddingMa.Expression.ToString().Contains("ViewBiddingSystem"))
        {
            var biddingCode = es.ToString().Trim();
            return [new UnknownNode { OriginalCode = Truncate(biddingCode), SourceLine = GetLine(es) }];
        }

        // Unknown expression statement
        var code = es.ToString().Trim();
        _report.AddUnhandled(_passageName, code, GetLine(es));
        return [new UnknownNode { OriginalCode = Truncate(code), SourceLine = GetLine(es) }];
    }

    private static bool IsComparisonExpression(BinaryExpressionSyntax expr) =>
        expr.OperatorToken.Kind() is
            SyntaxKind.EqualsEqualsToken or
            SyntaxKind.ExclamationEqualsToken or
            SyntaxKind.LessThanToken or
            SyntaxKind.GreaterThanToken or
            SyntaxKind.LessThanEqualsToken or
            SyntaxKind.GreaterThanEqualsToken;

    // ── Variable assignment → EffectNode ─────────────────────────────────

    private MwsNode? ProcessAssignment(AssignmentExpressionSyntax assign, out List<MwsNode>? preamble)
    {
        preamble = null;
        // Must be this.Vars.X = ...
        if (!IsVarAccess(assign.Left, out var varName))
        {
            return null;
        }
        // Unwrap outer parentheses so patterns like ((cond) ? a : b) are handled correctly
        ExpressionSyntax right = assign.Right;
        while (right is ParenthesizedExpressionSyntax outerParen)
        {
            right = outerParen.Expression;
        }

        // Vars._SetupImage = "X" (or a two-branch string-literal ternary) — Cradle's setup-popup
        // corner image, consumed only by the Unity view itself (never read back by any passage
        // logic). Emitted as an image node instead of a dead assign; V2Serializer routes it into
        // the enclosing popup's header: list via its Style ("setup-image" — see ImageNode.Style).
        if (varName == "_SetupImage" && TryProcessSetupImageAssignment(right, out var setupImageNode))
        {
            return setupImageNode;
        }

        // Direct literal assignment
        if (right is LiteralExpressionSyntax lit2)
        {
            return new EffectNode
            {
                VarSets = new() { [varName!] = LiteralValue(lit2) }
            };
        }

        // GLOBALS.X — read from the GLOBALS static class (standard var initialization)
        if (right is MemberAccessExpressionSyntax globAccess &&
            globAccess.Expression.ToString() == "GLOBALS")
        {
            return new EffectNode
            {
                VarSets = new() { [varName!] = $"{{global:{globAccess.Name.Identifier.Text}}}" }
            };
        }

        // this.Vars.Y["key"] — array index access with Harlowe ordinal string ("1st", "2nd", …)
        // All ordinals convert to zero-based numeric indices per the MWS array index format.
        if (right is ElementAccessExpressionSyntax elemAccess && IsVarAccess(elemAccess.Expression, out var srcArray))
        {
            var indexArg = elemAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            var ordinal = GetStringValue(indexArg!);
            if (ordinal is not null)
            {
                var idx = HarloweOrdinalToIndex(ordinal);
                return new EffectNode { VarSets = new() { [varName!] = $"{{{srcArray}[{idx}]}}" } };
            }
            var indexStr = indexArg?.ToString() ?? "?";
            return new EffectNode { VarSets = new() { [varName!] = $"{{{srcArray}[{indexStr}]}}" } };
        }

        // ViewPopupPanel.instance.PassageValueString() — string input fallback
        // (normally caught by IsInputPromptIf; this fires only for bare assignments outside an if-else)
        if (right is InvocationExpressionSyntax pvs && GetSimpleMethodName(pvs) == "PassageValueString")
        {
            _report.AddInputPrompt(_passageName, varName!, "string", "", GetLine(pvs));
            return new EffectNode { VarSets = new() { [varName!] = "{input:string}" } };
        }

        // String interpolation or variable reference
        if (IsVarAccess(right, out var srcVar))
        {
            return new EffectNode { VarSets = new() { [varName!] = $"{{{srcVar}}}" } };
        }

        // String concatenation → template string: "The " + townname + " " + either([...])
        // or title + " " + nameA.  Gate: all nodes are recognisable AND at least one is a
        // string literal or either() call (to avoid consuming arithmetic binary expressions).
        if (right is BinaryExpressionSyntax concatBin && concatBin.OperatorToken.Text == "+"
            && IsTemplateConcatExpr(right) && ContainsStringOrEither(right))
        {
            var concatLets = new List<LetNode>();
            var concatTmpl = new StringBuilder();
            TryExtractTemplateConcat(right, concatLets, concatTmpl);
            preamble = concatLets.Count > 0 ? concatLets.Cast<MwsNode>().ToList() : null;
            return new EffectNode { VarSets = new() { [varName!] = concatTmpl.ToString() } };
        }

        // int.Parse(this.Vars.X) + N  →  var_math "+N"
        // this.Vars.X - macros1.a([value]) → var_remove (remove value from array)
        if (right is BinaryExpressionSyntax bin)
        {
            var mathExpr = ExtractVarMath(bin, varName!);
            if (mathExpr is not null)
            {
                return new EffectNode { VarMath = new() { [varName!] = mathExpr } };
            }

            if (bin.OperatorToken.Text == "-")
            {
                // Unwrap int.Parse(this.Vars.X) / int.Parse(macros1.a([N])) if present
                var leftExpr = UnwrapIntParse(bin.Left);
                var rightExpr = UnwrapIntParse(bin.Right);

                if (IsVarAccess(leftExpr, out var removeFrom) && removeFrom == varName &&
                    rightExpr is InvocationExpressionSyntax removeInv &&
                    GetSimpleMethodName(removeInv) == "a")
                {
                    var removeValues = ExtractMacroArgs(removeInv);
                    if (removeValues.Count == 1)
                    {
                        return new EffectNode { VarRemove = new() { [varName!] = removeValues[0].ToString()! } };
                    }
                }

                // var - either([vals]) / var - int.Parse(either([vals]))
                // → let _rnd = choose-one; var_math "-{_rnd}"
                if (rightExpr is InvocationExpressionSyntax eitherSub &&
                    GetSimpleMethodName(eitherSub) == "either")
                {
                    var eitherVals = ExtractMacroArgs(eitherSub);
                    var rndSub = $"_rnd_{_passageName}_{_varRandomSeq++}";
                    preamble = [new LetNode
                    {
                        Var = rndSub,
                        Random = new VarRandom { RandomType = "choose-one", Values = eitherVals },
                    }];
                    return new EffectNode { VarMath = new() { [varName!] = $"-{{{rndSub}}}" } };
                }
            }

            // random(min, max) + var  or  var + random(min, max)  →  let _rnd = range, varName = _rnd + addend.
            // Numeric arithmetic — must use VarMath (a bare expr like "heart + _rnd_0"), never
            // VarSets: VarSets' {var} display-brace syntax is only valid in display fields, not
            // assign/let's expr (which is always a bare expression — see mws-format-latest.md §4).
            // A {var}+{var} string here would either get misparsed as a record literal by the
            // engine's expression parser, or (if it contains any literal text/becomes multi-token)
            // get silently promoted whole to a restext:// key by RestextCollector's "template
            // string" heuristic, then substituted back verbatim at load time with the braces still
            // in it — see GameSession's ExprParseException regression test for the exact crash.
            if (bin.OperatorToken.Text == "+")
            {
                InvocationExpressionSyntax? randInv = null;
                ExpressionSyntax? addend = null;
                if (bin.Left is InvocationExpressionSyntax lInv && GetSimpleMethodName(lInv) == "random")
                    { randInv = lInv; addend = bin.Right; }
                else if (bin.Right is InvocationExpressionSyntax rInv && GetSimpleMethodName(rInv) == "random")
                    { randInv = rInv; addend = bin.Left; }

                if (randInv is not null && addend is not null)
                {
                    var rArgs = randInv.ArgumentList.Arguments;
                    var rMin = rArgs.Count > 0 ? TryParseInt(rArgs[0].Expression) : null;
                    var rMax = rArgs.Count > 1 ? TryParseInt(rArgs[1].Expression) : null;
                    var rndName = $"_rnd_{_passageName}_{_varRandomSeq++}";
                    preamble = [new LetNode
                    {
                        Var = rndName,
                        Random = new VarRandom { RandomType = "range", Min = rMin, Max = rMax },
                    }];
                    string addendExpr = IsVarAccess(addend, out var addVar) ? addVar!
                        : addend is LiteralExpressionSyntax addLit ? addLit.Token.ValueText
                        : addend.ToString();
                    return new EffectNode
                    {
                        VarMath = new() { [varName!] = $"= {addendExpr} + {rndName}" }
                    };
                }

                // Sum of 3+ variable references: VarA + VarB + VarC + ...
                var sumVars = new List<string>();
                if (TryExtractVarAddChain(bin, sumVars) && sumVars.Count >= 3)
                {
                    return new EffectNode { VarMath = new() { [varName!] = $"= {string.Join(" + ", sumVars)}" } };
                }
            }

            // General arithmetic with int.Parse() stripping.
            // Handles compound expressions like: int.Parse(Vars.X) + (int.Parse(Vars.Y) * N)
            // String-type variables inside int.Parse() become parseInt(varName) in the output.
            {
                var arithExpr = TryBuildArithExpr(bin);
                if (arithExpr is not null)
                {
                    return new EffectNode { VarMath = new() { [varName!] = $"= {arithExpr}" } };
                }
            }
        }

        // macros1.either(values)
        if (right is InvocationExpressionSyntax macroInv)
        {
            var macroName = GetSimpleMethodName(macroInv);
            switch (macroName)
            {
                case "either":
                {
                    var values = ExtractMacroArgs(macroInv);
                    return new EffectNode
                    {
                        VarRandom = new() { [varName!] = new VarRandom
                        {
                            RandomType = "choose-one",
                            Values = values,
                        }}
                    };
                }
                case "random":
                {
                    var args2 = macroInv.ArgumentList.Arguments;
                    var min = args2.Count > 0 ? TryParseInt(args2[0].Expression) : null;
                    var max = args2.Count > 1 ? TryParseInt(args2[1].Expression) : null;
                    return new EffectNode
                    {
                        VarRandom = new() { [varName!] = new VarRandom
                        {
                            RandomType = "range",
                            Min = min,
                            Max = max,
                        }}
                    };
                }
                case "shuffled":
                {
                    // shuffled([(HarloweSpread)this.Vars.X]) where X == varName → X = X.shuffle()
                    var args = macroInv.ArgumentList.Arguments;
                    if (args.Count == 1 &&
                        args[0].Expression is ArrayCreationExpressionSyntax arr &&
                        arr.Initializer?.Expressions.Count == 1 &&
                        arr.Initializer.Expressions[0] is CastExpressionSyntax cast &&
                        cast.Type.ToString() == "HarloweSpread" &&
                        IsVarAccess(cast.Expression, out var shuffleVar) &&
                        shuffleVar == varName)
                    {
                        return new EffectNode { VarSets = new() { [varName!] = $"{varName}.shuffle()" } };
                    }
                    var values = ExtractMacroArgs(macroInv);
                    return new EffectNode
                    {
                        VarRandom = new() { [varName!] = new VarRandom
                        {
                            RandomType = "shuffled_array",
                            Values = values,
                        }}
                    };
                }
                case "a":
                {
                    var values = ExtractMacroArgs(macroInv);
                    // Track int arrays so localList initializers can find the mirror
                    if (varName is not null && values.All(v => v is int or long))
                        {
                            _passageIntArrayVars[varName] = values.Select(v => Convert.ToInt32(v)).ToList();
                        }

                        return new EffectNode
                    {
                        VarSets = new() { [varName!] = values }
                    };
                }
                case "num":
                    // Type coercion — treat as no-op var_math
                    return new EffectNode { VarMath = new() { [varName!] = "+0" } };
            }
        }

        // Ternary (cond) ? a : b  — including chained (cond1) ? a : (cond2) ? b : c
        if (right is ConditionalExpressionSyntax ternary)
        {
            var condStr = SimplifyCondition(UnwrapParens(ternary.Condition).ToString());
            var whenTrue = UnwrapParens(ternary.WhenTrue);
            var whenFalse = UnwrapParens(ternary.WhenFalse);

            // Chained ternary: whenFalse is itself a ternary — flatten and emit SwitchNode or multi-branch ConditionalNode
            if (whenFalse is ConditionalExpressionSyntax)
            {
                var branches = FlattenTernaryChain(condStr, whenTrue, whenFalse);
                return BuildChainedTernaryNode(varName!, branches, GetLine(ternary));
            }

            // Simple ternary: if either branch is a macro call, decompose into a ConditionalNode
            if (IsMacroCall(whenTrue) || IsMacroCall(whenFalse))
            {
                return new ConditionalNode
                {
                    Branches =
                    [
                        new ConditionalBranch { Condition = condStr, Nodes = [BuildBranchEffect(varName!, whenTrue)] },
                        new ConditionalBranch { Else = true, Nodes = [BuildBranchEffect(varName!, whenFalse)] },
                    ],
                };
            }

            // Simple string ternary — warn if either branch can't be cleanly expressed
            if (!IsSimpleValue(whenTrue) || !IsSimpleValue(whenFalse))
            {
                _report.AddWarning(_passageName, $"Unresolved ternary branch for {varName}",
                    ternary.ToString(), GetLine(ternary));
            }

            return new EffectNode
            {
                VarSets = new() { [varName!] = $"({condStr}) ? {GetStringOrLiteral(whenTrue)} : {GetStringOrLiteral(whenFalse)}" }
            };
        }

        // localList[Random.Range(0, localList.Count)] — random pick from mirrored Vars array
        if (right is ElementAccessExpressionSyntax pickAccess &&
            pickAccess.Expression is IdentifierNameSyntax pickListId &&
            _localIntLists.TryGetValue(pickListId.Identifier.Text, out var pickVarsRef) &&
            pickVarsRef is not null &&
            pickAccess.ArgumentList.Arguments.Count == 1 &&
            IsRandomRangeFromCount(pickAccess.ArgumentList.Arguments[0].Expression, pickListId.Identifier.Text))
        {
            var letVar = $"_pick_{_passageName.Replace(" ", "_").Replace("-", "_")}_{_varRandomSeq++}";
            preamble = [new LetNode { Var = letVar, PickFrom = pickVarsRef }];
            return new EffectNode { VarSets = new() { [varName!] = $"{{{letVar}}}" } };
        }

        // Fallback — record raw expression
        _report.AddWarning(_passageName, $"Unhandled assignment RHS for {varName}",
            right.ToString(), GetLine(right));
        return new EffectNode { VarSets = new() { [varName!] = $"?({Truncate(right.ToString())})" } };
    }

    // ── Conditional (if/else) → ConditionalNode ───────────────────────────

    private (List<MwsNode> Hoisted, ConditionalNode Node) BuildConditional(IfStatementSyntax ifs)
    {
        var branches = new List<ConditionalBranch>();
        var hoisted = new List<MwsNode>();
        StatementSyntax? current = ifs;

        while (current is IfStatementSyntax currentIf)
        {
            if (HasUnresolvableApiCall(currentIf.Condition))
            {
                _report.AddWarning(_passageName, "Unresolved API call in condition",
                    currentIf.Condition.ToString(), GetLine(currentIf));
            }

            var (condHoisted, condText) = HoistInlineEithers(currentIf.Condition);
            hoisted.AddRange(condHoisted);

            var condStr = SimplifyCondition(condText);
            // Translate localIntList.Count references to varsArrayRef.count() — the engine has no
            // bare count(...) function (only rand_between/max/min/parseInt are registered function
            // calls; array element count is the .count() method — see EvalArrayMethod), so the
            // earlier count(varsArrayRef) form threw "Unknown function 'count'" at evaluation time.
            foreach (var (listName, varsRef) in _localIntLists)
            {
                if (varsRef is not null)
                {
                    condStr = condStr.Replace($"{listName}.Count", $"{varsRef}.count()");
                }
            }

            var bodyNodes = currentIf.Statement is BlockSyntax blk
                ? VisitStatements(blk.Statements)
                : VisitStatements([currentIf.Statement]);

            branches.Add(new ConditionalBranch { Condition = condStr, Nodes = bodyNodes });
            current = currentIf.Else?.Statement;
        }

        if (current is not null)
        {
            var elseNodes = current is BlockSyntax elseBlk
                ? VisitStatements(elseBlk.Statements)
                : VisitStatements([current]);
            branches.Add(new ConditionalBranch { Else = true, Nodes = elseNodes });
        }

        return (hoisted, new ConditionalNode { Branches = branches });
    }

    // Finds every macros1.either(...) call embedded directly in a condition expression — as
    // opposed to one used as the right-hand side of an assignment, which ProcessExpressionStatement
    // already converts to a VarRandom — and hoists each into its own synthetic `let` node emitted
    // as a sibling before the conditional, replacing the call's own source text with a reference to
    // that temp variable. Cradle draws a fresh, independent value every time either() is called, so
    // a condition can't call it inline (MWS conditions only test already-bound state); hoisting to a
    // `let` right before the conditional preserves a once-per-occurrence draw with the same timing.
    private (List<MwsNode> Hoisted, string Condition) HoistInlineEithers(ExpressionSyntax condition)
    {
        var hoisted = new List<MwsNode>();
        var text = condition.ToString();
        foreach (var inv in condition.DescendantNodesAndSelf()
                     .OfType<InvocationExpressionSyntax>()
                     .Where(i => GetSimpleMethodName(i) == "either"))
        {
            var original = inv.ToString();
            if (!text.Contains(original))
            {
                continue; // already substituted by an earlier, textually-identical occurrence
            }

            var values = ExtractMacroArgs(inv);
            var tempVar = $"_rnd_{_passageName.Replace(" ", "_").Replace("-", "_")}_{_varRandomSeq++}";
            hoisted.Add(new LetNode { Var = tempVar, Random = new VarRandom { RandomType = "choose-one", Values = values } });
            text = text.Replace(original, tempVar);
        }

        return (hoisted, text);
    }

    // ── Input prompt detection ─────────────────────────────────────────────
    // Two patterns handled:
    //   A) if (PassageValueNumber() >= 0) { store } else { OnGenerationBtn(...) }
    //   B) if (this.ispopup [&&...]) { OnGenerationBtn(...) } else { store = PassageValue*() }

    private bool IsInputPromptIf(IfStatementSyntax ifs, out InputPromptNode? node)
    {
        node = null;
        var condStr = ifs.Condition.ToString();
        var elseStmt = ifs.Else?.Statement;
        if (elseStmt is null)
        {
            return false;
        }

        // Pattern A: main condition contains PassageValueNumber → else has OnGenerationBtn
        if (condStr.Contains("PassageValueNumber"))
        {
            var elseStmts = elseStmt is BlockSyntax ba ? ba.Statements.AsEnumerable() : [elseStmt];
            foreach (var s in elseStmts)
            {
                if (s is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }
                    && GetSimpleMethodName(inv) == "OnGenerationBtn")
                {
                    var args = inv.ArgumentList.Arguments;
                    var promptId = args.Count > 0 ? GetStringValue(args[0].Expression) ?? "" : "";
                    var text = args.Count > 1 ? ExtractTemplateString(args[1].Expression) : "";
                    var inputType = args.Count > 2 ? GetStringValue(args[2].Expression) ?? "string" : "string";
                    var storeIn = ExtractStoreIn(ifs.Statement);
                    var resolvedType = inputType == "number" ? "number" : "string";

                    node = new InputPromptNode
                    {
                        PromptId = promptId,
                        Text = text,
                        InputType = resolvedType,
                        StoreIn = storeIn ?? promptId.ToLowerInvariant(),
                        ResumePassage = _passageName,
                    };
                    _report.AddInputPrompt(_passageName, node.StoreIn, resolvedType, text);
                    return true;
                }
            }
        }

        // Pattern B: main condition contains ispopup → if-body has OnGenerationBtn, else stores value
        // The OnGenerationBtn may be nested inside an inner conditional (e.g. players == 2 selects prompt text)
        if (condStr.Contains("ispopup"))
        {
            var ifStmts = ifs.Statement is BlockSyntax bb ? bb.Statements.AsEnumerable() : [ifs.Statement];
            var genBtn = FindFirstOnGenerationBtn(ifStmts);
            if (genBtn is not null)
            {
                var storeIn = ExtractStoreIn(elseStmt);
                if (storeIn is not null)
                {
                    var args = genBtn.ArgumentList.Arguments;
                    var promptId = args.Count > 0 ? GetStringValue(args[0].Expression) ?? _passageName : _passageName;
                    var text = args.Count > 1 ? ExtractTemplateString(args[1].Expression) : "";
                    var inputType = args.Count > 2 ? GetStringValue(args[2].Expression) ?? "string" : "string";
                    var resolvedType = inputType == "number" ? "number" : "string";

                    node = new InputPromptNode
                    {
                        PromptId = promptId,
                        Text = text,
                        InputType = resolvedType,
                        StoreIn = storeIn,
                        ResumePassage = _passageName,
                    };
                    _report.AddInputPrompt(_passageName, storeIn, resolvedType, text);
                    return true;
                }
            }
        }

        return false;
    }

    // Searches stmts (and one level of nested if/else) for the first OnGenerationBtn invocation.
    private static InvocationExpressionSyntax? FindFirstOnGenerationBtn(IEnumerable<StatementSyntax> stmts)
    {
        foreach (var s in stmts)
        {
            if (s is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }
                && GetSimpleMethodName(inv) == "OnGenerationBtn")
            {
                return inv;
            }

            if (s is IfStatementSyntax innerIf)
            {
                var thenStmts = innerIf.Statement is BlockSyntax bt ? bt.Statements.AsEnumerable() : [innerIf.Statement];
                var found = FindFirstOnGenerationBtn(thenStmts);
                if (found is not null)
                {
                    return found;
                }

                if (innerIf.Else is not null)
                {
                    var elseStmts2 = innerIf.Else.Statement is BlockSyntax be ? be.Statements.AsEnumerable() : [innerIf.Else.Statement];
                    found = FindFirstOnGenerationBtn(elseStmts2);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
        }
        return null;
    }

    // Unwraps a chain of .Replace(literal, literal) calls, returning the base expression and
    // the ordered list of (find, with) pairs. Returns false if the chain is not purely Replace calls
    // with string literals, or if there are no Replace calls at all.
    private static bool TryUnwrapReplaceChain(
        ExpressionSyntax expr,
        out ExpressionSyntax? baseExpr,
        out List<(string find, string with)> pairs)
    {
        pairs = [];
        baseExpr = null;
        ExpressionSyntax current = UnwrapParens(expr);

        // Walk the outer-most chain: expr is the result of the last Replace call
        while (current is InvocationExpressionSyntax ci
            && ci.Expression is MemberAccessExpressionSyntax ma
            && ma.Name.Identifier.Text == "Replace"
            && ci.ArgumentList.Arguments.Count == 2
            && ci.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax findLit
            && ci.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax withLit)
        {
            pairs.Insert(0, (findLit.Token.ValueText, withLit.Token.ValueText));
            current = UnwrapParens(ma.Expression);
        }

        if (pairs.Count == 0)
        {
            return false;
        }

        baseExpr = current;
        return true;
    }

    // Returns true if expr is Random.Range(0, listName.Count) — the canonical C# random-index
    // expression used to pick an element from a local List.
    private static bool IsRandomRangeFromCount(ExpressionSyntax expr, string listName)
    {
        if (expr is not InvocationExpressionSyntax inv)
        {
            return false;
        }

        if (GetSimpleMethodName(inv) != "Range")
        {
            return false;
        }

        var args = inv.ArgumentList.Arguments;
        if (args.Count != 2)
        {
            return false;
        }

        if (args[0].Expression is not LiteralExpressionSyntax zeroLit ||
            !zeroLit.IsKind(SyntaxKind.NumericLiteralExpression) ||
            Convert.ToInt32(zeroLit.Token.Value) != 0)
        {
            return false;
        }

        if (args[1].Expression is not MemberAccessExpressionSyntax countMa)
        {
            return false;
        }

        if (countMa.Name.Identifier.Text != "Count")
        {
            return false;
        }

        if (countMa.Expression is not IdentifierNameSyntax countId)
        {
            return false;
        }

        return countId.Identifier.Text == listName;
    }

    // Two-statement input prompt:
    //   if (this.ispopup [&& this.iscreation*]) { ... OnGenerationBtn(...) }
    //   if (PassageValueNumber() >= 0 | PassageValueString() != null) { this.Vars.X = ...; }
    private bool TryDetectInputPrompt(IfStatementSyntax setupIf, IfStatementSyntax storeIf, out InputPromptNode? node)
    {
        node = null;

        // Setup if must contain ispopup in the condition and have no else branch
        var setupCond = setupIf.Condition.ToString();
        if (!setupCond.Contains("ispopup"))
        {
            return false;
        }

        if (setupIf.Else is not null)
        {
            return false;
        }

        var setupBody = setupIf.Statement is BlockSyntax sb ? sb.Statements.AsEnumerable() : [setupIf.Statement];
        InvocationExpressionSyntax? genBtn = null;
        foreach (var s in setupBody)
        {
            if (s is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }
                && GetSimpleMethodName(inv) == "OnGenerationBtn")
            {
                genBtn = inv; break;
            }
        }
        if (genBtn is null)
        {
            return false;
        }

        // Store if must contain a PassageValue* call in its condition
        var storeCond = storeIf.Condition.ToString();
        if (!storeCond.Contains("PassageValueNumber") && !storeCond.Contains("PassageValueString"))
        {
            return false;
        }

        var storeIn = ExtractStoreIn(storeIf.Statement);
        if (storeIn is null)
        {
            return false;
        }

        var args = genBtn.ArgumentList.Arguments;
        var promptId = args.Count > 0 ? GetStringValue(args[0].Expression) ?? _passageName : _passageName;
        var text = args.Count > 1 ? ExtractTemplateString(args[1].Expression) : "";
        var inputType = args.Count > 2 ? GetStringValue(args[2].Expression) ?? "string" : "string";

        var resolvedType = inputType == "number" ? "number" : "string";
        node = new InputPromptNode
        {
            PromptId = promptId,
            Text = text,
            InputType = resolvedType,
            StoreIn = storeIn,
            ResumePassage = _passageName,
        };
        _report.AddInputPrompt(_passageName, storeIn, resolvedType, text);
        return true;
    }

    private string ExtractTemplateString(ExpressionSyntax expr)
    {
        expr = UnwrapParens(expr);
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return lit.Token.ValueText;
        }

        if (IsVarAccess(expr, out var v))
        {
            return $"{{{v}}}";
        }

        if (expr is BinaryExpressionSyntax bin && bin.OperatorToken.Text == "+")
        {
            return ExtractTemplateString(bin.Left) + ExtractTemplateString(bin.Right);
        }

        _report.AddWarning(_passageName, "Unhandled template expression", expr.ToString(), GetLine(expr));
        return $"?({Truncate(expr.ToString())})";
    }

    // Returns true when every leaf of the + chain is a recognisable template part:
    // string literal, int literal, var access, or either() invocation.
    private bool IsTemplateConcatExpr(ExpressionSyntax expr)
    {
        expr = UnwrapParens(expr);
        if (expr is LiteralExpressionSyntax)
        {
            return true;
        }

        if (IsVarAccess(expr, out _))
        {
            return true;
        }

        if (expr is InvocationExpressionSyntax inv && GetSimpleMethodName(inv) == "either")
        {
            return true;
        }

        if (expr is BinaryExpressionSyntax bin && bin.OperatorToken.Text == "+")
        {
            return IsTemplateConcatExpr(bin.Left) && IsTemplateConcatExpr(bin.Right);
        }

        return false;
    }

    // Returns true when the + chain contains at least one string literal or either() call,
    // distinguishing it from a purely numeric arithmetic expression.
    private static bool ContainsStringOrEither(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return true;
        }

        if (expr is InvocationExpressionSyntax inv)
        {
            var name = (inv.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text
                ?? (inv.Expression as IdentifierNameSyntax)?.Identifier.Text;
            if (name == "either")
            {
                return true;
            }
        }
        if (expr is BinaryExpressionSyntax bin && bin.OperatorToken.Text == "+")
        {
            return ContainsStringOrEither(bin.Left) || ContainsStringOrEither(bin.Right);
        }

        return false;
    }

    // Walks a binary + chain and appends to template. either() calls produce a let node.
    // Pre-condition: IsTemplateConcatExpr(expr) == true (all leaves are known types).
    private void TryExtractTemplateConcat(ExpressionSyntax expr, List<LetNode> lets, StringBuilder template)
    {
        expr = UnwrapParens(expr);

        if (expr is LiteralExpressionSyntax lit)
        {
            template.Append(lit.IsKind(SyntaxKind.StringLiteralExpression)
                ? lit.Token.ValueText
                : lit.Token.ValueText);
            return;
        }
        if (IsVarAccess(expr, out var varName))
        {
            template.Append($"{{{varName}}}");
            return;
        }
        if (expr is InvocationExpressionSyntax inv && GetSimpleMethodName(inv) == "either")
        {
            var values = ExtractMacroArgs(inv);
            var rndName = $"_rnd_{_passageName}_{_varRandomSeq++}";
            lets.Add(new LetNode
            {
                Var = rndName,
                Random = new VarRandom { RandomType = "choose-one", Values = values },
            });
            template.Append($"{{{rndName}}}");
            return;
        }
        if (expr is BinaryExpressionSyntax bin && bin.OperatorToken.Text == "+")
        {
            TryExtractTemplateConcat(bin.Left, lets, template);
            TryExtractTemplateConcat(bin.Right, lets, template);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // Parses a newline-free text() literal into pending TextRuns: TextMesh Pro rich-text
    // sprite spans, or (falling back) a single run with layout tags stripped.
    private void AddPlainTextRuns(string raw, string? currentStyle)
    {
        var richRuns = _spriteMapper.TryParseRichText(raw);
        if (richRuns is not null)
        {
            foreach (var (t, assetRef) in richRuns)
            {
                if (assetRef is not null)
                {
                    AddRun(new TextRun { AssetRef = assetRef });
                }
                else if (!string.IsNullOrEmpty(t))
                {
                    AddRun(new TextRun { Text = t, Style = currentStyle });
                }
            }
        }
        else
        {
            var cleaned = _spriteMapper.StripLayoutTags(raw);
            if (!string.IsNullOrEmpty(cleaned))
            {
                AddRun(new TextRun { Text = cleaned, Style = currentStyle });
            }
        }
    }

    // Splits a text() literal containing embedded \n into text/break segments: the text
    // before each newline run is flushed as its own TextNode, then one BreakNode is emitted
    // per '\n' character (ConsolidateBreaks later merges runs of 2+ into a single paragraph
    // break, matching how consecutive lineBreak() calls already behave). Each text segment
    // is trimmed only on the side(s) adjacent to a break, so a lone trailing "\n" with
    // nothing after it contributes just a BreakNode, no empty/whitespace-only TextNode.
    private List<MwsNode> ProcessTextLiteralWithBreaks(string raw, string? currentStyle)
    {
        var result = new List<MwsNode>();
        var segments = Regex.Split(raw, "(\n+)");

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0)
            {
                continue;
            }

            if (segment[0] == '\n')
            {
                result.AddRange(FlushText());
                for (var n = 0; n < segment.Length; n++)
                {
                    result.Add(new BreakNode { WithinStyleScope = _styleStack.Count > 0 });
                }
                continue;
            }

            var text = segment;
            if (i + 1 < segments.Length && segments[i + 1].Length > 0 && segments[i + 1][0] == '\n')
            {
                text = text.TrimEnd();
            }
            if (i > 0 && segments[i - 1].Length > 0 && segments[i - 1][0] == '\n')
            {
                text = text.TrimStart();
            }

            if (text.Length > 0)
            {
                AddPlainTextRuns(text, currentStyle);
                // Flush eagerly (mirroring the per-statement FlushAndAdd flush every other
                // yield-return case gets) rather than leaving this run pending — the caller's
                // own FlushAndAdd() flushes BEFORE appending our returned nodes, so a run left
                // pending here would surface ahead of everything we already returned.
                result.AddRange(FlushText());
            }
        }

        return result;
    }

    private List<MwsNode> FlushText()
    {
        // No runs: nothing to emit. Keep pending layout state (align, size) in place so it
        // survives the FlushAndAdd([]) calls that happen for each standalone layout tag.
        // Emit any queued unpreserved-tag warnings now so they fire with the correct source line.
        if (_pendingRuns.Count == 0)
        {
            var emptyTags = _pendingUnpreservedTags;
            _pendingUnpreservedTags = [];
            foreach (var (tag, line) in emptyTags)
            {
                _report.AddWarning(_passageName, $"Unpreserved layout tag: `{tag}`", null, line);
            }

            return [];
        }

        // Capture and reset all state now that we have runs to emit.
        var runs = _pendingRuns;
        var startLine = _pendingTextStartLine;
        var align = _pendingAlign;
        var size = _pendingSize;
        var sizeLine = _pendingSizeLine;
        var unpreservedTags = _pendingUnpreservedTags;

        _pendingRuns = [];
        _pendingTextStartLine = null;
        _pendingAlign = null;
        _pendingSize = null;
        _pendingSizeLine = null;
        _pendingUnpreservedTags = [];

        foreach (var (tag, line) in unpreservedTags)
        {
            _report.AddWarning(_passageName, $"Unpreserved layout tag: `{tag}`", null, line);
        }

        // Single AssetRef run with a <size=N> wrapper → image node
        if (size is not null && runs.Count == 1 && runs[0].AssetRef is not null && runs[0].Text is null)
        {
            return [new ImageNode
            {
                AssetRef = runs[0].AssetRef!,
                Size = size,
                Align = align,
                SourceLine = startLine,
            }];
        }

        // <size> surrounds an inline icon in a mixed template — size cannot be preserved
        if (size is not null && runs.Any(r => r.AssetRef is not null))
        {
            _report.AddWarning(_passageName,
                $"Unpreserved `<size={size}>`: surrounds an inline icon in a mixed text template — size not preserved; cannot convert to image node.",
                null, sizeLine ?? startLine);
        }

        return [new TextNode { Runs = runs, SourceLine = startLine, Align = align }];
    }

    // Adds a run to the pending buffer; records the source line of the first run in the group.
    private void AddRun(TextRun run)
    {
        if (_pendingRuns.Count == 0)
        {
            _pendingTextStartLine = _currentStatementLine;
        }

        _pendingRuns.Add(run);
    }

    // Regex for open <align=...> and <size=...> tags used by ProcessLayoutTag.
    private static readonly Regex AlignOpenTagRegex = new(
        @"^<align\s*=\s*""?([^""\s>]+)""?\s*/?>$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SizeOpenTagRegex = new(
        @"^<size\s*=\s*([^>]+?)\s*/?>$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyClosingTagRegex = new(
        @"^</\w", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Called for standalone HTML-like layout tags (e.g. <align="center">, <size=200>, </align>).
    // Sets or records state that is applied when the pending runs are flushed.
    // Closing tags are no-ops — state is cleared at FlushText regardless.
    private void ProcessLayoutTag(string raw)
    {
        // Closing tag — ignore; state is cleared at FlushText
        if (AnyClosingTagRegex.IsMatch(raw))
        {
            return;
        }

        var alignMatch = AlignOpenTagRegex.Match(raw);
        if (alignMatch.Success)
        {
            _pendingAlign = NormalizeAlign(alignMatch.Groups[1].Value);
            return;
        }

        var sizeMatch = SizeOpenTagRegex.Match(raw);
        if (sizeMatch.Success)
        {
            _pendingSize = sizeMatch.Groups[1].Value.Trim();
            _pendingSizeLine = _currentStatementLine;
            return;
        }

        // Any other open tag (line-height, color, b, i, etc.) — record for warning at FlushText
        _pendingUnpreservedTags.Add((raw, _currentStatementLine));
    }

    private static string NormalizeAlign(string value) => value.ToLowerInvariant() switch
    {
        "justify" => "justified",
        var v => v,
    };

    private List<MwsNode> FlushAndAdd(List<MwsNode> nodes)
    {
        var result = FlushText();
        result.AddRange(nodes);
        return result;
    }

    private static bool IsCradleCleanupStatement(StatementSyntax stmt)
    {
        // StyleScope styleScope = null;  (local declaration)
        if (stmt is LocalDeclarationStatementSyntax decl)
        {
            return decl.Declaration.Type.ToString().Contains("StyleScope");
        }

        // stylescopeN = null;  (Cradle nested scope cleanup — any variable name starting with "styleScope")
        if (stmt is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assign })
        {
            if (assign.Left is IdentifierNameSyntax id &&
                id.Identifier.Text.StartsWith("styleScope", StringComparison.OrdinalIgnoreCase) &&
                IsNullLiteral(assign.Right))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsStyleScopeUsing(UsingStatementSyntax us, out string? scopeName, out string? hookId)
    {
        scopeName = null; hookId = null;
        var expr = us.Expression ?? us.Declaration?.Variables.FirstOrDefault()?.Initializer?.Value;
        if (expr is not InvocationExpressionSyntax inv)
        {
            return false;
        }

        if (GetSimpleMethodName(inv) != "styleScope")
        {
            return false;
        }

        var args = inv.ArgumentList.Arguments;
        scopeName = args.Count > 0 ? GetStringValue(args[0].Expression) : null;
        hookId = args.Count > 1 ? GetStringValue(args[1].Expression) : null;
        return scopeName is not null;
    }

    private static string? ExtractStoreIn(StatementSyntax ifStatement)
    {
        // Unwrap else-if: the else clause may be another IfStatementSyntax — look inside its body
        if (ifStatement is IfStatementSyntax innerIf)
        {
            return ExtractStoreIn(innerIf.Statement);
        }

        var stmts = ifStatement is BlockSyntax b ? b.Statements.AsEnumerable() : [ifStatement];
        foreach (var s in stmts)
        {
            if (s is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assign } &&
                IsVarAccess(assign.Left, out var varName))
            {
                return varName;
            }
        }
        return null;
    }

    private static bool IsVarAccess(ExpressionSyntax expr, out string? varName)
    {
        varName = null;
        if (expr is not MemberAccessExpressionSyntax outer)
        {
            return false;
        }

        // this.Vars.X  (old partial-class files)
        if (outer.Expression is MemberAccessExpressionSyntax innerMember &&
            innerMember.Name.Identifier.Text == "Vars")
        {
            varName = outer.Name.Identifier.Text;
            return true;
        }

        // Vars.X  (complete-class files — no explicit "this." prefix)
        if (outer.Expression is IdentifierNameSyntax id && id.Identifier.Text == "Vars")
        {
            varName = outer.Name.Identifier.Text;
            return true;
        }

        return false;
    }

    private static bool IsNullLiteral(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression);

    private static string GetSimpleMethodName(InvocationExpressionSyntax inv)
    {
        if (inv.Expression is MemberAccessExpressionSyntax m)
        {
            return m.Name.Identifier.Text;
        }

        if (inv.Expression is IdentifierNameSyntax id)
        {
            return id.Identifier.Text;
        }

        return "";
    }

    private static string? GetStringValue(ExpressionSyntax expr) => expr switch
    {
        LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression)
            => lit.Token.ValueText,
        BinaryExpressionSyntax bin when bin.OperatorToken.IsKind(SyntaxKind.PlusToken)
            => GetStringValue(bin.Left) is { } l && GetStringValue(bin.Right) is { } r ? l + r : null,
        _ => null,
    };

    // Converts Harlowe ordinal strings ("1st", "2nd", "3rd", …) to zero-based integers.
    private static int HarloweOrdinalToIndex(string ordinal)
    {
        // Strip the 2-character suffix (st/nd/rd/th) and parse the number.
        if (ordinal.Length >= 3 && int.TryParse(ordinal[..^2], out var n))
        {
            return n - 1;
        }

        return 0;
    }

    private static object LiteralValue(LiteralExpressionSyntax lit)
    {
        if (lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return lit.Token.ValueText;
        }

        if (lit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            var val = lit.Token.Value;
            return val is double d ? (int)d : val ?? 0;
        }
        if (lit.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            return true;
        }

        if (lit.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            return false;
        }

        return lit.Token.ValueText;
    }

    private static int? TryParseInt(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit &&
            double.TryParse(lit.Token.ValueText, out var d))
        {
            return (int)d;
        }

        return null;
    }

    private static ExpressionSyntax UnwrapParens(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax p)
        {
            expr = p.Expression;
        }

        return expr;
    }

    // Strips int.Parse(...) wrapper if present; otherwise returns expr unchanged.
    private static ExpressionSyntax UnwrapIntParse(ExpressionSyntax expr)
    {
        if (expr is InvocationExpressionSyntax inv &&
            (inv.Expression is IdentifierNameSyntax { Identifier.Text: "Parse" } ||
             (inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.Text == "Parse")) &&
            inv.ArgumentList.Arguments.Count == 1)
        {
            return inv.ArgumentList.Arguments[0].Expression;
        }

        return expr;
    }

    private static bool IsMacroCall(ExpressionSyntax expr) =>
        expr is InvocationExpressionSyntax inv &&
        GetSimpleMethodName(inv) is "either" or "random" or "a" or "shuffled";

    private static bool IsSimpleValue(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax || IsVarAccess(expr, out _);

    // True when a condition expression contains Unity singleton API calls that can't be expressed in MWS.
    private static bool HasUnresolvableApiCall(ExpressionSyntax condition) =>
        condition.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(m => m.Expression.ToString().Contains(".instance"));

    private static readonly Regex EqCondRegex = new(@"^(\w+)\s*==\s*(.+)$", RegexOptions.Compiled);

    private List<(string Cond, ExpressionSyntax Expr)> FlattenTernaryChain(
        string firstCond, ExpressionSyntax firstTrue, ExpressionSyntax firstFalse)
    {
        var result = new List<(string, ExpressionSyntax)>();
        result.Add((firstCond, firstTrue));
        var current = firstFalse;
        while (current is ConditionalExpressionSyntax nested)
        {
            var nestedCond = SimplifyCondition(UnwrapParens(nested.Condition).ToString());
            var nestedTrue = UnwrapParens(nested.WhenTrue);
            result.Add((nestedCond, nestedTrue));
            current = UnwrapParens(nested.WhenFalse);
        }
        result.Add(("else", current));
        return result;
    }

    private MwsNode BuildChainedTernaryNode(string varName,
        List<(string Cond, ExpressionSyntax Expr)> branches, int? sourceLine)
    {
        // All non-else conditions must be "var == value" equality on the same variable → SwitchNode
        var condBranches = branches.Where(b => b.Cond != "else").ToList();
        var matches = condBranches.Select(b => EqCondRegex.Match(b.Cond)).ToList();
        if (matches.All(m => m.Success) &&
            matches.Select(m => m.Groups[1].Value).Distinct().Count() == 1)
        {
            var switchVar = matches[0].Groups[1].Value;
            var cases = new List<SwitchCase>();
            foreach (var (cond, expr) in branches)
            {
                if (cond == "else")
                {
                    cases.Add(new SwitchCase { Default = true, Nodes = [BuildBranchEffect(varName, expr)] });
                }
                else
                {
                    var rawVal = EqCondRegex.Match(cond).Groups[2].Value.Trim();
                    object matchVal = int.TryParse(rawVal, out var n) ? (object)n : rawVal;
                    cases.Add(new SwitchCase { Match = matchVal, Nodes = [BuildBranchEffect(varName, expr)] });
                }
            }
            return new SwitchNode { On = switchVar, Cases = cases, SourceLine = sourceLine };
        }

        // Fallback: multi-branch ConditionalNode
        return new ConditionalNode
        {
            Branches = branches.Select(b => b.Cond == "else"
                ? new ConditionalBranch { Else = true, Nodes = [BuildBranchEffect(varName, b.Expr)] }
                : new ConditionalBranch { Condition = b.Cond, Nodes = [BuildBranchEffect(varName, b.Expr)] })
                .ToList(),
            SourceLine = sourceLine,
        };
    }

    private EffectNode BuildBranchEffect(string varName, ExpressionSyntax expr)
    {
        if (IsVarAccess(expr, out var v))
        {
            return new EffectNode { VarSets = new() { [varName] = $"{{{v}}}" } };
        }

        if (expr is LiteralExpressionSyntax lit)
        {
            return new EffectNode { VarSets = new() { [varName] = lit.Token.ValueText } };
        }

        if (expr is InvocationExpressionSyntax inv)
        {
            var macroName = GetSimpleMethodName(inv);
            if (macroName == "either")
            {
                var values = ExtractMacroArgs(inv);
                return new EffectNode { VarRandom = new() { [varName] = new VarRandom { RandomType = "choose-one", Values = values } } };
            }
            if (macroName == "random")
            {
                var rArgs = inv.ArgumentList.Arguments;
                var min = rArgs.Count > 0 ? TryParseInt(rArgs[0].Expression) : null;
                var max = rArgs.Count > 1 ? TryParseInt(rArgs[1].Expression) : null;
                return new EffectNode { VarRandom = new() { [varName] = new VarRandom { RandomType = "range", Min = min, Max = max } } };
            }
        }
        _report.AddWarning(_passageName, $"Unhandled branch expression for {varName}", expr.ToString(), GetLine(expr));
        return new EffectNode { VarSets = new() { [varName] = $"?({Truncate(expr.ToString())})" } };
    }

    private static string? GetStringOrLiteral(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit)
        {
            return lit.Token.ValueText;
        }

        if (IsVarAccess(expr, out var v))
        {
            return $"{{{v}}}";
        }

        return expr.ToString();
    }

    // Resolves a + chain to a string, handling string literals, Vars.X accesses, and
    // local variables registered in _localVars. Returns null if any leaf is unrecognised.
    private string? TryBuildConcatString(ExpressionSyntax expr)
    {
        expr = UnwrapParens(expr);
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return lit.Token.ValueText;
        }

        if (IsVarAccess(expr, out var v))
        {
            return $"{{{v}}}";
        }
        // Vars.X.ToString() — strip the no-op .ToString() call
        if (expr is InvocationExpressionSyntax toStringInv
            && toStringInv.ArgumentList.Arguments.Count == 0
            && toStringInv.Expression is MemberAccessExpressionSyntax toStringMa2
            && toStringMa2.Name.Identifier.Text == "ToString"
            && IsVarAccess(toStringMa2.Expression, out var tsv))
        {
            return $"{{{tsv}}}";
        }

        if (expr is IdentifierNameSyntax id && _localVars.TryGetValue(id.Identifier.Text, out var lv))
        {
            return lv;
        }

        if (expr is BinaryExpressionSyntax bin && bin.OperatorToken.Text == "+")
        {
            var left = TryBuildConcatString(bin.Left);
            var right = TryBuildConcatString(bin.Right);
            if (left is not null && right is not null)
            {
                return left + right;
            }
        }
        return null;
    }

    private List<object> ExtractMacroArgs(InvocationExpressionSyntax inv)
    {
        // macros1.either(new StoryVar[] { 1, 2, "x" }) or macros1.shuffled(new StoryVar[] { ... })
        var result = new List<object>();
        var args = inv.ArgumentList.Arguments;

        foreach (var arg in args)
        {
            if (arg.Expression is ArrayCreationExpressionSyntax arr && arr.Initializer is not null)
            {
                foreach (var elem in arr.Initializer.Expressions)
                {
                    if (elem is LiteralExpressionSyntax lit2)
                    {
                        result.Add(LiteralValue(lit2));
                    }
                    else if (IsVarAccess(elem, out var vn))
                    {
                        result.Add($"{{{vn}}}");
                    }
                    else
                    {
                        result.Add((object?)TryBuildConcatString(elem) ?? elem.ToString());
                    }
                }
            }
            else if (arg.Expression is LiteralExpressionSyntax lit3)
            {
                result.Add(LiteralValue(lit3));
            }
            else if (IsVarAccess(arg.Expression, out var vn2))
            {
                result.Add($"{{{vn2}}}");
            }
            else if (TryBuildConcatString(arg.Expression) is { } sv)
            {
                result.Add(sv);
            }
        }
        return result;
    }

    // Recursively builds a clean arithmetic expression string by stripping int.Parse() wrappers.
    // Returns null if the expression contains patterns that can't be reduced (e.g. non-variable
    // arguments, unknown constructs). For string-type variables inside int.Parse(), emits
    // parseInt(varName) to preserve the explicit coercion in MWS expression syntax.
    private string? TryBuildArithExpr(ExpressionSyntax expr)
    {
        // Strip outer parentheses
        while (expr is ParenthesizedExpressionSyntax paren)
        {
            expr = paren.Expression;
        }

        // int.Parse(Vars.X) — strip when X is int/unknown type; emit parseInt() when string type
        if (expr is InvocationExpressionSyntax parseInv &&
            (parseInv.Expression is IdentifierNameSyntax { Identifier.Text: "Parse" } ||
             (parseInv.Expression is MemberAccessExpressionSyntax parseMa &&
              parseMa.Name.Identifier.Text == "Parse")) &&
            parseInv.ArgumentList.Arguments.Count == 1)
        {
            var inner = parseInv.ArgumentList.Arguments[0].Expression;
            if (!IsVarAccess(inner, out var parsedVar) || parsedVar is null)
            {
                return null;
            }
            // String-declared variable: keep the coercion using the MWS parseInt() function
            if (_variables is not null &&
                _variables.TryGetValue(parsedVar, out var parseDef) &&
                parseDef.VarType == VarKind.String)
            {
                return $"parseInt({parsedVar})";
            }
            // Int or unknown type: int.Parse is redundant — discard it
            return parsedVar;
        }

        // Bare Vars.X access
        if (IsVarAccess(expr, out var bareVar) && bareVar is not null)
        {
            return bareVar;
        }

        // Numeric literal
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            return lit.Token.ValueText;
        }

        // Binary arithmetic: recurse into both sides
        if (expr is BinaryExpressionSyntax bin)
        {
            var op = bin.OperatorToken.Text;
            if (op is not ("+" or "-" or "*" or "/"))
            {
                return null;
            }

            var left = TryBuildArithExpr(bin.Left);
            if (left is null)
            {
                return null;
            }

            var right = TryBuildArithExpr(bin.Right);
            if (right is null)
            {
                return null;
            }

            return $"{left} {op} {right}";
        }

        return null;
    }

    private static bool TryExtractVarAddChain(ExpressionSyntax expr, List<string> vars)
    {
        var unwrapped = UnwrapIntParse(expr);
        if (IsVarAccess(unwrapped, out var v) && v is not null) { vars.Add(v); return true; }
        if (unwrapped is BinaryExpressionSyntax bin && bin.OperatorToken.Text == "+")
        {
            return TryExtractVarAddChain(bin.Left, vars) && TryExtractVarAddChain(bin.Right, vars);
        }

        return false;
    }

    private static string? ExtractVarMath(BinaryExpressionSyntax bin, string varName)
    {
        // int.Parse(this.Vars.X) OP N
        if (bin.Right is LiteralExpressionSyntax rLit && rLit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            var op = bin.OperatorToken.Text;
            var val = rLit.Token.ValueText;
            if (op == "+" || op == "-" || op == "*")
            {
                return $"{op}{val}";
            }
        }
        // N + int.Parse(this.Vars.X)
        if (bin.Left is LiteralExpressionSyntax lLit && lLit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            if (bin.OperatorToken.Text == "+")
            {
                return $"+{lLit.Token.ValueText}";
            }
        }
        // int.Parse(Vars.X) OP int.Parse(Vars.Y)  →  "+{Y}" / "-{Y}"
        // Both sides must unwrap to var accesses; this guards against consuming random()+var.
        var lhsUnwrapped2 = UnwrapIntParse(bin.Left);
        var rhsUnwrapped2 = UnwrapIntParse(bin.Right);
        if (IsVarAccess(lhsUnwrapped2, out _) && IsVarAccess(rhsUnwrapped2, out var rhsVar))
        {
            var op2 = bin.OperatorToken.Text;
            if (op2 == "+" || op2 == "-")
            {
                return $"{op2}{{{rhsVar}}}";
            }
        }
        return null;
    }

    private static string? ExtractFragmentRef(ExpressionSyntax callbackExpr)
    {
        // () => base.enchantHook("hookId", Cmd, new Func<...>(this.passageN_Fragment_M), false)
        if (callbackExpr is ParenthesizedLambdaExpressionSyntax lambda &&
            lambda.ExpressionBody is InvocationExpressionSyntax enchant)
        {
            var enchantArgs = enchant.ArgumentList.Arguments;
            foreach (var a in enchantArgs)
            {
                if (a.Expression is ObjectCreationExpressionSyntax objCreate)
                {
                    var innerArgs = objCreate.ArgumentList?.Arguments;
                    if (innerArgs.HasValue && innerArgs.Value.Count > 0)
                    {
                        var memberAccess = innerArgs.Value[0].Expression?.ToString();
                        return memberAccess;
                    }
                }
            }
        }
        return callbackExpr.ToString();
    }

    private static bool IsReplaceEnchant(ExpressionSyntax callbackExpr)
    {
        // True if HarloweEnchantCommand.Replace, false if .None
        return !callbackExpr.ToString().Contains("HarloweEnchantCommand.None");
    }

    private static bool IsCheckProgress(InvocationExpressionSyntax inv, out CheckProgressNode? node)
    {
        node = null;
        if (GetSimpleMethodName(inv) != "CheckProgress")
        {
            return false;
        }

        var args = inv.ArgumentList.Arguments;
        node = new CheckProgressNode
        {
            CurrentPassage = args.Count > 0 ? GetStringValue(args[0].Expression) ?? "" : "",
            TargetPassage = args.Count > 1 ? GetStringValue(args[1].Expression) ?? "" : "",
        };
        return true;
    }

    private static bool IsSetLocationCall(InvocationExpressionSyntax inv) =>
        GetSimpleMethodName(inv) == "SetLocationIndicatorIcon";

    private static bool IsShowEventPopupCall(InvocationExpressionSyntax inv) =>
        GetSimpleMethodName(inv) == "ShowEventPopup";

    // ViewController.instance.ChangeView(ViewController.instance.<view>) — extracts the trailing
    // "<view>" identifier off the single argument (a member-access chain), e.g. "scoreEntry" from
    // "ViewController.instance.scoreEntry". False if this isn't a ChangeView call, or its argument
    // isn't a plain member access this can read a trailing name off of.
    private static bool TryGetChangeViewTarget(InvocationExpressionSyntax inv, out string? viewName)
    {
        viewName = null;
        if (GetSimpleMethodName(inv) != "ChangeView")
        {
            return false;
        }

        if (inv.ArgumentList.Arguments is not [{ Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: { } name } }])
        {
            return false;
        }

        viewName = name;
        return true;
    }

    private bool IsSetupPassagenameAssignment(AssignmentExpressionSyntax assign, out string? nextPassage)
    {
        nextPassage = null;
        if (!assign.Left.ToString().Contains("SetupPassagename"))
        {
            return false;
        }

        var literal = GetStringValue(assign.Right);
        if (literal is not null)
        {
            nextPassage = literal;
        }
        else
        {
            // Complex or ternary expression — convert to MWS expression syntax and wrap in ${}.
            var expr = SimplifyCondition(assign.Right.ToString());
            nextPassage = $"${{{expr}}}";
            _report.AddWarning(_passageName, "SetupPassagename uses a computed expression",
                assign.Right.ToString(), GetLine(assign.Right));
        }
        return true;
    }

    // Handles the two shapes Vars._SetupImage is actually assigned in this source: a plain string
    // literal, or a two-branch ternary between two string literals. image.asset has no dynamic/
    // ${expr} evaluation at the engine level (unlike e.g. link.target), so a ternary expands into a
    // ConditionalNode with one ImageNode per branch instead of asking for that engine capability
    // for a single occurrence. Any other shape is left unhandled (returns false) so the caller falls
    // through to the ordinary assignment handling — but is still reported, so an unrecognized shape
    // shows up in the extraction report instead of silently producing a dead assign.
    private bool TryProcessSetupImageAssignment(ExpressionSyntax right, out MwsNode? node)
    {
        var literal = GetStringValue(right);
        if (literal is not null)
        {
            node = new ImageNode { AssetRef = $"image://setup/{literal}", Style = "setup-image" };
            return true;
        }

        if (right is ConditionalExpressionSyntax ternary)
        {
            var whenTrue = GetStringValue(ternary.WhenTrue);
            var whenFalse = GetStringValue(ternary.WhenFalse);
            if (whenTrue is not null && whenFalse is not null)
            {
                var condition = SimplifyCondition(ternary.Condition.ToString());
                node = new ConditionalNode
                {
                    Branches =
                    [
                        new ConditionalBranch
                        {
                            Condition = condition,
                            Nodes = [new ImageNode { AssetRef = $"image://setup/{whenTrue}", Style = "setup-image" }],
                        },
                        new ConditionalBranch
                        {
                            Else = true,
                            Nodes = [new ImageNode { AssetRef = $"image://setup/{whenFalse}", Style = "setup-image" }],
                        },
                    ],
                };
                return true;
            }
        }

        _report.AddWarning(_passageName, "_SetupImage uses an unrecognized expression shape (not a string literal or a two-branch string ternary)",
            right.ToString(), GetLine(right));
        node = null;
        return false;
    }

    private static bool IsIgnorableCall(InvocationExpressionSyntax inv)
    {
        var name = GetSimpleMethodName(inv);
        // Unity API calls with no MWS equivalent — logged but not emitted as nodes. ShowEventPopup
        // used to be here too — it has a real MWS equivalent now, see IsShowEventPopupCall above,
        // checked earlier in the dispatch so it never reaches this list. ChangeView is NOT here —
        // every ChangeView(...) call is already claimed earlier by the more specific
        // TryGetChangeViewTarget branch above, which handles the "no MWS equivalent, safe to drop"
        // .generations case itself; adding "ChangeView" to this list would be unreachable dead code.
        return name is "Clear" or "EnableDisableContinueBtn" or "OnGenerationBtn"
            or "PassageValueNumber" or "Log";
    }

    private static bool IsIgnorableAssignment(ExpressionStatementSyntax es)
    {
        if (es.Expression is not AssignmentExpressionSyntax assign)
        {
            return false;
        }
        // this.ispasscode = ..., this.ispopup = ... — Cradle double-trigger guards (old files: explicit this.)
        if (assign.Left is MemberAccessExpressionSyntax m && m.Expression.ToString() == "this")
        {
            return m.Name.Identifier.Text is "ispasscode" or "ispopup" or "iscreationA";
        }
        // ispasscode = ..., ispopup = ... — same guards without "this." (complete-class files)
        if (assign.Left is IdentifierNameSyntax id)
        {
            return id.Identifier.Text is "ispasscode" or "ispopup" or "iscreationA";
        }

        return false;
    }

    private string SimplifyCondition(string cond)
    {
        // Normalize "this.Vars.X" and "Vars.X" → "X"
        cond = Regex.Replace(cond, @"(?:this\.)?Vars\.(\w+)", m => m.Groups[1].Value);
        // Normalize "int.Parse(...)" → the inner expression
        cond = Regex.Replace(cond, @"int\.Parse\((\w+)\)", "$1");
        // Normalize compound falsy: "x == 0 || x == """ → "!x"
        cond = Regex.Replace(cond, @"(\w+)\s*==\s*0\s*\|\|\s*\1\s*==\s*""""", "!$1");

        // String.IsNullOrEmpty(x) has no MWS equivalent — the engine has no "String" namespace/type
        // at all, so it passed straight through untranslated and threw "Unknown variable 'String'"
        // at evaluation time. There's no separate "null" StoryValue variant a variable could ever
        // hold beyond an empty string, so StoryValue.AsBool's existing falsy handling (0/""/"0")
        // already covers everything IsNullOrEmpty would check — these three rules translate it away
        // entirely rather than trying to represent it as a real MWS call. The argument is resolved
        // through _localVars first — "string s = Vars.warriorA.ToString();" registers s as a
        // {warriorA} alias (see the "string varName = Vars.X" declaration handling below), and
        // String.IsNullOrEmpty is just as often called on that local alias as on Vars.X directly
        // (real occurrence: Fear of the Unknown's Player1Statsfin — String.IsNullOrEmpty(s) — a bare
        // "s" isn't a declared MWS variable at all, only "warriorA" is).
        string ResolveLocalAlias(string name) =>
            _localVars.TryGetValue(name, out var val) && val.StartsWith('{') && val.EndsWith('}')
                ? val[1..^1]
                : name;
        //   "!x || String.IsNullOrEmpty(x)" → "!x" — the clause is fully redundant with the just-
        //   collapsed !x above (real occurrence: Fear of the Unknown's PrivateHomeTile, "bhome == 0
        //   || bhome == "" || String.IsNullOrEmpty(bhome)").
        cond = Regex.Replace(cond, @"!(\w+)\s*\|\|\s*String\.IsNullOrEmpty\(\1\)",
            m => $"!{ResolveLocalAlias(m.Groups[1].Value)}");
        //   "!String.IsNullOrEmpty(x)" → "x" (x is truthy, i.e. non-empty) — real occurrence: A
        //   Time of War's newmeat check.
        cond = Regex.Replace(cond, @"!String\.IsNullOrEmpty\((\w+)\)",
            m => ResolveLocalAlias(m.Groups[1].Value));
        //   Any remaining un-prefixed "String.IsNullOrEmpty(x)" → "!x".
        cond = Regex.Replace(cond, @"String\.IsNullOrEmpty\((\w+)\)",
            m => $"!{ResolveLocalAlias(m.Groups[1].Value)}");

        // Mathf.Max(new T[] { a, b, c }) → max(a, b, c)
        cond = Regex.Replace(cond,
            @"Mathf\.Max\(\s*new\s+\w+\s*\[\s*\]\s*\{([^}]*)\}\s*\)",
            m =>
            {
                var args = m.Groups[1].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .Where(a => a.Length > 0);
                return $"max({string.Join(", ", args)})";
            });
        // Mathf.Max(a, b) → max(a, b)
        cond = Regex.Replace(cond, @"Mathf\.Max\(", "max(");

        // Substitute local computed variables (LINQ aliases → aggregate expressions)
        foreach (var (name, expr) in _localComputedVars)
        {
            cond = Regex.Replace(cond, $@"\b{Regex.Escape(name)}\b", _ => expr);
        }

        return cond.Trim();
    }

    // Builds a heading template string from visitor-emitted TextNodes, preserving
    // icon references ({icon:slug}) and bold/italic markers (**text** / _text_).
    private static string BuildHeadingTemplate(List<MwsNode> nodes)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var n in nodes)
        {
            if (n is not TextNode tn)
            {
                continue;
            }

            foreach (var run in tn.Runs)
            {
                if (run.AssetRef is not null)
                {
                    sb.Append($"{{icon:{run.AssetRef.Replace("icon://", "")}}}");
                }
                else if (!string.IsNullOrEmpty(run.Text))
                {
                    if (run.Style == "bold")
                    {
                        sb.Append(MwsExprHelper.WrapEmphasis(run.Text, "**"));
                    }
                    else if (run.Style == "italic")
                    {
                        sb.Append(MwsExprHelper.WrapEmphasis(run.Text, "_"));
                    }
                    else
                    {
                        sb.Append(run.Text);
                    }
                }
            }
        }
        return MwsExprHelper.CollapseAdjacentSpaces(sb.ToString().Trim());
    }

    private UnknownNode Unknown(SyntaxNode node)
    {
        var code = node.ToString().Trim();
        _report.AddUnhandled(_passageName, code, GetLine(node));
        return new UnknownNode { OriginalCode = Truncate(code) };
    }

    // Returns the 1-based line number in the original source file.
    // The wrapped source prepends 2 lines, so Roslyn's 0-based line - 1 = original 1-based line.
    private int? GetLine(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        if (!span.IsValid)
        {
            return null;
        }

        var line0 = span.StartLinePosition.Line;
        // line0 == 0: synthetic/rehomed node (all positions zeroed) — skip.
        if (line0 == 0)
        {
            return null;
        }

        var line = line0 + _lineOffset;
        return line >= 1 ? line : null;
    }

    private static string Truncate(string s) =>
        s.Length > 200 ? s[..200] + "…" : s;

    // Returns nodes to emit (possibly empty list) if the local declaration was handled,
    // or null to fall through to the unknown-node handler.
    private List<MwsNode>? ProcessLocalDeclaration(LocalDeclarationStatementSyntax decl)
    {
        bool anyHandled = false;
        var nodes = new List<MwsNode>();

        foreach (var v in decl.Declaration.Variables)
        {
            var name = v.Identifier.Text;
            var init = v.Initializer?.Value;
            if (init is null)
            {
                continue;
            }

            // string varName = "literal" → track for EOG message resolution
            if (init is LiteralExpressionSyntax strLit &&
                strLit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                _localVars[name] = strLit.Token.ValueText;
                anyHandled = true;
                continue;
            }

            // string varName = this.Vars.X  — store as "{X}" placeholder
            // string varName = this.Vars.X.ToString()  — same, strip .ToString()
            {
                var initUnwrapped = init;
                if (initUnwrapped is InvocationExpressionSyntax toStrInv &&
                    toStrInv.Expression is MemberAccessExpressionSyntax toStrMa &&
                    toStrMa.Name.Identifier.Text == "ToString" &&
                    toStrInv.ArgumentList.Arguments.Count == 0)
                {
                    initUnwrapped = toStrMa.Expression;
                }

                if (IsVarAccess(initUnwrapped, out var trackedVar))
                {
                    _localVars[name] = $"{{{trackedVar}}}";
                    anyHandled = true;
                    continue;
                }
            }

            // string varName = "str " + var + "suffix"  — template concat
            if (IsTemplateConcatExpr(init) && ContainsStringOrEither(init))
            {
                var concatLets = new List<LetNode>();
                var concatTmpl = new StringBuilder();
                TryExtractTemplateConcat(init, concatLets, concatTmpl);
                _localVars[name] = concatTmpl.ToString();
                foreach (var let in concatLets)
                {
                    nodes.Add(let);
                }

                anyHandled = true;
                continue;
            }

            // Action<...> varName = ViewEndOfGeneration.S_OnEndOfGeneration → track delegate
            if (init.ToString().Contains("S_OnEndOfGeneration"))
            {
                _eogDelegates.Add(name);
                anyHandled = true;
                continue;
            }

            // List<T> varName = new List<T>(new T[] { this.Vars.A, ... }) → let array node
            if (TryExtractListInit(decl.Declaration.Type, init, out var listVars))
            {
                _localArrayVars[name] = listVars!;
                nodes.Add(new LetNode { Var = name, Array = listVars! });
                anyHandled = true;
                continue;
            }

            // LINQ countif: int num = (from value in play where value == play.Max() ...).Count()
            // Emits: LetNode for max_<array> and tracks num → countif expression
            if (TryExtractLinqCountIf(name, init, nodes))
            {
                anyHandled = true;
                continue;
            }

            // string localVar = (var op N) ? either([...]) : ((var op M) ? either([...]) : ...)
            // Chained ternary where all branches are either() calls on the same comparison variable.
            // Emits a ConditionalNode with one LetNode per branch; registers the local var
            // so subsequent text() references resolve to the synthetic let variable.
            if (init is ConditionalExpressionSyntax ternaryInit)
            {
                var topCond = SimplifyCondition(UnwrapParens(ternaryInit.Condition).ToString());
                var topTrue = UnwrapParens(ternaryInit.WhenTrue);
                var topFalse = UnwrapParens(ternaryInit.WhenFalse);

                if (topFalse is ConditionalExpressionSyntax)
                {
                    var branches = FlattenTernaryChain(topCond, topTrue, topFalse);
                    bool allEither = branches.All(b => b.Cond == "else" ||
                        (b.Expr is InvocationExpressionSyntax ei && GetSimpleMethodName(ei) == "either"));
                    if (allEither)
                    {
                        var letVarName = $"_rnd_{_passageName}_{_varRandomSeq++}";
                        _localVars[name] = $"{{{letVarName}}}";
                        var condBranches = branches.Select(b =>
                        {
                            List<MwsNode> branchNodes = [];
                            if (b.Expr is InvocationExpressionSyntax eitherInv)
                            {
                                branchNodes.Add(new LetNode
                                {
                                    Var = letVarName,
                                    Random = new VarRandom
                                    {
                                        RandomType = "choose-one",
                                        Values = ExtractMacroArgs(eitherInv),
                                    },
                                });
                            }

                            return b.Cond == "else"
                                ? new ConditionalBranch { Else = true, Nodes = branchNodes }
                                : new ConditionalBranch { Condition = b.Cond, Nodes = branchNodes };
                        }).ToList();
                        nodes.Add(new ConditionalNode { Branches = condBranches, SourceLine = _currentStatementLine });
                        anyHandled = true;
                        continue;
                    }
                }

                // String-literal ternary (possibly nested): passage name computed via conditions.
                // Stores conditional LetNode structure; consumed by BuildEogSetupMarker when the
                // local var is passed as passageName to S_OnSetSpecialSetup.
                {
                    var condNodes = TryBuildPassageNameConditional(name, ternaryInit);
                    if (condNodes is not null)
                    {
                        _localPassageConditionals[name] = condNodes;
                        anyHandled = true;
                        continue;
                    }
                }
            }

            // List<int> varName = new List<int> { intLiterals... }
            // Mirror of a Vars array — track and suppress (the Vars assignment is already emitted)
            if (decl.Declaration.Type.ToString().Contains("List<int>") &&
                init is ObjectCreationExpressionSyntax listCreate &&
                listCreate.Initializer is InitializerExpressionSyntax listInit &&
                listInit.Expressions.All(e => e is LiteralExpressionSyntax l &&
                    l.IsKind(SyntaxKind.NumericLiteralExpression)))
            {
                var intVals = listInit.Expressions
                    .OfType<LiteralExpressionSyntax>()
                    .Select(l => Convert.ToInt32(l.Token.Value))
                    .ToList();
                var mirrorVar = _passageIntArrayVars
                    .Where(kv => kv.Value.SequenceEqual(intVals))
                    .Select(kv => kv.Key)
                    .FirstOrDefault();
                _localIntLists[name] = mirrorVar;
                anyHandled = true;
                continue;
            }
        }

        return anyHandled ? nodes : null;
    }

    // Recognizes: new List<T>(new T[] { this.Vars.A, this.Vars.B, ... })
    private static bool TryExtractListInit(TypeSyntax type, ExpressionSyntax init, out List<string>? vars)
    {
        vars = null;
        if (!type.ToString().StartsWith("List<", StringComparison.Ordinal))
        {
            return false;
        }

        if (init is not ObjectCreationExpressionSyntax objCreate)
        {
            return false;
        }

        var arg = objCreate.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        if (arg is not ArrayCreationExpressionSyntax arr || arr.Initializer is null)
        {
            return false;
        }

        var result = new List<string>();
        foreach (var elem in arr.Initializer.Expressions)
        {
            if (IsVarAccess(elem, out var varName))
            {
                result.Add(varName!);
            }
            else
            {
                return false;
            }
        }
        vars = result;
        return true;
    }

    // Recognizes: int num = (from value in <arr> where value == <arr>.Max() select value).Count<int>()
    // Emits:      LetNode { Var = "max_<arr>", Compute = "max(<scalars making up arr>)" }
    // Tracks:     _localComputedVars[num] = "<arr>.countif(\"=\" + max_<arr>)"
    private bool TryExtractLinqCountIf(string varName, ExpressionSyntax init, List<MwsNode> emittedNodes)
    {
        if (init is not InvocationExpressionSyntax countInv)
        {
            return false;
        }

        if (GetSimpleMethodName(countInv) != "Count")
        {
            return false;
        }

        var countReceiver = (countInv.Expression as MemberAccessExpressionSyntax)?.Expression;

        // Query syntax: (from value in <arr> where value == <arr>.Max() select value).Count()
        if (countReceiver is ParenthesizedExpressionSyntax paren &&
            paren.Expression is QueryExpressionSyntax query)
        {
            if (query.FromClause.Expression is not IdentifierNameSyntax fromId)
            {
                return false;
            }

            var arrayVarName = fromId.Identifier.Text;
            var whereClause = query.Body.Clauses.OfType<WhereClauseSyntax>().FirstOrDefault();
            if (whereClause?.Condition is not BinaryExpressionSyntax whereBin)
            {
                return false;
            }

            if (!whereBin.IsKind(SyntaxKind.EqualsExpression))
            {
                return false;
            }

            if (!whereBin.Right.ToString().Equals($"{arrayVarName}.Max()", StringComparison.Ordinal))
            {
                return false;
            }

            EmitLinqCountIf(varName, arrayVarName, emittedNodes);
            return true;
        }

        // Method syntax: <arr>.Where(x => x == <arr>.Max()).Count()
        if (countReceiver is InvocationExpressionSyntax whereInv &&
            GetSimpleMethodName(whereInv) == "Where" &&
            (whereInv.Expression as MemberAccessExpressionSyntax)?.Expression is IdentifierNameSyntax whereArrId)
        {
            var arrayVarName = whereArrId.Identifier.Text;
            var lambdaArg = whereInv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (lambdaArg is SimpleLambdaExpressionSyntax lambda &&
                lambda.Body is BinaryExpressionSyntax lambdaBin &&
                lambdaBin.IsKind(SyntaxKind.EqualsExpression) &&
                lambdaBin.Right.ToString().Equals($"{arrayVarName}.Max()", StringComparison.Ordinal))
            {
                EmitLinqCountIf(varName, arrayVarName, emittedNodes);
                return true;
            }
        }

        return false;
    }

    // Shared by both TryExtractLinqCountIf branches (query syntax and method syntax), which
    // recognize the identical "count elements tied for the max" idiom in two different C# spellings.
    //
    // max(...) is a FUNCTION taking individual scalar args (see ExpressionEvaluator.EvalFunction) —
    // there is no array-taking max in the expression language, so max(<arr>) itself is invalid
    // (throws "Cannot convert array to int" once <arr> — a genuine MWS array, per the LetNode.Array
    // temporary-array mechanism TryExtractListInit emits for its declaration — reaches AsInt()).
    // When <arr> was built from named scalars via that same mechanism (_localArrayVars, populated at
    // its declaration site), use them directly: max(playA, playB, ...). Otherwise fall back to
    // sorting descending and taking the first element — there's no dedicated "max element" array
    // method either.
    //
    // countif(...) is a METHOD on the array (arr.countif(pattern), see EvalArrayMethod), not a bare
    // function — and its pattern argument must be a real expression (quoted "=" concatenated with
    // the computed max), not unquoted bare text, which the engine has no way to interpolate inside
    // an expr (see the whole String.IsNullOrEmpty saga for why bare braces/text don't work either).
    //
    // Real-world crash: Cost of Disease's MostInvestigated passage.
    private void EmitLinqCountIf(string varName, string arrayVarName, List<MwsNode> emittedNodes)
    {
        var maxVarName = $"max_{arrayVarName}";
        var maxExpr = _localArrayVars.TryGetValue(arrayVarName, out var scalars)
            ? $"max({string.Join(", ", scalars)})"
            : $"{arrayVarName}.toSorted(\"descending\")[0]";
        emittedNodes.Add(new LetNode { Var = maxVarName, Compute = maxExpr });
        _localComputedVars[varName] = $"{arrayVarName}.countif(\"=\" + {maxVarName})";
    }

    // Builds an EogSetupMarkerNode from S_OnSetSpecialSetup?.Invoke(title, round, passageName, body).
    // passageName may be a literal, a tracked local var, or a computed conditional.
    private EogSetupMarkerNode BuildEogSetupMarker(SeparatedSyntaxList<ArgumentSyntax> args)
    {
        var title = args.Count > 0 ? GetStringValue(args[0].Expression) : null;
        int completedRound = -1;
        if (args.Count > 1 && args[1].Expression is LiteralExpressionSyntax rl &&
            rl.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            int.TryParse(rl.Token.ValueText, out completedRound);
        }

        string? passageName = null;
        List<MwsNode>? passageNameNodes = null;
        if (args.Count > 2)
        {
            var psnExpr = args[2].Expression;
            if (psnExpr is LiteralExpressionSyntax psnLit && psnLit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                passageName = psnLit.Token.ValueText;
            }
            else if (psnExpr is IdentifierNameSyntax psnId)
            {
                var localName = psnId.Identifier.Text;
                if (_localVars.TryGetValue(localName, out var literalPsn))
                {
                    passageName = literalPsn;
                }
                else if (_localPassageConditionals.TryGetValue(localName, out var condNodes))
                {
                    passageNameNodes = RenameLetVarInNodes(condNodes, localName, "passageName");
                }
            }
        }

        var bodyText = args.Count > 3 ? GetStringValue(args[3].Expression) : null;
        return new EogSetupMarkerNode
        {
            Title = title,
            CompletedRound = completedRound,
            BodyText = BuildEogMessageTemplate(bodyText),
            PassageName = passageName,
            PassageNameNodes = passageNameNodes,
        };
    }

    // Recursively builds a ConditionalNode tree from a ternary expression where ALL leaves are
    // string literals. Returns null if any leaf is not a string literal.
    private List<MwsNode>? TryBuildPassageNameConditional(string varName, ExpressionSyntax expr)
    {
        expr = UnwrapParens(expr);
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return [new LetNode { Var = varName, Compute = $"\"{lit.Token.ValueText.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"" }];
        }

        if (expr is ConditionalExpressionSyntax cond)
        {
            var condStr = SimplifyCondition(UnwrapParens(cond.Condition).ToString());
            var trueNodes = TryBuildPassageNameConditional(varName, cond.WhenTrue);
            var falseNodes = TryBuildPassageNameConditional(varName, cond.WhenFalse);
            if (trueNodes is null || falseNodes is null)
            {
                return null;
            }

            return [new ConditionalNode
            {
                Branches =
                [
                    new ConditionalBranch { Condition = condStr, Nodes = trueNodes },
                    new ConditionalBranch { Else = true, Nodes = falseNodes },
                ],
            }];
        }
        return null;
    }

    // Deep-renames all LetNode.Var occurrences of oldName to newName within a node tree.
    // Converts a conditional LetNode tree (from _localPassageConditionals) to GotoNodes.
    // Each LetNode leaf with a quoted-string Compute value becomes a GotoNode with that target.
    private static List<MwsNode> BuildGotoNodesFromLetConditionals(List<MwsNode> nodes)
    {
        var result = new List<MwsNode>();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case LetNode let when let.Compute is { Length: >= 3 } c && c[0] == '"' && c[^1] == '"':
                    result.Add(new GotoNode { Target = c[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\") });
                    break;
                case ConditionalNode cond:
                    result.Add(new ConditionalNode
                    {
                        Branches = cond.Branches.Select(b => new ConditionalBranch
                        {
                            Condition = b.Condition,
                            Else = b.Else,
                            Nodes = BuildGotoNodesFromLetConditionals(b.Nodes),
                        }).ToList(),
                        SourceLine = cond.SourceLine,
                    });
                    break;
            }
        }
        return result;
    }

    private static List<MwsNode> RenameLetVarInNodes(List<MwsNode> nodes, string oldName, string newName) =>
        nodes.Select(n => RenameLetVar(n, oldName, newName)).ToList();

    private static MwsNode RenameLetVar(MwsNode node, string oldName, string newName) => node switch
    {
        LetNode let when let.Var == oldName => new LetNode { Var = newName, Compute = let.Compute },
        ConditionalNode cond => new ConditionalNode
        {
            Branches = cond.Branches.Select(b => new ConditionalBranch
            {
                Condition = b.Condition,
                Else = b.Else,
                Nodes = RenameLetVarInNodes(b.Nodes, oldName, newName),
            }).ToList(),
            SourceLine = cond.SourceLine,
        },
        _ => node,
    };

    // Detects s_OnEndOfGeneration(arg, N) or ViewEndOfGeneration.S_OnEndOfGeneration(arg, N).
    private bool TryBuildEogNode(InvocationExpressionSyntax inv, out EndOfGenerationNode? node)
    {
        node = null;
        var methodName = GetSimpleMethodName(inv);
        var exprStr = inv.Expression.ToString();

        bool isEog = _eogDelegates.Contains(methodName) ||
                     exprStr.Contains("S_OnEndOfGeneration");
        if (!isEog)
        {
            return false;
        }

        var args = inv.ArgumentList.Arguments;
        string? rawMessage = null;
        if (args.Count > 0)
        {
            var msgExpr = args[0].Expression;
            if (msgExpr is LiteralExpressionSyntax msgLit &&
                msgLit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                rawMessage = msgLit.Token.ValueText;
            }
            else if (msgExpr is IdentifierNameSyntax msgId &&
                     _localVars.TryGetValue(msgId.Identifier.Text, out var stored))
            {
                rawMessage = stored;
            }
        }

        int generation = 0;
        if (args.Count > 1 && args[1].Expression is LiteralExpressionSyntax genLit &&
            genLit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            int.TryParse(genLit.Token.ValueText, out generation);
        }

        node = new EndOfGenerationNode
        {
            Generation = generation,
            Message = BuildEogMessageTemplate(rawMessage),
        };
        return true;
    }

    // Converts Unity Rich Text in EOG messages to MWS template strings.
    // <sprite="X" index=N> → {icon:slug}, <b>text</b> → stripped (kept as plain text for now).
    // TryParseRichText trims each segment, so we add spaces between adjacent segments to
    // restore the word boundaries that surrounded the original sprite tags.
    private string? BuildEogMessageTemplate(string? rawMessage)
    {
        if (rawMessage is null)
        {
            return null;
        }

        var richRuns = _spriteMapper.TryParseRichText(rawMessage);
        if (richRuns is not null)
        {
            var sb = new System.Text.StringBuilder();
            bool prevWasSeg = false;
            foreach (var (text, assetRef) in richRuns)
            {
                if (prevWasSeg)
                {
                    sb.Append(' ');
                }

                if (assetRef is not null)
                {
                    sb.Append($"{{icon:{assetRef.Replace("icon://", "")}}}");
                }
                else if (text is not null)
                {
                    sb.Append(text);
                }

                prevWasSeg = true;
            }
            return sb.ToString().Trim();
        }
        return _spriteMapper.StripLayoutTags(rawMessage).Trim();
    }
}
