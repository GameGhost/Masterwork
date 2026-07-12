using System.Text.Json;

namespace Masterwork.Extractor;

// Loads an external per-passage-name classification map (--progress-map): a layout override and/or
// a progress-tracker value, keyed by the passage name a source file's PassageTracker.instance
// calls/tags use. Two independent, module-agnostic concerns share one file because in practice
// (Cost of Disease) they cover the same passage names — see docs/mws-format-latest.md and
// masterwork-plan notes on the progress-bar survey for the reference-app mechanism this reproduces.
public class ProgressMapper
{
    private sealed record Entry(string? Layout, int? Progress, bool HasProgressField);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    // True only when a real --progress-map file was supplied (FromJsonFile), never for the default
    // Empty() mapper — distinguishes "this module opted into progress tracking, and this specific
    // CheckProgress passage is unexpectedly missing from the map" (worth a warning) from "no
    // --progress-map was supplied at all" (every passage looks "missing," but that's expected and
    // must never warn — see PassageBodyVisitor's CheckProgress handling).
    public bool IsConfigured { get; private set; }

    public static ProgressMapper Empty() => new();

    public static ProgressMapper FromJsonFile(string jsonPath)
    {
        var mapper = new ProgressMapper { IsConfigured = true };
        try
        {
            var json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                Console.Error.WriteLine($"Warning: progress map JSON '{jsonPath}' is not an object; ignoring it");
                return mapper;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value;
                if (value.ValueKind != JsonValueKind.Object)
                {
                    // Tolerate a top-level "_comment"-style metadata string; not a passage entry.
                    continue;
                }

                string? layout = value.TryGetProperty("layout", out var l) && l.ValueKind == JsonValueKind.String
                    ? l.GetString()
                    : null;
                var hasProgressField = value.TryGetProperty("progress", out var p);
                int? progress = hasProgressField && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

                mapper._entries[prop.Name] = new Entry(layout, progress, hasProgressField);
            }

            Console.WriteLine($"Progress map loaded: {mapper._entries.Count} entries from '{Path.GetFileName(jsonPath)}'");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: could not load progress map from '{jsonPath}': {ex.Message}");
        }

        return mapper;
    }

    // Layout override for `passageName`, or null when the map has no entry (or no "layout" field on
    // its entry) — the normal case for the vast majority of passages, so this never warns.
    public string? TryGetLayoutOverride(string passageName) =>
        _entries.TryGetValue(passageName, out var entry) ? entry.Layout : null;

    // Returns true when `passageName` has a map entry at all (used only to detect drift at
    // CheckProgress call sites, where every real occurrence should be accounted for). `progress` is
    // the value to assign — itself nullable for an entry that deliberately carries no progress value.
    public bool TryGetProgressValue(string passageName, out int? progress)
    {
        if (_entries.TryGetValue(passageName, out var entry) && entry.HasProgressField)
        {
            progress = entry.Progress;
            return true;
        }

        progress = null;
        return false;
    }
}
