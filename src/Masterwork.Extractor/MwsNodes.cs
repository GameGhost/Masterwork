using System.Text;
using System.Text.RegularExpressions;

namespace Masterwork.Extractor;

// ── Passage ────────────────────────────────────────────────────────────────

public class MwsPassage
{
    public string Format { get; set; } = "mws/0.3";
    public string PassageId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string[] Tags { get; set; } = [];
    public string Layout { get; set; } = "narration";
    public List<MwsNode> Nodes { get; set; } = [];
    public bool Debug { get; set; }

    // Extraction metadata — not serialized to YAML content, used for comment injection.
    public int? PassageIndex { get; set; }
    public int? MainMethodSourceLine { get; set; }
    public string? SourceFile { get; set; }
    // True when this passage is ever referenced as a static include_passage target elsewhere in the
    // same source file — its Nodes are spliced verbatim into the includer's own body at render time,
    // so it deliberately never gets a title hoisted (see CradleExtractor.BuildPassages' own remarks).
    // Surfaced as a YAML comment (Program.cs's InjectSourceComments) so a reader isn't left wondering
    // why an otherwise heading-shaped passage has none.
    public bool IsIncludeTarget { get; set; }

    public Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?>
        {
            ["format"] = "mws/0.3",
            ["passage_id"] = PassageId,
        };
        if (!string.IsNullOrEmpty(Title) && Title != PassageId)
        {
            d["title"] = Title;
        }

        if (Tags.Length > 0)
        {
            d["tags"] = Tags;
        }

        d["layout"] = Layout;
        if (Debug)
        {
            d["debug"] = true;
        }

        d["nodes"] = Nodes.Select(n => n.ToDict()).ToList();
        return d;
    }
}

// ── Base node ──────────────────────────────────────────────────────────────

public abstract class MwsNode
{
    public abstract string Type { get; }
    // Extraction metadata — not serialized to YAML content, used for comment injection.
    public int? SourceLine { get; set; }
    public abstract Dictionary<string, object?> ToDict();
}

// ── Text ───────────────────────────────────────────────────────────────────

public class TextRun
{
    public string? Text { get; set; }
    public string? Style { get; set; }
    public string? AssetRef { get; set; }

    public Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?>();
        if (Text is not null)
        {
            d["text"] = Text;
        }

        if (Style is not null)
        {
            d["style"] = Style;
        }

        if (AssetRef is not null)
        {
            d["asset_ref"] = AssetRef;
        }

        return d;
    }
}

public class TextNode : MwsNode
{
    public override string Type => "text";

    // Template path — single i18n-translatable string.
    // Use {varName} for variable refs, {icon:slug} for inline assets,
    // **...** for bold spans, _..._ for italic spans.
    public string? Template { get; set; }
    public string? Style { get; set; }      // uniform style for the whole string
    public List<string>? Lets { get; set; } // let-var names consumed by this template

    // Runs path — kept for mixed asset+text nodes that need separate localization keys.
    public List<TextRun> Runs { get; set; } = [];

    // Optional horizontal alignment from source <align=...> tag.
    public string? Align { get; set; }

    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type };
        string value;
        if (Template is not null)
        {
            value = MwsExprHelper.ApplyInlineStyle(Template, Style);
        }
        else if (Runs.Count > 0)
        {
            value = MwsExprHelper.BuildValueFromRuns(Runs);
        }
        else
        {
            value = "";
        }

        d["value"] = value;
        if (Lets is { Count: > 0 })
        {
            d["lets"] = Lets;
        }

        if (Align is not null)
        {
            d["align"] = Align;
        }

        // "bold"/"italic" are baked into the markdown value above via ApplyInlineStyle, not
        // exposed as their own field — but a non-emphasis style (e.g. "special-event", see
        // PassageBodyVisitor.IsShowEventPopupCall) has no markdown representation and must
        // surface as a real v0.3 `style:` field for module CSS to hook into.
        if (Style is not null and not "bold" and not "italic")
        {
            d["style"] = Style;
        }

        return d;
    }
}

