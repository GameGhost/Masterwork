using Masterwork.ModuleFormat;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Masterwork.ModulePacker <module-directory> <output.mwm>");
    return 1;
}

var moduleDir = args[0];
var outputPath = args[1];

if (!Directory.Exists(moduleDir))
{
    Console.Error.WriteLine($"Module directory not found: {moduleDir}");
    return 1;
}

var bytes = ModulePackage.WriteToBytes(moduleDir);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
File.WriteAllBytes(outputPath, bytes);

Console.WriteLine($"Wrote {outputPath} ({bytes.Length:N0} bytes)");
return 0;
