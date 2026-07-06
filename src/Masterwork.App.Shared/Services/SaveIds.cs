namespace Masterwork.App.Shared.Services;

/// <summary>Save-id conventions shared by every <see cref="ISaveStore"/> implementation.</summary>
public static class SaveIds
{
    /// <summary>The single, continuously-updated autosave id for a given module — deterministic so "does this module have an in-progress session" is just a lookup.</summary>
    public static string Autosave(string moduleId) => $"autosave:{moduleId}";

    /// <summary>Generates a fresh id for a new named save.</summary>
    public static string NewNamedSave() => Guid.NewGuid().ToString("n");
}
