using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public sealed class ModuleLogicLintTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mw-lint-test-" + Guid.NewGuid().ToString("n"));
    private readonly string _module;

    public ModuleLogicLintTests()
    {
        _module = Path.Combine(_root, "module");
        Directory.CreateDirectory(Path.Combine(_module, "passages"));
        File.WriteAllText(Path.Combine(_module, "manifest.yaml"), "id: 'test.module'\nversion: '1.0.0'\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_module, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Passage(string id, string nodes) =>
        $"format: 'mws/0.6'\npassage_id: '{id}'\nlayout: 'hub'\nnodes:\n{nodes}";

    private IReadOnlyList<LogicLintFinding> Check(params string[] packs) => ModuleLogicLint.Check(_module, packs);

    [Fact]
    public void LiteralInIf_EqualToRestextValue_Fails()
    {
        Write("en-US.restext", "Choice_Yes=yes\n");
        Write("passages/Seedy.mws.yaml", Passage("Seedy", """
            - type: 'conditional'
              branches:
              - if: 'seedy == "yes"'
                nodes: []
            """));

        var finding = Assert.Single(Check());

        Assert.Contains("\"yes\"", finding.Message);
        Assert.Contains("Choice_Yes", finding.Message);
        Assert.Contains("Seedy", finding.Message);
    }

    [Fact]
    public void LiteralComparedByKey_Passes()
    {
        Write("en-US.restext", "Choice_Yes=yes\n");
        Write("passages/Seedy.mws.yaml", Passage("Seedy", """
            - type: 'assign'
              var: 'seedy'
              expr: '"restext://Choice_Yes"'
            - type: 'conditional'
              branches:
              - if: 'seedy == "restext://Choice_Yes" && mood != "calm"'
                nodes: []
            """));

        Assert.Empty(Check());
    }

    [Fact]
    public void LiteralEqualToAnotherLocalesValue_Fails()
    {
        Write("en-US.restext", "Choice_Yes=yes\n");
        Write("fr-CA.restext", "Choice_Yes=oui\n");
        Write("passages/Seedy.mws.yaml", Passage("Seedy", """
            - type: 'assign'
              var: 'seedy'
              expr: '"oui"'
            """));

        var finding = Assert.Single(Check());

        Assert.Contains("Choice_Yes (fr-CA)", finding.Message);
    }

    [Fact]
    public void LiteralEqualToAnAssetPacksValue_Fails()
    {
        var pack = Path.Combine(_root, "pack");
        Directory.CreateDirectory(pack);
        File.WriteAllText(Path.Combine(pack, "en-US.restext"), "Pack_Faction=Separatists\n");
        Write("passages/War.mws.yaml", Passage("War", """
            - type: 'link'
              label: 'Go'
              target: '${warwinner == "Separatists" ? "A" : "B"}'
            """));

        Assert.Empty(Check());
        Assert.Contains("Pack_Faction", Assert.Single(Check(pack)).Message);
    }

    [Fact]
    public void PlainTargetAndMatchValues_AreCompared()
    {
        Write("en-US.restext", "Focus_Biology=Biology\nPlace_Hospital=Hospital\n");
        Write("passages/Focus.mws.yaml", Passage("Focus", """
            - type: 'switch'
              on: 'focus'
              cases:
              - match: ['Chemistry', '=Biology']
                nodes: []
            - type: 'link'
              label: 'Go'
              target: 'Hospital'
            """));

        var findings = Check();

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Message.Contains("Focus_Biology"));
        Assert.Contains(findings, f => f.Message.Contains("Place_Hospital"));
    }

    [Fact]
    public void NumericMatchPatterns_AreNotCompared()
    {
        Write("en-US.restext", "Countdown_Two=2\nCountdown_Four=4\n");
        Write("passages/Count.mws.yaml", Passage("Count", """
            - type: 'switch'
              on: 'players'
              cases:
              - match: 2
                nodes: []
              - match: '>=4'
                nodes: []
            """));

        Assert.Empty(Check());
    }

    [Fact]
    public void BasePassageReplacedByAnOverride_IsNotLinted()
    {
        Write("en-US.restext", "Choice_Yes=yes\n");
        Write("passages/Seedy.mws.yaml", Passage("Seedy", """
            - type: 'assign'
              var: 'seedy'
              expr: '"yes"'
            """));
        Write("passages-override/Seedy.mws.yaml", "---\n" + Passage("Seedy", """
            - type: 'assign'
              var: 'seedy'
              expr: '"restext://Choice_Yes"'
            """));

        Assert.Empty(Check());
    }

    [Fact]
    public void PassagesAreReadFromTheManifestsPassageFolders()
    {
        Write("manifest.yaml", "id: 'test.module'\nversion: '1.0.0'\npassages: 'base'\npassages_override: 'mine'\n");
        Write("en-US.restext", "Choice_Yes=yes\n");
        Write("passages/Stale.mws.yaml", Passage("Stale", "- type: 'assign'\n  var: 'a'\n  expr: '\"yes\"'\n"));
        Write("base/Seedy.mws.yaml", Passage("Seedy", "- type: 'assign'\n  var: 'a'\n  expr: '\"yes\"'\n"));
        Write("mine/Seedy.mws.yaml", Passage("Seedy", "- type: 'assign'\n  var: 'a'\n  expr: '\"restext://Choice_Yes\"'\n"));
        Write("base/Other.mws.yaml", Passage("Other", "- type: 'assign'\n  var: 'b'\n  expr: '\"yes\"'\n"));

        Assert.Contains("(Other)", Assert.Single(Check()).Message);
    }

    [Fact]
    public void KeysSharingDefaultValue_ThatDifferInAnotherLocale_Fail()
    {
        Write("en-US.restext", "Setup_Number_Two=two\nNumber_Two_Lower=two\n");
        Write("fr-CA.restext", "Setup_Number_Two=Deux\nNumber_Two_Lower=deux\n");
        Write("passages/Count.mws.yaml", Passage("Count", """
            - type: 'assign'
              var: 'n'
              expr: '"restext://Setup_Number_Two"'
            - type: 'conditional'
              branches:
              - if: 'n == "restext://Number_Two_Lower"'
                nodes: []
            """));

        var finding = Assert.Single(Check());

        Assert.Contains("Number_Two_Lower and Setup_Number_Two", finding.Message);
        Assert.Contains("fr-CA", finding.Message);
        Assert.Contains("['Number_Two_Lower', 'Setup_Number_Two']", finding.Message);
    }

    [Fact]
    public void KeysSharingAValueInEveryLocale_Pass()
    {
        Write("en-US.restext", "Setup_Number_Two=two\nNumber_Two_Lower=two\n");
        Write("fr-CA.restext", "Setup_Number_Two=deux\n");
        Write("fr-CA.overrides.restext", "Number_Two_Lower=deux\n");
        Write("passages/Count.mws.yaml", Passage("Count", """
            - type: 'conditional'
              branches:
              - if: 'n == "restext://Setup_Number_Two" || n == "restext://Number_Two_Lower"'
                nodes: []
            """));

        Assert.Empty(Check());
    }

    [Fact]
    public void Allowlist_ExemptsLiteralsAndKeyPairs()
    {
        Write("en-US.restext", "Town_Default=Transylvania\nSetup_Number_Two=two\nNumber_Two_Lower=two\n");
        Write("fr-CA.restext", "Town_Default=Transylvanie\nSetup_Number_Two=Deux\nNumber_Two_Lower=deux\n");
        Write("passages/Town.mws.yaml", Passage("Town", """
            - type: 'conditional'
              branches:
              - if: 'townname == "Transylvania"'
                nodes: []
              - if: 'n == "restext://Setup_Number_Two" || n == "restext://Number_Two_Lower"'
                nodes: []
            """));
        Assert.Equal(2, Check().Count);

        var literalFinding = Check().Single(f => f.Message.Contains("Transylvania"));
        Assert.Contains(ModuleLogicLint.AllowlistFileName, literalFinding.Message);
        Assert.Contains("- 'Transylvania'", literalFinding.Message);

        Write(ModuleLogicLint.AllowlistFileName, """
            literals:
            - 'Transylvania'
            shared_keys:
            - ['Setup_Number_Two', 'Number_Two_Lower']
            """);

        Assert.Empty(Check());
    }
}
