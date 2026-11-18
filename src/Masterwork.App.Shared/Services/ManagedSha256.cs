using System.Security.Cryptography;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Plain managed SHA-256. Fast everywhere except the browser, where the WASM interpreter runs it at
/// roughly a second per megabyte — which is why <see cref="BrowserCrypto"/> exists and why this is
/// only a fallback for small inputs there.
/// </summary>
public static class ManagedSha256
{
    /// <summary>Returns the lowercase hex SHA-256 of <paramref name="bytes"/>.</summary>
    public static string ComputeHex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
