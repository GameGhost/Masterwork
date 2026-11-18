using Masterwork.App.Shared.Services;

// Usage: Masterwork.App.Web.StaticPages <host page template> <output directory>
// Writes index.html (the template with WebAppPages' tokens filled) and manifest.json for the flavor
// this tool was built as.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Masterwork.App.Web.StaticPages <host page template> <output directory>");
    return 2;
}

var (templatePath, outputDirectory) = (args[0], args[1]);
var whiteLabel = ThemeModules.Load(typeof(Program).Assembly).Config;

Directory.CreateDirectory(outputDirectory);
var template = await File.ReadAllTextAsync(templatePath);
await WriteIfChangedAsync(Path.Combine(outputDirectory, "index.html"), await WebAppPages.RenderHostPageAsync(template, whiteLabel));
await WriteIfChangedAsync(Path.Combine(outputDirectory, "manifest.json"), WebAppPages.ManifestJson(whiteLabel));
return 0;

// Unchanged files keep their timestamps, so an incremental build doesn't see new inputs every time.
static async Task WriteIfChangedAsync(string path, string content)
{
    if (File.Exists(path) && await File.ReadAllTextAsync(path) == content)
    {
        return;
    }
    await File.WriteAllTextAsync(path, content);
}