public class ImageNode : MwsNode
{
    public override string Type => "image";

    public string AssetRef { get; set; } = "";
    // Size preserved as-is from the source <size=N> tag.
    public string? Size { get; set; }
    // Optional horizontal alignment from source <align=...> tag.
    public string? Align { get; set; }
    // Open, module-styled vocabulary — e.g. "setup-image" for a Vars._SetupImage-derived image,
    // which V2Serializer also uses to route this node into the enclosing popup's header: list
    // instead of content: (see TransformPopup/TransformSetupNotificationBlock).
    public string? Style { get; set; }

    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type, ["asset"] = AssetRef };
        if (Size is not null)
        {
            d["size"] = Size;
        }

        if (Align is not null)
        {
            d["align"] = Align;
        }

        if (Style is not null)
        {
            d["style"] = Style;
        }

        return d;
    }
}

// ── Structural ─────────────────────────────────────────────────────────────

public class BreakNode : MwsNode
{
    public override string Type => "break";
    // True when this break was emitted while a bold/italic styleScope was still open (i.e. a
    // lineBreak() call nested inside the same using block as the text around it) — distinguishes
    // "one heading's own internal line break" from "a break between two unrelated statements" for
    // CradleExtractor.TryHoistHeadingTitleSubtitle. Extraction metadata only, not serialized.
    public bool WithinStyleScope { get; set; }
    public override Dictionary<string, object?> ToDict() => new() { ["type"] = Type };
}

public class ParagraphBreakNode : MwsNode
{
    public override string Type => "paragraph_break";
    // See BreakNode.WithinStyleScope — true only when every break merged into this one was itself
    // within the same open styleScope.
    public bool WithinStyleScope { get; set; }
    public override Dictionary<string, object?> ToDict() => new() { ["type"] = Type };
}

public class SectionHeadingNode : MwsNode
{
    public override string Type => "section_heading";
    public string Text { get; set; } = "";
    public override Dictionary<string, object?> ToDict() => new() { ["type"] = Type, ["text"] = Text };
}

public class SectionBodyNode : MwsNode
{
    public override string Type => "section_body";
    public List<MwsNode> Nodes { get; set; } = [];
    public override Dictionary<string, object?> ToDict() => new()
    {
        ["type"] = Type,
        ["nodes"] = Nodes.Select(n => n.ToDict()).ToList(),
    };
}

public class SetupBlockNode : MwsNode
{
    public override string Type => "setup_block";
    public List<MwsNode> Nodes { get; set; } = [];
    public override Dictionary<string, object?> ToDict() => new()
    {
        ["type"] = Type,
        ["nodes"] = Nodes.Select(n => n.ToDict()).ToList(),
    };
}

// ── Navigation ─────────────────────────────────────────────────────────────

public class LinkNode : MwsNode
{
    public override string Type => "navigation";
    public string Label { get; set; } = "";
    public string? Target { get; set; }
    public bool StateAffecting { get; set; } = true;
    public string? TimelineLabel { get; set; }
    // State effects that execute when the link is followed (from stitched enchantHook fragments)
    public List<MwsNode> Nodes { get; set; } = [];

    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?>
        {
            ["type"] = Type,
            ["label"] = Label,
            ["state_affecting"] = StateAffecting,
        };
        if (Target is not null)
        {
            d["target"] = Target;
        }

        if (TimelineLabel is not null)
        {
            d["timeline_label"] = TimelineLabel;
        }

        if (Nodes.Count > 0)
        {
            d["nodes"] = Nodes.Select(n => n.ToDict()).ToList();
        }

        return d;
    }
}

public class ExpandLinkNode : MwsNode
{
    public override string Type => "expand_link";
    public string Label { get; set; } = "";
    public bool StateAffecting { get; set; }
    public List<MwsNode> ExpandNodes { get; set; } = [];

