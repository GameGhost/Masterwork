namespace Masterwork.ModuleFormat;

/// <summary>
/// A single deserialized <c>.mws.yaml</c> passage: the header fields plus its node tree, after
/// restext resolution has run (see <see cref="Masterwork.ModuleFormat.RestextResolver"/>).
/// </summary>
public sealed record MwsPassageDoc
{
    /// <summary>Canonical passage identifier — the key used to look this passage up by target string.</summary>
    public required string PassageId { get; init; }

    /// <summary>Display title; defaults to <see cref="PassageId"/> when absent.</summary>
    public string? Title { get; init; }

    /// <summary>One of <c>hub</c>, <c>event</c>, <c>narration</c>, <c>private</c>, <c>modal</c> — an open, module-extensible vocabulary.</summary>
    public required string Layout { get; init; }

    /// <summary>Source tags — drives layout inference and carries authoring markers like <c>Begins-Here</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary><see langword="true"/> for developer-only passages excluded from player builds.</summary>
    public bool Debug { get; init; }

    /// <summary>Location header shown in the app's UI chrome, if present.</summary>
    public Location? Location { get; init; }

    /// <summary>The <c>passage_id</c> that must have been visited before this passage may be rendered.</summary>
    public string? CheckProgress { get; init; }

    /// <summary>
    /// <see langword="true"/> for a passage that represents a recognized game ending (hoisted from
    /// the Cradle source's <c>EnableDisableContinueBtn(true)</c> call the same way a
    /// <c>SetLocationIndicatorIcon</c> call is hoisted into <see cref="Location"/> — see
    /// masterwork-plan-rev13.md Q18). The app uses this to record a Town Record/Memory and delete
    /// that module's autosave once a playthrough reaches it.
    /// </summary>
    public bool Ending { get; init; }

    /// <summary>The passage's node tree, in document order.</summary>
    public IReadOnlyList<Node> Nodes { get; init; } = [];
}
