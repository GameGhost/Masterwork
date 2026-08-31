using Masterwork.Engine;
using Masterwork.Engine.Rendering;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class ModuleLoaderTests
{
    // ── module::entrypoint ────────────────────────────────────────────────

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

        var session = new GameSession(module, masterSeed: 1, startPassageIdOverride: "Onboarding");

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
            """
            format: 'mws/0.3'
            passage_id: 'Onboarding'
            layout: 'narration'
            nodes:
            - type: 'goto'
              target: '${module::entrypoint}'
            """,
        ]);

        var session = new GameSession(module, masterSeed: 1, startPassageIdOverride: "Onboarding");

        Assert.Equal("ModuleStart", session.CurrentRender.PassageId);
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
    public void LoadFromSources_AudioTrackTitleContainsRestextRef_ResolvesLikeContent()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'P1'
                tags:
                - 'Begins-Here'
                layout: 'narration'
                nodes:
                - type: 'audio_track'
                  asset: 'audio://vo/greeting'
                  title: 'restext://TrackTitle_001'
                """,
            ],
            restextText: "TrackTitle_001=Listen to the greeting\n");

        var track = Assert.IsType<AudioTrackNode>(module.Passages["P1"].Nodes[0]);
        Assert.Equal("Listen to the greeting", track.Title);
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
    public void LoadFromSources_TernaryTitleContainsRestextRefs_ResolvesEachBranchWithEscaping()
    {
        // CradleExtractor.TryBuildTernaryHeading collapses several branches' own headings into one
        // computed title — each branch's text now gets its own restext key (RestextCollector.
        // ExtractLiteralsFromBracedTitleExpr), embedded inside the ternary's own quoted-literal
        // syntax. A restext:// reference there must be resolved via RestextResolver.
        // ResolveTitleOrSubtitle's ResolveExpr path (escaping embedded '"'/'\' in the looked-up
        // text) — not ResolveDisplay's unescaped splice, which would let a locale value containing
        // '"' (like the second branch here) break the ternary's own string-literal syntax.
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.4'
                passage_id: 'P1'
                title: '{gunsbonus == 1 ? "restext://Heading_A" : "restext://Heading_B"}'
                tags:
                - 'Begins-Here'
                layout: 'narration'
                nodes: []
                """,
            ],
            restextText: "Heading_A=Knowledge Bonus\nHeading_B=She said \"hi\" today\n");

        var passage = module.Passages["P1"];
        Assert.Equal(
            "{gunsbonus == 1 ? \"Knowledge Bonus\" : \"She said \\\"hi\\\" today\"}",
            passage.Title);
    }

    [Fact]
    public async Task FollowLink_TernaryTitleWithEmbeddedQuoteInLocaleText_RendersCorrectTitle()
    {
        // Strongest proof escaping actually matters: renders the destination passage end-to-end
        // (parses AND evaluates the resolved ternary expression) with a locale value that contains
        // a literal '"' — if RestextResolver's escaping were missing or wrong, this would either
        // throw a parse error (an unescaped '"' terminates the string literal early — see
        // ExpressionParser's string-literal handling) or silently produce a garbled title.
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
                - type: 'assign'
                  var: 'gunsbonus'
                  expr: '2'
                - type: 'link'
                  label: 'Go'
                  target: 'P2'
                  snapshot: true
                """,
                """
                format: 'mws/0.4'
                passage_id: 'P2'
                title: '{gunsbonus == 1 ? "restext://Heading_A" : "restext://Heading_B"}'
                layout: 'narration'
                nodes: []
                """,
            ],
            restextText: "Heading_A=Knowledge Bonus\nHeading_B=She said \"hi\" today\n");

        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        await session.FollowLinkAsync(navId);

        Assert.Equal("She said \"hi\" today", session.CurrentRender.Title);
    }

    // ── per-key restext fallback across preferred/default locale ────────────

    [Fact]
    public void LoadFromSources_PreferredLocaleHasKey_PreferredValueWinsOverDefault()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Greeting'
                """,
            ],
            restextText: "Greeting=Bonjour\n",
            defaultRestextText: "Greeting=Hello\n");

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("Bonjour", text.Value);
    }

    [Fact]
    public void LoadFromSources_KeyMissingFromPreferredLocale_FallsBackToDefaultLocaleValue()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://OnlyInDefault'
                """,
            ],
            restextText: "Greeting=Bonjour\n",
            defaultRestextText: "Greeting=Hello\nOnlyInDefault=Fallback text\n");

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("Fallback text", text.Value);
    }

    [Fact]
    public void LoadFromSources_KeyMissingFromBothLocales_StillWarnsAndLeavesRawRestextUri()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Missing'
                """,
            ],
            restextText: "Greeting=Bonjour\n",
            defaultRestextText: "Greeting=Hello\n");

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("restext://Missing", text.Value);
        Assert.Contains(module.Warnings.Items, w => w.Message.Contains("Missing"));
    }

    [Fact]
    public void LoadFromSources_DefaultRestextOverrideText_MergesIntoFallbackBeforeOverlay()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://OnlyInDefault'
                """,
            ],
            restextText: "Greeting=Bonjour\n",
            defaultRestextText: "Greeting=Hello\nOnlyInDefault=Base fallback\n",
            defaultRestextOverrideText: "OnlyInDefault=Overridden fallback\n");

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("Overridden fallback", text.Value);
    }

    [Fact]
    public void LoadFromSources_NoDefaultRestextText_PreservesOldBehavior_RawUriForMissingKey()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Missing'
                """,
            ],
            restextText: "Greeting=Bonjour\n");

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("restext://Missing", text.Value);
    }

    // ── dependency (asset-pack) restext fallback, underneath the module's own ───

    [Fact]
    public void LoadFromSources_ModuleOwnPreferredKeyWinsOverDependency()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Shared_001'
                """,
            ],
            restextText: "Shared_001=Module's own text\n",
            dependencyRestexts: [new DependencyRestext("Shared_001=Asset pack text\n", null)]);

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("Module's own text", text.Value);
    }

    [Fact]
    public void LoadFromSources_ModuleOwnDefaultKeyWinsOverDependencySelected()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Shared_001'
                """,
            ],
            restextText: "Greeting=Bonjour\n",
            defaultRestextText: "Greeting=Hello\nShared_001=Module's default text\n",
            dependencyRestexts: [new DependencyRestext("Shared_001=Asset pack selected-locale text\n", null)]);

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("Module's default text", text.Value);
    }

    [Fact]
    public void LoadFromSources_KeyOnlyInDependencySelectedLocale_ResolvesFromDependency()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Shared_001'
                """,
            ],
            restextText: "Greeting=Bonjour\n",
            dependencyRestexts: [new DependencyRestext(
                "Shared_001=Asset pack selected-locale text\n",
                "Shared_001=Asset pack default-locale text\n")]);

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("Asset pack selected-locale text", text.Value);
    }

    [Fact]
    public void LoadFromSources_KeyMissingFromDependencySelectedLocale_FallsBackToDependencyDefault()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Shared_001'
                """,
            ],
            restextText: "Greeting=Bonjour\n",
            // RestextText is null — the asset pack doesn't ship this locale at all, per
            // "an asset pack may support more locales than the module, but not vice versa": this
            // is the mirror case, an asset pack missing a locale the module resolved to.
            dependencyRestexts: [new DependencyRestext(null, "Shared_001=Asset pack default-locale text\n")]);

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("Asset pack default-locale text", text.Value);
    }

    [Fact]
    public void LoadFromSources_KeyMissingEverywhere_StillWarnsAndLeavesRawUri()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Missing'
                """,
            ],
            restextText: "Greeting=Bonjour\n",
            dependencyRestexts: [new DependencyRestext(
                "Shared_001=Asset pack text\n", "Shared_001=Asset pack default text\n")]);

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("restext://Missing", text.Value);
        Assert.Contains(module.Warnings.Items, w => w.Message.Contains("Missing"));
    }

    [Fact]
    public void LoadFromSources_TwoDependencies_BothContributeDistinctKeys()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://FromPackOne'
                - type: 'break'
                - type: 'text'
                  value: 'restext://FromPackTwo'
                """,
            ],
            restextText: "Greeting=Bonjour\n",
            dependencyRestexts:
            [
                new DependencyRestext("FromPackOne=Pack one's text\n", null),
                new DependencyRestext("FromPackTwo=Pack two's text\n", null),
            ]);

        var texts = module.Passages["Start"].Nodes.OfType<TextNode>().ToList();
        Assert.Equal("Pack one's text", texts[0].Value);
        Assert.Equal("Pack two's text", texts[1].Value);
    }

    [Fact]
    public void LoadFromSources_TwoDependencies_LaterDependencyWinsOnSharedKey()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Shared_001'
                """,
            ],
            dependencyRestexts:
            [
                new DependencyRestext("Shared_001=First pack's text\n", null),
                new DependencyRestext("Shared_001=Second pack's text\n", null),
            ]);

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("Second pack's text", text.Value);
    }

    [Fact]
    public void LoadFromSources_TwoDependencies_EarlierDependencySelectedLocaleOutranksLaterDependencyDefaultLocale()
    {
        // Pins the two-pass merge order: pack two only has this key in ITS OWN default locale
        // (e.g. the player's selected locale isn't one it ships), while pack one genuinely has it
        // in the player's selected locale. Pack one's real translation must win even though pack
        // two is declared later — a naive one-dependency-at-a-time merge would get this backwards,
        // since pack two's default-locale write would land after pack one's selected-locale write.
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Shared_001'
                """,
            ],
            dependencyRestexts:
            [
                new DependencyRestext("Shared_001=Pack one's real translation\n", "Shared_001=Pack one's default\n"),
                new DependencyRestext(null, "Shared_001=Pack two's default (wrong language)\n"),
            ]);

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("Pack one's real translation", text.Value);
    }

    [Fact]
    public void LoadFromSources_NoDependencyRestextGiven_PreservesOldBehavior_RawUriForMissingKey()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            [
                """
                format: 'mws/0.5'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'restext://Missing'
                """,
            ],
            restextText: "Greeting=Bonjour\n");

        var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
        Assert.Equal("restext://Missing", text.Value);
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

    // A dependency asset pack's layout chrome needs no new LoadFromSources parameter — the caller
    // just concatenates dependency-then-module layout YAML into one list, relying on the existing
    // "later entry wins on matching layout_id" dictionary assignment: the module overrides a
    // matching id, and a dependency-only id passes through untouched.
    [Fact]
    public void LoadFromSources_DependencyThenModuleLayoutChromeConcatenated_ModuleWinsOnCollision()
    {
        var loader = new ModuleLoader();

        var module = loader.LoadFromSources(
            passageYamls: [],
            layoutChromeYamls:
            [
                // Dependency (asset pack) layout chrome, listed first.
                """
                format: 'mws/0.4'
                layout_id: 'hub_shared'
                header:
                - type: 'text'
                  value: 'Asset pack chrome'
                """,
                """
                format: 'mws/0.4'
                layout_id: 'hub_asset_only'
                header:
                - type: 'text'
                  value: 'Only the asset pack defines this one'
                """,
                // Module's own layout chrome, listed second — wins on a matching layout_id.
                """
                format: 'mws/0.4'
                layout_id: 'hub_shared'
                header:
                - type: 'text'
                  value: 'Module chrome'
                """,
            ]);

        Assert.Equal(2, module.LayoutChrome.Count);
        var overridden = Assert.IsType<TextNode>(module.LayoutChrome["hub_shared"].Header.Single());
        Assert.Equal("Module chrome", overridden.Value);
        var dependencyOnly = Assert.IsType<TextNode>(module.LayoutChrome["hub_asset_only"].Header.Single());
        Assert.Equal("Only the asset pack defines this one", dependencyOnly.Value);
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