    public override Dictionary<string, object?> ToDict() => new()
    {
        ["type"] = Type,
        ["label"] = Label,
        ["state_affecting"] = StateAffecting,
        ["expand_nodes"] = ExpandNodes.Select(n => n.ToDict()).ToList(),
    };
}

public class GotoNode : MwsNode
{
    public override string Type => "goto";
    public string Target { get; set; } = "";
    public string? Condition { get; set; }

    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type, ["target"] = Target };
        if (Condition is not null)
        {
            d["condition"] = Condition;
        }

        return d;
    }
}

public class GotoMenuNode : MwsNode
{
    public override string Type => "goto_menu";
    public string Target { get; set; } = "main_menu";
    public override Dictionary<string, object?> ToDict() => new() { ["type"] = Type, ["target"] = Target };
}

public class IncludePassageNode : MwsNode
{
    public override string Type => "include_passage";
    public string Target { get; set; } = "";
    public override Dictionary<string, object?> ToDict() => new() { ["type"] = Type, ["target"] = Target };
}

public class EndOfGenerationNode : MwsNode
{
    public override string Type => "end_of_generation";
    public int Generation { get; set; }
    public string? Message { get; set; }

    public override Dictionary<string, object?> ToDict() => new()
    {
        ["type"] = Type,
        ["generation"] = Generation,
        ["message"] = Message,
    };
}

// ViewEndOfRound.instance.SetEndOfRound(body, round, next, body2) called directly, not via
// PassageTracker.CheckProgress (see EndOfRoundMarkerNode for that indirect, progress-map-driven
// path) — always a bare top-level statement with no enclosing clickable link in source (e.g. A
// Time of War's MartialPre3), so V2Serializer.TransformModal synthesizes the whole auto-display
// popup directly from these fields, the same way TransformEndOfGeneration does for
// EndOfGenerationNode's own "top-level marker call, no trigger link" shape. ToDict() below is
// never actually reached — TransformNode's switch always intercepts ModalNode first — but is kept
// consistent with the fields regardless, matching every other extractor-internal node's own shape.
public class ModalNode : MwsNode
{
    public override string Type => "modal";
    public string? Chrome { get; set; }
    public string? Body { get; set; }
    public int? Round { get; set; }
    public string? Next { get; set; }
    public string? Body2 { get; set; }

    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type };
        if (Chrome is not null)
        {
            d["chrome"] = Chrome;
        }

        if (Body is not null)
        {
            d["body"] = Body;
        }

        if (Round is not null)
        {
            d["round"] = Round;
        }

        if (Next is not null)
        {
            d["next"] = Next;
        }

        if (Body2 is not null)
        {
            d["body2"] = Body2;
        }

        return d;
    }
}

// ── Logic ──────────────────────────────────────────────────────────────────

public class ConditionalBranch
{
    public string? Condition { get; set; }
    public bool? Else { get; set; }
    public List<MwsNode> Nodes { get; set; } = [];

    public Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?>();
        if (Condition is not null)
        {
            d["if"] = Condition;
        }

        d["then"] = Nodes.Select(n => n.ToDict()).ToList();
        return d;
    }
}

public class ConditionalNode : MwsNode
{
    public override string Type => "conditional";
    public List<ConditionalBranch> Branches { get; set; } = [];
    public override Dictionary<string, object?> ToDict()
    {
        var ifBranches = Branches.Where(b => b.Else != true).ToList();
        var elseBranch = Branches.FirstOrDefault(b => b.Else == true);

        // Flat format: single if-branch with no else
        if (ifBranches.Count == 1 && elseBranch is null)
        {
            return new Dictionary<string, object?>
            {
                ["type"] = Type,
                ["if"] = ifBranches[0].Condition,
                ["then"] = ifBranches[0].Nodes.Select(n => n.ToDict()).ToList(),
            };
        }

        // Multi-branch format
        var d = new Dictionary<string, object?>
        {
            ["type"] = Type,
            ["conditions"] = ifBranches.Select(b => b.ToDict()).ToList(),
        };
        if (elseBranch is not null)
        {
            d["else"] = elseBranch.Nodes.Select(n => n.ToDict()).ToList();
        }

        return d;
    }
}

