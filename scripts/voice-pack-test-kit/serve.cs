#:sdk Microsoft.NET.Sdk.Web

// Serves the voice pack test kit's site folder over https://localhost:5443, with CORS for the web
// head and no caching, so a rebuilt stage shows up on the app's next catalog refresh.
// Usage: dotnet run serve.cs -- <site-folder>   (stop with Ctrl+C to test being offline)

using Microsoft.Extensions.FileProviders;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("out", "site"));
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"No site folder at {root}. Run build.ps1 first.");
    return 1;
}

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("https://localhost:5443");
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.AccessControlAllowOrigin = "*";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(root),
    RequestPath = "",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
});

Console.WriteLine($"Serving {root} at https://localhost:5443/catalog.json");
app.Run();
return 0;
