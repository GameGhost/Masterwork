using Masterwork.ModuleFormat;
using Masterwork.Engine.Expressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.Engine.Rendering;

/// <summary>
/// <inheritdoc cref="IPassageRenderer"/> Popup content is rendered eagerly, alongside the rest of
/// the passage, against a private sandbox clone of the store (see <see cref="RenderedPopup"/>'s
/// remarks) — <see cref="RenderNodeList"/> is the entry point used for that, and for
/// <c>include_passage</c>.
/// </summary>
public sealed class PassageRenderer : IPassageRenderer
{
    private readonly IExpressionEvaluator _evaluator;
    private readonly ILogger<PassageRenderer> _logger;

    /// <summary>Creates a renderer wired to the default <see cref="ExpressionEvaluator"/>, discarding log output.</summary>
    public PassageRenderer() : this(new ExpressionEvaluator(), NullLogger<PassageRenderer>.Instance)
    {
    }

    /// <summary>Creates a renderer with an explicit evaluator dependency, e.g. for testing with mocks.</summary>
    public PassageRenderer(IExpressionEvaluator evaluator, ILogger<PassageRenderer>? logger = null)
    {
        _evaluator = evaluator;
        _logger = logger ?? NullLogger<PassageRenderer>.Instance;
    }

    /// <inheritdoc/>
    public PassageRenderResult Render(
        MwsPassageDoc passage, VariableStore store, LoadedModule module, IReadOnlySet<string> visitedPassageIds)
    {
        _logger.LogDebug("Rendering passage '{PassageId}'", passage.PassageId);

        if (passage.CheckProgress is not null && !visitedPassageIds.Contains(passage.CheckProgress))
        {
            throw new CheckProgressViolationException(passage.PassageId, passage.CheckProgress);
        }

        var ctx = new RenderContext(store, module);
        var nodes = RenderNodes(passage.Nodes, ctx);

        return new PassageRenderResult(
            PassageId: passage.PassageId,
            Layout: passage.Layout,
            LocationName: passage.Location?.Name,
            LocationIcon: passage.Location?.Icon,
            Nodes: nodes,
            Actions: ctx.Actions,
            Checkpoints: ctx.Checkpoints,
            PendingGoto: ctx.PendingGoto,
            IsEnding: passage.Ending);
    }

    /// <inheritdoc/>
    public NodeListRenderResult RenderNodeList(IReadOnlyList<Node> nodes, VariableStore store, LoadedModule module, string actionIdPrefix = "")
    {
        var ctx = new RenderContext(store, module, actionIdPrefix);
        var rendered = RenderNodes(nodes, ctx);
        return new NodeListRenderResult(rendered, ctx.Actions, ctx.PendingGoto, ctx.PendingGotoLabel);
    }

    private sealed class RenderContext(VariableStore store, LoadedModule module, string actionIdPrefix = "")
    {
        public VariableStore Store { get; } = store;
        public LoadedModule Module { get; } = module;
        public List<RenderedAction> Actions { get; } = [];
        public List<RenderedCheckpoint> Checkpoints { get; } = [];
        public string? PendingGoto { get; set; }
        public string? PendingGotoLabel { get; set; }

        private int _actionCounter;
        public string NextActionId(string prefix) => $"{actionIdPrefix}{prefix}_{_actionCounter++}";
    }

    private List<RenderedNode> RenderNodes(IReadOnlyList<Node> nodes, RenderContext ctx)
    {
        var output = new List<RenderedNode>();
        foreach (var node in nodes)
        {
            RenderNode(node, ctx, output);
            if (ctx.PendingGoto is not null)
            {
                break;
            }
        }
        return output;
    }

