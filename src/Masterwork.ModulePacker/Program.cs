using Masterwork.ModuleFormat;

void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Masterwork.ModulePacker asset <asset-pack-directory> <output.mwassets>");
    Console.Error.WriteLine("  Masterwork.ModulePacker module <module-directory> <output.mwm>");
    Console.Error.WriteLine("  Masterwork.ModulePacker standalone <module-directory> <output.mwm> <asset-pack-directory> [asset-pack-directory ...]");
}

if (args.Length < 3)
{
    PrintUsage();
    return 1;
}

var mode = args[0];
var sourceDir = args[1];
var outputPath = args[2];

if (!Directory.Exists(sourceDir))
{
    Console.Error.WriteLine($"Directory not found: {sourceDir}");
    return 1;
}

byte[] bytes;
switch (mode)
{
    case "asset":
        bytes = AssetPackPackage.WriteToBytes(sourceDir);
        break;

    case "module":
        bytes = ModulePackage.WriteToBytes(sourceDir);
        break;

    case "standalone":
        var assetPackDirs = args[3..];
        if (assetPackDirs.Length == 0)
        {
            Console.Error.WriteLine("standalone mode needs at least one asset-pack directory.");
            return 1;
        }

        foreach (var dir in assetPackDirs)
        {
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"Asset pack directory not found: {dir}");
                return 1;
            }
        }

        bytes = ModulePackage.WriteStandaloneToBytes(sourceDir, assetPackDirs);
        break;

    default:
        PrintUsage();
        return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
File.WriteAllBytes(outputPath, bytes);

Console.WriteLine($"Wrote {outputPath} ({bytes.Length:N0} bytes)");
return 0;
