using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MasterWork.ModuleFormat;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MasterWork.Extractor;

partial class Program
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

        // Derive a human-readable module title: prefer explicit --module-title, fall back to source filename.
        var moduleTitle = opts.ModuleTitle;
        if (string.IsNullOrEmpty(moduleTitle) && sourceFiles.Count == 1)
        {
            var stem = Path.GetFileNameWithoutExtension(sourceFiles[0]);
            // "ATimeOfWar" → "A Time Of War", "FearOfTheUnknown" → "Fear Of The Unknown"
            moduleTitle = string.Concat(stem.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
        }

        var report = new ExtractionReport
        {
            SourceFilePath = sourceFiles.Count == 1 ? sourceFiles[0] : null,
            OutputDirPath = Path.GetFullPath(opts.OutputDir),
            ModuleTitle = moduleTitle,
        };
        var extractor = new CradleExtractor(opts, spriteMapper, report);

        Console.WriteLine("Extracting...");
        var passages = extractor.Extract(sourceFiles);

        Console.WriteLine($"Extracted {passages.Count} passages.");

        // Flag passages with no inbound references
        var referencedIds = CollectReferencedPassageIds(passages);
        var isolated = passages
            .Where(p => !referencedIds.Contains(p.PassageId))
            .OrderBy(p => p.PassageIndex ?? int.MaxValue)
            .Select(p => p.PassageId)
            .ToList();
        if (isolated.Count > 0)
            report.AddIsolatedPassages(isolated);

        if (opts.DryRun)
        {
            report.PrintSummary();
            Console.WriteLine("[dry-run] No files written.");
            return 0;
        }

        Directory.CreateDirectory(opts.OutputDir);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)
            .Build();

        var restext = new RestextCollector();

        foreach (var passage in passages)
        {
            var prefix = passage.PassageIndex.HasValue ? $"{passage.PassageIndex.Value:D5}-" : "";
            var fileName = $"{prefix}{SanitizeFileName(passage.PassageId)}.mws.yaml";
            report.PassageFiles[passage.PassageId] = fileName;
            var outPath = Path.Combine(opts.OutputDir, fileName);
            var dict = passage.ToDict();
            // Extract strings → restext://Key and collect for en-US.restext
            restext.CollectPassage(passage.PassageId, fileName, dict);
            var yaml = "---\n" + serializer.Serialize(dict);
            yaml = InjectSourceComments(yaml, passage);
            yaml = InjectRestextComments(yaml, restext.BuildCommentMap());
            File.WriteAllText(outPath, yaml, Encoding.UTF8);
        }

        // Apply hand-authored overrides before writing the report so override info is included
        ApplyOverrides(opts, report);

        // Print summary after overrides so unknown-node count excludes suppressed passages
        report.PrintSummary();

        // Write restext locale file
        WriteRestextFile(restext, opts.OutputDir);

        // Write variables manifest
        var vars = extractor.GetDiscoveredVariables();
        WriteVarsManifest(vars, opts.OutputDir, serializer);

        // Write extraction report
        var reportPath = Path.Combine(opts.OutputDir, "_extraction-report.md");
        report.SetVariables(vars);
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
                case "--overrides" when i + 1 < args.Length:
                    opts.OverridesDir = args[++i]; break;
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

        if (opts.OverridesDir is not null && !Directory.Exists(opts.OverridesDir))
        {
            Console.Error.WriteLine($"Overrides directory not found: {opts.OverridesDir}");
            return null;
        }

        return opts;
    }

    private static void ApplyOverrides(ExtractionOptions opts, ExtractionReport report)
    {
        if (opts.OverridesDir is null) return;

        var overrideFiles = Directory.GetFiles(opts.OverridesDir, "*.mws.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)
            .ToList();

        if (overrideFiles.Count == 0)
        {
            Console.WriteLine("[overrides] No override files found.");
            return;
        }

        Console.WriteLine($"[overrides] Applying {overrideFiles.Count} override(s)...");

        foreach (var overridePath in overrideFiles)
        {
            var fileName = Path.GetFileName(overridePath);
            var generatedPath = Path.Combine(opts.OutputDir, fileName);

            if (!File.Exists(generatedPath))
            {
                Console.Error.WriteLine($"[overrides] SKIP {fileName}: no matching generated file in output dir.");
                continue;
            }

            var overrideContent = File.ReadAllText(overridePath, Encoding.UTF8);
            var generatedContent = File.ReadAllText(generatedPath, Encoding.UTF8);

            var overrideId = ExtractPassageId(overrideContent);
            var generatedId = ExtractPassageId(generatedContent);

            if (overrideId is null)
            {
                Console.Error.WriteLine($"[overrides] SKIP {fileName}: could not extract passage_id from override.");
                continue;
            }
            if (generatedId is null)
            {
                Console.Error.WriteLine($"[overrides] SKIP {fileName}: could not extract passage_id from generated file.");
                continue;
            }
            if (!string.Equals(overrideId, generatedId, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"[overrides] SKIP {fileName}: passage_id mismatch — override='{overrideId}' vs generated='{generatedId}'.");
                continue;
            }

            File.Copy(overridePath, generatedPath, overwrite: true);
            report.AddOverrideApplied(overrideId, fileName);
            Console.WriteLine($"[overrides] Applied: {fileName} (passage_id: {overrideId})");
        }
    }

    [GeneratedRegex(@"^passage_id:\s*(\S+)", RegexOptions.Multiline)]
    private static partial Regex PassageIdRegex();

    private static string? ExtractPassageId(string content)
    {
        var m = PassageIdRegex().Match(content);
        return m.Success ? m.Groups[1].Value : null;
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

    // Appends " # "preview"" after each restext://Key in the YAML.
    // Only touches lines that contain a restext:// reference.
    [GeneratedRegex(@"restext://(\S+)")]
    private static partial Regex RestextKeyRegex();

    private static string InjectRestextComments(string yaml, IReadOnlyDictionary<string, string> commentMap)
    {
        if (commentMap.Count == 0) return yaml;
        return RestextKeyRegex().Replace(yaml, m =>
        {
            var key = m.Groups[1].Value;
            if (!commentMap.TryGetValue(key, out var value)) return m.Value;
            return $"{m.Value} {BuildRestextPreview(value)}";
        });
    }

    private static string BuildRestextPreview(string value)
    {
        if (value.Contains('\n')) return "# [multiline]";
        var escaped = value.Replace("\"", "\\\"");
        return escaped.Length <= 80 ? $"# \"{escaped}\"" : $"# \"{escaped[..77]}...\"";
    }

    // Writes en-US.restext: one Key=Value per string, grouped by passage with a comment header.
    // Multi-line values use the """...""" block syntax.
    private static void WriteRestextFile(RestextCollector restext, string outputDir)
    {
        var outPath = Path.Combine(outputDir, "en-US.restext");
        var sb = new StringBuilder();
        sb.AppendLine("# MasterWork locale file — en-US");
        sb.AppendLine("# Format: Key=Value  (one string per line)");
        sb.AppendLine("# Multi-line values: Key=\"\"\"");
        sb.AppendLine("#   line 1");
        sb.AppendLine("# \"\"\"");
        sb.AppendLine();

        int totalStrings = 0;
        foreach (var (fileName, entries) in restext.Passages)
        {
            sb.AppendLine($"# {fileName}");
            foreach (var entry in entries)
            {
                if (entry.Value.Contains('\n'))
                {
                    sb.AppendLine($"{entry.Key}=\"\"\"");
                    sb.AppendLine(entry.Value);
                    sb.AppendLine("\"\"\"");
                }
                else
                {
                    sb.AppendLine($"{entry.Key}={entry.Value}");
                }
                totalStrings++;
            }
            sb.AppendLine();
        }

        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"Locale file: {outPath} ({totalStrings} strings)");
    }

    private static HashSet<string> CollectReferencedPassageIds(List<MwsPassage> passages)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in passages)
            CollectFromNodes(p.Nodes, ids);
        return ids;
    }

    private static void CollectFromNodes(List<MwsNode> nodes, HashSet<string> ids)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case LinkNode lk: ids.Add(lk.Target); break;
                case GotoNode go: ids.Add(go.Target); break;
                case IncludePassageNode inc: ids.Add(inc.Target); break;
                case SetupNotificationNode sn when sn.NextPassage is not null:
                    ids.Add(sn.NextPassage); break;
                case CheckProgressNode cp:
                    ids.Add(cp.CurrentPassage); ids.Add(cp.TargetPassage); break;
                case ModalNode mo when mo.Next is not null:
                    ids.Add(mo.Next); break;
                case ConditionalNode cond:
                    foreach (var b in cond.Branches) CollectFromNodes(b.Nodes, ids);
                    break;
                case SwitchNode sw:
                    foreach (var c in sw.Cases) CollectFromNodes(c.Nodes, ids);
                    break;
                case SectionBodyNode sec: CollectFromNodes(sec.Nodes, ids); break;
                case SetupBlockNode sb2: CollectFromNodes(sb2.Nodes, ids); break;
                case ExpandLinkNode exp: CollectFromNodes(exp.ExpandNodes, ids); break;
                case ForeachNode fe: CollectFromNodes(fe.Nodes, ids); break;
                // choose-one values on a let node may be passage IDs (dynamic inclusion pattern)
                case LetNode let when let.Random?.RandomType == "choose-one":
                    foreach (var v in let.Random.Values.OfType<string>()) ids.Add(v);
                    break;
            }
        }
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
              --overrides <dir>       Directory of hand-authored override YAML files;
                                      each must match the generated filename and passage_id
            """);
    }
}
