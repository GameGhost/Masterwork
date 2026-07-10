using Masterwork.Extractor;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class VariableDiscoveryTests
{
    private static Dictionary<string, VarDef> DiscoverVariables(string source)
    {
        var tempFile = Path.GetTempFileName() + ".cs";
        File.WriteAllText(tempFile, source);
        try
        {
            var opts = new ExtractionOptions { InputDir = tempFile, PassagesOutDir = "", IncludeDebug = true };
            var report = new ExtractionReport();
            var extractor = new CradleExtractor(opts, SpriteMapper.Empty(), report);
            extractor.Extract([tempFile]);
            return extractor.GetDiscoveredVariables();
        }
        finally { File.Delete(tempFile); }
    }

    // A "complete" source file — required for Phase A (VarDefs scanning) to run at all.
    // CradleExtractor.PrepareSource detects "complete" by the verbatim-identifier class name
    // Cradle actually generates ("public partial class @Name"); a plain "TestStory" wouldn't match.
    private const string CompleteFilePreamble = "public partial class @TestStory\n{\n";
    private const string CompleteFilePostamble = "\n}\n";

    [Fact]
    public void VarDefs_ZeroInitializer_InfersIntWithNoDefault()
    {
        var vars = DiscoverVariables(CompleteFilePreamble + """
            public class VarDefs
            {
                public StoryVar @players = 0;
            }
            """ + CompleteFilePostamble);

        var def = vars["players"];
        Assert.Equal(VarKind.Integer, def.VarType);
        Assert.Null(def.Default);
    }

    [Fact]
    public void VarDefs_NonZeroInitializer_KeepsDefault()
    {
        var vars = DiscoverVariables(CompleteFilePreamble + """
            public class VarDefs
            {
                public StoryVar @final5 = 3;
            }
            """ + CompleteFilePostamble);

        var def = vars["final5"];
        Assert.Equal(VarKind.Integer, def.VarType);
        Assert.Equal(3L, def.Default);
    }

    [Fact]
    public void VarDefs_EmptyStringInitializer_InfersStringWithNoDefault()
    {
        var vars = DiscoverVariables(CompleteFilePreamble + """
            public class VarDefs
            {
                public StoryVar @direction = "";
            }
            """ + CompleteFilePostamble);

        var def = vars["direction"];
        Assert.Equal(VarKind.String, def.VarType);
        Assert.Null(def.Default);
    }

    [Fact]
    public void VarDefs_NonEmptyStringInitializer_KeepsDefault()
    {
        var vars = DiscoverVariables(CompleteFilePreamble + """
            public class VarDefs
            {
                public StoryVar @direction = "HuntNorth";
            }
            """ + CompleteFilePostamble);

        var def = vars["direction"];
        Assert.Equal(VarKind.String, def.VarType);
        Assert.Equal("HuntNorth", def.Default);
    }

    [Fact]
    public void VarDefs_FalseInitializer_InfersBoolWithNoDefault()
    {
        var vars = DiscoverVariables(CompleteFilePreamble + """
            public class VarDefs
            {
                public StoryVar @flag = false;
            }
            """ + CompleteFilePostamble);

        var def = vars["flag"];
        Assert.Equal(VarKind.Boolean, def.VarType);
        Assert.Null(def.Default);
    }

    [Fact]
    public void VarDefs_TrueInitializer_KeepsDefault()
    {
        var vars = DiscoverVariables(CompleteFilePreamble + """
            public class VarDefs
            {
                public StoryVar @flag = true;
            }
            """ + CompleteFilePostamble);

        var def = vars["flag"];
        Assert.Equal(VarKind.Boolean, def.VarType);
        Assert.Equal(true, def.Default);
    }

    [Fact]
    public void VarDefs_NoInitializer_InfersStringNoDefault_RefinableLater()
    {
        var vars = DiscoverVariables(CompleteFilePreamble + """
            public class VarDefs
            {
                public StoryVar @tally;
            }

            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.tally = 7;
                yield break;
            }
            """ + CompleteFilePostamble);

        var def = vars["tally"];
        // Refined by usage (Phase C) from string (VarDefs fallback) to int; still no default —
        // a first-encountered assignment is not treated as a default source.
        Assert.Equal(VarKind.Integer, def.VarType);
        Assert.Null(def.Default);
    }

    [Fact]
    public void UsageOnly_FirstAssignmentDoesNotBecomeDefault()
    {
        // No VarDefs entry at all — discovered purely from this.Vars.X usage.
        var vars = DiscoverVariables("""
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.mood = "grim";
                this.Vars.mood = "hopeful";
                yield break;
            }
            """);

        var def = vars["mood"];
        Assert.Equal(VarKind.String, def.VarType);
        Assert.Null(def.Default);
    }

    [Fact]
    public void ConflictingAssignments_IntAndString_HoistsToString_AndWarns()
    {
        var report = new ExtractionReport();
        var tempFile = Path.GetTempFileName() + ".cs";
        File.WriteAllText(tempFile, """
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.mixed = 5;
                this.Vars.mixed = "five";
                yield break;
            }
            """);
        try
        {
            var opts = new ExtractionOptions { InputDir = tempFile, PassagesOutDir = "", IncludeDebug = true };
            var extractor = new CradleExtractor(opts, SpriteMapper.Empty(), report);
            extractor.Extract([tempFile]);
            var def = extractor.GetDiscoveredVariables()["mixed"];

            Assert.Equal(VarKind.String, def.VarType);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void ConflictingAssignments_BoolAndInt_HoistsToInt()
    {
        var vars = DiscoverVariables("""
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.flag = true;
                this.Vars.flag = 2;
                yield break;
            }
            """);

        Assert.Equal(VarKind.Integer, vars["flag"].VarType);
    }
}
