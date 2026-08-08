using Masterwork.Extractor;

namespace Masterwork.Tests;

public class RestextCollectorTests
{
    [Fact]
    public void CollectPassage_HyphenatedPassageId_KeyIsSanitized()
    {
        var collector = new RestextCollector();
        var dict = new Dictionary<string, object?>
        {
            ["nodes"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "text", ["value"] = "Hello there" },
            },
        };

        collector.CollectPassage("TCOD-TownName", "000-TCOD-TownName.mws.yaml", dict);

        var entries = Assert.Single(collector.Passages).Entries;
        var entry = Assert.Single(entries);
        Assert.Equal("TCOD_TownName_001", entry.Key);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", entry.Key);
    }

    [Fact]
    public void CollectPassage_SpacedPassageId_KeyIsSanitized()
    {
        var collector = new RestextCollector();
        var dict = new Dictionary<string, object?>
        {
            ["nodes"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "text", ["value"] = "Hello there" },
            },
        };

        collector.CollectPassage("TITLE SCREEN", "000-TITLE-SCREEN.mws.yaml", dict);

        var entries = Assert.Single(collector.Passages).Entries;
        var entry = Assert.Single(entries);
        Assert.Equal("TITLE_SCREEN_001", entry.Key);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", entry.Key);
    }

    [Fact]
    public void CollectPassage_SanitizedKey_RoundTripsThroughDictAsRestextRef()
    {
        var collector = new RestextCollector();
        var dict = new Dictionary<string, object?>
        {
            ["nodes"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "text", ["value"] = "Hello there" },
            },
        };

        collector.CollectPassage("Preparations-TCOD", "000-Preparations-TCOD.mws.yaml", dict);

        var nodes = (List<Dictionary<string, object?>>)dict["nodes"]!;
        Assert.Equal("restext://Preparations_TCOD_001", nodes[0]["value"]);
    }

    [Fact]
    public void CollectPassage_TitleAndSubtitle_ExtractedAsRestextRefs()
    {
        var collector = new RestextCollector();
        var dict = new Dictionary<string, object?>
        {
            ["title"] = "YELLOW FEVER",
            ["subtitle"] = "Early Years",
            ["nodes"] = new List<Dictionary<string, object?>>(),
        };

        collector.CollectPassage("Fever1", "000-Fever1.mws.yaml", dict);

        Assert.Equal("restext://Fever1_001", dict["title"]);
        Assert.Equal("restext://Fever1_002", dict["subtitle"]);
        var entries = Assert.Single(collector.Passages).Entries;
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Key == "Fever1_001" && e.Value == "YELLOW FEVER");
        Assert.Contains(entries, e => e.Key == "Fever1_002" && e.Value == "Early Years");
    }

    [Fact]
    public void CollectPassage_TernaryTitle_ExtractsEachBranchAsOwnRestextRef()
    {
        // CradleExtractor.TryBuildTernaryHeading collapses several branches' own headings into one
        // computed title — each branch's own text needs its own restext key, since it's player-
        // facing text, embedded inside the ternary's "?"/":" syntax rather than as the whole field.
        var collector = new RestextCollector();
        var dict = new Dictionary<string, object?>
        {
            ["title"] = "{gunsbonus == 1 ? \"Knowledge Bonus\" : gunsbonus == 2 ? \"Ingredient Bonus\" : \"Wealth Bonus\"}",
            ["nodes"] = new List<Dictionary<string, object?>>(),
        };

        collector.CollectPassage("SeedGUNS", "000-SeedGUNS.mws.yaml", dict);

        Assert.Equal(
            "{gunsbonus == 1 ? \"restext://SeedGUNS_001\" : gunsbonus == 2 ? \"restext://SeedGUNS_002\" : \"restext://SeedGUNS_003\"}",
            dict["title"]);
        var entries = Assert.Single(collector.Passages).Entries;
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Key == "SeedGUNS_001" && e.Value == "Knowledge Bonus");
        Assert.Contains(entries, e => e.Key == "SeedGUNS_002" && e.Value == "Ingredient Bonus");
        Assert.Contains(entries, e => e.Key == "SeedGUNS_003" && e.Value == "Wealth Bonus");
    }

    [Fact]
    public void CollectPassage_TernaryTitle_DoesNotExtractConditionComparandLiterals()
    {
        // Regression: Cost of Disease's Gen1CreepyYes — "{hunters == "evil" ? "Agreed" : "Agreed"}"
        // — an earlier version of the extraction regex matched EVERY quoted literal in the ternary
        // regardless of position, wrongly extracting the CONDITION's own comparand ("evil", an
        // internal state token being compared against `hunters`, not player-facing text) into its
        // own restext key too — silently corrupting the condition itself (comparing against a
        // possibly-translated string instead of the untranslated state value it was written
        // against), not just adding an unwanted extra entry.
        var collector = new RestextCollector();
        var dict = new Dictionary<string, object?>
        {
            ["title"] = "{hunters == \"evil\" ? \"Agreed\" : \"Agreed\"}",
            ["nodes"] = new List<Dictionary<string, object?>>(),
        };

        collector.CollectPassage("Gen1CreepyYes", "000-Gen1CreepyYes.mws.yaml", dict);

        Assert.Equal(
            "{hunters == \"evil\" ? \"restext://Gen1CreepyYes_001\" : \"restext://Gen1CreepyYes_001\"}",
            dict["title"]);
        var entries = Assert.Single(collector.Passages).Entries;
        var entry = Assert.Single(entries);
        Assert.Equal("Gen1CreepyYes_001", entry.Key);
        Assert.Equal("Agreed", entry.Value);
    }

    [Fact]
    public void CollectPassage_TernaryTitle_ExtractsValueEvenWhenItCoincidesWithARealPassageId()
    {
        // Regression: Fear of the Unknown's PEWitch3 — one branch's own heading text is "Isolation",
        // which ALSO happens to be the passage_id of a completely unrelated passage elsewhere in the
        // module. The shuffled-array extraction path defensively skips a literal matching a known
        // passage id (ambiguous there — an array element genuinely could be a navigation string),
        // but a title ternary's VALUE always comes from TryHoistHeadingTitleSubtitle's bold-heading
        // extraction — real narrative text, never a passage id by construction — so that exclusion
        // must NOT apply here, or a coincidental word match silently leaves real player-facing text
        // un-restexted.
        var collector = new RestextCollector(passageIds: ["Isolation", "PEWitch3"]);
        var dict = new Dictionary<string, object?>
        {
            ["title"] = "{path4 == \"PEIsolation\" ? \"Isolation\" : \"Something Else\"}",
            ["nodes"] = new List<Dictionary<string, object?>>(),
        };

        collector.CollectPassage("PEWitch3", "000-PEWitch3.mws.yaml", dict);

        Assert.Equal(
            "{path4 == \"PEIsolation\" ? \"restext://PEWitch3_001\" : \"restext://PEWitch3_002\"}",
            dict["title"]);
        var entries = Assert.Single(collector.Passages).Entries;
        Assert.Contains(entries, e => e.Key == "PEWitch3_001" && e.Value == "Isolation");
        Assert.Contains(entries, e => e.Key == "PEWitch3_002" && e.Value == "Something Else");
    }

    private static Dictionary<string, object?> TextPassageDict(string value) => new()
    {
        ["nodes"] = new List<Dictionary<string, object?>>
        {
            new() { ["type"] = "text", ["value"] = value },
        },
    };

    [Fact]
    public void BuildRenameMap_CuratedIdMatchesCommonValue_UsesCuratedIdInsteadOfAutoGenerated()
    {
        var curated = new Dictionary<string, string> { ["Common_Continue"] = "Continue" };
        var collector = new RestextCollector(curatedRestext: curated);

        collector.CollectPassage("P1", "000-P1.mws.yaml", TextPassageDict("Continue"));
        collector.CollectPassage("P2", "001-P2.mws.yaml", TextPassageDict("Continue"));

        var renames = collector.BuildRenameMap();

        Assert.Equal("Common_Continue", Assert.Single(renames).Value);
    }

    [Fact]
    public void BuildRenameMap_NoCuratedMatch_FallsBackToAutoGeneratedCommonId()
    {
        var curated = new Dictionary<string, string> { ["Common_Continue"] = "Continue" };
        var collector = new RestextCollector(curatedRestext: curated);

        collector.CollectPassage("P1", "000-P1.mws.yaml", TextPassageDict("Hello there"));
        collector.CollectPassage("P2", "001-P2.mws.yaml", TextPassageDict("Hello there"));

        var renames = collector.BuildRenameMap();

        Assert.Equal("Common_001", Assert.Single(renames).Value);
    }

    [Fact]
    public void BuildRenameMap_ValueUsedOnlyOnce_NotPromotedEvenWithCuratedMatch()
    {
        var curated = new Dictionary<string, string> { ["Common_Continue"] = "Continue" };
        var collector = new RestextCollector(curatedRestext: curated);

        collector.CollectPassage("P1", "000-P1.mws.yaml", TextPassageDict("Continue"));

        var renames = collector.BuildRenameMap();

        Assert.Empty(renames);
    }

    [Fact]
    public void ReportUnusedCuratedIds_UnmatchedCuratedId_WarnsAndOmitsFromRenameMap()
    {
        var curated = new Dictionary<string, string> { ["Common_NeverSeen"] = "Some text nobody wrote" };
        var collector = new RestextCollector(curatedRestext: curated);

        collector.CollectPassage("P1", "000-P1.mws.yaml", TextPassageDict("Hello there"));
        collector.CollectPassage("P2", "001-P2.mws.yaml", TextPassageDict("Hello there"));
        collector.BuildRenameMap();

        var report = new ExtractionReport();
        collector.ReportUnusedCuratedIds(report);

        var tempPath = Path.GetTempFileName();
        try
        {
            report.Write(tempPath);
            var written = File.ReadAllText(tempPath);
            Assert.Contains("Common_NeverSeen", written);
            Assert.Contains("Some text nobody wrote", written);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void RestoreNonTemplateAssignments_ShuffledChoicesConsumedByBareVarTextNode_StayRestextified()
    {
        // Regression: a let node's shuffled-array choices are provisionally restext-extracted, then
        // reverted by RestoreNonTemplateAssignments unless the let var appears as a {varName}
        // placeholder in some ALREADY-EXTRACTED restext entry. But a text node whose value is
        // EXACTLY '{tempVar}' (no surrounding static text) never gets its own entry — WalkTextNode
        // deliberately skips bare single-var values, there being nothing of its own to translate —
        // so the var was invisible to that check even though it's plainly displayed. Real-world
        // bug: Fear of the Unknown's JunkPalaceSignIn and A Time of War's BattleTime, both
        // text(macros1.either(...)) — the picked choice is shown via a bare {tempVar} text node,
        // and every one of the either() choices silently reverted to raw literals.
        var collector = new RestextCollector();
        var dict = new Dictionary<string, object?>
        {
            ["nodes"] = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["type"] = "let",
                    ["var"] = "_rnd_0",
                    ["expr"] = """["First choice text here", "Second choice text here"].shuffled("P1_0")[0]""",
                },
                new() { ["type"] = "text", ["value"] = "{_rnd_0}" },
            },
        };

        collector.CollectPassage("P1", "000-P1.mws.yaml", dict);
        collector.RestoreNonTemplateAssignments();

        var nodes = (List<Dictionary<string, object?>>)dict["nodes"]!;
        var expr = (string)nodes[0]["expr"]!;
        Assert.Contains("restext://", expr);
        Assert.DoesNotContain("First choice text here", expr);
        Assert.DoesNotContain("Second choice text here", expr);
    }
}
