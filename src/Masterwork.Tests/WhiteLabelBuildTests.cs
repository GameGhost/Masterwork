using System.Diagnostics;
using System.Text.Json;

namespace Masterwork.Tests;

/// <summary>
/// Evaluates the real host projects with MSBuild to check what each <c>WhiteLabelFlavor</c> puts in
/// them. Evaluation only — nothing is built — so each case costs about a second.
/// </summary>
/// <remarks>
/// Needs the .NET SDK on the path and, on Windows, the MAUI workload (the MAUI head is evaluated
/// there). A machine without them can leave this class out with
/// <c>dotnet test --filter "Category!=BuildEvaluation"</c>; it deliberately doesn't skip itself, since
/// a silent skip would hide a real breakage on a machine that is meant to run it.
/// </remarks>
[Trait("Category", "BuildEvaluation")]
public class WhiteLabelBuildTests
{
    private static readonly string SrcDirectory = FindSrcDirectory();

    // Generous for an evaluation (normally about a second), but bounded, so a hung MSBuild fails this
    // test instead of hanging the whole run.
    private static readonly TimeSpan EvaluationTimeout = TimeSpan.FromMinutes(2);

    // ApplicationTitle is the OS-level name and must equal the theme's WhiteLabelConfig.AppName, the
    // in-app one; the Test row is checked against TestTheme directly. The shipping theme isn't
    // referenced by this project, so its row is the one place the two values are written side by side.
    private static readonly Dictionary<string, (string ThemeProject, string ModuleType, string ApplicationId, string ApplicationTitle)> Flavors = new()
    {
        ["MyFathersWork"] = ("Masterwork.App.Theme.MyFathersWork", "Masterwork.App.Theme.MyFathersWork.MyFathersWorkTheme", "ca.digitalghost.masterwork.app", "My Father's Work"),
        ["Test"] = ("Masterwork.App.Theme.Test", "Masterwork.App.Theme.Test.TestTheme", "ca.digitalghost.masterwork.testflavor", "Test Flavor"),
    };

    public static TheoryData<string, string> HostsAndFlavors()
    {
        var data = new TheoryData<string, string>();
        foreach (var host in Hosts())
        {
            foreach (var flavor in Flavors.Keys)
            {
                data.Add(host, flavor);
            }
        }
        return data;
    }

    public static TheoryData<string> AllHosts() => new(Hosts());

    // The MAUI head only has a target framework to evaluate on Windows once Android is skipped.
    private static IEnumerable<string> Hosts() => OperatingSystem.IsWindows()
        ? ["Masterwork.App", "Masterwork.App.Web.Client", "Masterwork.App.Web"]
        : ["Masterwork.App.Web.Client", "Masterwork.App.Web"];

    [Theory]
    [MemberData(nameof(HostsAndFlavors))]
    public void Flavor_ReferencesItsThemeAndStampsItsModule(string host, string flavor)
    {
        var expected = Flavors[flavor];

        var evaluation = Evaluate(host, flavor);

        Assert.Equal([expected.ThemeProject], ThemeReferences(evaluation));
        Assert.Equal([$"typeof({expected.ModuleType})"], ThemeModuleAttributes(evaluation));
    }

    [Theory]
    [InlineData("MyFathersWork")]
    [InlineData("Test")]
    public void Flavor_SetsTheMauiIdentity(string flavor)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var evaluation = Evaluate("Masterwork.App", flavor);

