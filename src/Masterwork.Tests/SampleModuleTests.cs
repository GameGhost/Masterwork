using System.Linq;
using Masterwork.App.Shared.SampleData;
using Masterwork.Engine;
using Masterwork.Engine.Rendering;
using Masterwork.Engine.Session;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

// Pins the embedded sample module (used by the App's SessionSetup page in lieu of real module
// loading — see masterwork-plan-rev10.md Q10) against regressions: it must keep loading cleanly
// and exercising every standard node type Phase 2 Milestones A/B rely on.
public class SampleModuleTests
{
    [Fact]
    public void LoadsCleanly_WithNoWarnings()
    {
        var module = new ModuleLoader().LoadFromSources(SampleModule.PassageYamls, SampleModule.VariablesYaml);

        Assert.Equal(6, module.Passages.Count);
        Assert.Equal("Start", module.StartPassageId);
        Assert.Empty(module.Warnings.Items);
    }

    [Fact]
    public async Task FullPlaythrough_ExercisesEveryNodeType()
    {
        var module = new ModuleLoader().LoadFromSources(SampleModule.PassageYamls, SampleModule.VariablesYaml);
        var session = new GameSession(module, masterSeed: 1);

        Assert.Equal("Start", session.CurrentRender.PassageId);
        Assert.Equal("hub", session.CurrentRender.Layout);

        // Well: text, checkpoint, navigation.
        var wellNav = session.CurrentRender.Actions.OfType<RenderedLink>().Single(a => a.Label == "Visit the old well");
        var wellResult = await session.FollowLinkAsync(wellNav.Id);
        Assert.Equal("Well", wellResult.PassageId);
        Assert.Equal("event", wellResult.Layout);
        Assert.Single(wellResult.Checkpoints);
        Assert.Equal(SnapshotKind.Checkpoint, session.Timeline[^1].Kind);

        var backToStart = wellResult.Actions.OfType<RenderedLink>().Single();
        await session.FollowLinkAsync(backToStart.Id);

        // Survey: input.
        var surveyNav = session.CurrentRender.Actions.OfType<RenderedLink>().Single(a => a.Label == "Take the survey");
        var surveyResult = await session.FollowLinkAsync(surveyNav.Id);
        var input = surveyResult.Actions.OfType<RenderedInput>().Single();
        var submitLink = surveyResult.Actions.OfType<RenderedLink>().Single();
        session.UpdateInputDraft(input.Id, "4");
        var surveyResultResult = await session.FollowLinkAsync(submitLink.Id);
        Assert.Equal("SurveyResult", surveyResultResult.PassageId);
        Assert.Contains("4", surveyResultResult.Nodes.OfType<RenderedText>().First().Value);

        // SurveyResult: generic popup + voting-layout popup.
        var genericPopup = surveyResultResult.Actions.OfType<RenderedPopup>().Single(p => p.Layout is null);
        Assert.Single(genericPopup.Content);
        await session.ClosePopupAsync(genericPopup.Id, accept: false);

        var votingPopup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single(p => p.Layout == "voting");
        var afterVoteClose = await session.ClosePopupAsync(votingPopup.Id, accept: true);
        Assert.Equal("Start", afterVoteClose.PassageId);

        // Rumors: let + foreach over a shuffled array.
        var rumorsNav = session.CurrentRender.Actions.OfType<RenderedLink>().Single(a => a.Label == "Listen to the rumors");
        var rumorsResult = await session.FollowLinkAsync(rumorsNav.Id);
        Assert.Equal("Rumors", rumorsResult.PassageId);
        Assert.Equal(3, rumorsResult.Nodes.OfType<RenderedText>().Count());
        var backFromRumors = rumorsResult.Actions.OfType<RenderedLink>().Single();
        await session.FollowLinkAsync(backFromRumors.Id);
    }

    [Fact]
    public async Task HubEvolves_AsWellVisitsAccumulate_ThenReachesEnding()
    {
        var module = new ModuleLoader().LoadFromSources(SampleModule.PassageYamls, SampleModule.VariablesYaml);
        var session = new GameSession(module, masterSeed: 1);

        // Not yet visited: no route to the Ending.
        Assert.DoesNotContain(session.CurrentRender.Actions.OfType<RenderedLink>(), a => a.Label.Contains("Confront"));
        Assert.Contains(session.CurrentRender.Nodes.OfType<RenderedText>(), t => t.Value.Contains("quiet for as long as anyone remembers"));

        for (var visit = 1; visit <= 3; visit++)
        {
            var wellNav = session.CurrentRender.Actions.OfType<RenderedLink>().Single(a => a.Label == "Visit the old well");
            var wellResult = await session.FollowLinkAsync(wellNav.Id);

            // Exercises both random mechanisms: a shuffled-array pick (let) and rand_between (switch).
            Assert.Contains(wellResult.Nodes.OfType<RenderedText>(), t =>
                t.Value.Contains("ice cold") || t.Value.Contains("frog") || t.Value.Contains("echo"));

            var backNav = wellResult.Actions.OfType<RenderedLink>().Single();
            await session.FollowLinkAsync(backNav.Id);
            Assert.Equal((long)visit, session.Current.Variables["wellVisits"].AsInt());
        }

        var endingNav = session.CurrentRender.Actions.OfType<RenderedLink>().Single(a => a.Label.Contains("Confront"));
        var endingResult = await session.FollowLinkAsync(endingNav.Id);

        Assert.Equal("Ending", endingResult.PassageId);
        // The 'ending' assign runs during this terminal passage's own render, so its new value is
        // only observable in this render's output — not in session.Current.Variables, which is a
        // snapshot of state from just *before* this passage rendered (see SessionSnapshot's doc
        // comment on precision).
        Assert.Contains(endingResult.Nodes.OfType<RenderedText>(), t => t.Value.Contains("END-WellDepths"));
        Assert.Empty(endingResult.Actions);
    }
}
