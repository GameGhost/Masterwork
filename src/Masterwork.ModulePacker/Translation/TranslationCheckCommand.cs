using System.Text;
using Masterwork.ModuleFormat;

namespace Masterwork.ModulePacker.Translation;

/// <summary>
/// The <c>checktranslation</c> mode: runs the deterministic translation checks over a source
/// (canonical English) and target restext or resx pair, optionally fixing the target first.
/// </summary>
public static class TranslationCheckCommand
{
    public const int ExitPassed = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;

    public const string Usage =
        "  Masterwork.ModulePacker checktranslation <source-file> <target-file> [--locale <code>] [--termbase <file>]... [--json <output.json>|-] [--fix]\n" +
        "      Both files are .restext or both are .resx. The locale comes from the target file's name (fr-CA.restext,\n" +
        "      AppStrings.fr-CA.resx) unless --locale gives it. --fix rewrites the target file's Unicode normalization\n" +
        "      and typography (never its wording) before checking. --json - writes JSON to stdout and the summary to stderr.\n" +
        "      Exit 0 passed (flags allowed), 1 a hard failure, 2 bad arguments or unreadable input.";

    /// <param name="args">The arguments after the mode word.</param>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryParseArgs(args, out var options, out var error))
        {
            stderr.WriteLine(error);
            stderr.WriteLine("Usage:");
            stderr.WriteLine(Usage);
            return ExitUsage;
        }

        TranslationReport report;
        try
        {
            var termBase = TermBase.Merge(options.TermBases.Select(path => ParseTermBase(path)));
            report = Check(options.Source, options.Target, options.Kind, options.Locale, termBase, options.Fix);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            stderr.WriteLine(ex.Message);
            return ExitUsage;
        }

        var summaryOut = options.Json == "-" ? stderr : stdout;
        summaryOut.Write(report.ToSummary());
        if (options.Json == "-")
        {
            stdout.WriteLine(report.ToJson());
        }
        else if (options.Json is not null)
        {
            File.WriteAllText(options.Json, report.ToJson(), new UTF8Encoding(false));
        }

        return report.Passed ? ExitPassed : ExitFailed;
    }

    /// <summary>Checks a file pair, after fixing the target file in place when <paramref name="fix"/> is set.</summary>
    public static TranslationReport Check(string sourcePath, string targetPath, TranslationFileKind kind, TranslationLocale locale, TermBase termBase, bool fix)
    {
        var source = Parse(File.ReadAllText(sourcePath), kind);

        var targetBytes = File.ReadAllBytes(targetPath);
        var hasBom = targetBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var targetText = new UTF8Encoding(false).GetString(targetBytes.AsSpan(hasBom ? 3 : 0));

        IReadOnlyList<string> fixedKeys = [];
        if (fix)
        {
            string Fix(string value) => TranslationChecks.Fix(value, kind, locale);
            (targetText, fixedKeys) = kind == TranslationFileKind.Resx
                ? ResxStrings.Rewrite(targetText, Fix)
                : RestextLines.Rewrite(targetText, Fix);
            if (fixedKeys.Count > 0)
            {
                File.WriteAllText(targetPath, targetText, new UTF8Encoding(hasBom));
            }
        }

        var target = Parse(targetText, kind);
        var fileProblems = kind == TranslationFileKind.Restext
            ? RestextLines.MalformedLines(targetText)
                .Select(n => new FileProblem(n, "Line has no '='; a value may have been broken across lines."))
                .ToList()
            : [];

        var fixedSet = fixedKeys.ToHashSet(StringComparer.Ordinal);
        var strings = new List<StringReport>();
        foreach (var (key, src) in source)
        {
            if (!target.TryGetValue(key, out var tgt))
            {
                strings.Add(new StringReport(key, src, null, [], [new(TranslationChecks.Untranslated, "The target file has no entry for this key.")], false));
                continue;
            }

            var verdict = TranslationChecks.Check(src, tgt, kind, locale, termBase);
            strings.Add(new StringReport(key, src, tgt, verdict.Failures, verdict.Flags, fixedSet.Contains(key)));
        }

        foreach (var (key, tgt) in target.Where(t => !source.ContainsKey(t.Key)))
        {
            strings.Add(new StringReport(key, null, tgt, [new(TranslationChecks.UnknownKey, "The source file has no such key, so this string is never shown.")], [], fixedSet.Contains(key)));
        }

        return new TranslationReport(kind, locale.Code, sourcePath, targetPath, fileProblems, strings);
    }

    private static IReadOnlyDictionary<string, string> Parse(string text, TranslationFileKind kind) =>
        kind == TranslationFileKind.Resx ? ResxStrings.Parse(text) : new RestextFile().Parse(text);

    private static TermBase ParseTermBase(string path)
    {
        try
        {
            return TermBase.Parse(File.ReadAllText(path));
        }
        catch (FormatException ex)
        {
            throw new FormatException($"{path}: {ex.Message}", ex);
        }
    }

    private sealed record Options(string Source, string Target, TranslationFileKind Kind, TranslationLocale Locale, IReadOnlyList<string> TermBases, string? Json, bool Fix);

    private static bool TryParseArgs(IReadOnlyList<string> args, out Options options, out string error)
    {
        options = null!;
        var positional = new List<string>();
        var termBases = new List<string>();
        string? json = null;
        string? localeCode = null;
        var fix = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--fix":
                    fix = true;
                    break;
                case "--termbase" or "--json" or "--locale" when i + 1 >= args.Count:
                    error = $"{args[i]} needs a value.";
                    return false;
                case "--termbase":
                    termBases.Add(args[++i]);
                    break;
                case "--json":
                    json = args[++i];
                    break;
                case "--locale":
                    localeCode = args[++i];
                    break;
                case var a when a.StartsWith("--", StringComparison.Ordinal):
                    error = $"Unknown option {a}.";
                    return false;
                default:
                    positional.Add(args[i]);
                    break;
            }
        }

        if (positional.Count != 2)
        {
            error = "Expected a source file and a target file.";
            return false;
        }

        var kinds = positional.Select(p => Path.GetExtension(p).ToLowerInvariant() switch
        {
            ".restext" => TranslationFileKind.Restext,
            ".resx" => TranslationFileKind.Resx,
            _ => (TranslationFileKind?)null,
        }).ToList();
        if (kinds[0] is null || kinds[0] != kinds[1])
        {
            error = "Both files must be .restext, or both .resx.";
            return false;
        }

        localeCode ??= TranslationLocale.CodeFromFileName(positional[1]);
        if (localeCode is null)
        {
            error = $"Can't tell the locale from {Path.GetFileName(positional[1])}; pass --locale.";
            return false;
        }

        TranslationLocale locale;
        try
        {
            locale = TranslationLocale.For(localeCode);
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }

        foreach (var path in positional.Concat(termBases).Where(p => !File.Exists(p)))
        {
            error = $"File not found: {path}";
            return false;
        }

        options = new Options(positional[0], positional[1], kinds[0]!.Value, locale, termBases, json, fix);
        error = "";
        return true;
    }
}
