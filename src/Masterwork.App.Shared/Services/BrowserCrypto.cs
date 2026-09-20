using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>Raised when the browser's own crypto can't be reached and there is no usable alternative.</summary>
public sealed class BrowserCryptoUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// SHA-256 via the browser's Web Crypto, with the JS module imported once and kept.
///
/// The import used to happen per call, which made every hash depend on the network still being
/// reachable: going offline mid-install turned the next hash into a failed module fetch and a
/// silent fall back to managed hashing — around a minute of frozen UI per 50MB under the WASM
/// interpreter. Holding the reference means the module is fetched once, early, and a later
/// disconnection can't take it away.
/// </summary>
public sealed class BrowserCrypto(IJSRuntime js)
{
    private const string ModulePath = "./_content/Masterwork.App.Shared/cryptoHash.js";

    // Managed SHA-256 costs roughly a second per megabyte here. Below this it's an acceptable
    // fallback; above it, failing loudly beats appearing to hang.
    private const int ManagedFallbackLimitBytes = 4 * 1024 * 1024;

    private IJSObjectReference? _module;

    /// <summary>Whether this platform should use the browser's crypto at all.</summary>
    public static bool IsBrowser => OperatingSystem.IsBrowser();

    /// <summary>
    /// Returns the lowercase hex SHA-256 of <paramref name="bytes"/>, computed natively.
    /// </summary>
    /// <exception cref="BrowserCryptoUnavailableException">
    /// The module or <c>crypto.subtle</c> couldn't be reached — the latter needs a secure context
    /// (HTTPS or localhost) — and the input is too large for the managed fallback to be reasonable.
    /// </exception>
    public async Task<string> Sha256HexAsync(byte[] bytes)
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return await _module.InvokeAsync<string>("sha256Hex", bytes);
        }
        catch (JSException ex)
        {
            _module = null;

            if (bytes.Length > ManagedFallbackLimitBytes)
            {
                throw new BrowserCryptoUnavailableException(
                    "The browser's cryptography couldn't be reached, and this content is too large to verify without it.", ex);
            }

            return ManagedSha256.ComputeHex(bytes);
        }
    }

    /// <summary>Warms the module up so the first real hash doesn't also pay for fetching it.</summary>
    public async Task PreloadAsync()
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }
        catch (JSException)
        {
            // Left for the first real call to report; a failed warm-up is not itself an error.
        }
    }
}
