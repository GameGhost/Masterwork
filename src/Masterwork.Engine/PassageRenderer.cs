using Masterwork.ModuleFormat;

namespace Masterwork.Engine;

/// <summary>
/// Walks a passage's node tree, applying assign/let mutations to the <see cref="VariableStore"/>
/// as it goes (in document order) and producing a flat rendered-node tree for the UI. Popup
/// content is left unevaluated (see the popup transaction model — content is only rendered when
/// the popup opens); <see cref="RenderNodeList"/> is the entry point used for that deferred
/// render, and for <c>include_passage</c>.
/// </summary>
public static class PassageRenderer
{
    /// <summary>Renders a passage to a flat node/action tree.</summary>
    /// <param name="passage">The passage to render.</param>
    /// <param name="store">Variable store to read from and mutate via <c>assign</c>/<c>let</c> nodes.</param>
    /// <param name="module">The loaded module, used to resolve <c>include_passage</c> targets.</param>
    /// <param name="visitedPassageIds">Passages considered "visited" for <c>check_progress</c> validation.</param>
    /// <exception cref="CheckProgressViolationException">The passage's <c>check_progress</c> prerequisite hasn't been visited.</exception>
    public static PassageRenderResult Render(
        MwsPassageDoc passage, VariableStore store, LoadedModule module, IReadOnlySet<string> visitedPassageIds)
    {
        if (passage.CheckProgress is not null && !visitedPassageIds.Contains(passage.CheckProgress))
        {
            throw new CheckProgressViolationException(passage.PassageId, passage.CheckProgress);
        }

        var ctx = new RenderContext(store, module);
        var nodes = RenderNodes(passage.Nodes, ctx);

        return new PassageRenderResult(
            PassageId: passage.PassageId,
            LocationName: passage.Location?.Name,
            LocationIcon: passage.Location?.Icon,
            Nodes: nodes,
            Actions: ctx.Actions,
            Checkpoints: ctx.Checkpoints,
            PendingGoto: ctx.PendingGoto);
    }

    /// <summary>
    /// Renders a raw node list in isolation — used for popup content on open, and internally for
    /// <c>include_passage</c> inlining.
    /// </summary>
    public static IReadOnlyList<RenderedNode> RenderNodeList(IReadOnlyList<Node> nodes, VariableStore store, LoadedModule module) =>
        RenderNodes(nodes, new RenderContext(store, module));

    private sealed class RenderContext(VariableStore store, LoadedModule module)
    {
        public VariableStore Store { get; } = store;
        public LoadedModule Module { get; } = module;
        public List<RenderedAction> Actions { get; } = [];
        public List<RenderedCheckpoint> Checkpoints { get; } = [];
        public string? PendingGoto { get; set; }

        private int _actionCounter;
        public string NextActionId(string prefix) => $"{prefix}_{_actionCounter++}";
    }

    private static List<RenderedNode> RenderNodes(IReadOnlyList<Node> nodes, RenderContext ctx)
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

