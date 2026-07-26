using System.Security.Cryptography;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Computes a <c>.mwm</c> package's SHA256 over its raw (still-zipped) bytes. Primary path (when
/// <paramref name="js"/> is given, i.e. on both Blazor heads' normal call sites) is the browser's
/// native <c>crypto.subtle.digest</c> (<c>wwwroot/cryptoHash.js</c>) — measured dramatically faster
/// than driving .NET's own <see cref="IncrementalHash"/> chunk-by-chunk under Blazor WASM's
/// interpreter (a 37MB file: well under a second via <c>crypto.subtle</c> vs. 45+ seconds of
/// managed hashing, confirmed by a single 'setTimeout' handler clocking ~49 seconds in the browser's
/// own performance-violation log — the chunking/yielding was never the problem, the managed SHA256
/// implementation itself was). Falls back to the managed, chunked implementation if the JS call
/// throws (e.g. <c>crypto.subtle</c> unavailable in an insecure, non-HTTPS/non-localhost context) or
/// if no <see cref="IJSRuntime"/> is supplied at all (keeps this usable from contexts/tests that
/// don't have one).
/// </summary>
public static class ModuleHasher
{
    private const int ChunkSizeBytes = 1024 * 1024;

    /// <summary>
    /// Returns the lowercase hex SHA256 of <paramref name="bytes"/>. <paramref name="progress"/>, if
    /// given, is reported (bytes hashed so far, total bytes) — for the <paramref name="js"/> path
    /// this is just a (0, total) / (total, total) pair bracketing the single native call, since
    /// crypto.subtle.digest has no intermediate progress of its own to report.
    /// </summary>
    public static async Task<string> ComputeHashAsync(byte[] bytes, IProgress<(int Done, int Total)>? progress = null, IJSRuntime? js = null)
    {
        if (js is not null)
        {
            try
            {
                progress?.Report((0, bytes.Length));
                var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/cryptoHash.js");
                var hex = await module.InvokeAsync<string>("sha256Hex", bytes);
                progress?.Report((bytes.Length, bytes.Length));
                return hex;
            }
            catch (JSException)
            {
                // Fall through to the managed path below.
            }
        }

        return await ComputeHashManagedAsync(bytes, progress);
    }

    private static async Task<string> ComputeHashManagedAsync(byte[] bytes, IProgress<(int Done, int Total)>? progress)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var total = bytes.Length;
        progress?.Report((0, total));

        var offset = 0;
        while (offset < bytes.Length)
        {
            var length = Math.Min(ChunkSizeBytes, bytes.Length - offset);
            incremental.AppendData(bytes, offset, length);
            offset += length;
            progress?.Report((offset, total));
            await Task.Delay(1);
        }

        return Convert.ToHexStringLower(incremental.GetHashAndReset());
    }
}
