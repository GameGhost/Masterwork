using Masterwork.Extractor.Reconcile;

namespace Masterwork.Tests;

public sealed class ReconcilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mw-reconcile-test-" + Guid.NewGuid().ToString("n"));
    private string English => Path.Combine(_root, "en");
    private string French => Path.Combine(_root, "fr");
    private string Module => Path.Combine(_root, "module");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void Write(string dir, string relativePath, string content)
    {
        var path = Path.Combine(dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Passage(string id, string nodes) =>
        $"format: 'mws/0.4'\npassage_id: '{id}'\nlayout: 'narration'\nnodes:\n{nodes}";

    private static string Text(string key) => $"- type: 'text'\n  value: 'restext://{key}'\n";

    private const string Break = "- type: 'break'\n";

    private static string Link(string key, string target) =>
        $"- type: 'navigation'\n  label: 'restext://{key}'\n  target: '{target}'\n";

    /// <summary>Writes one passage per language plus each side's extraction restext; the live module gets the English restext.</summary>
    private void Scenario(string id, string englishNodes, string frenchNodes, string englishRestext, string frenchRestext, string? frenchId = null)
    {
        Write(English, $"passages/00001-{id}.mws.yaml", Passage(id, englishNodes));
        Write(English, "en-US.restext", englishRestext);
        Write(French, $"passages/00001-{frenchId ?? id}.mws.yaml", Passage(frenchId ?? id, frenchNodes));
        Write(French, "en-US.restext", frenchRestext);
        Write(Module, "en-US.restext", englishRestext);
    }

    private Reconciliation Run() => Reconciler.Run(English, French, Module);

    private static ReconciledKey Key(Reconciliation r, string key) => Assert.Single(r.Keys, k => k.Key == key);

    [Fact]
    public void MatchingTextNode_GetsOfficialFrench()
    {
        Scenario("Hall",
            Text("Hall_001"),
            Text("Hall_001"),
            "Hall_001=Welcome to the hall.\n",
            "Hall_001=Bienvenue dans la salle.\n");

        var key = Key(Run(), "Hall_001");

        Assert.Equal(KeyBucket.Official, key.Bucket);
        Assert.Equal("Bienvenue dans la salle.", key.French);
        Assert.Equal("Welcome to the hall.", key.English);
    }

    [Theory]
    [InlineData("Ceci fait trente caractères ok", KeyBucket.Official)]
    [InlineData("Ceci fait vingt-six car.", KeyBucket.Draft)]
    public void LengthRatio_MatchesTheTranslationChecksRange(string french, KeyBucket expected)
    {
        const string english = "This English sentence is forty chars ok.";
        Scenario("Hall", Text("Hall_001"), Text("Hall_001"), $"Hall_001={english}\n", $"Hall_001={french}\n");

        var key = Key(Run(), "Hall_001");

        Assert.Equal(expected, key.Bucket);
    }

    [Fact]
    public void Restext_WritesOriginAboveEachKey_AndOmitsKeysWithoutFrench()
    {
        Scenario("Hall",
            Text("Hall_001") + Break + Text("Hall_002") + Text("Hall_003"),
            Text("Hall_001") + Break + Text("Hall_002"),
            "# MasterWork locale file — en-US\n# Format: Key=Value\n\n# passages/00001-Hall.mws.yaml\nHall_001=Welcome to the hall.\nHall_002=Sit down,\nHall_003= please.\n",
            "Hall_001=Bienvenue dans la salle.\nHall_002=Asseyez-vous, je vous prie.\n");
        Write(Module, "en-US.overrides.restext", "# passages-override — hand-authored\nSetup_Title=Setup\n");

        var restext = ReconcileOutput.Restext(Run(), "fr-CA");

        Assert.Equal("""
            # Masterwork locale file — fr-CA
            # Format: Key=Value  (one string per line)
            # Reconciled from the official French script. Each key's "# origin:" is one of
            # official | official, revised | unity, revised | agent, or draft (official French awaiting the agent pass).

            # passages/00001-Hall.mws.yaml
            # origin: official
            Hall_001=Bienvenue dans la salle.
            # origin: draft
            Hall_002=Asseyez-vous, je vous prie.
            # origin: draft
            Hall_003=Asseyez-vous, je vous prie.

            """.ReplaceLineEndings("\n"), restext);
    }

    [Theory]
    [InlineData("{nameC} has won.", "{nameC}n'a pas gagné.", "{nameC} n'a pas gagné.")]
    [InlineData("Gain **15** coins.", "Gagnez **15**pièces.", "Gagnez **15** pièces.")]
    public void LostSpaceAfterPlaceholderOrBold_IsRestored_AndStaysOfficial(string english, string french, string expected)
    {
        Scenario("Hall", Text("Hall_001"), Text("Hall_001"), $"Hall_001={english}\n", $"Hall_001={french}\n");

        var key = Key(Run(), "Hall_001");

        Assert.Equal(KeyBucket.OfficialFixed, key.Bucket);
        Assert.Equal("official", key.Origin);
        Assert.Equal(expected, key.French);
    }

    [Fact]
    public void LostSpace_IsRestoredOnlyWhereTheEnglishHasOne()
    {
        Scenario("Hall", Text("Hall_001"), Text("Hall_001"),
            "Hall_001={n} coins and {n}s.\n", "Hall_001={n}pièces et {n}s.\n");

        Assert.Equal("{n} pièces et {n}s.", Key(Run(), "Hall_001").French);
    }

    [Fact]
    public void LinkLabelsWithDifferentKeyCounts_AreADraft()
    {
        Scenario("Hall",
            "- type: 'navigation'\n  label: '{a ? \"restext://Hall_001\" : \"restext://Hall_002\"}'\n  target: 'Attic'\n",
            Link("Hall_001", "Attic"),
            "Hall_001=Go up.\nHall_002=Climb.\n",
            "Hall_001=Montez.\n");

        Assert.Equal(KeyBucket.Draft, Key(Run(), "Hall_002").Bucket);
    }

    [Fact]
    public void DevNoteUses_DontVoteOnASharedKey()
    {
        WriteSharedKeyScenario("Continue", "Poursuivre", "Continuer");
        Write(English, "passages/00009-9Note.mws.yaml", Passage("9Note", Text("Common_Continue")));
        Write(French, "passages/00009-9Note.mws.yaml", Passage("9Note", Text("Common_001")));

        var key = Key(Run(), "Common_Continue");

        Assert.Equal(KeyBucket.Conflict, key.Bucket);
        Assert.Equal("Poursuivre", key.French);
    }

    [Fact]
    public void SharedKey_WordedDifferentlyAndMismatched_NamesBothReasons()
    {
        WriteSharedKeyScenario("{name} wins.", "{name} gagne.", "Victoire !", "Victoire !");

        var key = Key(Run(), "Common_Continue");

        Assert.Equal(KeyBucket.Draft, key.Bucket);
        Assert.Contains("differs between uses", key.Reason);
        Assert.Contains("placeholders", key.Reason);
    }

    [Fact]
    public void SharedKey_WithCaseVariants_TakesTheMajority()
    {
        WriteSharedKeyScenario("Continue", "Continuer", "continuer", "Continuer");

        var key = Key(Run(), "Common_Continue");

        Assert.Equal(KeyBucket.OfficialFixed, key.Bucket);
        Assert.Equal("Continuer", key.French);
    }

    [Fact]
    public void SharedKey_WithTiedCaseVariants_FollowsTheEnglishCapitalization()
    {
        WriteSharedKeyScenario("click here.", "Cliquez ici.", "cliquez ici.");

        Assert.Equal("cliquez ici.", Key(Run(), "Common_Continue").French);
    }

    [Fact]
    public void SharedKey_WithDifferentWording_GoesToTheAgentAsADraft()
    {
        WriteSharedKeyScenario("Continue", "Continuer", "Poursuivre", "Continuer");

        var key = Key(Run(), "Common_Continue");

        Assert.Equal(KeyBucket.Conflict, key.Bucket);
        Assert.Equal("draft", key.Origin);
        Assert.Equal("Continuer", key.French);
        Assert.Equal(["Continuer", "Poursuivre"], key.Candidates);
    }

    [Fact]
    public void SharedKey_LeftInEnglishAtOneUse_TakesTheFrench()
    {
        WriteSharedKeyScenario("none", "aucun", "none");

        var key = Key(Run(), "Common_Continue");

        Assert.Equal(KeyBucket.Official, key.Bucket);
        Assert.Equal("aucun", key.French);
    }

    /// <summary>One English key used in a passage per French text, each French passage giving it a different French key.</summary>
    private void WriteSharedKeyScenario(string english, params string[] french)
    {
        var frRestext = "";
        for (var i = 0; i < french.Length; i++)
        {
            var id = $"Room{i}";
            Write(English, $"passages/{i:D5}-{id}.mws.yaml", Passage(id, Text("Common_Continue")));
            Write(French, $"passages/{i:D5}-{id}.mws.yaml", Passage(id, Text($"Common_{i:D3}")));
            frRestext += $"Common_{i:D3}={french[i]}\n";
        }

        Write(English, "en-US.restext", $"Common_Continue={english}\n");
        Write(Module, "en-US.restext", $"Common_Continue={english}\n");
        Write(French, "en-US.restext", frRestext);
    }

    [Fact]
    public void PlaceholderMismatch_IsADraft()
    {
        Scenario("Hall", Text("Hall_001"), Text("Hall_001"), "Hall_001={name} wins.\n", "Hall_001=Victoire !\n");

        var key = Key(Run(), "Hall_001");

        Assert.Equal(KeyBucket.Draft, key.Bucket);
        Assert.Contains("placeholders", key.Reason);
    }

    [Fact]
    public void EnglishLeftInTheFrenchScript_GetsNoFrench()
    {
        Scenario("Hall", Text("Hall_001"), Text("Hall_001"), "Hall_001=Welcome home.\n", "Hall_001=Welcome home.\n");

        var result = Run();

        Assert.Equal(KeyBucket.EnglishInFrench, Key(result, "Hall_001").Bucket);
        Assert.DoesNotContain("Hall_001", ReconcileOutput.Restext(result, "fr-CA"));
    }

    [Fact]
    public void FragmentCountMismatch_IsADraftNamingTheSplitGroup()
    {
        Scenario("Hall",
            Text("Hall_001") + Text("Hall_002"),
            Text("Hall_001"),
            "Hall_001=Sit down,\nHall_002= please.\n",
            "Hall_001=Asseyez-vous, je vous prie.\n");

        var key = Key(Run(), "Hall_001");

        Assert.Equal(KeyBucket.Draft, key.Bucket);
        Assert.Equal("Asseyez-vous, je vous prie.", key.French);
        Assert.Contains("Hall_002", key.Reason);
    }

    [Fact]
    public void RenamedFrenchPassage_PairsWithItsEnglishId()
    {
        Scenario("Martia1Pre2", Text("Martia1Pre2_001"), Text("MartialPre2_001"),
            "Martia1Pre2_001=War is coming.\n", "MartialPre2_001=La guerre approche.\n", frenchId: "MartialPre2");

        var key = Key(Run(), "Martia1Pre2_001");

        Assert.Equal(KeyBucket.Official, key.Bucket);
        Assert.Equal("La guerre approche.", key.French);
    }

    [Fact]
    public void EnglishPassageWithoutFrench_LeavesItsKeysUnmatched()
    {
        Scenario("Hall", Text("Hall_001"), Text("Hall_001"), "Hall_001=Welcome.\nAttic_001=Dusty.\n", "Hall_001=Bienvenue.\n");
        Write(English, "passages/00002-Attic.mws.yaml", Passage("Attic", Text("Attic_001")));

        var result = Run();

        var key = Key(result, "Attic_001");
        Assert.Equal(KeyBucket.Unmatched, key.Bucket);
        Assert.Contains("no French", key.Reason);
        Assert.Equal(["Attic"], result.EnglishPassagesWithoutFrench);
    }

    [Fact]
    public void SwitchCases_PairByTheirMatchValue_NotTheirOrder()
    {
        static string Case(int match, string key) =>
            $"  - match: {match}\n    nodes:\n    - type: 'text'\n      value: 'restext://{key}'\n";
        const string Switch = "- type: 'switch'\n  on: 'roll'\n  cases:\n";
        Scenario("Hall",
            Switch + Case(1, "Hall_001") + Case(2, "Hall_002"),
            Switch + Case(2, "Hall_001") + Case(1, "Hall_002"),
            "Hall_001=One.\nHall_002=Two.\n",
            "Hall_001=Deux.\nHall_002=Un.\n");

        var result = Run();

        // The French lists the cases in the other order: only one can align, and it must be the same case.
        var one = Key(result, "Hall_001");
        var two = Key(result, "Hall_002");
        Assert.Contains(one.French, new[] { "Un.", null });
        Assert.Contains(two.French, new[] { "Deux.", null });
        Assert.True(one.French is not null || two.French is not null);
    }

    [Fact]
    public void DevNotePassageKeys_AreLeftOutOfOutputAndCoverage()
    {
        Scenario("Hall", Text("Hall_001"), Text("Hall_001"), "Hall_001=Welcome.\n_9note_001=Remember the tokens.\n", "Hall_001=Bienvenue.\n");
        Write(English, "passages/00002-9note.mws.yaml", Passage("9note", Text("_9note_001")));

        var result = Run();

        Assert.DoesNotContain(result.Keys, k => k.Key == "_9note_001");
        Assert.Equal(["_9note_001"], result.DevNoteKeys);
    }

    [Fact]
    public void PassageTaggedNote_IsADevNote()
    {
        Scenario("Hall", Text("Hall_001"), Text("Hall_001"), "Hall_001=Welcome.\nWampyre_001=Draft idea.\n", "Hall_001=Bienvenue.\n");
        Write(English, "passages/00002-Wampyre.mws.yaml",
            "format: 'mws/0.4'\npassage_id: 'Wampyre'\ntags:\n- 'NOTE'\nnodes:\n" + Text("Wampyre_001"));

        Assert.Equal(["Wampyre_001"], Run().DevNoteKeys);
    }

    [Theory]
    [InlineData("VariableList-ATOW")]
    [InlineData("Variable List FnP")]
    [InlineData("NoteFactions")]
    [InlineData("HUBExample")]
    public void DevNotePassageNames_AreRecognized(string id) => Assert.True(Reconciler.IsDevNotePassage(id));

    [Fact]
    public void PlaceholderOnlyText_IdenticalInBothLanguages_IsOfficial()
    {
        Scenario("Hall", Text("Hall_001"), Text("Hall_001"), "Hall_001={nameB}.\n", "Hall_001={nameB}.\n");

        Assert.Equal(KeyBucket.Official, Key(Run(), "Hall_001").Bucket);
    }

    [Fact]
    public void FrenchOnlyText_IsReportedWithTheEnglishOnEitherSide()
    {
        Scenario("Hall",
            Text("Hall_001") + Link("Hall_002", "Attic"),
            Text("Hall_001") + Link("Hall_002", "Attic") + Text("Hall_003"),
            "Hall_001=Welcome.\nHall_002=Go upstairs.\n",
            "Hall_001=Bienvenue.\nHall_002=Montez.\nHall_003=Le grenier est poussiéreux.\n");

        var result = Run();

        var text = Assert.Single(result.FrenchOnly);
        Assert.Equal(new FrenchOnlyText("Hall", "Go upstairs.", "Le grenier est poussiéreux.", null), text);
        Assert.Contains("Le grenier est poussiéreux.", ReconcileOutput.Report(result, "fr-CA", "Test"));
    }

    [Fact]
    public void Report_CountsBuckets_AndListsKeysThatArentPlainOfficial()
    {
        Scenario("Hall",
            Text("Hall_001") + Break + Text("Hall_002"),
            Text("Hall_001") + Break + Text("Hall_002"),
            "Hall_001=Welcome.\nHall_002=Welcome home.\n",
            "Hall_001=Bienvenue.\nHall_002=Welcome home.\n");

        var report = ReconcileOutput.Report(Run(), "fr-CA", "Test");

        Assert.Contains("| Official | 1 | 50.0% |", report);
        Assert.Contains("| No French: English left in the French script | 1 | 50.0% |", report);
        Assert.Contains("| `Hall_002` | Welcome home. |", report);
        Assert.DoesNotContain("| `Hall_001` |", report);
    }

    [Fact]
    public void OverrideKey_HasNoSource_AndIsNotWritten()
    {
        Scenario("Hall", Text("Hall_001"), Text("Hall_001"), "Hall_001=Welcome.\n", "Hall_001=Bienvenue.\n");
        Write(Module, "en-US.overrides.restext", "Setup_Title=Setup\n");

        var result = Run();

        Assert.Equal(KeyBucket.NoSource, Key(result, "Setup_Title").Bucket);
        Assert.DoesNotContain("Setup_Title", ReconcileOutput.Restext(result, "fr-CA"));
    }
}
