using System.Text.RegularExpressions;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IModuleStyleInjector"/>
public sealed partial class JsModuleStyleInjector(IJSRuntime js, IAssetResolver assetResolver) : IModuleStyleInjector
{
    private IJSObjectReference? _module;

    // Matches url(icon://slug), url(image://slug), url(font://slug), with or without quotes; slug
    // may contain '/' for a subfolder (e.g. image://setup/StorybookToken).
    [GeneratedRegex(@"url\(\s*(['""]?)(icon|image|font)://([^'"")\s]+)\1\s*\)")]
    private static partial Regex AssetUrlPattern();

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/moduleStyle.js");

    /// <inheritdoc/>
    public async Task ApplyAsync(string? css)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("setModuleStyle", await ResolveAssetUrlsAsync(css));
    }

    // Module CSS references its own assets directly (url(icon://slug) / url(image://slug)) rather
    // than a fixed node-level "background" property — CSS alone can express background-size,
    // repeat, and multi-layered images, which a fixed property couldn't. Since the CSS is injected
    // as raw text (not loaded as an external stylesheet with its own base URL), those references
    // need to be resolved to real URLs here, the same way RenderedImageView resolves icon://
    // /image:// refs for <img> tags. Public (not just called from ApplyAsync) so it's directly
    // testable without needing a real/fake IJSRuntime — it never touches js.
    public async Task<string?> ResolveAssetUrlsAsync(string? css)
    {
        if (string.IsNullOrEmpty(css))
        {
            return css;
        }

        var matches = AssetUrlPattern().Matches(css);
        if (matches.Count == 0)
        {
            return css;
        }

        var sb = new System.Text.StringBuilder();
        var lastIndex = 0;
        foreach (Match match in matches)
        {
            sb.Append(css.AsSpan(lastIndex, match.Index - lastIndex));

            var scheme = match.Groups[2].Value;
            var slug = match.Groups[3].Value;
            var resolved = await assetResolver.ResolveAsync($"{scheme}://{slug}");
            sb.Append(resolved is not null ? $"url(\"{resolved}\")" : match.Value);

            lastIndex = match.Index + match.Length;
        }

        sb.Append(css.AsSpan(lastIndex));
        return sb.ToString();
    }
}