public class SwitchCase
{
    public object? Match { get; set; }  // int for numeric equality, string for pattern (e.g. "<=5")
    public bool? Default { get; set; }
    public List<MwsNode> Nodes { get; set; } = [];

    public Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?>();
        if (Match is not null)
        {
            d["match"] = Match;
        }

        d["nodes"] = Nodes.Select(n => n.ToDict()).ToList();
        return d;
    }
}

public class SwitchNode : MwsNode
{
    public override string Type => "switch";
    public string On { get; set; } = "";
    public List<SwitchCase> Cases { get; set; } = [];

    public override Dictionary<string, object?> ToDict()
    {
        var matchCases = Cases.Where(c => c.Default != true).ToList();
        var defaultCase = Cases.FirstOrDefault(c => c.Default == true);
        var d = new Dictionary<string, object?>
        {
            ["type"] = Type,
            ["on"] = On,
            ["cases"] = matchCases.Select(c => c.ToDict()).ToList(),
        };
        if (defaultCase is not null)
        {
            d["default"] = defaultCase.Nodes.Select(n => n.ToDict()).ToList();
        }

        return d;
    }
}

public class VarRandom
{
    public string RandomType { get; set; } = "choose-one";
    public List<object> Values { get; set; } = [];
    public int? Min { get; set; }
    public int? Max { get; set; }
    public string? SeedKey { get; set; }

    public Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = RandomType };
        if (Values.Count > 0)
        {
            d["values"] = Values;
        }

        if (Min.HasValue)
        {
            d["min"] = Min;
        }

        if (Max.HasValue)
        {
            d["max"] = Max;
        }

        if (SeedKey is not null)
        {
            d["seed_key"] = SeedKey;
        }

        return d;
    }
}

// Direction + optional property for sort operations.
// Used both for in-place sort (EffectNode.VarSort, From=null) and
// for sort-into-var (LetNode.Sort, From=source array name).
public class SortSpec
{
    public string? From { get; set; }
    public string Direction { get; set; } = "ascending";
    public string? Property { get; set; }

    public Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["direction"] = Direction };
        if (From is not null)
        {
            d["from"] = From;
        }

        if (Property is not null)
        {
            d["property"] = Property;
        }

        return d;
    }
}

public class EffectNode : MwsNode
{
    public override string Type => "effect";
    public Dictionary<string, object?>? VarSets { get; set; }
    public Dictionary<string, string>? VarMath { get; set; }
    public Dictionary<string, VarRandom>? VarRandom { get; set; }
    // Array mutation: push a constructed value onto a named array variable
    public Dictionary<string, string>? VarPush { get; set; }
    // Array mutation: pop from a named array variable (discard result)
    public string? VarPop { get; set; }
    // Array sort in-place: {arrayVar: {direction, property}}
    public Dictionary<string, SortSpec>? VarSort { get; set; }
    // Array remove: remove a specific value from a named array variable
    public Dictionary<string, string>? VarRemove { get; set; }

    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type };
        if (VarSets is { Count: > 0 })
        {
            d["var_sets"] = VarSets;
        }

        if (VarMath is { Count: > 0 })
        {
            d["var_math"] = VarMath;
        }

        if (VarRandom is { Count: > 0 })
        {
            d["var_random"] = VarRandom.ToDictionary(
                kv => kv.Key,
                kv => (object?)kv.Value.ToDict());
        }

        if (VarPush is { Count: > 0 })
        {
            d["var_push"] = VarPush;
        }

        if (VarPop is not null)
        {
            d["var_pop"] = VarPop;
        }

        if (VarSort is { Count: > 0 })
        {
            d["var_sort"] = VarSort.ToDictionary(
                kv => kv.Key,
                kv => (object?)kv.Value.ToDict());
        }

        if (VarRemove is { Count: > 0 })
        {
            d["var_remove"] = VarRemove;
        }

        return d;
    }
}

