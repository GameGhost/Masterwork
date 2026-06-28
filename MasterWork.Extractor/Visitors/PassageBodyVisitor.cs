using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MasterWork.ModuleFormat;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MasterWork.Extractor.Visitors;

// Walks the statements of a Cradle passageN_Main() iterator method and
// produces a list of MwsNode objects representing the passage content.
public class PassageBodyVisitor
{
    private readonly string _passageName;
    private readonly SpriteMapper _spriteMapper;
    private readonly ExtractionReport _report;

    // Current accumulated text runs — flushed into a TextNode at the next non-text node
    private List<TextRun> _pendingRuns = [];
    // Style stack — tracks nested styleScope calls (bold, italic, etc.)
    private readonly Stack<string> _styleStack = new();
    // Source line of the statement currently being processed (1-based in original file)
    private int? _currentStatementLine;
    // Source line of the first text() call that started the current pending-runs buffer
    private int? _pendingTextStartLine;
    // Monotonically incrementing counter for unique inline-random variable names within a passage
    private int _varRandomSeq = 0;
    // Local string variables declared in this passage method (name → literal value)
    private readonly Dictionary<string, string> _localVars = new(StringComparer.Ordinal);
    // Local delegate variables that are captures of ViewEndOfGeneration.S_OnEndOfGeneration
    private readonly HashSet<string> _eogDelegates = new(StringComparer.Ordinal);
    // Local array variables: name → list of member variable names (for let array nodes)
    private readonly Dictionary<string, List<string>> _localArrayVars = new(StringComparer.Ordinal);
    // Local computed variables: name → aggregate expression string (for condition substitution)
    // e.g. "num" → "countif(==max_play, play)" from a LINQ Count-where-Max pattern
    private readonly Dictionary<string, string> _localComputedVars = new(StringComparer.Ordinal);

    public PassageBodyVisitor(string passageName, SpriteMapper spriteMapper, ExtractionReport report)
    {
        _passageName = passageName;
        _spriteMapper = spriteMapper;
        _report = report;
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
            if (IsCradleCleanupStatement(stmt)) continue;

            // yield break — end of iterator
            if (stmt is YieldStatementSyntax { RawKind: (int)SyntaxKind.YieldBreakStatement })
                break;

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
                var inner = us.Statement is BlockSyntax blk
                    ? VisitStyleScopeBlock(scopeName!, hookId, blk)
                    : VisitStyleScopeBlock(scopeName!, hookId,
                        SyntaxFactory.Block(us.Statement));

                TagNodes(inner, _currentStatementLine);
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
                var cond = BuildConditional(ifs);
                cond.SourceLine = _currentStatementLine;
                result.Add(cond);
                continue;
            }

            // Expression statements: assignments, Unity API calls
            if (stmt is ExpressionStatementSyntax es)
            {
                if (IsIgnorableAssignment(es)) continue;
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
            if (n.SourceLine is null) n.SourceLine = line;
    }

    // ── Style scope handling ───────────────────────────────────────────────

    private List<MwsNode> VisitStyleScopeBlock(string scopeName, string? hookId, BlockSyntax block)
    {
        switch (scopeName)
        {
            case "bold":
            case "italic":
                _styleStack.Push(scopeName);
                var styled = VisitStatements(block.Statements);
                _styleStack.Pop();
                return styled;

            case "hubTitle":
            case "heading":
            {
                _styleStack.Push(scopeName);
                var inner = VisitStatements(block.Statements);
                _styleStack.Pop();
                var text = CollectText(inner);
                return [new SectionHeadingNode { Text = text }];
            }

            case "hubDetails":
            {
                var inner = VisitStatements(block.Statements);
                return [new SectionBodyNode { Nodes = inner }];
            }

            case "setupStyle":
            case "setupStyleEvnt":
            {
                var inner = VisitStatements(block.Statements);
                return [new SetupBlockNode { Nodes = inner }];
            }

            case "hook":
                // enchantHook wrapper — the inner link will reference this hookId.
                // We just pass through the inner content; the link handler stitches the fragment.
                return VisitStatements(block.Statements);

            default:
                _report.AddWarning(_passageName, $"Unknown styleScope: {scopeName}", sourceLine: GetLine(block));
                return VisitStatements(block.Statements);
        }
    }

