namespace Masterwork.ModuleFormat;

// ── Passage ────────────────────────────────────────────────────────────────

public sealed record Location
{
    public string? Name { get; init; }
    public string? Icon { get; init; }
}

public sealed record MwsPassageDoc
{
    public required string PassageId { get; init; }
    public string? Title { get; init; }
    public required string Layout { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public bool Debug { get; init; }
    public Location? Location { get; init; }
    public string? CheckProgress { get; init; }
    public IReadOnlyList<Node> Nodes { get; init; } = [];
}

// ── Base node ──────────────────────────────────────────────────────────────

public abstract record Node
{
    public abstract string Type { get; }
}

// ── Text ───────────────────────────────────────────────────────────────────

public sealed record TextNode : Node
{
    public override string Type => "text";
    public required string Value { get; init; }
    public Alignment? Align { get; init; }
    public IReadOnlyList<string> Lets { get; init; } = [];
}

public sealed record ImageNode : Node
{
    public override string Type => "image";
    public required string Asset { get; init; }
    public string? Size { get; init; }
    public Alignment? Align { get; init; }
}

// ── Structural ─────────────────────────────────────────────────────────────

public sealed record BreakNode : Node
{
    public override string Type => "break";
}

public sealed record ParagraphBreakNode : Node
{
    public override string Type => "paragraph_break";
}

public sealed record SectionNode : Node
{
    public override string Type => "section";
    public string? Title { get; init; }
    public string? Style { get; init; }
    public bool Collapsed { get; init; }
    public IReadOnlyList<Node> Content { get; init; } = [];
}

// ── Variables ──────────────────────────────────────────────────────────────

public sealed record LetNode : Node
{
    public override string Type => "let";
    public required string Var { get; init; }
    public required string Expr { get; init; }
}

public sealed record AssignNode : Node
{
    public override string Type => "assign";
    public required string Var { get; init; }
    public required string Expr { get; init; }
}

// ── Navigation ─────────────────────────────────────────────────────────────

public sealed record NavigationNode : Node
{
    public override string Type => "navigation";
    public required string Label { get; init; }
    public string? Style { get; init; }
    // Passage_id, or "${expr}" for a dynamic target.
    public required string Target { get; init; }
    public required bool StateAffecting { get; init; }
    public string? TimelineLabel { get; init; }
    public IReadOnlyList<Node> OnClick { get; init; } = [];
}

public sealed record PopupNode : Node
{
    public override string Type => "popup";
    public string? Label { get; init; }
    public string? Style { get; init; }
    public string? Layout { get; init; }
    public IReadOnlyList<Node> Content { get; init; } = [];
    // Passage_id, or "${expr}" for a dynamic target.
    public string? OnClose { get; init; }
    public string? Button { get; init; }
    public bool StateAffecting { get; init; }
}

public sealed record GotoNode : Node
{
    public override string Type => "goto";
    // Passage_id, or "${expr}" for a dynamic target.
    public required string Target { get; init; }
}

public sealed record IncludePassageNode : Node
{
    public override string Type => "include_passage";
    // Passage_id, or "${expr}" for a dynamic target.
    public required string Target { get; init; }
}

// ── Input & interaction ────────────────────────────────────────────────────

public sealed record InputNode : Node
{
    public override string Type => "input";
    public required string Label { get; init; }
    public string? Style { get; init; }
    public required string Text { get; init; }
    public required InputValueType InputType { get; init; }
    public required string Var { get; init; }
    // Passage_id, or "${expr}" for a dynamic target.
    public required string OnSubmit { get; init; }
}

public sealed record PromptNode : Node
{
    public override string Type => "prompt";
    public required string Text { get; init; }
    public required InputValueType InputType { get; init; }
    public required string Var { get; init; }
}

// ── Logic ──────────────────────────────────────────────────────────────────

public sealed record ConditionalBranch
{
    public required string If { get; init; }
    public required IReadOnlyList<Node> Then { get; init; }
}

public sealed record ConditionalNode : Node
{
    public override string Type => "conditional";
    public required IReadOnlyList<ConditionalBranch> Conditions { get; init; }
    public IReadOnlyList<Node>? Else { get; init; }
}

public sealed record SwitchCase
{
    // int, string, restext://Key (pre-resolution), list of those (any-of), or pattern string (e.g. ">3").
    public required object Match { get; init; }
    public required IReadOnlyList<Node> Nodes { get; init; }
}

public sealed record SwitchNode : Node
{
    public override string Type => "switch";
    public required string On { get; init; }
    public required IReadOnlyList<SwitchCase> Cases { get; init; }
    public IReadOnlyList<Node>? Default { get; init; }
}

public sealed record ForEachNode : Node
{
    public override string Type => "foreach";
    public required string Var { get; init; }
    public required string In { get; init; }
    public required IReadOnlyList<Node> Do { get; init; }
}

// ── Milestone / integration ───────────────────────────────────────────────

public sealed record CheckpointNode : Node
{
    public override string Type => "checkpoint";
    public required string Id { get; init; }
    public string? Display { get; init; }
    public string? Diagnostic { get; init; }
}

public sealed record RecordNode : Node
{
    public override string Type => "record";
    public required string Id { get; init; }
}

// ── Fallback ───────────────────────────────────────────────────────────────

public sealed record UnknownNode : Node
{
    private readonly string _type;
    public UnknownNode(string type) { _type = type; }
    public override string Type => _type;
}
