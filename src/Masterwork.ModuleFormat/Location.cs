namespace Masterwork.ModuleFormat;

/// <summary>
/// The <c>location:</c> header on a passage — shown in the app's UI chrome when that passage is
/// entered (e.g. a hub location's name and icon).
/// </summary>
public sealed record Location
{
    /// <summary>Display name, resolved from any <c>restext://</c> reference.</summary>
    public string? Name { get; init; }

    /// <summary>Asset URI for the location's icon (not restext-resolved — it's a URI, not display text).</summary>
    public string? Icon { get; init; }
}