    private void RenderNode(Node node, RenderContext ctx, List<RenderedNode> output)
    {
        switch (node)
        {
            case TextNode t:
                output.Add(new RenderedText(ctx.Store.ExpandTemplate(t.Value), t.Align) { Style = t.Style });
                break;
            case BreakNode b:
                output.Add(new RenderedBreak { Style = b.Style });
                break;
            case ImageNode img:
                output.Add(new RenderedImage(img.Asset, img.Size, img.Align) { Title = ExpandOrNull(img.Title, ctx.Store), Style = img.Style });
                break;
            case SectionNode s:
                output.Add(new RenderedSection(ExpandOrNull(s.Title, ctx.Store), s.Collapsed, RenderNodes(s.Content, ctx)) { Style = s.Style });
                break;
            case LetNode let:
                ctx.Store.SetLetVariable(let.Var, _evaluator.Evaluate(let.Expr, ctx.Store));
                break;
            case AssignNode assign:
                ctx.Store.SetSessionVariable(assign.Var, _evaluator.Evaluate(assign.Expr, ctx.Store));
                break;
            case LinkNode link:
                RenderLink(link, ctx, output);
                break;
            case PopupNode popup:
                RenderPopup(popup, ctx, output);
                break;
            case InputNode input:
                RenderInput(input, ctx, output);
                break;
            case GotoNode go:
                ctx.PendingGoto = ResolveTargetNow(go.Target, ctx);
                ctx.PendingGotoLabel = ExpandOrNull(go.SnapshotLabel, ctx.Store);
                break;
            case IncludePassageNode inc:
                var targetId = ResolveTargetNow(inc.Target, ctx);
                if (ctx.Module.Passages.TryGetValue(targetId, out var includedPassage))
                {
                    output.AddRange(RenderNodes(includedPassage.Nodes, ctx));
                }
                else
                {
                    _logger.LogWarning("include_passage target '{TargetId}' does not exist; nothing was inlined", targetId);
                }

                break;
            case ConditionalNode cond:
                var condBranch = SelectConditionalBranch(cond, ctx.Store);
                if (condBranch is not null)
                {
                    output.AddRange(RenderNodes(condBranch, ctx));
                }

                break;
            case SwitchNode sw:
                var switchCase = SelectSwitchCase(sw, ctx.Store);
                if (switchCase is not null)
                {
                    output.AddRange(RenderNodes(switchCase, ctx));
                }

                break;
            case ForEachNode fe:
                RenderForEach(fe, ctx, output);
                break;
            case CheckpointNode cp:
                ctx.Checkpoints.Add(new RenderedCheckpoint(cp.Id, cp.Display, cp.Diagnostic));
                break;
            case RecordNode rec:
                // Achievement triggers are deferred to Phase 3; no-op at runtime.
                _logger.LogDebug("Skipping 'record' node (achievement triggers deferred to Phase 3): {Id}", rec.Id);
                break;
        }
    }

    private void RenderLink(LinkNode link, RenderContext ctx, List<RenderedNode> output)
    {
        var rendered = new RenderedLink
        {
            Id = ctx.NextActionId("link"),
            Style = link.Style,
            Label = ctx.Store.ExpandTemplate(link.Label),
            Target = link.Target,
            StateAffecting = link.StateAffecting,
            SnapshotLabel = ExpandOrNull(link.SnapshotLabel, ctx.Store),
            OnClickRaw = link.OnClick,
        };
        output.Add(rendered);
        ctx.Actions.Add(rendered);
    }

    private void RenderPopup(PopupNode popup, RenderContext ctx, List<RenderedNode> output)
    {
        // Rendered eagerly (not deferred until the popup opens) against a sandboxed clone, so
        // showing/hiding the popup is a pure UI concern — see RenderedPopup's remarks. The sandbox
        // isn't merged into the live store until GameSession.ClosePopupAsync commits it on Accept.
        var id = ctx.NextActionId("popup");
        var sandbox = ctx.Store.Clone();
        var contentResult = RenderNodeList(popup.Content, sandbox, ctx.Module, actionIdPrefix: $"{id}_");

        var rendered = new RenderedPopup
        {
            Id = id,
            Style = popup.Style,
            Label = ExpandOrNull(popup.Label, ctx.Store),
            Layout = popup.Layout,
            AutoDisplay = popup.Label is null,
            Content = contentResult.Nodes,
            Actions = contentResult.Actions,
            Sandbox = sandbox,
            Okay = ExpandOrNull(popup.Okay, ctx.Store),
            Cancel = ExpandOrNull(popup.Cancel, ctx.Store),
            OnCloseRaw = popup.OnClose,
            Target = popup.Target,
            StateAffecting = popup.StateAffecting,
            SnapshotLabel = ExpandOrNull(popup.SnapshotLabel, ctx.Store),
        };
        output.Add(rendered);
        ctx.Actions.Add(rendered);
    }

