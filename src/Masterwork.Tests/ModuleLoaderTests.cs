using Masterwork.Engine;
using Masterwork.Engine.Rendering;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class ModuleLoaderTests
{
    // ── module::entrypoint (masterwork-plan-rev14.md Q24) ───────────────────

    [Fact]
    public async Task ModuleEntrypoint_NavigationTarget_ResolvesToModuleStartPassage()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'ModuleStart'
            tags:
            - 'Begins-Here'
            layout: 'hub'
            nodes:
            - type: 'text'
              value: 'Welcome to the real story.'
            """,
        ]);

        var onboarding = loader.LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'Onboarding'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Continue'
              target: '${module::entrypoint}'
              snapshot: true
            """,
        ]);

        var merged = loader.MergeDependency(module, onboarding);
        var session = new GameSession(merged, masterSeed: 1, startPassageIdOverride: "Onboarding");

        var nav = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        var result = await session.FollowLinkAsync(nav.Id);

        Assert.Equal("ModuleStart", result.PassageId);
    }

    [Fact]
    public void ModuleEntrypoint_GotoTarget_ResolvesToModuleStartPassage()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'ModuleStart'
            tags:
            - 'Begins-Here'
            layout: 'hub'
            nodes:
            - type: 'text'
              value: 'Welcome to the real story.'
            """,
        ]);

        var onboarding = loader.LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'Onboarding'
            layout: 'narration'
            nodes:
            - type: 'goto'
              target: '${module::entrypoint}'
            """,
        ]);

        var merged = loader.MergeDependency(module, onboarding);
        var session = new GameSession(merged, masterSeed: 1, startPassageIdOverride: "Onboarding");

        Assert.Equal("ModuleStart", session.CurrentRender.PassageId);
    }

    [Fact]
    public void MergeDependency_AddsDependencyPassagesAndVariables()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'ModuleStart'
            tags:
            - 'Begins-Here'
            layout: 'hub'
            nodes: []
            """,
        ]);

        var dependency = loader.LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'Onboarding'
            layout: 'narration'
            nodes: []
            """,
        ], """
            standard_variables: []
            variables:
              townname:
                type: 'string'
                default: 'Sampleton'
            """);

        var merged = loader.MergeDependency(module, dependency);

        Assert.True(merged.Passages.ContainsKey("ModuleStart"));
        Assert.True(merged.Passages.ContainsKey("Onboarding"));
        Assert.True(merged.Variables.ContainsKey("townname"));
        Assert.Equal("ModuleStart", merged.StartPassageId);
    }

    [Fact]
    public void MergeDependency_ModuleOwnDeclarationsWinOnCollision()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'Shared'
            tags:
            - 'Begins-Here'
            layout: 'hub'
            nodes:
            - type: 'text'
              value: 'Module version'
            """,
        ]);

        var dependency = loader.LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'Shared'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Dependency version'
            """,
        ]);

        var merged = loader.MergeDependency(module, dependency);

        var shared = merged.Passages["Shared"];
        var text = Assert.IsType<TextNode>(shared.Nodes.Single());
        Assert.Equal("Module version", text.Value);
    }

    // ── app::gameover reserved target ───────────────────────────────────────

    [Fact]
    public void LoadFromSources_LinkTargetsAppGameOver_NoUnresolvedPassageRefWarning()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'Ending'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Finish'
              target: 'app::gameover'
            """,
        ]);

        Assert.DoesNotContain(module.Warnings.Items,
            w => w.Kind == "unresolved_passage_ref" && w.Message.Contains("app::gameover"));
    }

    [Fact]
    public void LoadFromSources_LinkTargetsNonexistentPassage_StillWarns()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'Ending'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Finish'
              target: 'DoesNotExist'
            """,
        ]);

        Assert.Contains(module.Warnings.Items,
            w => w.Kind == "unresolved_passage_ref" && w.Message.Contains("DoesNotExist"));
    }

    // ── passages-override merge (LoadFromSources overridePassageYamls) ─────

    [Fact]
    public void LoadFromSources_Override_ReplacesMatchingPassageAndAddsNew()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'Extracted version'
                """,
                """
                format: 'mws/0.3'
                passage_id: 'Untouched'
                layout: 'hub'
                nodes: []
                """,
            ],
            overridePassageYamls:
            [
                """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'Hand-authored version'
                """,
                """
                format: 'mws/0.3'
                passage_id: 'NewOverridePassage'
                layout: 'hub'
                nodes: []
                """,
            ]);

        Assert.Equal(3, module.Passages.Count);
        Assert.True(module.Passages.ContainsKey("Untouched"));
        Assert.True(module.Passages.ContainsKey("NewOverridePassage"));

        var start = module.Passages["Start"];
        var text = Assert.IsType<TextNode>(start.Nodes.Single());
        Assert.Equal("Hand-authored version", text.Value);
    }

    // ── LoadFromDirectory folder-convention resolution ──────────────────────

    [Fact]
    public void LoadFromDirectory_PassagesAndOverrideSubfolders_MergedByConvention()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-loader-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(dir, "passages"));
        Directory.CreateDirectory(Path.Combine(dir, "passages-override"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "passages", "001-Start.mws.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'Extracted'
                """);
            File.WriteAllText(Path.Combine(dir, "passages-override", "001-Start.mws.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'Hand-authored'
                """);

            var module = new ModuleLoader().LoadFromDirectory(dir);

            var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
            Assert.Equal("Hand-authored", text.Value);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadFromDirectory_PassageFileMissingMwsExtension_WarnsAndIsNotLoaded()
    {
        // A ".yaml" file that isn't named "*.mws.yaml" is invisible to the passages/ glob — this
        // pins the real-world bug it causes (a link targeting that passage's own passage_id fails
        // at playtime with no indication why) by asserting the loader at least warns about it.
        var dir = Path.Combine(Path.GetTempPath(), "mw-loader-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(dir, "passages"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "passages", "001-Start.mws.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'Hi'
                """);
            File.WriteAllText(Path.Combine(dir, "passages", "002-Entry.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Entry'
                layout: 'narration'
                nodes:
                - type: 'text'
                  value: 'Never loaded'
                """);

            var module = new ModuleLoader().LoadFromDirectory(dir);

            Assert.False(module.Passages.ContainsKey("Entry"));
            Assert.Contains(module.Warnings.Items,
                w => w.Kind == "stray_yaml_file" && w.Message.Contains("002-Entry.yaml"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadFromDirectory_LegacyFlatLayout_NoPassagesSubfolder_StillLoads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-loader-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "001-Start.mws.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes: []
                """);

            var module = new ModuleLoader().LoadFromDirectory(dir);

            Assert.Equal("Start", module.StartPassageId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── restext overrides (<culture>.overrides.restext) ─────────────────────

    [Fact]
    public void LoadFromSources_RestextOverride_ReplacesMatchingKeyAndAddsNew()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Start_001'
                - type: 'text'
                  value: 'restext://Start_002'
                """,
            ],
            restextText: "Start_001=Extracted greeting\nStart_002=Untouched line\n",
            restextOverrideText: "Start_001=Hand-authored greeting\nStart_003=Brand new line\n");

        Assert.Equal("Hand-authored greeting", module.Locale["Start_001"]);
        Assert.Equal("Untouched line", module.Locale["Start_002"]);
        Assert.Equal("Brand new line", module.Locale["Start_003"]);
    }

    // RestextCollector.SanitizeForRestextKey prepends '_' to a restext key when the source
    // passage_id doesn't start with a letter (e.g. "1sttime-Suspicion" → "_1sttime_Suspicion_001").
    // RestextResolver's own restext://Key regex must accept that shape too, or the reference is
    // left unresolved as a literal "restext://..." string in the rendered passage.
    [Fact]
    public void LoadFromSources_RestextKeyStartsWithUnderscore_ResolvesCorrectly()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.3'
                passage_id: '1sttime-Suspicion'
                tags:
                - 'Begins-Here'
                layout: 'narration'
                nodes:
                - type: 'text'
                  value: 'restext://_1sttime_Suspicion_001'
                """,
            ],
            restextText: "_1sttime_Suspicion_001=Place the Suspicion marker.\n");

        var passage = module.Passages["1sttime-Suspicion"];
        var text = Assert.IsType<TextNode>(passage.Nodes[0]);
        Assert.Equal("Place the Suspicion marker.", text.Value);
    }

    [Fact]
    public void LoadFromSources_PopupHeaderContainsRestextRef_ResolvesLikeContent()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.4'
                passage_id: 'P1'
                tags:
                - 'Begins-Here'
                layout: 'narration'
                nodes:
                - type: 'popup'
                  header:
                  - type: 'image'
                    asset: 'image://setup/StorybookToken'
                    style: 'setup-image'
                    title: 'restext://Header_001'
                  content: []
                """,
            ],
            restextText: "Header_001=Storybook token\n");

        var popup = Assert.IsType<PopupNode>(module.Passages["P1"].Nodes[0]);
        var image = Assert.IsType<ImageNode>(Assert.Single(popup.Header));
        Assert.Equal("Storybook token", image.Title);
    }

    [Fact]
    public void LoadFromSources_PassageTitleAndSubtitleContainRestextRefs_ResolveLikeContent()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.4'
                passage_id: 'P1'
                title: 'restext://Title_001'
                subtitle: 'restext://Subtitle_001'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes: []
                """,
            ],
            restextText: "Title_001=YELLOW FEVER\nSubtitle_001=Early Years\n");

        var passage = module.Passages["P1"];
        Assert.Equal("YELLOW FEVER", passage.Title);
        Assert.Equal("Early Years", passage.Subtitle);
    }

    [Fact]
    public void LoadFromDirectory_RestextOverrideFile_MergedByConvention()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-loader-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "001-Start.mws.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Start_001'
                """);
            File.WriteAllText(Path.Combine(dir, "en-US.restext"), "Start_001=Extracted greeting\n");
            File.WriteAllText(Path.Combine(dir, "en-US.overrides.restext"), "Start_001=Hand-authored greeting\n");

            var module = new ModuleLoader().LoadFromDirectory(dir);

            var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
            Assert.Equal("Hand-authored greeting", text.Value);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Layout chrome ─────────────────────────────────────────────────────────

    [Fact]
    public void LoadFromSources_LayoutChromeYaml_KeyedByLayoutIdNotFilename()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            passageYamls: [],
            layoutChromeYamls:
            [
                """
                format: 'mws/0.4'
                layout_id: 'hub_early'
                header:
                - type: 'text'
                  value: 'Early Years chrome'
                """,
            ]);

        Assert.True(module.LayoutChrome.ContainsKey("hub_early"));
        var text = Assert.IsType<TextNode>(module.LayoutChrome["hub_early"].Header.Single());
        Assert.Equal("Early Years chrome", text.Value);
    }

    [Fact]
    public void LoadFromSources_NoLayoutChromeYamls_LayoutChromeIsEmpty()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(passageYamls: []);

        Assert.Empty(module.LayoutChrome);
    }

    [Fact]
    public void LoadFromSources_LayoutChromeContainsRestextRef_ResolvesLikePassageNodes()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            passageYamls: [],
            restextText: "Chrome_001=Resolved chrome text\n",
            layoutChromeYamls:
            [
                """
                format: 'mws/0.4'
                layout_id: 'hub_early'
                header:
                - type: 'text'
                  value: 'restext://Chrome_001'
                """,
            ]);

        var text = Assert.IsType<TextNode>(module.LayoutChrome["hub_early"].Header.Single());
        Assert.Equal("Resolved chrome text", text.Value);
    }

    [Fact]
    public void LoadFromDirectory_LayoutsFolder_DiscoveredAndLoaded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-loader-test-" + Guid.NewGuid().ToString("n"));
        var layoutsDir = Path.Combine(dir, "layouts");
        Directory.CreateDirectory(layoutsDir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "001-Start.mws.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub_early'
                nodes: []
                """);
            File.WriteAllText(Path.Combine(layoutsDir, "hub_early.mws.yaml"), """
                format: 'mws/0.4'
                layout_id: 'hub_early'
                header:
                - type: 'text'
                  value: 'From layouts folder'
                """);

            var module = new ModuleLoader().LoadFromDirectory(dir);

            var text = Assert.IsType<TextNode>(module.LayoutChrome["hub_early"].Header.Single());
            Assert.Equal("From layouts folder", text.Value);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Hand-authored variables/ folder ─────────────────────────────────────────

    [Fact]
    public void LoadFromSources_AdditionalVariableYaml_NewName_IsAdded()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            passageYamls: [],
            variablesYaml: """
                variables:
                  round: int
                """,
            additionalVariableYamls:
            [
                """
                variables:
                  mwA: bool
                """,
            ]);

        Assert.True(module.Variables.ContainsKey("round"));
        Assert.Equal(VarKind.Boolean, module.Variables["mwA"].VarType);
    }

    [Fact]
    public void LoadFromSources_AdditionalVariableYaml_CollidingName_OverridesBaseDeclaration()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            passageYamls: [],
            variablesYaml: """
                variables:
                  scoreA: string
                """,
            additionalVariableYamls:
            [
                """
                variables:
                  scoreA: int
                """,
            ]);

        Assert.Equal(VarKind.Integer, module.Variables["scoreA"].VarType);
    }

    [Fact]
    public void LoadFromSources_NoAdditionalVariableYamls_VariablesUnchanged()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            passageYamls: [],
            variablesYaml: """
                variables:
                  round: int
                """);

        Assert.Single(module.Variables);
        Assert.True(module.Variables.ContainsKey("round"));
    }

    [Fact]
    public void LoadFromDirectory_VariablesFolder_DiscoveredAndMerged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-loader-test-" + Guid.NewGuid().ToString("n"));
        var variablesDir = Path.Combine(dir, "variables");
        Directory.CreateDirectory(variablesDir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "001-Start.mws.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'narration'
                nodes: []
                """);
            File.WriteAllText(Path.Combine(dir, "_variables.yaml"), """
                variables:
                  round: int
                """);
            File.WriteAllText(Path.Combine(variablesDir, "scoring.yaml"), """
                variables:
                  mwA: bool
                  tie2ScoreA: int
                """);

            var module = new ModuleLoader().LoadFromDirectory(dir);

            Assert.True(module.Variables.ContainsKey("round"));
            Assert.Equal(VarKind.Boolean, module.Variables["mwA"].VarType);
            Assert.Equal(VarKind.Integer, module.Variables["tie2ScoreA"].VarType);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
