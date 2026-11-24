using System.Text;
using Masterwork.Translation;

namespace Masterwork.Extractor.Reconcile;

/// <summary>The <c>reconcile</c> subcommand: argument parsing and file output around <see cref="Reconciler"/>.</summary>
internal static class ReconcileCommand
{
    public static int Run(string[] args)
    {
        if (args is ["--help" or "-h", ..])
        {
            PrintUsage();
            return 0;
        }

        if (args.Length < 3)
        {
            PrintUsage();
            return 1;
        }

        var (englishDir, frenchDir, moduleDir) = (args[0], args[1], args[2]);
        var locale = "fr-CA";
        string? outPath = null;
        string? reportPath = null;
        string? title = null;
        var renames = new Dictionary<string, string>(Reconciler.CradleFrenchRenames, StringComparer.Ordinal);
        for (var i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--locale" when i + 1 < args.Length:
                    locale = args[++i]; break;
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i]; break;
                case "--report" when i + 1 < args.Length:
                    reportPath = args[++i]; break;
                case "--title" when i + 1 < args.Length:
                    title = args[++i]; break;
                case "--rename" when i + 1 < args.Length && args[i + 1].IndexOf('=') > 0:
                    var rename = args[++i];
                    var eq = rename.IndexOf('=');
                    renames[rename[..eq]] = rename[(eq + 1)..];
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 1;
            }
        }

        foreach (var dir in new[] { Path.Combine(englishDir, "passages"), Path.Combine(frenchDir, "passages"), moduleDir })
        {
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"Directory not found: {dir}");
                return 1;
            }
        }

        outPath ??= Path.Combine(moduleDir, $"{locale}.restext");
        reportPath ??= Path.Combine(moduleDir, ".source", $"_reconcile-report.{locale}.md");
        title ??= Path.GetFileName(Path.GetFullPath(moduleDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        TranslationLocale targetLocale;
        try
        {
            targetLocale = TranslationLocale.For(locale);
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var result = Reconciler.Run(englishDir, frenchDir, moduleDir, renames, targetLocale);

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, ReconcileOutput.Restext(result, locale), utf8);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, ReconcileOutput.Report(result, locale, title), utf8);

        Console.WriteLine($"mw-extract reconcile: {result.Keys.Count} keys ({result.DevNoteKeys.Count} dev-note keys excluded)");
        foreach (var (bucket, count) in ReconcileOutput.BucketCounts(result))
        {
            Console.WriteLine($"  {ReconcileOutput.BucketLabel(bucket),-44} {count,5}  {100.0 * count / Math.Max(1, result.Keys.Count),5:F1}%");
        }

        Console.WriteLine($"  French-only texts: {result.FrenchOnly.Count}");
        Console.WriteLine($"Restext: {outPath}");
        Console.WriteLine($"Report:  {reportPath}");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            mw-extract reconcile <english-extract-dir> <french-extract-dir> <module-dir> [options]

            Pairs a French Cradle extraction with a scenario's live English keys and writes the
            scenario's starting French restext plus a Markdown report. Both extraction dirs are
            unmodified extractor output (passages/ + en-US.restext), written to scratch, never
            into the module.

            Options:
              --locale <culture>   Locale written (default: fr-CA)
              --out <file>         Restext output (default: <module-dir>/<locale>.restext)
              --report <file>      Report output (default: <module-dir>/.source/_reconcile-report.<locale>.md)
              --title <title>      Scenario name for the report heading (default: module folder name)
              --rename <fr>=<en>   Map a renamed French passage id to its English id; may repeat.
                                   The five known Cradle French renames are always applied.
            """);
    }
}