    private void RenderInput(InputNode input, RenderContext ctx, List<RenderedNode> output)
    {
        var rendered = new RenderedInput
        {
            Id = ctx.NextActionId("input"),
            Style = input.Style,
            Label = ctx.Store.ExpandTemplate(input.Label),
            Var = input.Var,
            InputType = ResolveInputType(input.Var, ctx),
            Min = input.Min,
            Max = input.Max,
        };
        output.Add(rendered);
        ctx.Actions.Add(rendered);
    }

    // input's value type has no YAML field of its own — it's derived from the bound variable's own
    // declared type, since the two must agree anyway (there's nowhere else a mismatch could come
    // from). A variable that isn't declared at all falls back to String rather than a hard load
    // error, matching this codebase's general warn-and-degrade-gracefully pattern.
    private InputValueType ResolveInputType(string varName, RenderContext ctx)
    {
        if (!ctx.Module.Variables.TryGetValue(varName, out var varDef))
        {
            _logger.LogWarning("input node binds to undeclared variable '{VarName}'; defaulting to string input", varName);
            return InputValueType.String;
        }

        return varDef.VarType == VarKind.Integer ? InputValueType.Number : InputValueType.String;
    }

    private void RenderForEach(ForEachNode fe, RenderContext ctx, List<RenderedNode> output)
    {
        var items = ctx.Store.GetVariable(fe.In).AsArray();
        foreach (var item in items)
        {
            ctx.Store.SetLetVariable(fe.Var, item);
            output.AddRange(RenderNodes(fe.Do, ctx));
            if (ctx.PendingGoto is not null)
            {
                break;
            }
        }
    }

    private IReadOnlyList<Node>? SelectConditionalBranch(ConditionalNode cond, VariableStore store)
    {
        foreach (var branch in cond.Conditions)
        {
            if (_evaluator.Evaluate(branch.If, store).AsBool())
            {
                return branch.Then;
            }
        }

        return cond.Else;
    }

    private IReadOnlyList<Node>? SelectSwitchCase(SwitchNode sw, VariableStore store)
    {
        var onValue = store.GetVariable(sw.On);
        foreach (var c in sw.Cases)
        {
            if (SwitchCaseMatches(onValue, c.Match))
            {
                return c.Nodes;
            }
        }

        return sw.Default;
    }

    private bool SwitchCaseMatches(StoryValue value, object match)
    {
        if (match is List<object> list)
        {
            return list.Any(m => SwitchCaseMatches(value, m));
        }

        var patternStr = match switch
        {
            long l => l.ToString(),
            bool b => b ? "1" : "0",
            string s => s,
            _ => match.ToString() ?? "",
        };
        return _evaluator.MatchesPattern(value, patternStr);
    }

    // "${module::entrypoint}" is a special sentinel, not an ordinary expression — see the same
    // constant/comment on GameSession.ResolveTarget (masterwork-plan-rev14.md Q24).
    private const string ModuleEntrypointTarget = "${module::entrypoint}";

    // Resolves a target field immediately: strips the "${...}" wrapper and evaluates it if present,
    // otherwise treats it as a literal passage_id. Used for `goto` and `include_passage`, which must
    // resolve at render time. link.Target and popup.Target stay raw in the rendered output —
    // GameSession resolves them at follow/close time (see RenderedLink.Target and friends).
    private string ResolveTargetNow(string raw, RenderContext ctx) =>
        raw switch
        {
            ModuleEntrypointTarget => ctx.Module.StartPassageId
                ?? throw new InvalidOperationException("'module::entrypoint' was used as a target, but this module has no 'Begins-Here' passage."),
            _ when raw.StartsWith("${", StringComparison.Ordinal) && raw.EndsWith('}') =>
                _evaluator.Evaluate(raw[2..^1], ctx.Store).AsString(),
            _ => raw,
        };

    private static string? ExpandOrNull(string? template, VariableStore store) =>
        template is null ? null : store.ExpandTemplate(template);
}
