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
            - type: 'navigation'
              label: 'Continue'
              target: '${module::entrypoint}'
              state_affecting: true
            """,
        ]);

        var merged = loader.MergeDependency(module, onboarding);
        var session = new GameSession(merged, masterSeed: 1, startPassageIdOverride: "Onboarding");

        var nav = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single();
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
}
