using System.Linq;
using Masterwork.Engine;
using Masterwork.Engine.Rendering;
using Masterwork.Engine.Session;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

// Inline fixture content (not tied to any app-level "demo module" concept — the app's own built-in
// demo module was retired) exercising the engine end-to-end: an evolving hub, random/shuffled
// events, checkpoints, both popup variants, foreach over a shuffled array, and a terminal passage.
// Ported verbatim from the former SampleModule/BuiltInModules.Demo when that app-level concept was
// removed, specifically to keep this coverage without depending on it.
public class FixtureModuleTests
{
    private static readonly IReadOnlyList<string> PassageYamls =
    [
        """
        format: 'mws/0.5'
        passage_id: 'Start'
        tags:
        - 'Begins-Here'
        layout: 'hub'
        location:
          name: 'Town Square'
          icon: 'icon://village'
        nodes:
        - type: 'text'
          value: 'Welcome to {townname}, home of the Masterwork sample module.'
        - type: 'image'
          asset: 'icon://hospital'
        - type: 'text'
          value: 'The town crest bears the mark of {icon:village} — and, on this old copy, a faded {icon:nonexistent_test_icon} nobody can identify anymore.'
        - type: 'break'
        - type: 'conditional'
          conditions:
          - if: 'wellVisits == 0'
            then:
            - type: 'text'
              value: 'The old well at the square has been quiet for as long as anyone remembers.'
          - if: 'wellVisits == 1'
            then:
            - type: 'text'
              value: 'Villagers murmur about strange sounds from the well after your last visit.'
          - if: 'wellVisits == 2'
            then:
            - type: 'text'
              value: 'The murmurs have turned to whispers. Something is stirring beneath {townname}.'
          else:
          - type: 'text'
            value: 'The town is on edge. Whatever lives in the well is close to the surface now.'
        - type: 'break'
          style: 'paragraph'
        - type: 'section'
          title: 'About this scenario'
          content:
          - type: 'text'
            value: 'This is a small hand-authored demo scenario used to exercise the engine end-to-end. It is not derived from the original game.'
        - type: 'break'
          style: 'paragraph'
        - type: 'link'
          label: 'Visit the old well'
          target: 'Well'
          snapshot: true
        - type: 'link'
          label: 'Take the survey'
          target: 'Survey'
          snapshot: true
        - type: 'link'
          label: 'Listen to the rumors'
          target: 'Rumors'
          snapshot: true
        - type: 'conditional'
          if: 'wellVisits >= 3'
          then:
          - type: 'link'
            label: 'Confront what waits in the well'
            target: 'Ending'
            snapshot: true
        """,
        """
        format: 'mws/0.5'
        passage_id: 'Well'
        layout: 'event'
        nodes:
        - type: 'let'
          var: 'wellFlavor'
          expr: '["The water is ice cold tonight.", "A frog watches you from the mossy rim.", "The echo takes far too long to return."].shuffled("well_flavor")[0]'
        - type: 'text'
          value: '{wellFlavor}'
        - type: 'let'
          var: 'encounterRoll'
          expr: 'rand_between(1, 3, "well_encounter")'
        - type: 'switch'
          on: 'encounterRoll'
          cases:
          - match: 1
            nodes:
            - type: 'text'
              value: 'The bucket comes up empty, but the rope is oddly warm.'
          - match: 2
            nodes:
            - type: 'text'
              value: 'A coin glints at the bottom of the well. You toss in another for luck.'
          - match: 3
            nodes:
            - type: 'text'
              value: 'Something brushes against the bucket in the dark water below.'
          default:
          - type: 'text'
            value: 'The well is silent today.'
        - type: 'assign'
          var: 'wellVisits'
          expr: 'wellVisits + 1'
        - type: 'checkpoint'
          id: 'visited_well'
          display: 'Visited the well'
          diagnostic: 'checkpoint:well'
        - type: 'link'
          label: 'Step back'
          target: 'Start'
          snapshot: true
        """,
        """
        format: 'mws/0.5'
        passage_id: 'Survey'
        layout: 'narration'
        nodes:
        - type: 'text'
          value: 'Before we continue, a quick survey.'
        - type: 'input'
          label: 'How many people are in your group?'
          var: 'surveyCount'
        - type: 'link'
          label: 'Submit'
          target: 'SurveyResult'
          snapshot: true
        """,
        """
        format: 'mws/0.5'
        passage_id: 'SurveyResult'
        layout: 'narration'
        nodes:
        - type: 'text'
          value: 'Thanks! You said there are {surveyCount} people in your group.'
        - type: 'popup'
          label: 'What happens next?'
          cancel: 'Got it'
          snapshot: false
          content:
          - type: 'text'
            value: 'This is a generic popup with no named layout — just its content nodes and a dismiss button.'
        - type: 'popup'
          label: 'Cast your vote'
          layout: 'voting'
          snapshot: true
          target: 'Start'
          content:
          - type: 'text'
            value: 'Everyone vote now!'
        - type: 'link'
          label: 'Return to town'
          target: 'Start'
          snapshot: true
        """,
        """
        format: 'mws/0.5'
        passage_id: 'Rumors'
        layout: 'narration'
        nodes:
        - type: 'let'
          var: 'rumorList'
          expr: '["They say the mayor never sleeps.", "Someone heard laughter from the empty chapel.", "The miller swears his flour has gone missing again."].shuffled("rumor_order")'
        - type: 'foreach'
          var: 'rumor'
          in: 'rumorList'
          do:
          - type: 'text'
            value: '{rumor}'
          - type: 'break'
        - type: 'link'
          label: 'Head back to the square'
          target: 'Start'
          snapshot: true
        """,
        """
        format: 'mws/0.5'
        passage_id: 'Ending'
        layout: 'narration'
        nodes:
        - type: 'assign'
          var: 'ending'
          expr: '"END-WellDepths"'
        - type: 'text'
          value: 'You lower a lantern into the well and finally see it — pale eyes, patient and old, staring back from the depths beneath {townname}.'
        - type: 'break'
        - type: 'text'
          value: 'THE END ({ending})'
        """,
    ];

    private const string VariablesYaml = """
        standard_variables: []
        variables:
          surveyCount:
            type: 'int'
            default: 0
          wellVisits:
            type: 'int'
            default: 0
          townname:
            type: 'string'
            default: 'Sampleton'
          ending:
            type: 'string'
            default: ''
        """;

    [Fact]
    public void LoadsCleanly_WithNoWarnings()
    {
        var module = new ModuleLoader().LoadFromSources(PassageYamls, VariablesYaml);

        Assert.Equal(6, module.Passages.Count);
        Assert.Equal("Start", module.StartPassageId);
        Assert.Empty(module.Warnings.Items);
    }

    [Fact]
    public async Task FullPlaythrough_ExercisesEveryNodeType()
    {
        var module = new ModuleLoader().LoadFromSources(PassageYamls, VariablesYaml);
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
        var module = new ModuleLoader().LoadFromSources(PassageYamls, VariablesYaml);
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