// ── Let — passage-scoped variable ─────────────────────────────────────────

public class VarReplace
{
    public string Source { get; set; } = "";
    public object Find { get; set; } = "";  // string (single) or List<string> (multiple)
    public string With { get; set; } = "";

    public Dictionary<string, object?> ToDict() => new()
    {
        ["source"] = Source,
        ["find"] = Find,
        ["with"] = With,
    };
}

public class LetNode : MwsNode
{
    public override string Type => "let";
    public string Var { get; set; } = "";
    public VarRandom? Random { get; set; }
    public VarReplace? Replace { get; set; }
    public string? PickFrom { get; set; }  // pick a random element from a named array variable
    // Temporary array: list of variable names whose values form the array
    public List<string>? Array { get; set; }
    // Aggregate compute expression: max(...), min(...), countif(<pattern>, ...)
    public string? Compute { get; set; }
    // Pop the last element from a named array variable and assign to var
    public string? Pop { get; set; }
    // Dequeue (shift) the first element from a named array variable and assign to var
    public string? Dequeue { get; set; }
    // Sort a named array into this var (From = source array)
    public SortSpec? Sort { get; set; }

    public override Dictionary<string, object?> ToDict()
    {
        return new Dictionary<string, object?>
        {
            ["type"] = Type,
            ["var"] = Var,
            ["expr"] = MwsExprHelper.LetToExpr(this),
        };
    }
}

public class ForeachNode : MwsNode
{
    public override string Type => "foreach";
    public string Var { get; set; } = "";   // loop variable name
    public string In { get; set; } = "";    // array variable to iterate
    public List<MwsNode> Nodes { get; set; } = [];

    public override Dictionary<string, object?> ToDict() => new()
    {
        ["type"] = Type,
        ["var"] = Var,
        ["in"] = In,
        ["do"] = Nodes.Select(n => n.ToDict()).ToList(),
    };
}

// ── Input & interaction ────────────────────────────────────────────────────

public class InputPromptNode : MwsNode
{
    public override string Type => "input_prompt";
    public string PromptId { get; set; } = "";
    public string Text { get; set; } = "";
    public string InputType { get; set; } = "string";
    public string StoreIn { get; set; } = "";
    public string? ResumePassage { get; set; }

    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?>
        {
            ["type"] = Type,
            ["prompt_id"] = PromptId,
            ["text"] = Text,
            ["input"] = InputType,
            ["var"] = StoreIn,
        };
        if (ResumePassage is not null)
        {
            d["resume_passage"] = ResumePassage;
        }

        return d;
    }
}

// ── App integration ────────────────────────────────────────────────────────

public class SetLocationNode : MwsNode
{
    public override string Type => "set_location";
    public string? Name { get; set; }
    public string? Icon { get; set; }
    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type };
        if (Name is not null)
        {
            d["name"] = Name;
        }

        if (Icon is not null)
        {
            d["icon"] = Icon;
        }

        return d;
    }
}

