using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IIconTextExpander"/>
public sealed partial class IconTextExpander(IAssetResolver assetResolver) : IIconTextExpander
{
    [GeneratedRegex(@"\{icon:([A-Za-z0-9_]+)\}")]
    private static partial Regex IconRefPattern();

    /// <inheritdoc/>
    public async Task<MarkupString> ExpandAsync(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new MarkupString(string.Empty);
        }

        var sb = new StringBuilder();
        var lastIndex = 0;
        foreach (Match match in IconRefPattern().Matches(value))
        {
            sb.Append(WebUtility.HtmlEncode(value[lastIndex..match.Index]));

            var slug = match.Groups[1].Value;
            var url = await assetResolver.ResolveAsync($"icon://{slug}");
            if (url is not null)
            {
                sb.Append("<img src=\"").Append(WebUtility.HtmlEncode(url))
                  .Append("\" alt=\"").Append(WebUtility.HtmlEncode(slug))
                  .Append("\" class=\"mws-inline-icon\" />");
            }
            else
            {
                sb.Append(WebUtility.HtmlEncode(match.Value));
            }

            lastIndex = match.Index + match.Length;
        }

        sb.Append(WebUtility.HtmlEncode(value[lastIndex..]));
        return new MarkupString(sb.ToString());
    }
}
