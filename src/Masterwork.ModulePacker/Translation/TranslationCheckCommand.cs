using System.Text;
using Masterwork.ModuleFormat;

namespace Masterwork.ModulePacker.Translation;

/// <summary>
/// The <c>checktranslation</c> mode: runs the deterministic translation checks over an English and
/// French restext or resx pair, optionally fixing typography first.
/// </summary>
public static class TranslationCheckCommand
{
    public const int ExitPassed = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;

    public const string Usage =
        "  Masterwork.ModulePacker checktranslation <english-file> <french-file> [--termbase <file>]... [--json <output.json>|-] [--fix]\n" +
        "      Both files are .restext or both are .resx. --fix rewrites the French file's typography (spacing only)\n" +
        "      before checking. --json - writes JSON to stdout and the summary to stderr.\n" +
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
            report = Check(options.English, options.French, options.Kind, termBase, options.Fix);
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

    /// <summary>Checks a file pair, after fixing the French file's typography in place when <paramref name="fix"/> is set.</summary>
    public static TranslationReport Check(string englishPath, string frenchPath, TranslationFileKind kind, TermBase termBase, bool fix)
    {
        var english = Parse(File.ReadAllText(englishPath), kind);

        var frenchBytes = File.ReadAllBytes(frenchPath);
        var hasBom = frenchBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var frenchText = new UTF8Encoding(false).GetString(frenchBytes.AsSpan(hasBom ? 3 : 0));

        IReadOnlyList<string> fixedKeys = [];
        if (fix)
        {
            string Normalize(string value) => FrenchTypography.Normalize(value, kind).Value;
            (frenchText, fixedKeys) = kind == TranslationFileKind.Resx
                ? ResxStrings.Rewrite(frenchText, Normalize)
                : RestextLines.Rewrite(frenchText, Normalize);
            if (fixedKeys.Count > 0)
            {
                File.WriteAllText(frenchPath, frenchText, new UTF8Encoding(hasBom));
            }
        }

        var french = Parse(frenchText, kind);
        var fileProblems = kind == TranslationFileKind.Restext
            ? RestextLines.MalformedLines(frenchText)
                .Select(n => new FileProblem(n, "Line has no '='; a value may have been broken across lines."))
                .ToList()
            : [];

        var fixedSet = fixedKeys.ToHashSet(StringComparer.Ordinal);
        var strings = new List<StringReport>();
        foreach (var (key, en) in english)
        {
            if (!french.TryGetValue(key, out var fr))
            {
                strings.Add(new StringReport(key, en, null, [], [new(TranslationChecks.Untranslated, "The French file has no entry for this key.")], false));
                continue;
            }

            var verdict = TranslationChecks.Check(en, fr, kind, termBase);
            strings.Add(new StringReport(key, en, fr, verdict.Failures, verdict.Flags, fixedSet.Contains(key)));
        }

        foreach (var (key, fr) in french.Where(f => !english.ContainsKey(f.Key)))
        {
            strings.Add(new StringReport(key, null, fr, [new(TranslationChecks.UnknownKey, "The English file has no such key, so this string is never shown.")], [], fixedSet.Contains(key)));
        }

        return new TranslationReport(kind, englishPath, frenchPath, fileProblems, strings);
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

    private sealed record Options(string English, string French, TranslationFileKind Kind, IReadOnlyList<string> TermBases, string? Json, bool Fix);

    private static bool TryParseArgs(IReadOnlyList<string> args, out Options options, out string error)
    {
        options = null!;
        var positional = new List<string>();
        var termBases = new List<string>();
        string? json = null;
        var fix = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--fix":
                    fix = true;
                    break;
                case "--termbase" or "--json" when i + 1 >= args.Count:
                    error = $"{args[i]} needs a value.";
                    return false;
                case "--termbase":
                    termBases.Add(args[++i]);
                    break;
                case "--json":
                    json = args[++i];
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
            error = "Expected an English file and a French file.";
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

        foreach (var path in positional.Concat(termBases).Where(p => !File.Exists(p)))
        {
            error = $"File not found: {path}";
            return false;
        }

        options = new Options(positional[0], positional[1], kinds[0]!.Value, termBases, json, fix);
        error = "";
        return true;
    }
}