    private static void RenderNode(Node node, RenderContext ctx, List<RenderedNode> output)
    {
        switch (node)
        {
            case TextNode t:
                output.Add(new RenderedText(ctx.Store.ExpandTemplate(t.Value), t.Align));
                break;
            case BreakNode:
                output.Add(new RenderedBreak());
                break;
            case ParagraphBreakNode:
                output.Add(new RenderedParagraphBreak());
                break;
            case ImageNode img:
                output.Add(new RenderedImage(img.Asset, img.Size, img.Align));
                break;
            case SectionNode s:
                output.Add(new RenderedSection(ExpandOrNull(s.Title, ctx.Store), s.Style, s.Collapsed, RenderNodes(s.Content, ctx)));
                break;
            case LetNode let:
                ctx.Store.SetLetVariable(let.Var, ExpressionEvaluator.Evaluate(let.Expr, ctx.Store));
                break;
            case AssignNode assign:
                ctx.Store.SetSessionVariable(assign.Var, ExpressionEvaluator.Evaluate(assign.Expr, ctx.Store));
                break;
            case NavigationNode nav:
                RenderNavigation(nav, ctx, output);
                break;
            case PopupNode popup:
                RenderPopup(popup, ctx, output);
                break;
            case InputNode input:
                RenderInput(input, ctx, output);
                break;
            case GotoNode go:
                ctx.PendingGoto = ResolveTargetNow(go.Target, ctx.Store);
                break;
            case IncludePassageNode inc:
                var targetId = ResolveTargetNow(inc.Target, ctx.Store);
                if (ctx.Module.Passages.TryGetValue(targetId, out var includedPassage))
                {
                    output.AddRange(RenderNodes(includedPassage.Nodes, ctx));
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
            case RecordNode:
                // Achievement triggers are deferred to Phase 3; no-op at runtime.
                break;
            case PromptNode:
                // Spec'd but not yet emitted by the extractor; no real passages exercise this path.
                break;
        }
    }

    private static void RenderNavigation(NavigationNode nav, RenderContext ctx, List<RenderedNode> output)
    {
        var rendered = new RenderedNavigation
        {
            Id = ctx.NextActionId("nav"),
            Label = ctx.Store.ExpandTemplate(nav.Label),
            Style = nav.Style,
            Target = nav.Target,
            StateAffecting = nav.StateAffecting,
            TimelineLabel = nav.TimelineLabel,
            OnClickRaw = nav.OnClick,
        };
        output.Add(rendered);
        ctx.Actions.Add(rendered);
    }

    private static void RenderPopup(PopupNode popup, RenderContext ctx, List<RenderedNode> output)
    {
        var rendered = new RenderedPopup
        {
            Id = ctx.NextActionId("popup"),
            Label = ExpandOrNull(popup.Label, ctx.Store),
            Style = popup.Style,
            Layout = popup.Layout,
            AutoDisplay = popup.Label is null,
            RawContent = popup.Content,
            OnClose = popup.OnClose,
            Button = popup.Button,
            StateAffecting = popup.StateAffecting,
        };
        output.Add(rendered);
        ctx.Actions.Add(rendered);
    }

    private static void RenderInput(InputNode input, RenderContext ctx, List<RenderedNode> output)
    {
        var rendered = new RenderedInput
        {
            Id = ctx.NextActionId("input"),
            Label = ctx.Store.ExpandTemplate(input.Label),
            Style = input.Style,
            Text = ctx.Store.ExpandTemplate(input.Text),
            InputType = input.InputType,
            Var = input.Var,
            OnSubmit = input.OnSubmit,
        };
        output.Add(rendered);
        ctx.Actions.Add(rendered);
    }

    private static void RenderForEach(ForEachNode fe, RenderContext ctx, List<RenderedNode> output)
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

    private static IReadOnlyList<Node>? SelectConditionalBranch(ConditionalNode cond, VariableStore store)
    {
        foreach (var branch in cond.Conditions)
        {
            if (ExpressionEvaluator.Evaluate(branch.If, store).AsBool())
            {
                return branch.Then;
            }
        }

        return cond.Else;
    }

    private static IReadOnlyList<Node>? SelectSwitchCase(SwitchNode sw, VariableStore store)
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

    private static bool SwitchCaseMatches(ExprValue value, object match)
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
        return ExpressionEvaluator.MatchesPattern(value, patternStr);
    }

    // Resolves a target/onclose/onsubmit field immediately: strips the "${...}" wrapper and
    // evaluates it if present, otherwise treats it as a literal passage_id. Used for `goto` and
    // `include_passage`, which must resolve at render time. navigation.Target, input.Onsubmit and
    // popup.Onclose stay raw in the rendered output — the App resolves them at follow/submit/close
    // time (see RenderedNavigation.Target and friends).
    private static string ResolveTargetNow(string raw, VariableStore store) =>
        raw.StartsWith("${", StringComparison.Ordinal) && raw.EndsWith('}')
            ? ExpressionEvaluator.Evaluate(raw[2..^1], store).AsString()
            : raw;

    private static string? ExpandOrNull(string? template, VariableStore store) =>
        template is null ? null : store.ExpandTemplate(template);
}
