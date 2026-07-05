using System.Security.Cryptography;
using System.Text;

namespace Masterwork.Engine.Session;

/// <summary>
/// Model B (seeded lazy) PRNG: each seed_key maps to a fixed, deterministic value derived from
/// <c>(masterSeed, seedKey, occurrence#)</c> rather than a mutated shared <see cref="Random"/>
/// instance. This makes timeline rewind trivial — restoring position is just restoring an integer
/// occurrence count per key, with no need to replay internal RNG state draw-for-draw.
/// </summary>
public sealed class SessionPrng(long masterSeed)
{
    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);

    /// <summary>Returns a deterministic random integer in <c>[min, max]</c> for the given seed key.</summary>
    public long RandBetween(long min, long max, string seedKey)
    {
        var rng = CreateRandom(seedKey);
        return min + rng.NextInt64(max - min + 1);
    }

    /// <summary>Returns a deterministic Fisher-Yates permutation of <paramref name="items"/> for the given seed key.</summary>
    public IReadOnlyList<StoryValue> Shuffled(IReadOnlyList<StoryValue> items, string seedKey)
    {
        var rng = CreateRandom(seedKey);
        var arr = items.ToList();
        for (int i = arr.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return arr;
    }

    private Random CreateRandom(string seedKey)
    {
        var occurrence = _occurrences.GetValueOrDefault(seedKey);
        _occurrences[seedKey] = occurrence + 1;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{masterSeed}|{seedKey}|{occurrence}"));
        return new Random(BitConverter.ToInt32(bytes, 0));
    }

    /// <summary>Captures per-key occurrence counters, for timeline snapshotting.</summary>
    public IReadOnlyDictionary<string, int> SnapshotOccurrences() => new Dictionary<string, int>(_occurrences, StringComparer.Ordinal);

    /// <summary>Restores per-key occurrence counters from a prior <see cref="SnapshotOccurrences"/> capture.</summary>
    public void RestoreOccurrences(IReadOnlyDictionary<string, int> occurrences)
    {
        _occurrences.Clear();
        foreach (var (k, v) in occurrences)
        {
            _occurrences[k] = v;
        }
    }
}
