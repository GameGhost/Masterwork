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

    // Matches the icon-ref placeholders ExpandAsync masks real refs to before emphasis-matching
    // (see below) — \0 can never appear in restext content, so this can't collide with anything.
    [GeneratedRegex("\0(\\d+)\0")]
    private static partial Regex IconPlaceholderPattern();

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

        // Emphasis spans must be matched across the *whole* string, not per icon-split segment —
        // otherwise a **/_ pair that wraps an {icon:...} ref (opening delimiter before the icon,
        // closing delimiter after, e.g. "_(...{icon:creepy_icon}...)_") never sees its own closing
        // half, since each half would land in a different icon-split segment and neither matches
        // alone. But icon slugs commonly contain underscores themselves (creepy_icon,
        // s1_hearttoken), which the italic regex would misread as a delimiter mid-slug if run
        // directly on the raw string. So: mask real icon refs out to a delimiter-free placeholder
        // first, run emphasis-matching on the masked string, then expand placeholders back to
        // <img> tags per resulting segment (plain or inside a <strong>/<em>).
        var icons = new List<string>();
        var masked = IconRefPattern().Replace(value, m =>
        {
            icons.Add(m.Groups[1].Value);
            return $"\0{icons.Count - 1}\0";
        });

        var sb = new StringBuilder();
        var lastIndex = 0;
        foreach (Match match in EmphasisPattern().Matches(masked))
        {
            await AppendSegmentAsync(sb, masked[lastIndex..match.Index], icons);

            var isBold = match.Groups["bold"].Success;
            var inner = isBold ? match.Groups["bold"].Value : match.Groups["italic"].Value;
            var trimmed = inner.Trim();
            if (trimmed.Length == 0)
            {
                // A span with nothing but whitespace between its delimiters is left as plain
                // (encoded) text — there's nothing to emphasize.
                await AppendSegmentAsync(sb, match.Value, icons);
            }
            else
            {
                // Tolerant of malformed input: whitespace sitting just inside the delimiters
                // (e.g. "**Test markdown **", which the extractor and hand-authored restext are
                // also being cleaned up to avoid — see MwsExprHelper.WrapEmphasis) is trimmed from
                // the tagged content and re-emitted outside the tag, rather than left inside
                // <strong>/<em> or rejected outright.
                var lead = inner[..(inner.Length - inner.TrimStart().Length)];
                var trail = inner[inner.TrimEnd().Length..];
                var tag = isBold ? "strong" : "em";
                await AppendSegmentAsync(sb, lead, icons);
                sb.Append('<').Append(tag).Append('>');
                await AppendSegmentAsync(sb, trimmed, icons);
                sb.Append("</").Append(tag).Append('>');
                await AppendSegmentAsync(sb, trail, icons);
            }

            lastIndex = match.Index + match.Length;
        }

        await AppendSegmentAsync(sb, masked[lastIndex..], icons);
        return new MarkupString(sb.ToString());
    }

    // Expands icon placeholders within `segment` back to <img> tags, HTML-encoding everything else.
    private async Task AppendSegmentAsync(StringBuilder sb, string segment, List<string> icons)
    {
        var lastIndex = 0;
        foreach (Match match in IconPlaceholderPattern().Matches(segment))
        {
            sb.Append(WebUtility.HtmlEncode(segment[lastIndex..match.Index]));

            var slug = icons[int.Parse(match.Groups[1].Value)];
            var url = await assetResolver.ResolveAsync($"icon://{slug}");
            sb.Append(url is not null
                ? $"<img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"{WebUtility.HtmlEncode(slug)}\" class=\"mws-inline-icon\" />"
                : WebUtility.HtmlEncode($"{{icon:{slug}}}"));

            lastIndex = match.Index + match.Length;
        }

        sb.Append(WebUtility.HtmlEncode(segment[lastIndex..]));
    }
}
