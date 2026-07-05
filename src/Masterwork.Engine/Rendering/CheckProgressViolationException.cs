namespace Masterwork.Engine.Rendering;

/// <summary>Thrown by <see cref="PassageRenderer.Render"/> when a passage's <c>check_progress</c> prerequisite hasn't been visited yet.</summary>
public sealed class CheckProgressViolationException(string passageId, string requiredPassageId)
    : Exception($"Passage '{passageId}' requires '{requiredPassageId}' to have been visited first.")
{
    /// <summary>The passage that couldn't be rendered.</summary>
    public string PassageId { get; } = passageId;

    /// <summary>The passage_id that must have been visited first.</summary>
    public string RequiredPassageId { get; } = requiredPassageId;
}
