using Masterwork.Engine;
using Masterwork.Engine.Session;

namespace Masterwork.Tests;

public class PrngTests
{
    [Fact]
    public void SameSeedAndKey_SameValue()
    {
        var a = new SessionPrng(42).RandBetween(1, 1000, "some_key");
        var b = new SessionPrng(42).RandBetween(1, 1000, "some_key");
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentKeys_DifferentValues()
    {
        var prng = new SessionPrng(42);
        var a = prng.RandBetween(1, 1_000_000, "key_a");
        var b = prng.RandBetween(1, 1_000_000, "key_b");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RandBetween_AlwaysInRange()
    {
        var prng = new SessionPrng(7);
        for (var i = 0; i < 1000; i++)
        {
            var v = prng.RandBetween(6, 10, $"key_{i}");
            Assert.InRange(v, 6, 10);
        }
    }

    [Fact]
    public void Shuffled_ProducesPermutation()
    {
        var prng = new SessionPrng(1);
        var input = new List<StoryValue> { StoryValue.Of("a"), StoryValue.Of("b"), StoryValue.Of("c"), StoryValue.Of("d") };
        var result = prng.Shuffled(input, "shuffle_key");

        Assert.Equal(input.Count, result.Count);
        Assert.Equal(
            input.Select(v => v.AsString()).OrderBy(s => s),
            result.Select(v => v.AsString()).OrderBy(s => s));
    }

    [Fact]
    public void Shuffled_SameSeedSameOrder()
    {
        var input = new List<StoryValue> { StoryValue.Of("a"), StoryValue.Of("b"), StoryValue.Of("c"), StoryValue.Of("d"), StoryValue.Of("e") };

        var a = new SessionPrng(99).Shuffled(input, "k");
        var b = new SessionPrng(99).Shuffled(input, "k");

        Assert.Equal(a.Select(v => v.AsString()), b.Select(v => v.AsString()));
    }
}
