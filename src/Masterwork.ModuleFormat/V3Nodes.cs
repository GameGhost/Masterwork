namespace Masterwork.ModuleFormat;

// ── Passage ────────────────────────────────────────────────────────────────

public sealed record V3Location
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
    public V3Location? Location { get; init; }
    public string? CheckProgress { get; init; }
    public IReadOnlyList<V3Node> Nodes { get; init; } = [];
}

// ── Base node ──────────────────────────────────────────────────────────────

public abstract record V3Node
{
    public abstract string Type { get; }
}

// ── Text ───────────────────────────────────────────────────────────────────

public sealed record V3TextNode : V3Node
{
    public override string Type => "text";
    public required string Value { get; init; }
    public string? Align { get; init; }
    public IReadOnlyList<string> Lets { get; init; } = [];
}

public sealed record V3ImageNode : V3Node
{
    public override string Type => "image";
    public required string Asset { get; init; }
    public string? Size { get; init; }
    public string? Align { get; init; }
}

// ── Structural ─────────────────────────────────────────────────────────────

public sealed record V3BreakNode : V3Node
{
    public override string Type => "break";
}

public sealed record V3ParagraphBreakNode : V3Node
{
    public override string Type => "paragraph_break";
}

public sealed record V3SectionNode : V3Node
{
    public override string Type => "section";
    public string? Title { get; init; }
    public string? Style { get; init; }
    public bool Collapsed { get; init; }
    public IReadOnlyList<V3Node> Content { get; init; } = [];
}

// ── Variables ──────────────────────────────────────────────────────────────

public sealed record V3LetNode : V3Node
{
    public override string Type => "let";
    public required string Var { get; init; }
    public required string Expr { get; init; }
}

public sealed record V3AssignNode : V3Node
{
    public override string Type => "assign";
    public required string Var { get; init; }
    public required string Expr { get; init; }
}

// ── Navigation ─────────────────────────────────────────────────────────────

public sealed record V3NavigationNode : V3Node
{
    public override string Type => "navigation";
    public required string Label { get; init; }
    public string? Style { get; init; }
    // Passage_id, or "${expr}" for a dynamic target.
    public required string Target { get; init; }
    public required bool StateAffecting { get; init; }
    public string? TimelineLabel { get; init; }
    public IReadOnlyList<V3Node> OnClick { get; init; } = [];
}

public sealed record V3PopupNode : V3Node
{
    public override string Type => "popup";
    public string? Label { get; init; }
    public string? Style { get; init; }
    public string? Layout { get; init; }
    public IReadOnlyList<V3Node> Content { get; init; } = [];
    // Passage_id, or "${expr}" for a dynamic target.
    public string? OnClose { get; init; }
    public string? Button { get; init; }
    public bool StateAffecting { get; init; }
}

public sealed record V3GotoNode : V3Node
{
    public override string Type => "goto";
    // Passage_id, or "${expr}" for a dynamic target.
    public required string Target { get; init; }
}

public sealed record V3IncludePassageNode : V3Node
{
    public override string Type => "include_passage";
    // Passage_id, or "${expr}" for a dynamic target.
    public required string Target { get; init; }
}

// ── Input & interaction ────────────────────────────────────────────────────

public sealed record V3InputNode : V3Node
{
    public override string Type => "input";
    public required string Label { get; init; }
    public string? Style { get; init; }
    public required string Text { get; init; }
    // "string" | "number"
    public required string InputType { get; init; }
    public required string Var { get; init; }
    // Passage_id, or "${expr}" for a dynamic target.
    public required string OnSubmit { get; init; }
}

public sealed record V3PromptNode : V3Node
{
    public override string Type => "prompt";
    public required string Text { get; init; }
    // "string" | "number"
    public required string InputType { get; init; }
    public required string Var { get; init; }
}

// ── Logic ──────────────────────────────────────────────────────────────────

public sealed record V3ConditionalBranch
{
    public required string If { get; init; }
    public required IReadOnlyList<V3Node> Then { get; init; }
}

public sealed record V3ConditionalNode : V3Node
{
    public override string Type => "conditional";
    public required IReadOnlyList<V3ConditionalBranch> Conditions { get; init; }
    public IReadOnlyList<V3Node>? Else { get; init; }
}

public sealed record V3SwitchCase
{
    // int, string, restext://Key (pre-resolution), list of those (any-of), or pattern string (e.g. ">3").
    public required object Match { get; init; }
    public required IReadOnlyList<V3Node> Nodes { get; init; }
}

public sealed record V3SwitchNode : V3Node
{
    public override string Type => "switch";
    public required string On { get; init; }
    public required IReadOnlyList<V3SwitchCase> Cases { get; init; }
    public IReadOnlyList<V3Node>? Default { get; init; }
}

public sealed record V3ForEachNode : V3Node
{
    public override string Type => "foreach";
    public required string Var { get; init; }
    public required string In { get; init; }
    public required IReadOnlyList<V3Node> Do { get; init; }
}

// ── Milestone / integration ───────────────────────────────────────────────

public sealed record V3CheckpointNode : V3Node
{
    public override string Type => "checkpoint";
    public required string Id { get; init; }
    public string? Display { get; init; }
    public string? Diagnostic { get; init; }
}

public sealed record V3RecordNode : V3Node
{
    public override string Type => "record";
    public required string Id { get; init; }
}

// ── Fallback ───────────────────────────────────────────────────────────────

public sealed record V3UnknownNode : V3Node
{
    private readonly string _type;
    public V3UnknownNode(string type) { _type = type; }
    public override string Type => _type;
}