public class SetupNotificationNode : MwsNode
{
    public override string Type => "setup_notification";
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? NextPassage { get; set; }
    // Set instead of NextPassage when the destination is a Cradle either()/random() draw
    // (SetupPassagename = macros1.either(...)) — Cradle draws a fresh value every call, and the
    // draw expression (e.g. ["A","B"].shuffled(seedKey)[0]) is a pure function of the PRNG state,
    // so it can be embedded directly wherever the resolved target string is needed (V2Serializer's
    // ResolveSetupTarget) instead of being hoisted into an intermediate variable. Two hoisting
    // attempts were tried and reverted: a `let` doesn't survive a popup's own content→target scope
    // boundary (see git history), and a session-variable `assign` works but pollutes popup content
    // with an unrelated statement and needlessly widened TryGetSetupTargetArms's branch pattern
    // (a real regression — see git history). Assigned a SeedKey later by
    // CradleExtractor.AssignSeedKeysInNodes, same as every other VarRandom in the tree.
    public VarRandom? Random { get; set; }
    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type };
        if (Title is not null)
        {
            d["title"] = Title;
        }

        if (Text is not null)
        {
            d["text"] = Text;
        }

        if (NextPassage is not null)
        {
            d["next_passage"] = NextPassage;
        }

        return d;
    }
}

public class CheckProgressNode : MwsNode
{
    public override string Type => "check_progress";
    public string CurrentPassage { get; set; } = "";
    public string TargetPassage { get; set; } = "";
    public override Dictionary<string, object?> ToDict() => new()
    {
        ["type"] = Type,
        ["current_passage"] = CurrentPassage,
        ["target_passage"] = TargetPassage,
    };
}

// ── Milestone ──────────────────────────────────────────────────────────────

public class CheckpointNode : MwsNode
{
    public override string Type => "checkpoint";
    public string Id { get; set; } = "";
    public string? DisplayLabel { get; set; }
    public string? DiagnosticLabel { get; set; }
    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type, ["id"] = Id };
        if (DisplayLabel is not null)
        {
            d["display"] = DisplayLabel;
        }

        if (DiagnosticLabel is not null)
        {
            d["diagnostic"] = DiagnosticLabel;
        }

        return d;
    }
}

// ── Fallback ───────────────────────────────────────────────────────────────

public class UnknownNode : MwsNode
{
    public override string Type => "unknown";
    public string OriginalCode { get; set; } = "";
    public string? Note { get; set; }
    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type, ["original_code"] = OriginalCode };
        if (Note is not null)
        {
            d["note"] = Note;
        }

        return d;
    }
}

// ── Expression building helpers ────────────────────────────────────────────
// Converts intermediate node fields (VarRandom, SortSpec, etc.) into v0.2 expression strings.
// Used by TextNode.ToDict(), LetNode.ToDict(), and V2Serializer for EffectNode expansion.

public static partial class MwsExprHelper
{
    public static string EscapeStr(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static string ApplyInlineStyle(string template, string? style) => style switch
    {
        "bold" => WrapEmphasis(template, "**"),
        "italic" => WrapEmphasis(template, "_"),
        _ => template,
    };

    // Wraps `text` in `marker` (e.g. "**" or "_"), keeping any leading/trailing whitespace outside
    // the delimiters instead of inside them. CommonMark requires a valid opening delimiter not be
    // followed by whitespace, and a valid closing delimiter not be preceded by it — Cradle's own
    // bold/italic runs often include the space that separated them from neighboring plain text, so
    // wrapping the raw run verbatim (e.g. "**Test markdown **") produces delimiters no standard
    // markdown parser recognizes as emphasis at all.
    public static string WrapEmphasis(string text, string marker)
    {
        var core = text.Trim();
        if (core.Length == 0)
        {
            return text; // whitespace-only — nothing to emphasize
        }

        var lead = text[..(text.Length - text.TrimStart().Length)];
        var trail = text[text.TrimEnd().Length..];
        return $"{lead}{marker}{core}{marker}{trail}";
    }

    // Merges consecutive same-style runs into one buffer before wrapping, so a bold/italic span
    // built from several runs (e.g. "Turn to " + bold("The Cost of Disease") + " book") gets exactly
    // one pair of delimiters around its full text — and WrapEmphasis only ever sees the true
    // leading/trailing edge of that span, not a mid-span run boundary, so it can correctly place
    // whitespace outside the delimiters (see WrapEmphasis's remarks).
    public static string BuildValueFromRuns(List<TextRun> runs)
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
                "bold" => WrapEmphasis(text, "**"),
                "italic" => WrapEmphasis(text, "_"),
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

            if (run.Style != currentStyle)
            {
                FlushBuffer();
                currentStyle = run.Style;
            }

            buffer.Append(run.Text);
        }

