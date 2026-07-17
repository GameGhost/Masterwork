namespace Masterwork.Engine;

/// <summary>
/// Thrown when a <c>link</c>/<c>popup</c>/<c>goto</c> target (or a followed <c>goto</c> chain)
/// resolves to a <c>passage_id</c> that doesn't exist in the loaded module — e.g. a typo in
/// hand-authored content, or a passage referenced before it's been written yet. Replaces the raw
/// <see cref="KeyNotFoundException"/> an unchecked dictionary lookup would otherwise throw, which
/// carries no context about which passage was missing.
/// </summary>
public sealed class PassageNotFoundException(string passageId)
    : Exception($"Passage '{passageId}' does not exist in this module.")
{
    /// <summary>The passage_id that couldn't be found.</summary>
    public string PassageId { get; } = passageId;
}
