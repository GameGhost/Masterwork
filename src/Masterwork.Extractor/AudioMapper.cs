using System.Text.Json;

namespace Masterwork.Extractor;

// Loads an external passage-name -> narration slug mapping (--audio-map), consulted purely by
// passage name at serialization time (V2Serializer.ToDict) to synthesize an audio_track node for a
// mapped passage. Unlike SpriteMapper/ProgressMapper, this has no corresponding Cradle source
// construct to consult while walking the AST, so it isn't threaded through PassageBodyVisitor at
// all, just SerializationContext. The slug is plain (audio://vo/<slug>): which voice track plays it
// is the player's choice at play time, never an expression in the passage.
public class AudioMapper
{
    public sealed record Entry(string Slug, string Title);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public static AudioMapper Empty() => new();

    public static AudioMapper FromJsonFile(string jsonPath)
    {
        var mapper = new AudioMapper();
        try
        {
            var json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                Console.Error.WriteLine($"Warning: audio map JSON '{jsonPath}' is not an object; ignoring it");
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

                var slug = value.TryGetProperty("slug", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                var title = value.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

                if (slug is null || title is null)
                {
                    var legacy = value.TryGetProperty("male", out _) || value.TryGetProperty("female", out _)
                        ? " (gendered 'male'/'female' entries are no longer read: give one plain 'slug' and put each reading in its own voice track)"
                        : "";
                    Console.Error.WriteLine($"Warning: audio map entry '{prop.Name}' is missing 'slug'/'title'; skipping it{legacy}");
                    continue;
                }

                mapper._entries[prop.Name] = new Entry(slug, title);
            }

            Console.WriteLine($"Audio map loaded: {mapper._entries.Count} entries from '{Path.GetFileName(jsonPath)}'");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: could not load audio map from '{jsonPath}': {ex.Message}");
        }

        return mapper;
    }

    // Returns true when `passageName` has a mapped entry — the normal case is false (most passages
    // have no VO narration), so callers must never warn on a miss.
    public bool TryGetEntry(string passageName, out Entry entry)
    {
        if (_entries.TryGetValue(passageName, out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }
}
