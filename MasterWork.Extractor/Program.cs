using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MasterWork.ModuleFormat;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MasterWork.Extractor;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] == "--help" || args[0] == "-h")
        {
            PrintUsage();
            return 0;
        }

        var opts = ParseArgs(args);
        if (opts is null) return 1;

        Console.WriteLine($"mw-extract: {opts.InputDir} → {opts.OutputDir}");

        // Accept either a single .cs file or a directory
        List<string> sourceFiles;
        if (File.Exists(opts.InputDir) && opts.InputDir.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            sourceFiles = [opts.InputDir];
        }
        else
        {
            sourceFiles = Directory.GetFiles(opts.InputDir, "*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .ToList();
        }

        if (sourceFiles.Count == 0)
        {
            Console.Error.WriteLine($"No .cs files found in: {opts.InputDir}");
            return 1;
        }

        Console.WriteLine($"  Source files: {sourceFiles.Count}");
        foreach (var f in sourceFiles)
            Console.WriteLine($"    {Path.GetFileName(f)}");

        var spriteMapper = opts.SpriteMapPath is not null
            ? SpriteMapper.FromJsonFile(opts.SpriteMapPath)
            : SpriteMapper.Empty();

        var report = new ExtractionReport
        {
            SourceFilePath = sourceFiles.Count == 1 ? sourceFiles[0] : null,
            OutputDirPath = Path.GetFullPath(opts.OutputDir),
        };
        var extractor = new CradleExtractor(opts, spriteMapper, report);

        Console.WriteLine("Extracting...");
        var passages = extractor.Extract(sourceFiles);

        Console.WriteLine($"Extracted {passages.Count} passages.");
        report.PrintSummary();

        if (opts.DryRun)
        {
            Console.WriteLine("[dry-run] No files written.");
            return 0;
        }

        Directory.CreateDirectory(opts.OutputDir);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)
            .Build();

        foreach (var passage in passages)
        {
            var outPath = Path.Combine(opts.OutputDir, $"{SanitizeFileName(passage.PassageId)}.mws.yaml");
            var dict = passage.ToDict();
            var yaml = "---\n" + serializer.Serialize(dict);
            yaml = InjectSourceComments(yaml, passage);
            File.WriteAllText(outPath, yaml, Encoding.UTF8);
        }

        // Write variables manifest
        var vars = extractor.GetDiscoveredVariables();
        WriteVarsManifest(vars, opts.OutputDir, serializer);

        // Write extraction report
        var reportPath = Path.Combine(opts.OutputDir, "_extraction-report.md");
        report.Write(reportPath);

        Console.WriteLine($"Done. {passages.Count} passages written to: {opts.OutputDir}");
        return 0;
    }

    private static void WriteVarsManifest(
        Dictionary<string, VarDef> vars,
        string outputDir,
        ISerializer serializer)
    {
        var standard = vars.Values.Where(v => v.IsStandard).OrderBy(v => v.Name).ToList();
        var module = vars.Values.Where(v => !v.IsStandard).OrderBy(v => v.Name).ToList();

        var manifest = new Dictionary<string, object?>
        {
            ["standard_variables"] = standard.Select(v => v.Name).ToList(),
            ["variables"] = module.ToDictionary(
                v => v.Name,
                v => (object?)new Dictionary<string, object?> { ["type"] = v.VarType, ["default"] = v.Default ?? DefaultForType(v.VarType) }),
        };

        var outPath = Path.Combine(outputDir, "_variables.yaml");
        File.WriteAllText(outPath, "---\n" + serializer.Serialize(manifest), Encoding.UTF8);
        Console.WriteLine($"Variables manifest: {outPath}");
    }

    private static object DefaultForType(string type) => type switch
    {
        "int" => 0,
        "array" => new List<object>(),
        _ => "",
    };

    private static ExtractionOptions? ParseArgs(string[] args)
    {
        var opts = new ExtractionOptions
        {
            InputDir = args[0],
            OutputDir = args[1],
        };

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--module-id" when i + 1 < args.Length:
                    opts.ModuleId = args[++i]; break;
                case "--module-title" when i + 1 < args.Length:
                    opts.ModuleTitle = args[++i]; break;
                case "--sprite-map" when i + 1 < args.Length:
                    opts.SpriteMapPath = args[++i]; break;
                case "--include-debug":
                    opts.IncludeDebug = true; break;
                case "--dry-run":
                    opts.DryRun = true; break;
                case "--seed-analysis":
                    opts.SeedAnalysis = true; break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return null;
            }
        }

        if (!Directory.Exists(opts.InputDir) && !File.Exists(opts.InputDir))
        {
            Console.Error.WriteLine($"Input path not found: {opts.InputDir}");
            return null;
        }

        return opts;
    }

    // Injects YAML comments for passage and node source locations.
    // Format: "# filename:line-number" before the --- marker and before each top-level node.
    private static string InjectSourceComments(string yaml, MwsPassage passage)
    {
        if (passage.SourceFile is null && passage.Nodes.All(n => n.SourceLine is null))
            return yaml;

        var sourceFileName = passage.SourceFile is not null
            ? Path.GetFileName(passage.SourceFile)
            : null;

        var lines = yaml.Split('\n');
        var result = new List<string>(lines.Length + passage.Nodes.Count + 2);

        int nodeIndex = 0;
        bool inNodes = false;
        bool firstLine = true;

        foreach (var line in lines)
        {
            // Prepend passage-level comment before the YAML document marker
            if (firstLine && line == "---" && passage.MainMethodSourceLine.HasValue && sourceFileName is not null)
                result.Add($"# {sourceFileName}:{passage.MainMethodSourceLine}");
            firstLine = false;

            if (!inNodes && line.TrimEnd() == "nodes:")
            {
                inNodes = true;
                result.Add(line);
                continue;
            }

            // Unindented list item = top-level node entry
            if (inNodes && line.StartsWith("- ") && nodeIndex < passage.Nodes.Count)
            {
                var node = passage.Nodes[nodeIndex++];
                if (node.SourceLine.HasValue && sourceFileName is not null)
                    result.Add($"# {sourceFileName}:{node.SourceLine}");
            }

            result.Add(line);
        }

        return string.Join('\n', result);
    }

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));

    private static void PrintUsage()
    {
        Console.WriteLine("""
            mw-extract <input-dir> <output-dir> [options]

            Converts Cradle C# scenario files to MWS YAML.

            Options:
              --module-id <id>        Module ID (e.g. original.cost_of_disease)
              --module-title <title>  Human-readable title
              --sprite-map <json>     Path to ItemObtain JSON for sprite → asset_ref mapping
              --include-debug         Include devpage-gated debug passages
              --dry-run               Parse and report without writing files
              --seed-analysis         Emit seed dependency report
            """);
    }
}
