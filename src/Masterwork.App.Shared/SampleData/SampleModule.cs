namespace Masterwork.App.Shared.SampleData;

/// <summary>
/// A small, hand-authored MWS v0.4 scenario (not derived from any copyrighted source), pre-loaded
/// into the app as a permanent, non-deletable entry in <see cref="Masterwork.App.Shared.Services.EmbeddedModuleStore"/>.
/// Originally just an engine test fixture (Phase 2), it doubles as a feature showcase and the
/// primary vehicle for testing app-shell mechanics that need real, playable content — the
/// autosave/named-save model, the ending-triggered autosave cleanup, and Manage Modules' non-deletable
/// built-in entry (masterwork-plan-rev13.md Phase 3 Milestone E) — without depending on the much
/// larger Cost of Disease content (Milestone D).
/// </summary>
public static class SampleModule
{
    /// <summary>
    /// Six passages: an evolving hub (<c>Start</c>, revisited and changing as <c>wellVisits</c>
    /// grows), an event passage exercising <c>rand_between</c> (via <c>switch</c>) and
    /// <c>.shuffled(...)</c> (via <c>let</c>), an input prompt, generic and <c>voting</c>-layout
    /// popups, a <c>foreach</c> over a shuffled array (<c>let</c> + <c>foreach</c> together), and a
    /// terminal <c>ending: true</c> passage reachable once the hub has evolved enough.
    /// </summary>
    public static readonly IReadOnlyList<string> PassageYamls =
    [
        """
        format: 'mws/0.4'
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
        format: 'mws/0.4'
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
        format: 'mws/0.4'
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
        format: 'mws/0.4'
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
        format: 'mws/0.4'
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
        format: 'mws/0.4'
        passage_id: 'Ending'
        layout: 'narration'
        ending: true
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

    /// <summary>Declares the session variables this demo scenario writes to — an int, a string, and a running counter, showcasing the variable-type range alongside the node-type coverage above.</summary>
    public const string VariablesYaml = """
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
}
