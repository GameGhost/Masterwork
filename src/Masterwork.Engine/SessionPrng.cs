using System.Security.Cryptography;
using System.Text;

namespace Masterwork.Engine;

// Model B (seeded lazy) PRNG: each seed_key maps to a fixed, deterministic value derived from
// (masterSeed, seedKey, occurrence#) rather than a mutated shared Random instance. This makes
// timeline rewind trivial — restoring position is just restoring an integer occurrence count per
// key, with no need to replay internal RNG state draw-for-draw.
public sealed class SessionPrng(long masterSeed)
{
    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);

    public long RandBetween(long min, long max, string seedKey)
    {
        var rng = CreateRandom(seedKey);
        return min + rng.NextInt64(max - min + 1);
    }

    public IReadOnlyList<ExprValue> Shuffled(IReadOnlyList<ExprValue> items, string seedKey)
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

    // Per-key occurrence counters, for timeline snapshotting.
    public IReadOnlyDictionary<string, int> SnapshotOccurrences() => new Dictionary<string, int>(_occurrences, StringComparer.Ordinal);

    public void RestoreOccurrences(IReadOnlyDictionary<string, int> occurrences)
    {
        _occurrences.Clear();
        foreach (var (k, v) in occurrences) _occurrences[k] = v;
    }
}
