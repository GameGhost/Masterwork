using System.Text;
using System.Text.Json;
using Masterwork.ModulePacker.Translation;
using Masterwork.Translation;

namespace Masterwork.Tests;

public sealed class TranslationCheckCommandTests : IDisposable
{
    private const char Nbsp = '\u00A0';

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mw-trcheck-test-" + Guid.NewGuid().ToString("n"));

    public TranslationCheckCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string Read(string name) => File.ReadAllText(Path.Combine(_root, name));

    private (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = TranslationCheckCommand.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private static JsonElement StringEntry(JsonDocument json, string key) =>
        json.RootElement.GetProperty("strings").EnumerateArray().Single(s => s.GetProperty("key").GetString() == key);

    private static string[] Checks(JsonElement entry, string list) =>
        entry.GetProperty(list).EnumerateArray().Select(f => f.GetProperty("check").GetString()!).ToArray();

    [Fact]
    public void CleanRestextPair_Passes()
    {
        var en = Write("en-US.restext", "# Format: Key=Value\nGo=Continue\nGain=Gain {n} tokens.\n");
        var fr = Write("fr-CA.restext", "# origin: official\nGo=Continuer\nGain=Gagnez {n} jetons.\n");

        var (exit, output, _) = Run(en, fr);

        Assert.Equal(TranslationCheckCommand.ExitPassed, exit);
        Assert.Contains("Checked 2 fr-CA strings", output);
        Assert.Contains("PASS", output);
    }

    [Fact]
    public void HardFailure_ExitsNonZeroAndNamesTheKey()
    {
        var en = Write("en-US.restext", "Gain=Gain {n} tokens.\n");
        var fr = Write("fr-CA.restext", "Gain=Gagnez des jetons.\n");

        var (exit, output, _) = Run(en, fr);

        Assert.Equal(TranslationCheckCommand.ExitFailed, exit);
        Assert.Contains("Gain: placeholders", output);
        Assert.Contains("FAIL", output);
    }

    [Fact]
    public void FlagsAlone_StillPass()
    {
        var en = Write("en-US.restext", "Name=Advanced Weaponry\n");
        var fr = Write("fr-CA.restext", "Name=Advanced Weaponry\n");

        var (exit, output, _) = Run(en, fr);

        Assert.Equal(TranslationCheckCommand.ExitPassed, exit);
        Assert.Contains("Name: identical-to-source", output);
    }

    [Fact]
    public void Json_ListsEveryStringWithItsFindings()
    {
        var en = Write("en-US.restext", "Go=Continue\nGain=Gain {n} tokens.\nMissing=Only in English\n");
        var fr = Write("fr-CA.restext", "Go=Continuer\nGain=Gagnez des jetons.\nStale=Seulement en français\n");
        var jsonPath = Path.Combine(_root, "report.json");

        Run(en, fr, "--json", jsonPath);

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.Equal("restext", json.RootElement.GetProperty("kind").GetString());
        Assert.False(json.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(4, json.RootElement.GetProperty("counts").GetProperty("strings").GetInt32());
        Assert.Empty(Checks(StringEntry(json, "Go"), "failures"));
        Assert.Equal([TranslationChecks.Placeholders], Checks(StringEntry(json, "Gain"), "failures"));
        Assert.Equal("Gagnez des jetons.", StringEntry(json, "Gain").GetProperty("target").GetString());
        Assert.Equal([TranslationChecks.Untranslated], Checks(StringEntry(json, "Missing"), "flags"));
        Assert.Equal(JsonValueKind.Null, StringEntry(json, "Missing").GetProperty("target").ValueKind);
        Assert.Equal([TranslationChecks.UnknownKey], Checks(StringEntry(json, "Stale"), "failures"));
    }

    [Fact]
    public void Json_NamesTheLocaleAndFiles()
    {
        var en = Write("en-US.restext", "Go=Continue\n");
        var de = Write("de-DE.restext", "Go=Weiter\n");
        var jsonPath = Path.Combine(_root, "report.json");

        Run(en, de, "--json", jsonPath);

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.Equal("de-DE", json.RootElement.GetProperty("locale").GetString());
        Assert.Equal(en, json.RootElement.GetProperty("sourceFile").GetString());
        Assert.Equal(de, json.RootElement.GetProperty("targetFile").GetString());
        Assert.Equal("Continue", StringEntry(json, "Go").GetProperty("source").GetString());
        Assert.Equal("Weiter", StringEntry(json, "Go").GetProperty("target").GetString());
    }

    [Fact]
    public void LocaleOption_OverridesTheFileName()
    {
        var en = Write("en-US.restext", "Ask=Ready?\n");
        var target = Write("translation.restext", "Ask=Prêt ?\n");

        Assert.Equal(TranslationCheckCommand.ExitPassed, Run(en, target, "--locale", "de-DE").Exit);
        Assert.Equal(TranslationCheckCommand.ExitFailed, Run(en, target, "--locale", "fr-CA").Exit);
    }

    [Fact]
    public void UnknownLocale_IsAUsageError()
    {
        var en = Write("en-US.restext", "A=a\n");
        var target = Write("translation.restext", "A=a\n");

        var (exit, _, err) = Run(en, target);

        Assert.Equal(TranslationCheckCommand.ExitUsage, exit);
        Assert.Contains("--locale", err);
    }

    [Fact]
    public void Fix_ComposesDecomposedCharacters()
    {
        var en = Write("en-US.restext", "Title=Opera\n");
        var de = Write("de-DE.restext", "Title=Ope\u0301ra\n");

        var (exit, _, _) = Run(en, de, "--fix");

        Assert.Equal(TranslationCheckCommand.ExitPassed, exit);
        Assert.Equal("Title=Opéra\n", Read("de-DE.restext"));
    }

    [Fact]
    public void JsonDash_WritesJsonToStdoutAndSummaryToStderr()
    {
        var en = Write("en-US.restext", "Go=Continue\n");
        var fr = Write("fr-CA.restext", "Go=Continuer\n");

        var (_, output, err) = Run(en, fr, "--json", "-");

        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.GetProperty("passed").GetBoolean());
        Assert.Contains("PASS", err);
    }

    [Fact]
    public void Fix_RewritesOnlySpacingAndKeepsOriginComments()
    {
        var en = Write("en-US.restext", "Ask=Ready?\r\nNote=Note: read\r\nGo=Continue\r\n");
        var fr = Write("fr-CA.restext", "# origin: official\r\nAsk=Prêt ?\r\n# origin: agent\r\nNote=Note: lire\r\nGo=Continuer\r\n");

        var (exit, output, _) = Run(en, fr, "--fix");

        Assert.Equal(TranslationCheckCommand.ExitPassed, exit);
        Assert.Equal($"# origin: official\r\nAsk=Prêt?\r\n# origin: agent\r\nNote=Note{Nbsp}: lire\r\nGo=Continuer\r\n", Read("fr-CA.restext"));
        Assert.Contains("Fixed 2 strings", output);
    }

    [Fact]
    public void Fix_ReportsWhichStringsItChanged()
    {
        var en = Write("en-US.restext", "Ask=Ready?\nGo=Continue\n");
        var fr = Write("fr-CA.restext", "Ask=Prêt ?\nGo=Continuer\n");
        var jsonPath = Path.Combine(_root, "report.json");

        Run(en, fr, "--fix", "--json", jsonPath);

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.True(StringEntry(json, "Ask").GetProperty("fixed").GetBoolean());
        Assert.False(StringEntry(json, "Go").GetProperty("fixed").GetBoolean());
    }

    [Fact]
    public void WithoutFix_TheFrenchFileIsNotTouched()
    {
        var en = Write("en-US.restext", "Ask=Ready?\n");
        var fr = Write("fr-CA.restext", "Ask=Prêt ?\n");

        var (exit, _, _) = Run(en, fr);

        Assert.Equal(TranslationCheckCommand.ExitFailed, exit);
        Assert.Equal("Ask=Prêt ?\n", Read("fr-CA.restext"));
    }

    [Fact]
    public void ValueBrokenAcrossLines_IsAFileProblem()
    {
        var en = Write("en-US.restext", "Long=One line\n");
        var fr = Write("fr-CA.restext", "Long=Une\nligne\n");

        var (exit, output, _) = Run(en, fr);

        Assert.Equal(TranslationCheckCommand.ExitFailed, exit);
        Assert.Contains("line 2", output);
    }

    [Fact]
    public void TermBases_AreMergedAndApplied()
    {
        var en = Write("en-US.restext", "A=End of Round\nB=Your Masterwork\n");
        var fr = Write("fr-CA.restext", "A=Fin du tour\nB=Votre Chef-d'œuvre\n");
        var shared = Write("terms.yaml", "terms:\n  - source: '\\bRounds?\\b'\n    target: 'manche'\n    note: 'tour is a player turn'\n");
        var scenario = Write("scenario-terms.yaml", "forbidden:\n  - 'Chef-d.œuvre'\n");
        var jsonPath = Path.Combine(_root, "report.json");

        var (exit, _, _) = Run(en, fr, "--termbase", shared, "--termbase", scenario, "--json", jsonPath);

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.Equal(TranslationCheckCommand.ExitFailed, exit);
        Assert.Equal([TranslationChecks.TermMissing], Checks(StringEntry(json, "A"), "flags"));
        Assert.Equal([TranslationChecks.ForbiddenTerm], Checks(StringEntry(json, "B"), "failures"));
    }

    [Fact]
    public void InvalidTermBasePattern_IsAUsageError()
    {
        var en = Write("en-US.restext", "A=a\n");
        var fr = Write("fr-CA.restext", "A=a\n");
        var terms = Write("terms.yaml", "forbidden:\n  - '(unclosed'\n");

        var (exit, _, err) = Run(en, fr, "--termbase", terms);

        Assert.Equal(TranslationCheckCommand.ExitUsage, exit);
        Assert.Contains("terms.yaml", err);
        Assert.Contains("(unclosed", err);
    }

    [Theory]
    [InlineData("en-US.restext")]
    [InlineData("en-US.restext", "fr-CA.resx")]
    [InlineData("en-US.txt", "fr-CA.txt")]
    [InlineData("en-US.restext", "fr-CA.restext", "--bogus")]
    public void BadArguments_AreAUsageError(params string[] names)
    {
        var args = names.Select(n => n.StartsWith("--") ? n : Write(n, "A=a\n")).ToArray();

        var (exit, _, err) = Run(args);

        Assert.Equal(TranslationCheckCommand.ExitUsage, exit);
        Assert.Contains("checktranslation", err);
    }

    private const string ResxHeader = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<root>\r\n  <resheader name=\"resmimetype\"><value>text/microsoft-resx</value></resheader>\r\n";

    private static string Resx(params (string Name, string Value)[] entries) =>
        ResxHeader
        + string.Concat(entries.Select(e => $"  <data name=\"{e.Name}\" xml:space=\"preserve\"><value>{e.Value}</value></data>\r\n"))
        + "  <data name=\"Icon\" type=\"System.Byte[], mscorlib\" mimetype=\"application/x-microsoft.net.object.bytearray.base64\"><value>AAAA</value></data>\r\n"
        + "</root>\r\n";

    [Fact]
    public void ResxPair_ChecksDecodedValuesIncludingHtmlTags()
    {
        var en = Write("AppStrings.resx", Resx(("Tagline", "A companion for &lt;em&gt;{0}&lt;/em&gt;."), ("Ok", "OK")));
        var fr = Write("AppStrings.fr-CA.resx", Resx(("Tagline", "Un compagnon pour {0}."), ("Ok", "OK")));
        var jsonPath = Path.Combine(_root, "report.json");

        var (exit, _, _) = Run(en, fr, "--json", jsonPath);

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.Equal(TranslationCheckCommand.ExitFailed, exit);
        Assert.Equal("resx", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("counts").GetProperty("strings").GetInt32());
        Assert.Equal([TranslationChecks.HtmlTags], Checks(StringEntry(json, "Tagline"), "failures"));
    }

    [Fact]
    public void ResxFix_RewritesOnlyTheChangedValues()
    {
        var en = Write("AppStrings.resx", Resx(("Ask", "Ready?"), ("Tagline", "For &lt;em&gt;{0}&lt;/em&gt;: hi")));
        var fr = Write("AppStrings.fr-CA.resx", Resx(("Ask", "Prêt ?"), ("Tagline", "Pour &lt;em&gt;{0}&lt;/em&gt;: salut")));

        var (exit, _, _) = Run(en, fr, "--fix");

        Assert.Equal(TranslationCheckCommand.ExitPassed, exit);
        Assert.Equal(Resx(("Ask", "Prêt?"), ("Tagline", $"Pour &lt;em&gt;{{0}}&lt;/em&gt;{Nbsp}: salut")), Read("AppStrings.fr-CA.resx"));
    }

    [Fact]
    public void Fix_KeepsAByteOrderMark()
    {
        var en = Write("en-US.restext", "Ask=Ready?\n");
        var frPath = Path.Combine(_root, "fr-CA.restext");
        File.WriteAllText(frPath, "Ask=Prêt ?\n", new UTF8Encoding(true));

        Run(en, frPath, "--fix");

        var bytes = File.ReadAllBytes(frPath);
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal("Ask=Prêt?\n", new UTF8Encoding(false).GetString(bytes.AsSpan(3)));
    }
}