    // ── yield return expression dispatch ──────────────────────────────────

    private List<MwsNode> ProcessYieldExpression(ExpressionSyntax expr)
    {
        if (expr is not InvocationExpressionSyntax inv) return [Unknown(expr)];

        var methodName = GetSimpleMethodName(inv);

        return methodName switch
        {
            "text" => ProcessTextInvocation(inv),
            "lineBreak" => [new BreakNode()],
            "link" => [ProcessLink(inv)],
            "passage" => ProcessPassageInclusionNodes(inv),
            "abort" => [ProcessAbort(inv)],
            _ => [Unknown(inv)],
        };
    }

    // ── text() ────────────────────────────────────────────────────────────

    private List<MwsNode> ProcessTextInvocation(InvocationExpressionSyntax inv)
    {
        var arg = inv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (arg is null) return [];

        var currentStyle = _styleStack.Count > 0 ? _styleStack.Peek() : null;
        if (currentStyle == "hubTitle") currentStyle = null; // handled by scope

        // string literal
        if (arg is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var raw = lit.Token.ValueText;

            // Check for TextMesh Pro rich text
            var richRuns = _spriteMapper.TryParseRichText(raw);
            if (richRuns is not null)
            {
                foreach (var (t, assetRef) in richRuns)
                {
                    if (assetRef is not null)
                        AddRun(new TextRun { AssetRef = assetRef });
                    else if (!string.IsNullOrEmpty(t))
                        AddRun(new TextRun { Text = t, Style = currentStyle });
                }
            }
            else
            {
                var cleaned = _spriteMapper.StripLayoutTags(raw);
                if (!string.IsNullOrEmpty(cleaned))
                    AddRun(new TextRun { Text = cleaned, Style = currentStyle });
            }
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
            var richRuns2 = _spriteMapper.TryParseRichText(raw2);
            if (richRuns2 is not null)
            {
                foreach (var (t2, assetRef2) in richRuns2)
                {
                    if (assetRef2 is not null)
                        AddRun(new TextRun { AssetRef = assetRef2 });
                    else if (!string.IsNullOrEmpty(t2))
                        AddRun(new TextRun { Text = t2, Style = style });
                }
            }
            else
            {
                var cleaned2 = _spriteMapper.StripLayoutTags(raw2);
                if (!string.IsNullOrEmpty(cleaned2))
                    AddRun(new TextRun { Text = cleaned2, Style = style });
            }
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
        var label = args.Count > 0 ? GetStringValue(args[0].Expression) ?? "" : "";
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
            };
        }

