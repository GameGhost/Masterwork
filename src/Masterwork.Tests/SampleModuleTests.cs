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
        var wellNav = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single(a => a.Label == "Visit the old well");
        var wellResult = await session.FollowLinkAsync(wellNav.Id);
        Assert.Equal("Well", wellResult.PassageId);
        Assert.Equal("event", wellResult.Layout);
        Assert.Single(wellResult.Checkpoints);
        Assert.Equal(SnapshotKind.Checkpoint, session.Timeline[^1].Kind);

        var backToStart = wellResult.Actions.OfType<RenderedNavigation>().Single();
        await session.FollowLinkAsync(backToStart.Id);

        // Survey: input.
        var surveyNav = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single(a => a.Label == "Take the survey");
        var surveyResult = await session.FollowLinkAsync(surveyNav.Id);
        var input = surveyResult.Actions.OfType<RenderedInput>().Single();
        var surveyResultResult = await session.SubmitInputAsync(input.Id, 4L);
        Assert.Equal("SurveyResult", surveyResultResult.PassageId);
        Assert.Contains("4", surveyResultResult.Nodes.OfType<RenderedText>().First().Value);

        // SurveyResult: generic popup + voting-layout popup.
        var genericPopup = surveyResultResult.Actions.OfType<RenderedPopup>().Single(p => p.Layout is null);
        var genericPopupOpen = await session.OpenPopupAsync(genericPopup.Id);
        Assert.Single(genericPopupOpen.Content);
        await session.ClosePopupAsync(genericPopup.Id);

        var votingPopup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single(p => p.Layout == "voting");
        await session.OpenPopupAsync(votingPopup.Id);
        var afterVoteClose = await session.ClosePopupAsync(votingPopup.Id);
        Assert.Equal("Start", afterVoteClose.PassageId);

        // Secret: private layout + private-gate confirmation.
        var secretNav = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single(a => a.Label == "Read the secret note");
        var secretResult = await session.FollowLinkAsync(secretNav.Id);
        Assert.Equal("private", secretResult.Layout);
        Assert.DoesNotContain(secretResult.PassageId, session.ViewState.ConfirmedGates);
        session.ConfirmPrivateGate(secretResult.PassageId);
        Assert.Contains(secretResult.PassageId, session.ViewState.ConfirmedGates);
        var backFromSecret = secretResult.Actions.OfType<RenderedNavigation>().Single();
        await session.FollowLinkAsync(backFromSecret.Id);
    }

    [Fact]
    public async Task HubEvolves_AsWellVisitsAccumulate_ThenReachesEnding()
    {
        var module = new ModuleLoader().LoadFromSources(SampleModule.PassageYamls, SampleModule.VariablesYaml);
        var session = new GameSession(module, masterSeed: 1);

        // Not yet visited: no route to the Ending.
        Assert.DoesNotContain(session.CurrentRender.Actions.OfType<RenderedNavigation>(), a => a.Label.Contains("Confront"));
        Assert.Contains(session.CurrentRender.Nodes.OfType<RenderedText>(), t => t.Value.Contains("quiet for as long as anyone remembers"));

        for (var visit = 1; visit <= 3; visit++)
        {
            var wellNav = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single(a => a.Label == "Visit the old well");
            var wellResult = await session.FollowLinkAsync(wellNav.Id);

            // Exercises both random mechanisms: a shuffled-array pick (let) and rand_between (switch).
            Assert.Contains(wellResult.Nodes.OfType<RenderedText>(), t =>
                t.Value.Contains("ice cold") || t.Value.Contains("frog") || t.Value.Contains("echo"));

            var backNav = wellResult.Actions.OfType<RenderedNavigation>().Single();
            await session.FollowLinkAsync(backNav.Id);
            Assert.Equal((long)visit, session.Current.Variables["wellVisits"].AsInt());
        }

        Assert.False(session.CurrentRender.IsEnding);
        var endingNav = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single(a => a.Label.Contains("Confront"));
        var endingResult = await session.FollowLinkAsync(endingNav.Id);

        Assert.Equal("Ending", endingResult.PassageId);
        Assert.True(endingResult.IsEnding);
        // The 'ending' assign runs during this terminal passage's own render, so its new value is
        // only observable in this render's output — not in session.Current.Variables, which is a
        // snapshot of state from just *before* this passage rendered (see SessionSnapshot's doc
        // comment on precision).
        Assert.Contains(endingResult.Nodes.OfType<RenderedText>(), t => t.Value.Contains("END-WellDepths"));
        Assert.Empty(endingResult.Actions);
    }
}
