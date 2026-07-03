using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Masterwork.ModuleFormat;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace Masterwork.Extractor;

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

        // Record command-line settings for the extraction report.
        if (opts.ModuleTitle is { Length: > 0 } mt) report.Settings["Module title"] = mt;
        if (opts.ModuleId is { Length: > 0 } mid) report.Settings["Module ID"] = mid;
        if (opts.SpriteMapPath is not null) report.Settings["Sprite map"] = Path.GetFileName(opts.SpriteMapPath);
        if (opts.RestextExcludeTags.Count > 0)
            report.Settings["Restext exclude tags"] = string.Join(", ", opts.RestextExcludeTags.Order());
        if (opts.RestextExcludeIds.Count > 0)
            report.Settings["Restext exclude IDs"] = string.Join(", ", opts.RestextExcludeIds.Order());
        if (opts.OverridesDir is not null) report.Settings["Overrides dir"] = opts.OverridesDir;
        if (opts.Breaks != BreaksMode.Omit) report.Settings["Extra breaks"] = opts.Breaks.ToString().ToLowerInvariant();

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
            .WithEventEmitter(next => new SingleQuotedStringValueEmitter(next))
            .Build();

        var restext = new RestextCollector(passages.Select(p => p.PassageId));

        // Build passage → relative YAML filename map for navigation target annotations
        var passageFileMap = new Dictionary<string, string>(passages.Count, StringComparer.Ordinal);
        foreach (var p in passages)
        {
            var pfx = p.PassageIndex.HasValue ? $"{p.PassageIndex.Value:D5}-" : "";
            passageFileMap[p.PassageId] = $"./{pfx}{SanitizeFileName(p.PassageId)}.mws.yaml";
        }

        // Filter trailing and non-rendered-sandwiched breaks from all passages.
        if (opts.Breaks != BreaksMode.Emit)
            foreach (var p in passages)
                p.Nodes = BreakFilter.Apply(p.Nodes, opts.Breaks);

        // Build the set of passage IDs excluded from restext extraction.
        var excludedFromRestext = new HashSet<string>(StringComparer.Ordinal);
        if (opts.RestextExcludeTags.Count > 0 || opts.RestextExcludeIds.Count > 0)
            foreach (var p in passages)
                if (opts.RestextExcludeIds.Contains(p.PassageId) ||
                    p.Tags.Any(t => opts.RestextExcludeTags.Contains(t)))
                    excludedFromRestext.Add(p.PassageId);

        // Phase 1: build all dicts and collect restext entries.
        // Restext line numbers depend on the complete file content, so collect everything first.
        var cachedPassages = new List<(MwsPassage Passage, Dictionary<string, object?> Dict, string FileName, string OutPath, string? RelSourcePath)>(passages.Count);
        var fullOutputDir = Path.GetFullPath(opts.OutputDir);
        foreach (var passage in passages)
        {
            var prefix = passage.PassageIndex.HasValue ? $"{passage.PassageIndex.Value:D5}-" : "";
            var fileName = $"{prefix}{SanitizeFileName(passage.PassageId)}.mws.yaml";
            report.PassageFiles[passage.PassageId] = fileName;
            report.AddTaggedPassage(passage.PassageId, passage.Tags);
            var outPath = Path.Combine(opts.OutputDir, fileName);
            var relSourcePath = passage.SourceFile is not null
                ? Path.GetRelativePath(fullOutputDir, passage.SourceFile).Replace('\\', '/')
                : null;
            var ctx = new SerializationContext(
                SourceRelativePath: relSourcePath,
                PassageFileMap: passageFileMap
            );
            var dict = V2Serializer.ToDict(passage, ctx);
            if (!excludedFromRestext.Contains(passage.PassageId))
                // Mutates dict in-place: replaces string values with restext://Key references
                restext.CollectPassage(passage.PassageId, fileName, dict,
                    isTemplate: !passage.Tags.Contains("notext", StringComparer.Ordinal));
            else
                report.AddRestextExclusion(passage.PassageId);
            cachedPassages.Add((passage, dict, fileName, outPath, relSourcePath));
        }

        // Restore string literals for variables never used in display-text templates.
        // Strings assigned to variables that only appear in logic (conditions, not {var} text) stay
        // as raw literals; strings whose variable names appear in any {varName} template stay as restext refs.
        restext.RestoreNonTemplateAssignments();

        // Scan condition/match string literals across passages before renaming.
        // This registers cross-passage usage so values that appear in conditions from
        // multiple passages get promoted to Common_NNN keys along with text-field duplicates.
        restext.ScanConditionLiterals(cachedPassages
            .Where(p => !excludedFromRestext.Contains(p.Passage.PassageId))
            .Select(p => (p.Passage.PassageId, p.Dict)));

        // Rename keys used in 2+ passages to Common_NNN and move them to a Common group.
        var renameMap = restext.BuildRenameMap();
        if (renameMap.Count > 0)
        {
            foreach (var (_, dict, _, _, _) in cachedPassages)
                RestextCollector.ApplyRenamesInDict(dict, renameMap);
            restext.ApplyRenames(renameMap);
        }

        // Replace string literals in condition/match fields with restext://Key URIs when the
        // literal exactly matches a restext value. Uses final (post-rename) Common keys.
        restext.ApplyConditionLiteralReplacements(cachedPassages
            .Where(p => !excludedFromRestext.Contains(p.Passage.PassageId))
            .Select(p => p.Dict));

        // Build restext comment map and line-number map before writing any YAML files.
        var commentMap = restext.BuildCommentMap();
        var keyLineMap = BuildRestextLineMap(restext);

        // Phase 2: serialize and write YAML files using the now-complete restext index.
        foreach (var (passage, dict, _, outPath, relSourcePath) in cachedPassages)
        {
            var yaml = ("---\n" + serializer.Serialize(dict)).Replace("\r\n", "\n").Replace("\r", "\n");
            yaml = InjectSentinelComments(yaml);
            yaml = InjectSourceComments(yaml, passage, relSourcePath);
            yaml = InjectRestextComments(yaml, commentMap, keyLineMap);
            File.WriteAllText(outPath, yaml.Replace("\n", "\r\n"), Encoding.UTF8);
        }

        // Apply hand-authored overrides before writing the report so override info is included
        ApplyOverrides(opts, report);

        // Print summary after overrides so unknown-node count excludes suppressed passages
        report.PrintSummary();

        // Write restext locale file
        restext.ReportDeduplicationStats(report);
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
                case "--restext-exclude-tag" when i + 1 < args.Length:
                    opts.RestextExcludeTags.Add(args[++i]); break;
                case "--restext-exclude-id" when i + 1 < args.Length:
                    opts.RestextExcludeIds.Add(args[++i]); break;
                case "--extra-breaks" when i + 1 < args.Length:
                    opts.Breaks = args[++i].ToLowerInvariant() switch
                    {
                        "omit" => BreaksMode.Omit,
                        "emit" => BreaksMode.Emit,
                        "emit-commented" => BreaksMode.EmitCommented,
                        var v => throw new ArgumentException($"Unknown --breaks mode: {v}. Use omit, emit, or emit-commented."),
                    };
                    break;
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
        if (!m.Success) return null;
        var val = m.Groups[1].Value;
        return val.Length >= 2 && val[0] == '\'' && val[^1] == '\'' ? val[1..^1] : val;
    }

    // Injects a "# relpath:line" comment before the YAML document marker (--- line).
    // All node-level comments are handled by InjectSentinelComments via V2Serializer sentinels.
    private static string InjectSourceComments(string yaml, MwsPassage passage, string? relSourcePath)
    {
        if (!passage.MainMethodSourceLine.HasValue || relSourcePath is null)
            return yaml;

        var lines = yaml.Split('\n');
        var result = new List<string>(lines.Length + 1);
        bool firstLine = true;

        foreach (var line in lines)
        {
            if (firstLine && line == "---")
                result.Add($"# {relSourcePath}:{passage.MainMethodSourceLine}");
            firstLine = false;
            result.Add(line);
        }

        return string.Join('\n', result);
    }

    // Converts _src sentinel list items and _link hint fields produced by V2Serializer
    // into YAML comments, then removes the sentinel lines from the output.
    //
    // _src sentinel:  "  - _src: path:line"  →  "  # path:line"  (block comment, same indentation)
    // _link hint:     "  _link: file"         →  appended as " # file" to the preceding line
    [GeneratedRegex(@"^(\s*)- _src: (.+)$")]
    private static partial Regex SrcSentinelRegex();

    [GeneratedRegex(@"^\s+_link: (.+)$")]
    private static partial Regex LinkHintRegex();

    [GeneratedRegex(@"^(\s*)- _commented_break: (.+)$")]
    private static partial Regex CommentedBreakRegex();

    private static string InjectSentinelComments(string yaml)
    {
        var lines = yaml.Split('\n');
        var result = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            var srcMatch = SrcSentinelRegex().Match(line);
            if (srcMatch.Success)
            {
                result.Add($"{srcMatch.Groups[1].Value}# {UnquoteYamlSingleScalar(srcMatch.Groups[2].Value)}");
                continue;
            }

            var linkMatch = LinkHintRegex().Match(line);
            if (linkMatch.Success)
            {
                if (result.Count > 0)
                    result[^1] += $" # {UnquoteYamlSingleScalar(linkMatch.Groups[1].Value)}";
                continue;
            }

            var cbMatch = CommentedBreakRegex().Match(line);
            if (cbMatch.Success)
            {
                result.Add($"{cbMatch.Groups[1].Value}# - type: {UnquoteYamlSingleScalar(cbMatch.Groups[2].Value)}");
                continue;
            }

            result.Add(line);
        }

        return string.Join('\n', result);
    }

    // Strips YAML single-quote wrapping (e.g. "'foo bar'" → "foo bar", "'it''s'" → "it's").
    private static string UnquoteYamlSingleScalar(string s) =>
        s.Length >= 2 && s[0] == '\'' && s[^1] == '\''
            ? s[1..^1].Replace("''", "'")
            : s;

    // Inserts restext comments above each YAML line that contains one or more restext://Key refs.
    //
    // Single reference on a line:
    //   # en-US.restext:NNN | "preview"
    //
    // Multiple references on a line (one comment per reference, in order):
    //   # en-US.restext:NNN | KEY | "preview"
    //   # en-US.restext:NNN | KEY | "preview"
    //
    // Preview: ≤30 chars shown in full; >30 truncated to 25 + "...". Multi-line: [multiline].
    // The field line itself is kept unchanged (no inline comment appended).
    [GeneratedRegex(@"restext://([A-Za-z0-9_]+)")]
    private static partial Regex RestextKeyRegex();

    private static string InjectRestextComments(
        string yaml,
        IReadOnlyDictionary<string, string> commentMap,
        IReadOnlyDictionary<string, int> keyLineMap)
    {
        if (commentMap.Count == 0) return yaml;

        var lines = yaml.Split('\n');
        var result = new List<string>(lines.Length + commentMap.Count);

        foreach (var line in lines)
        {
            var matches = RestextKeyRegex().Matches(line);
            if (matches.Count == 0)
            {
                result.Add(line);
                continue;
            }

            var indent = new string(' ', line.Length - line.TrimStart().Length);

            if (matches.Count == 1)
            {
                var key = matches[0].Groups[1].Value;
                if (commentMap.TryGetValue(key, out var value))
                {
                    var link = keyLineMap.TryGetValue(key, out var ln) ? $"en-US.restext:{ln}" : key;
                    result.Add($"{indent}# {link} | {FormatRestextPreview(value)}");
                }
            }
            else
            {
                foreach (Match m in matches)
                {
                    var key = m.Groups[1].Value;
                    if (!commentMap.TryGetValue(key, out var value)) continue;
                    var link = keyLineMap.TryGetValue(key, out var ln) ? $"en-US.restext:{ln}" : key;
                    result.Add($"{indent}# {link} | {key} | {FormatRestextPreview(value)}");
                }
            }

            result.Add(line);
        }

        return string.Join('\n', result);
    }

    // Restext values are single-line only. Source text can still contain embedded newlines
    // (e.g. multi-paragraph input prompts); collapse them to spaces so every value round-trips
    // as exactly one physical line, both in the .restext file and in inline preview comments.
    // Values without embedded newlines pass through unchanged (no incidental trimming).
    private static string NormalizeRestextValue(string value) =>
        value.Contains('\n') ? RestextNewlineRun().Replace(value, " ").Trim() : value;

    [GeneratedRegex(@"\s*\r?\n\s*")]
    private static partial Regex RestextNewlineRun();

    private static string FormatRestextPreview(string value)
    {
        var normalized = NormalizeRestextValue(value);
        var escaped = normalized.Replace("\"", "\\\"");
        return escaped.Length <= 30 ? $"\"{escaped}\"" : $"\"{escaped[..25]}...\"";
    }

    // Computes the 1-based line number of each restext key in the en-US.restext file
    // by simulating the WriteRestextFile output without actually writing it.
    private static Dictionary<string, int> BuildRestextLineMap(RestextCollector restext)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        // WriteRestextFile emits 2 header AppendLine calls + 1 blank AppendLine = 3 lines
        int line = 4;
        foreach (var (_, entries) in restext.Passages)
        {
            if (entries.Count == 0) continue;
            line++; // # section comment
            foreach (var entry in entries)
            {
                map[entry.Key] = line;
                line++; // key=value
            }
            line++; // blank line after each group
        }
        return map;
    }

    // Writes en-US.restext: one Key=Value per string, grouped by passage with a comment header.
    // Common strings (shared across passages) appear first under "# (Common)".
    private static void WriteRestextFile(RestextCollector restext, string outputDir)
    {
        var outPath = Path.Combine(outputDir, "en-US.restext");
        var sb = new StringBuilder();
        sb.AppendLine("# MasterWork locale file — en-US");
        sb.AppendLine("# Format: Key=Value  (one string per line)");
        sb.AppendLine();

        int totalStrings = 0;
        foreach (var (fileName, entries) in restext.Passages)
        {
            if (entries.Count == 0) continue;
            sb.AppendLine(fileName == "(Common)"
                ? "# Common strings — shared by multiple passages"
                : $"# {fileName}");
            foreach (var entry in entries)
            {
                sb.AppendLine($"{entry.Key}={NormalizeRestextValue(entry.Value)}");
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
                case LinkNode lk:
                    ids.Add(lk.Target);
                    if (lk.Nodes.Count > 0) CollectFromNodes(lk.Nodes, ids);
                    break;
                case GotoNode go when !go.Target.StartsWith("${", StringComparison.Ordinal):
                    ids.Add(go.Target); break;
                case IncludePassageNode inc when !inc.Target.StartsWith("${", StringComparison.Ordinal):
                    ids.Add(inc.Target); break;
                case SetupNotificationNode sn when sn.NextPassage is { Length: > 0 } np
                    && !np.StartsWith("${", StringComparison.Ordinal):
                    ids.Add(np); break;
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

    // Forces single-quote style for all string VALUES in the emitted YAML.
    // Tracks mapping key/value alternation so keys remain unquoted plain scalars.
    private sealed class SingleQuotedStringValueEmitter(IEventEmitter nextEmitter)
        : ChainedEventEmitter(nextEmitter)
    {
        // Stack entry: null = inside a sequence (all scalars are values);
        //              false = mapping — next scalar is a KEY;
        //              true  = mapping — next scalar is a VALUE.
        private readonly Stack<bool?> _ctx = [];

        // When a nested mapping or sequence starts as a VALUE, consume the parent's
        // value slot so the next sibling key is correctly treated as a key.
        private void ConsumeValueSlot()
        {
            if (_ctx.TryPeek(out var ctx) && ctx == true)
            {
                _ctx.TryPop(out _);
                _ctx.Push(false);
            }
        }

        public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter)
        {
            ConsumeValueSlot();
            _ctx.Push(false);
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(MappingEndEventInfo eventInfo, IEmitter emitter)
        {
            _ctx.TryPop(out _);
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter)
        {
            ConsumeValueSlot();
            _ctx.Push(null);
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(SequenceEndEventInfo eventInfo, IEmitter emitter)
        {
            _ctx.TryPop(out _);
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (_ctx.TryPeek(out var top))
            {
                bool isValue = top is null || top == true;
                if (top is not null)
                {
                    _ctx.TryPop(out _);
                    _ctx.Push(!top); // toggle key ↔ value
                }
                if (isValue && eventInfo.Source.Type == typeof(string))
                    eventInfo.Style = ScalarStyle.SingleQuoted;
            }
            base.Emit(eventInfo, emitter);
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
              --restext-exclude-tag <tag>
                                      Exclude passages with this tag from restext extraction;
                                      may be specified multiple times (e.g. --restext-exclude-tag notext)
              --restext-exclude-id <id>
                                      Exclude a specific passage ID from restext extraction;
                                      may be specified multiple times
              --extra-breaks <mode>   How to emit trailing/non-rendered-sandwiched breaks:
                                      omit (default), emit, emit-commented
            """);
    }
}
