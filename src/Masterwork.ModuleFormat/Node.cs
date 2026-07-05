namespace Masterwork.ModuleFormat;

/// <summary>
/// Base type for every MWS v0.3 passage node, deserialized from a YAML mapping's <c>type:</c>
/// field. See <see cref="Masterwork.ModuleFormat.PassageYamlParser"/> for the dispatch logic.
/// </summary>
public abstract record Node
{
    /// <summary>The YAML <c>type:</c> discriminator for this node (e.g. <c>"text"</c>).</summary>
    public abstract string Type { get; }
}
