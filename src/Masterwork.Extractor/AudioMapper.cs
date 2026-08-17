using System.Text.Json;

namespace Masterwork.Extractor;

// Loads an external passage-name -> gendered VO asset mapping (--audio-map), consulted purely by
// passage name at serialization time (V2Serializer.ToDict) to synthesize an audio_track node for a
// mapped passage — unlike SpriteMapper/ProgressMapper, this has no corresponding Cradle source
// construct to consult while walking the AST (a synthesized node isn't parsed from anything), so it
// isn't threaded through PassageBodyVisitor at all, just SerializationContext. See the Phase 4
// pilot's own hand-authored Hospitalintro passages-override entry, which this mechanism replaces
// going forward.
public class AudioMapper
{
    public sealed record Entry(string MaleSlug, string FemaleSlug, string Title);

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

                var male = value.TryGetProperty("male", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                var female = value.TryGetProperty("female", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
                var title = value.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

                if (male is null || female is null || title is null)
                {
                    Console.Error.WriteLine($"Warning: audio map entry '{prop.Name}' is missing 'male'/'female'/'title'; skipping it");
                    continue;
                }

                mapper._entries[prop.Name] = new Entry(male, female, title);
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
