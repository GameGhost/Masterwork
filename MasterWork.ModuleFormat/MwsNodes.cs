using System.Collections.Generic;
using System.Linq;

namespace MasterWork.ModuleFormat;

// ── Passage ────────────────────────────────────────────────────────────────

public class MwsPassage
{
    public string Format { get; set; } = "mws/1.0";
    public string PassageId { get; set; } = "";
    public string Title { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public string Layout { get; set; } = "narration";
    public List<MwsNode> Nodes { get; set; } = [];
    public bool Debug { get; set; }

    // Extraction metadata — not serialized to YAML content, used for comment injection.
    public int? PassageIndex { get; set; }
    public int? MainMethodSourceLine { get; set; }
    public string? SourceFile { get; set; }

    public Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?>
        {
            ["format"] = Format,
            ["passage_id"] = PassageId,
        };
        if (!string.IsNullOrEmpty(Title)) d["title"] = Title;
        if (Tags.Length > 0) d["tags"] = Tags;
        d["layout"] = Layout;
        if (Debug) d["debug"] = true;
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
        if (Text is not null) d["text"] = Text;
        if (Style is not null) d["style"] = Style;
        if (AssetRef is not null) d["asset_ref"] = AssetRef;
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

    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type };
        if (Template is not null)
        {
            d["template"] = Template;
            if (Style is not null) d["style"] = Style;
            if (Lets is { Count: > 0 }) d["lets"] = Lets;
        }
        else
        {
            d["runs"] = Runs.Select(r => r.ToDict()).ToList();
        }
        return d;
    }
}

// ── Structural ─────────────────────────────────────────────────────────────

public class BreakNode : MwsNode
{
    public override string Type => "break";
    public override Dictionary<string, object?> ToDict() => new() { ["type"] = Type };
}

public class ParagraphBreakNode : MwsNode
{
    public override string Type => "paragraph_break";
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
    public override string Type => "link";
    public string Label { get; set; } = "";
    public string Target { get; set; } = "";
    public bool StateAffecting { get; set; } = true;
    public string? TimelineLabel { get; set; }

    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?>
        {
            ["type"] = Type,
            ["label"] = Label,
            ["target"] = Target,
            ["state_affecting"] = StateAffecting,
        };
        if (TimelineLabel is not null) d["timeline_label"] = TimelineLabel;
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
        if (Condition is not null) d["condition"] = Condition;
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

public class ModalNode : MwsNode
{
    public override string Type => "modal";
    public string? Chrome { get; set; }
    public string? Body { get; set; }
    public int? Round { get; set; }
    public string? Next { get; set; }
    public string? Instruction { get; set; }

    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type };
        if (Chrome is not null) d["chrome"] = Chrome;
        if (Body is not null) d["body"] = Body;
        if (Round is not null) d["round"] = Round;
        if (Next is not null) d["next"] = Next;
        if (Instruction is not null) d["instruction"] = Instruction;
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
        if (Condition is not null) d["condition"] = Condition;
        if (Else == true) d["else"] = true;
        d["nodes"] = Nodes.Select(n => n.ToDict()).ToList();
        return d;
    }
}

public class ConditionalNode : MwsNode
{
    public override string Type => "conditional";
    public List<ConditionalBranch> Branches { get; set; } = [];
    public override Dictionary<string, object?> ToDict() => new()
    {
        ["type"] = Type,
        ["branches"] = Branches.Select(b => b.ToDict()).ToList(),
    };
}

public class SwitchCase
{
    public object? Match { get; set; }  // int for numeric equality, string for pattern (e.g. "<=5")
    public bool? Default { get; set; }
    public List<MwsNode> Nodes { get; set; } = [];

    public Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?>();
        if (Match is not null) d["match"] = Match;
        if (Default == true) d["default"] = true;
        d["nodes"] = Nodes.Select(n => n.ToDict()).ToList();
        return d;
    }
}

public class SwitchNode : MwsNode
{
    public override string Type => "switch";
    public string On { get; set; } = "";
    public List<SwitchCase> Cases { get; set; } = [];

    public override Dictionary<string, object?> ToDict() => new()
    {
        ["type"] = Type,
        ["on"] = On,
        ["cases"] = Cases.Select(c => c.ToDict()).ToList(),
    };
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
        if (Values.Count > 0) d["values"] = Values;
        if (Min.HasValue) d["min"] = Min;
        if (Max.HasValue) d["max"] = Max;
        if (SeedKey is not null) d["seed_key"] = SeedKey;
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
        if (From is not null) d["from"] = From;
        if (Property is not null) d["property"] = Property;
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
        if (VarSets is { Count: > 0 }) d["var_sets"] = VarSets;
        if (VarMath is { Count: > 0 }) d["var_math"] = VarMath;
        if (VarRandom is { Count: > 0 })
            d["var_random"] = VarRandom.ToDictionary(
                kv => kv.Key,
                kv => (object?)kv.Value.ToDict());
        if (VarPush is { Count: > 0 }) d["var_push"] = VarPush;
        if (VarPop is not null) d["var_pop"] = VarPop;
        if (VarSort is { Count: > 0 })
            d["var_sort"] = VarSort.ToDictionary(
                kv => kv.Key,
                kv => (object?)kv.Value.ToDict());
        if (VarRemove is { Count: > 0 }) d["var_remove"] = VarRemove;
        return d;
    }
}

// ── Let — passage-scoped variable ─────────────────────────────────────────

public class LetNode : MwsNode
{
    public override string Type => "let";
    public string Var { get; set; } = "";
    public VarRandom? Random { get; set; }
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
        var d = new Dictionary<string, object?> { ["type"] = Type, ["var"] = Var };
        if (Random is not null) d["random"] = Random.ToDict();
        if (Array is not null) d["array"] = Array;
        if (Compute is not null) d["compute"] = Compute;
        if (Pop is not null) d["pop"] = Pop;
        if (Dequeue is not null) d["dequeue"] = Dequeue;
        if (Sort is not null) d["sort"] = Sort.ToDict();
        return d;
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
        ["nodes"] = Nodes.Select(n => n.ToDict()).ToList(),
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
            ["input_type"] = InputType,
            ["store_in"] = StoreIn,
        };
        if (ResumePassage is not null) d["resume_passage"] = ResumePassage;
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
        if (Name is not null) d["name"] = Name;
        if (Icon is not null) d["icon"] = Icon;
        return d;
    }
}

public class SetupNotificationNode : MwsNode
{
    public override string Type => "setup_notification";
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? NextPassage { get; set; }
    public override Dictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?> { ["type"] = Type };
        if (Title is not null) d["title"] = Title;
        if (Text is not null) d["text"] = Text;
        if (NextPassage is not null) d["next_passage"] = NextPassage;
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
        if (DisplayLabel is not null) d["display_label"] = DisplayLabel;
        if (DiagnosticLabel is not null) d["diagnostic_label"] = DiagnosticLabel;
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
        if (Note is not null) d["note"] = Note;
        return d;
    }
}
