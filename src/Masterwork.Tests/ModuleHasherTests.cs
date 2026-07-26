using System.Security.Cryptography;
using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class ModuleHasherTests
{
    [Fact]
    public async Task ComputeHashAsync_MatchesSynchronousSha256()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("hello module world");

        var hash = await ModuleHasher.ComputeHashAsync(bytes);

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), hash);
    }

    [Fact]
    public async Task ComputeHashAsync_LargerThanOneChunk_MatchesSynchronousSha256()
    {
        // Exercises the multi-chunk path (chunk size is ~1MB) rather than the single-chunk
        // short-circuit, so the loop's offset/length bookkeeping across chunk boundaries is covered.
        var bytes = new byte[3 * 1024 * 1024 + 17];
        Random.Shared.NextBytes(bytes);

        var hash = await ModuleHasher.ComputeHashAsync(bytes);

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), hash);
    }

    [Fact]
    public async Task ComputeHashAsync_DifferentContent_DifferentHash()
    {
        var a = await ModuleHasher.ComputeHashAsync([1, 2, 3]);
        var b = await ModuleHasher.ComputeHashAsync([1, 2, 4]);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task ComputeHashAsync_EmptyBytes_MatchesSynchronousSha256()
    {
        var hash = await ModuleHasher.ComputeHashAsync([]);

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([])), hash);
    }

    [Fact]
    public async Task ComputeHashAsync_ReportsProgressFromZeroToTotalBytes()
    {
        var bytes = new byte[2 * 1024 * 1024 + 5];
        var reports = new List<(int Done, int Total)>();
        var progress = new Progress<(int Done, int Total)>(reports.Add);

        await ModuleHasher.ComputeHashAsync(bytes, progress);
        // Progress<T> posts back through the captured SynchronizationContext — xunit's default
        // context has no special draining, but Progress<T> without one invokes synchronously, so no
        // extra wait is needed here for the reports list to be fully populated.

        Assert.Equal((0, bytes.Length), reports[0]);
        Assert.Equal((bytes.Length, bytes.Length), reports[^1]);
    }
}