        FlushBuffer();
        return CollapseAdjacentSpaces(sb.ToString());
    }

    // WrapEmphasis moving a run's own leading/trailing whitespace outside its delimiters can land
    // it right next to a space that already existed from a separate, unstyled run (e.g. an icon ref
    // followed by its own plain-text space, immediately before a bold run whose leading space just
    // got relocated there too) — collapse the resulting run of 2+ spaces/tabs into one.
    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex AdjacentSpaceRun();

    public static string CollapseAdjacentSpaces(string s) => AdjacentSpaceRun().Replace(s, " ");

    public static string ValueToExpr(object v) => v switch
    {
        int n => n.ToString(),
        long l => l.ToString(),
        bool b => b ? "true" : "false",
        string s => StringValueToExpr(s),
        _ => $"\"{v}\"",
    };

    public static string StringValueToExpr(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "\"\"";
        }

        if (s.StartsWith("restext://"))
        {
            return $"\"{s}\"";
        }

        if (s.StartsWith("{") && s.EndsWith("}") && !s[1..^1].Contains('{'))
        {
            var inner = s[1..^1].Replace(".first()", "[0]");
            return inner;
        }
        if (s.StartsWith("(") || s.Contains("global:") || s.Contains("input:"))
        {
            return s;
        }

        if (s.StartsWith("?("))
        {
            return s;
        }

        if (long.TryParse(s, out _))
        {
            return s;
        }

        if (!s.Contains('{') && !s.Contains('+'))
        {
            return $"\"{EscapeStr(s)}\"";
        }

        // A mixed string containing literal text AND {var} placeholders (but not the bare
        // single-var wrapper handled above) is a display-text-style template, e.g. one choice of
        // an either() call whose other arg was a concatenation ("...center of " + townname + "...").
        // It is a VALUE to be stored (later interpolated at text-render time when the variable
        // holding it is displayed), not runtime-interpolatable expr code — the engine's expr
        // grammar has no {var} interpolation at all. Quote it so the braces survive as literal
        // characters in the array element instead of producing invalid, unparseable expr syntax.
        // Real-world crash: Fear of the Unknown's JunkPalaceSignIn passage, whose either() choices
        // are picked via a LetNode and displayed later via {tempVar} — same root bug shape as
        // VarSetStringToExpr's fallthrough (see PassageBodyVisitor's TryExtractExprConcat comment),
        // but this one only ever feeds a stored VALUE, so quoting (not VarMath-style rerouting) is
        // the correct fix here.
        if (s.Contains('{'))
        {
            return $"\"{EscapeStr(s)}\"";
        }

        return s;
    }

    public static string VarSetStringToExpr(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "\"\"";
        }

        if (s.StartsWith("restext://"))
        {
            return $"\"{s}\"";
        }

        if (s.StartsWith("{") && s.EndsWith("}") && !s[1..^1].Contains('{'))
        {
            var inner = s[1..^1].Replace(".first()", "[0]");
            return inner;
        }
        if (s.StartsWith("(") || s.Contains("global:") || s.Contains("input:"))
        {
            return s;
        }

        if (s.StartsWith("?("))
        {
            return s;
        }

        if (s.Contains(".shuffle()"))
        {
            return s;
        }

        if (!s.Contains('{'))
        {
            return $"\"{EscapeStr(s)}\"";
        }

        // A mixed string containing literal text AND {var} placeholders (but not the bare
        // single-var wrapper handled above) is a display-text-style template — e.g. a
        // concatenation like "The " + townname + " " + either(...). Now that the expression
        // grammar supports {var} interpolation inside quoted string literals (see
        // ExpressionEvaluator.ExpandTemplate), quoting it here is both crash-free AND
        // semantically correct: the braces are interpolated at evaluation time, not frozen as
        // literal text. This is also what makes the whole template eligible for restext
        // extraction as its own translatable resource (RestextCollector.WalkExprNode's quoted-
        // literal case) — see StringConcatWithEither_EmitsLetThenTemplate's regression comment.
        return $"\"{EscapeStr(s)}\"";
    }

    public static string VarSetValueToExpr(object? val) => val switch
    {
        null => "null",
        int n => n.ToString(),
        long l => l.ToString(),
        bool b => b ? "true" : "false",
        List<object> list => "[" + string.Join(", ", list.Select(v => ValueToExpr(v))) + "]",
        string s => VarSetStringToExpr(s),
        _ => $"\"{val}\"",
    };

    public static string VarMathToExpr(string varName, string math)
    {
        if (math == "+0")
        {
            return varName;
        }

        if (math.StartsWith("= "))
        {
            return math[2..];
        }

        var op = math[0];
        var operand = math[1..];
        if (operand.StartsWith("{") && operand.EndsWith("}"))
        {
            operand = operand[1..^1];
        }

        return $"{varName} {op} {operand}";
    }

    public static string ValuesToExprList(List<object> values) =>
        string.Join(", ", values.Select(ValueToExpr));

    public static string VarRandomToExpr(VarRandom vr)
    {
        var key = vr.SeedKey is not null ? $"\"{EscapeStr(vr.SeedKey)}\"" : "\"?\"";
        return vr.RandomType switch
        {
            "choose-one" => vr.Values.Count == 1
                ? ValueToExpr(vr.Values[0])
                : $"[{ValuesToExprList(vr.Values)}].shuffled({key})[0]",
            "range" => $"rand_between({vr.Min}, {vr.Max}, {key})",
            "rand-between" => $"rand_between({vr.Min}, {vr.Max}, {key})",
            "shuffled_array" => $"[{ValuesToExprList(vr.Values)}].shuffled({key})",
            _ => $"/* unsupported random type: {vr.RandomType} */",
        };
    }

    public static string ReplaceToExpr(VarReplace r)
    {
        var with = $"\"{EscapeStr(r.With)}\"";
        if (r.Find is List<string> finds)
        {
            var result = r.Source;
            foreach (var find in finds)
            {
                result = $"{result}.replace(\"{EscapeStr(find)}\", {with})";
            }

            return result;
        }
        var findStr = r.Find?.ToString() ?? "";
        return $"{r.Source}.replace(\"{EscapeStr(findStr)}\", {with})";
    }

    public static string SortToExpr(string source, SortSpec sort)
    {
        var dir = $"\"{EscapeStr(sort.Direction)}\"";
        if (sort.Property is not null)
        {
            return $"{source}.toSorted({dir}, \"{EscapeStr(sort.Property)}\")";
        }

        return $"{source}.toSorted({dir})";
    }

    public static string LetToExpr(LetNode let)
    {
        if (let.Random is not null)
        {
            return VarRandomToExpr(let.Random);
        }

        if (let.Replace is not null)
        {
            return ReplaceToExpr(let.Replace);
        }

        if (let.PickFrom is not null)
        {
            return $"{let.PickFrom}.shuffled(\"{EscapeStr(let.Var)}_0\")[0]";
        }

        if (let.Array is not null)
        {
            return "[" + string.Join(", ", let.Array) + "]";
        }

        if (let.Compute is not null)
        {
            return let.Compute;
        }

        if (let.Sort is not null)
        {
            return SortToExpr(let.Sort.From ?? let.Var, let.Sort);
        }

        if (let.Pop is not null)
        {
            return $"{let.Pop}[^1]";
        }

        if (let.Dequeue is not null)
        {
            return $"{let.Dequeue}[0]";
        }

        return "null";
    }
}