        Assert.Equal(Flavors[flavor].ApplicationId, Property(evaluation, "ApplicationId"));
        Assert.Equal(Flavors[flavor].ApplicationTitle, Property(evaluation, "ApplicationTitle"));
        Assert.NotEmpty(Items(evaluation, "MauiIcon"));
        Assert.NotEmpty(Items(evaluation, "MauiSplashScreen"));
    }

    [Theory]
    [MemberData(nameof(AllHosts))]
    public void NoFlavor_BuildsMyFathersWork(string host)
    {
        var implicitFlavor = Evaluate(host, flavor: null);
        var explicitFlavor = Evaluate(host, "MyFathersWork");

        Assert.Equal(ThemeReferences(explicitFlavor), ThemeReferences(implicitFlavor));
        Assert.Equal(ThemeModuleAttributes(explicitFlavor), ThemeModuleAttributes(implicitFlavor));
        Assert.Equal(Property(explicitFlavor, "ApplicationId"), Property(implicitFlavor, "ApplicationId"));
    }

    [Theory]
    [MemberData(nameof(AllHosts))]
    public void OnlyANonDefaultFlavor_GetsItsOwnOutputFolders(string host)
    {
        var defaultFlavor = Evaluate(host, "MyFathersWork");
        var testFlavor = Evaluate(host, "Test");

        // Some SDKs report these absolute, some relative; the trailing segments are what matter.
        Assert.EndsWith(@"\bin\", Normalize(Property(defaultFlavor, "BaseOutputPath")));
        Assert.EndsWith(@"\obj\", Normalize(Property(defaultFlavor, "BaseIntermediateOutputPath")));
        Assert.EndsWith(@"\bin\Test\", Normalize(Property(testFlavor, "BaseOutputPath")));
        Assert.EndsWith(@"\obj\Test\", Normalize(Property(testFlavor, "BaseIntermediateOutputPath")));

        static string Normalize(string? path) => @"\" + path!.Replace('/', '\\');
    }

    [Fact]
    public void TestFlavor_TitleMatchesItsThemeAppName() =>
        Assert.Equal(new Masterwork.App.Theme.Test.TestTheme().Config.AppName, Flavors["Test"].ApplicationTitle);

    private static IEnumerable<string> ThemeReferences(JsonElement evaluation) =>
        Items(evaluation, "ProjectReference")
            .Select(item => item.GetProperty("Filename").GetString()!)
            .Where(name => name.StartsWith("Masterwork.App.Theme.", StringComparison.Ordinal));

    private static IEnumerable<string> ThemeModuleAttributes(JsonElement evaluation) =>
        Items(evaluation, "AssemblyAttribute")
            .Where(item => item.GetProperty("Identity").GetString() == "Masterwork.App.Shared.Services.ThemeModuleAttribute")
            .Select(item => item.GetProperty("_Parameter1").GetString()!);

    private static string? Property(JsonElement evaluation, string name) =>
        evaluation.GetProperty("Properties").GetProperty(name).GetString();

    private static IEnumerable<JsonElement> Items(JsonElement evaluation, string type) =>
        evaluation.GetProperty("Items").TryGetProperty(type, out var items) ? items.EnumerateArray().ToList() : [];

    private static JsonElement Evaluate(string host, string? flavor)
    {
        var project = Path.Combine(SrcDirectory, host, $"{host}.csproj");
        var arguments = new List<string>
        {
            "msbuild", project, "-p:BuildAndroid=false",
            "-getProperty:ApplicationId", "-getProperty:ApplicationTitle",
            "-getProperty:BaseOutputPath", "-getProperty:BaseIntermediateOutputPath",
            "-getItem:ProjectReference", "-getItem:AssemblyAttribute", "-getItem:MauiIcon", "-getItem:MauiSplashScreen",
        };
        if (flavor is not null)
        {
            arguments.Add($"-p:WhiteLabelFlavor={flavor}");
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        arguments.ForEach(startInfo.ArgumentList.Add);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(EvaluationTimeout))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"Evaluating {host} ({flavor ?? "no flavor"}) did not finish within {EvaluationTimeout.TotalMinutes} minutes.");
        }

        Assert.True(process.ExitCode == 0, $"Evaluating {host} ({flavor ?? "no flavor"}) failed:\n{stdout.Result}\n{stderr.Result}");
        return JsonDocument.Parse(stdout.Result).RootElement.Clone();
    }

    private static string FindSrcDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Masterwork.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Masterwork.slnx not found above the test output directory.");
    }
}