        return new LinkNode
        {
            Label = label,
            Target = target ?? "",
            StateAffecting = true,
        };
    }

    // ── passage() ─────────────────────────────────────────────────────────

    private List<MwsNode> ProcessPassageInclusionNodes(InvocationExpressionSyntax inv)
    {
        var arg = inv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (arg is null) return [Unknown(inv)];

        var passageName = GetStringValue(arg);
        if (passageName is not null)
            return [new IncludePassageNode { Target = passageName }];

        // base.passage(this.Vars.X, ...) — dynamic inclusion via variable
        if (IsVarAccess(arg, out var targetVar))
            return [new IncludePassageNode { Target = $"{{{targetVar}}}" }];

        // base.passage(macros1.either([list of string passage IDs])) — random passage pick.
        // Emit: let var = choose-one(ids); include-passage target: '{var}'
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
                    new IncludePassageNode { Target = $"{{{tempVar}}}" },
                ];
            }
        }

        return [Unknown(inv)];
    }

    // ── abort() ───────────────────────────────────────────────────────────

    private MwsNode ProcessAbort(InvocationExpressionSyntax inv)
    {
        // abort(goToPassage: "Name") or abort("Name") or abort(this.Vars.X)
        var arg = inv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (arg is null) return Unknown(inv);

        var target = GetStringValue(arg);
        if (target is not null)
            return new GotoNode { Target = target };

        // Variable target: abort(this.Vars.tempeffect)
        if (IsVarAccess(arg, out var varName))
            return new GotoNode { Target = $"{{{varName}}}" };

        return Unknown(inv);
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
                return preamble is not null ? [..preamble, effect] : [effect];
        }

        // ViewItemObtain.SetupPassagename = "X"
        if (expr is AssignmentExpressionSyntax assignSetup &&
            IsSetupPassagenameAssignment(assignSetup, out var nextPassage))
        {
            return [new SetupNotificationNode { NextPassage = nextPassage }];
        }

        // ViewController.instance.ChangeView(...)
        if (expr is InvocationExpressionSyntax cvi && IsChangeViewMainMenu(cvi))
            return [new GotoMenuNode { Target = "main_menu" }];

        // PassageTracker.instance.CheckProgress(current, target)
        if (expr is InvocationExpressionSyntax cpInv && IsCheckProgress(cpInv, out var cp))
            return [cp!];

        // PassageTracker.instance.SetLocationIndicatorIcon(...) — usually paired with
        // locationName/locationIcon assignments; emit as SetLocationNode stub
        if (expr is InvocationExpressionSyntax locInv && IsSetLocationCall(locInv))
        {
            _report.AddInfo(_passageName, $"set_location call detected: {locInv}", GetLine(locInv));
            return [new SetLocationNode()];
        }

        // ViewGenerationEnding, ViewPopupPanel utility calls — skip
        if (expr is InvocationExpressionSyntax utilInv && IsIgnorableCall(utilInv))
            return [];

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
            return [eogNode!];

        // Unknown expression statement
        var code = es.ToString().Trim();
        _report.AddUnhandled(_passageName, code, GetLine(es));
        return [new UnknownNode { OriginalCode = Truncate(code) }];
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
        if (!IsVarAccess(assign.Left, out var varName)) return null;
        // Unwrap outer parentheses so patterns like ((cond) ? a : b) are handled correctly
        ExpressionSyntax right = assign.Right;
        while (right is ParenthesizedExpressionSyntax outerParen) right = outerParen.Expression;

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

        // this.Vars.Y["key"] — array index access; "1st" → .first() expression
        if (right is ElementAccessExpressionSyntax elemAccess && IsVarAccess(elemAccess.Expression, out var srcArray))
        {
            var indexArg = elemAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (GetStringValue(indexArg!) == "1st")
                return new EffectNode { VarSets = new() { [varName!] = $"{srcArray}.first()" } };
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
                return new EffectNode { VarMath = new() { [varName!] = mathExpr } };

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
                        return new EffectNode { VarRemove = new() { [varName!] = removeValues[0].ToString()! } };
                }
            }

            // random(min, max) + var  or  var + random(min, max)  →  let _rnd = range, varName = {addend} + {_rnd}
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
                    string addendStr = IsVarAccess(addend, out var addVar) ? $"{{{addVar}}}"
                        : addend is LiteralExpressionSyntax addLit ? addLit.Token.ValueText
                        : addend.ToString();
                    return new EffectNode
                    {
                        VarSets = new() { [varName!] = $"{addendStr} + {{{rndName}}}" }
                    };
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
                _report.AddWarning(_passageName, $"Unresolved ternary branch for {varName}",
                    ternary.ToString(), GetLine(ternary));
            return new EffectNode
            {
                VarSets = new() { [varName!] = $"({condStr}) ? {GetStringOrLiteral(whenTrue)} : {GetStringOrLiteral(whenFalse)}" }
            };
        }

        // Fallback — record raw expression
        _report.AddWarning(_passageName, $"Unhandled assignment RHS for {varName}",
            right.ToString(), GetLine(right));
        return new EffectNode { VarSets = new() { [varName!] = $"?({Truncate(right.ToString())})" } };
    }

    // ── Conditional (if/else) → ConditionalNode ───────────────────────────

    private ConditionalNode BuildConditional(IfStatementSyntax ifs)
    {
        var branches = new List<ConditionalBranch>();
        StatementSyntax? current = ifs;

        while (current is IfStatementSyntax currentIf)
        {
            if (HasUnresolvableApiCall(currentIf.Condition))
                _report.AddWarning(_passageName, "Unresolved API call in condition",
                    currentIf.Condition.ToString(), GetLine(currentIf));

            var condStr = SimplifyCondition(currentIf.Condition.ToString());
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

        return new ConditionalNode { Branches = branches };
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
        if (elseStmt is null) return false;

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
        if (condStr.Contains("this.ispopup"))
        {
            var ifStmts = ifs.Statement is BlockSyntax bb ? bb.Statements.AsEnumerable() : [ifs.Statement];
            InvocationExpressionSyntax? genBtn = null;
            foreach (var s in ifStmts)
            {
                if (s is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }
                    && GetSimpleMethodName(inv) == "OnGenerationBtn")
                { genBtn = inv; break; }
            }
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

    // Two-statement input prompt:
    //   if (this.ispopup [&& this.iscreation*]) { ... OnGenerationBtn(...) }
    //   if (PassageValueNumber() >= 0 | PassageValueString() != null) { this.Vars.X = ...; }
    private bool TryDetectInputPrompt(IfStatementSyntax setupIf, IfStatementSyntax storeIf, out InputPromptNode? node)
    {
        node = null;

        // Setup if must contain this.ispopup in the condition and have no else branch
        var setupCond = setupIf.Condition.ToString();
        if (!setupCond.Contains("this.ispopup")) return false;
        if (setupIf.Else is not null) return false;

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
        if (genBtn is null) return false;

        // Store if must contain a PassageValue* call in its condition
        var storeCond = storeIf.Condition.ToString();
        if (!storeCond.Contains("PassageValueNumber") && !storeCond.Contains("PassageValueString")) return false;
        var storeIn = ExtractStoreIn(storeIf.Statement);
        if (storeIn is null) return false;

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
            return lit.Token.ValueText;
        if (IsVarAccess(expr, out var v))
            return $"{{{v}}}";
        if (expr is BinaryExpressionSyntax bin && bin.OperatorToken.Text == "+")
            return ExtractTemplateString(bin.Left) + ExtractTemplateString(bin.Right);
        return $"?({Truncate(expr.ToString())})";
    }

    // Returns true when every leaf of the + chain is a recognisable template part:
    // string literal, int literal, var access, or either() invocation.
    private bool IsTemplateConcatExpr(ExpressionSyntax expr)
    {
        expr = UnwrapParens(expr);
        if (expr is LiteralExpressionSyntax) return true;
        if (IsVarAccess(expr, out _)) return true;
        if (expr is InvocationExpressionSyntax inv && GetSimpleMethodName(inv) == "either") return true;
        if (expr is BinaryExpressionSyntax bin && bin.OperatorToken.Text == "+")
            return IsTemplateConcatExpr(bin.Left) && IsTemplateConcatExpr(bin.Right);
        return false;
    }

    // Returns true when the + chain contains at least one string literal or either() call,
    // distinguishing it from a purely numeric arithmetic expression.
    private static bool ContainsStringOrEither(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
            return true;
        if (expr is InvocationExpressionSyntax inv)
        {
            var name = (inv.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text
                ?? (inv.Expression as IdentifierNameSyntax)?.Identifier.Text;
            if (name == "either") return true;
        }
        if (expr is BinaryExpressionSyntax bin && bin.OperatorToken.Text == "+")
            return ContainsStringOrEither(bin.Left) || ContainsStringOrEither(bin.Right);
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

    private List<MwsNode> FlushText()
    {
        if (_pendingRuns.Count == 0) return [];
        var node = new TextNode { Runs = _pendingRuns, SourceLine = _pendingTextStartLine };
        _pendingRuns = [];
        _pendingTextStartLine = null;
        return [node];
    }

    // Adds a run to the pending buffer; records the source line of the first run in the group.
    private void AddRun(TextRun run)
    {
        if (_pendingRuns.Count == 0) _pendingTextStartLine = _currentStatementLine;
        _pendingRuns.Add(run);
    }

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
            return decl.Declaration.Type.ToString().Contains("StyleScope");

        // stylescopeN = null;  (Cradle nested scope cleanup — any variable name starting with "styleScope")
        if (stmt is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assign })
        {
            if (assign.Left is IdentifierNameSyntax id &&
                id.Identifier.Text.StartsWith("styleScope", StringComparison.OrdinalIgnoreCase) &&
                IsNullLiteral(assign.Right))
                return true;
        }
        return false;
    }

    private static bool IsStyleScopeUsing(UsingStatementSyntax us, out string? scopeName, out string? hookId)
    {
        scopeName = null; hookId = null;
        var expr = us.Expression ?? us.Declaration?.Variables.FirstOrDefault()?.Initializer?.Value;
        if (expr is not InvocationExpressionSyntax inv) return false;
        if (GetSimpleMethodName(inv) != "styleScope") return false;

        var args = inv.ArgumentList.Arguments;
        scopeName = args.Count > 0 ? GetStringValue(args[0].Expression) : null;
        hookId = args.Count > 1 ? GetStringValue(args[1].Expression) : null;
        return scopeName is not null;
    }

    private static string? ExtractStoreIn(StatementSyntax ifStatement)
    {
        var stmts = ifStatement is BlockSyntax b ? b.Statements.AsEnumerable() : [ifStatement];
        foreach (var s in stmts)
        {
            if (s is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assign } &&
                IsVarAccess(assign.Left, out var varName))
                return varName;
        }
        return null;
    }

    private static bool IsVarAccess(ExpressionSyntax expr, out string? varName)
    {
        varName = null;
        // this.Vars.X
        if (expr is MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax innerMember } outer)
        {
            if (innerMember.Name.Identifier.Text == "Vars")
            {
                varName = outer.Name.Identifier.Text;
                return true;
            }
        }
        return false;
    }

    private static bool IsNullLiteral(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression);

    private static string GetSimpleMethodName(InvocationExpressionSyntax inv)
    {
        if (inv.Expression is MemberAccessExpressionSyntax m)
            return m.Name.Identifier.Text;
        if (inv.Expression is IdentifierNameSyntax id)
            return id.Identifier.Text;
        return "";
    }

    private static string? GetStringValue(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression)
            ? lit.Token.ValueText
            : null;

    private static object LiteralValue(LiteralExpressionSyntax lit)
    {
        if (lit.IsKind(SyntaxKind.StringLiteralExpression)) return lit.Token.ValueText;
        if (lit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            var val = lit.Token.Value;
            return val is double d ? (int)d : val ?? 0;
        }
        if (lit.IsKind(SyntaxKind.TrueLiteralExpression)) return true;
        if (lit.IsKind(SyntaxKind.FalseLiteralExpression)) return false;
        return lit.Token.ValueText;
    }

    private static int? TryParseInt(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit &&
            double.TryParse(lit.Token.ValueText, out var d))
            return (int)d;
        return null;
    }

    private static ExpressionSyntax UnwrapParens(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax p) expr = p.Expression;
        return expr;
    }

    // Strips int.Parse(...) wrapper if present; otherwise returns expr unchanged.
    private static ExpressionSyntax UnwrapIntParse(ExpressionSyntax expr)
    {
        if (expr is InvocationExpressionSyntax inv &&
            (inv.Expression is IdentifierNameSyntax { Identifier.Text: "Parse" } ||
             (inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.Text == "Parse")) &&
            inv.ArgumentList.Arguments.Count == 1)
            return inv.ArgumentList.Arguments[0].Expression;
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
            return new EffectNode { VarSets = new() { [varName] = $"{{{v}}}" } };
        if (expr is LiteralExpressionSyntax lit)
            return new EffectNode { VarSets = new() { [varName] = lit.Token.ValueText } };
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
        if (expr is LiteralExpressionSyntax lit) return lit.Token.ValueText;
        if (IsVarAccess(expr, out var v)) return $"{{{v}}}";
        return expr.ToString();
    }

    private static List<object> ExtractMacroArgs(InvocationExpressionSyntax inv)
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
                    if (elem is LiteralExpressionSyntax lit2) result.Add(LiteralValue(lit2));
                    else if (IsVarAccess(elem, out var vn)) result.Add($"{{{vn}}}");
                    else result.Add(elem.ToString());
                }
            }
            else if (arg.Expression is LiteralExpressionSyntax lit3)
                result.Add(LiteralValue(lit3));
        }
        return result;
    }

    private static string? ExtractVarMath(BinaryExpressionSyntax bin, string varName)
    {
        // int.Parse(this.Vars.X) + N
        if (bin.Right is LiteralExpressionSyntax rLit && rLit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            var op = bin.OperatorToken.Text;
            var val = rLit.Token.ValueText;
            if (op == "+" || op == "-" || op == "*") return $"{op}{val}";
        }
        // N + int.Parse(this.Vars.X)
        if (bin.Left is LiteralExpressionSyntax lLit && lLit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            if (bin.OperatorToken.Text == "+") return $"+{lLit.Token.ValueText}";
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
        if (GetSimpleMethodName(inv) != "CheckProgress") return false;
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

    private static bool IsChangeViewMainMenu(InvocationExpressionSyntax inv) =>
        GetSimpleMethodName(inv) == "ChangeView";

    private static bool IsSetupPassagenameAssignment(AssignmentExpressionSyntax assign, out string? nextPassage)
    {
        nextPassage = null;
        if (assign.Left.ToString().Contains("SetupPassagename"))
        {
            nextPassage = GetStringValue(assign.Right) ??
                (assign.Right is ConditionalExpressionSyntax ct
                    ? $"?({ct.Condition})"
                    : assign.Right.ToString());
            return true;
        }
        return false;
    }

    private static bool IsIgnorableCall(InvocationExpressionSyntax inv)
    {
        var name = GetSimpleMethodName(inv);
        // Unity API calls with no MWS equivalent — logged but not emitted as nodes
        return name is "Clear" or "EnableDisableContinueBtn" or "OnGenerationBtn"
            or "PassageValueNumber" or "ShowEventPopup" or "Log";
    }

    private static bool IsIgnorableAssignment(ExpressionStatementSyntax es)
    {
        // this.ispasscode = ..., this.ispopup = ..., this.iscreationA = ... — Cradle double-trigger guards
        if (es.Expression is AssignmentExpressionSyntax assign &&
            assign.Left is MemberAccessExpressionSyntax m &&
            m.Expression.ToString() == "this")
        {
            var field = m.Name.Identifier.Text;
            return field is "ispasscode" or "ispopup" or "iscreationA";
        }
        return false;
    }

    private string SimplifyCondition(string cond)
    {
        // Normalize "this.Vars.X" → "X"
        cond = Regex.Replace(cond, @"this\.Vars\.(\w+)", m => m.Groups[1].Value);
        // Normalize "int.Parse(...)" → the inner expression
        cond = Regex.Replace(cond, @"int\.Parse\((\w+)\)", "$1");
        // Normalize compound falsy: "x == 0 || x == """ → "!x"
        cond = Regex.Replace(cond, @"(\w+)\s*==\s*0\s*\|\|\s*\1\s*==\s*""""", "!$1");

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
            cond = Regex.Replace(cond, $@"\b{Regex.Escape(name)}\b", _ => expr);

        return cond.Trim();
    }

    private static string CollectText(List<MwsNode> nodes)
    {
        var parts = new List<string>();
        foreach (var n in nodes)
        {
            if (n is TextNode tn)
                parts.AddRange(tn.Runs.Select(r => r.Text ?? ""));
        }
        return string.Join("", parts).Trim();
    }

    private UnknownNode Unknown(SyntaxNode node)
    {
        var code = node.ToString().Trim();
        _report.AddUnhandled(_passageName, code, GetLine(node));
        return new UnknownNode { OriginalCode = Truncate(code) };
    }

    // Returns the 1-based line number in the original source file.
    // The wrapped source prepends 2 lines, so Roslyn's 0-based line - 1 = original 1-based line.
    private static int? GetLine(SyntaxNode node)
    {
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line - 1;
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
            if (init is null) continue;

            // string varName = "literal" → track for EOG message resolution
            if (init is LiteralExpressionSyntax strLit &&
                strLit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                _localVars[name] = strLit.Token.ValueText;
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
        }

        return anyHandled ? nodes : null;
    }

    // Recognizes: new List<T>(new T[] { this.Vars.A, this.Vars.B, ... })
    private static bool TryExtractListInit(TypeSyntax type, ExpressionSyntax init, out List<string>? vars)
    {
        vars = null;
        if (!type.ToString().StartsWith("List<", StringComparison.Ordinal)) return false;

        if (init is not ObjectCreationExpressionSyntax objCreate) return false;
        var arg = objCreate.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        if (arg is not ArrayCreationExpressionSyntax arr || arr.Initializer is null) return false;

        var result = new List<string>();
        foreach (var elem in arr.Initializer.Expressions)
        {
            if (IsVarAccess(elem, out var varName))
                result.Add(varName!);
            else
                return false;
        }
        vars = result;
        return true;
    }

    // Recognizes: int num = (from value in <arr> where value == <arr>.Max() select value).Count<int>()
    // Emits:      LetNode { Var = "max_<arr>", Compute = "max(<arr>)" }
    // Tracks:     _localComputedVars[num] = "countif(==max_<arr>, <arr>)"
    private bool TryExtractLinqCountIf(string varName, ExpressionSyntax init, List<MwsNode> emittedNodes)
    {
        if (init is not InvocationExpressionSyntax countInv) return false;
        if (GetSimpleMethodName(countInv) != "Count") return false;

        var receiver = (countInv.Expression as MemberAccessExpressionSyntax)?.Expression;
        if (receiver is not ParenthesizedExpressionSyntax paren) return false;
        if (paren.Expression is not QueryExpressionSyntax query) return false;

        // "from value in <arrayVar>"
        if (query.FromClause.Expression is not IdentifierNameSyntax fromId) return false;
        var arrayVarName = fromId.Identifier.Text;

        // "where value == <arrayVar>.Max()"
        var whereClause = query.Body.Clauses.OfType<WhereClauseSyntax>().FirstOrDefault();
        if (whereClause?.Condition is not BinaryExpressionSyntax whereBin) return false;
        if (!whereBin.IsKind(SyntaxKind.EqualsExpression)) return false;
        if (!whereBin.Right.ToString().Equals($"{arrayVarName}.Max()", StringComparison.Ordinal)) return false;

        var maxVarName = $"max_{arrayVarName}";
        emittedNodes.Add(new LetNode { Var = maxVarName, Compute = $"max({arrayVarName})" });
        _localComputedVars[varName] = $"countif(={maxVarName}, {arrayVarName})";
        return true;
    }

    // Detects s_OnEndOfGeneration(arg, N) or ViewEndOfGeneration.S_OnEndOfGeneration(arg, N).
    private bool TryBuildEogNode(InvocationExpressionSyntax inv, out EndOfGenerationNode? node)
    {
        node = null;
        var methodName = GetSimpleMethodName(inv);
        var exprStr = inv.Expression.ToString();

        bool isEog = _eogDelegates.Contains(methodName) ||
                     exprStr.Contains("S_OnEndOfGeneration");
        if (!isEog) return false;

        var args = inv.ArgumentList.Arguments;
        string? rawMessage = null;
        if (args.Count > 0)
        {
            var msgExpr = args[0].Expression;
            if (msgExpr is LiteralExpressionSyntax msgLit &&
                msgLit.IsKind(SyntaxKind.StringLiteralExpression))
                rawMessage = msgLit.Token.ValueText;
            else if (msgExpr is IdentifierNameSyntax msgId &&
                     _localVars.TryGetValue(msgId.Identifier.Text, out var stored))
                rawMessage = stored;
        }

        int generation = 0;
        if (args.Count > 1 && args[1].Expression is LiteralExpressionSyntax genLit &&
            genLit.IsKind(SyntaxKind.NumericLiteralExpression))
            int.TryParse(genLit.Token.ValueText, out generation);

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
        if (rawMessage is null) return null;
        var richRuns = _spriteMapper.TryParseRichText(rawMessage);
        if (richRuns is not null)
        {
            var sb = new System.Text.StringBuilder();
            bool prevWasSeg = false;
            foreach (var (text, assetRef) in richRuns)
            {
                if (prevWasSeg) sb.Append(' ');
                if (assetRef is not null)
                    sb.Append($"{{icon:{assetRef.Replace("icon://", "")}}}");
                else if (text is not null)
                    sb.Append(text);
                prevWasSeg = true;
            }
            return sb.ToString().Trim();
        }
        return _spriteMapper.StripLayoutTags(rawMessage).Trim();
    }
}
