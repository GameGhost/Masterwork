using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IFormattedTextExpander"/>
public sealed partial class FormattedTextExpander(IAssetResolver assetResolver) : IFormattedTextExpander
{
    [GeneratedRegex(@"\{icon:([A-Za-z0-9_]+)\}")]
    private static partial Regex IconRefPattern();

    // Matches **bold** or _italic_ spans. Content is non-greedy so "**a** and **b**" produces two
    // spans rather than one spanning "a** and **b". Only one of the two alternatives' groups will
    // have participated in any given match.
    [GeneratedRegex(@"\*\*(?<bold>[\s\S]*?)\*\*|_(?<italic>[\s\S]*?)_")]
    private static partial Regex EmphasisPattern();

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
            AppendEmphasized(sb, value[lastIndex..match.Index]);

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

        AppendEmphasized(sb, value[lastIndex..]);
        return new MarkupString(sb.ToString());
    }

    // Converts **bold**/_italic_ spans within a single (icon-ref-free) text segment into
    // <strong>/<em>, HTML-encoding everything else. Tolerant of malformed input: whitespace sitting
    // just inside the delimiters (e.g. "**Test markdown **", which the extractor and hand-authored
    // restext are also being cleaned up to avoid — see MwsExprHelper.WrapEmphasis) is trimmed from
    // the tagged content and re-emitted outside the tag, rather than left inside <strong>/<em> or
    // rejected outright. A span with nothing but whitespace between its delimiters is left as plain
    // (encoded) text — there's nothing to emphasize.
    private static void AppendEmphasized(StringBuilder sb, string segment)
    {
        var lastIndex = 0;
        foreach (Match match in EmphasisPattern().Matches(segment))
        {
            sb.Append(WebUtility.HtmlEncode(segment[lastIndex..match.Index]));

            var isBold = match.Groups["bold"].Success;
            var inner = isBold ? match.Groups["bold"].Value : match.Groups["italic"].Value;
            var trimmed = inner.Trim();
            if (trimmed.Length == 0)
            {
                sb.Append(WebUtility.HtmlEncode(match.Value));
            }
            else
            {
                var lead = inner[..(inner.Length - inner.TrimStart().Length)];
                var trail = inner[inner.TrimEnd().Length..];
                var tag = isBold ? "strong" : "em";
                sb.Append(WebUtility.HtmlEncode(lead))
                  .Append('<').Append(tag).Append('>')
                  .Append(WebUtility.HtmlEncode(trimmed))
                  .Append("</").Append(tag).Append('>')
                  .Append(WebUtility.HtmlEncode(trail));
            }

            lastIndex = match.Index + match.Length;
        }

        sb.Append(WebUtility.HtmlEncode(segment[lastIndex..]));
    }
}
