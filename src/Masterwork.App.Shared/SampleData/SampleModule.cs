namespace Masterwork.App.Shared.SampleData;

/// <summary>
/// A tiny, hand-authored MWS v0.3 test scenario (not derived from any copyrighted source) used to
/// exercise the Phase 2 rendering pipeline end-to-end while real module loading (directory/.mwm
/// based) is still deferred. Loaded via <see cref="Masterwork.ModuleFormat.IModuleLoader.LoadFromSources"/>,
/// which works identically on every host (unlike <c>LoadFromDirectory</c>, which needs real
/// filesystem access and can't run in a browser WebAssembly sandbox).
/// </summary>
public static class SampleModule
{
    /// <summary>
    /// Five passages covering every standard node type Milestone A/B need to exercise: text,
    /// break, paragraph_break, section, navigation, input, popup (generic and <c>voting</c>
    /// layout), checkpoint, and a <c>private</c>-layout passage (the private-gate mechanic).
    /// </summary>
    public static readonly IReadOnlyList<string> PassageYamls =
    [
        """
        format: 'mws/0.3'
        passage_id: 'Start'
        tags:
        - 'Begins-Here'
        layout: 'hub'
        location:
          name: 'Town Square'
          icon: 'icon://village'
        nodes:
        - type: 'text'
          value: 'Welcome to the Masterwork sample module.'
        - type: 'break'
        - type: 'section'
          title: 'About this scenario'
          content:
          - type: 'text'
            value: 'This is a small hand-authored test scenario used to exercise the Phase 2 rendering pipeline end-to-end. It is not derived from the original game.'
        - type: 'paragraph_break'
        - type: 'navigation'
          label: 'Visit the old well'
          target: 'Well'
          state_affecting: true
        - type: 'navigation'
          label: 'Take the survey'
          target: 'Survey'
          state_affecting: true
        - type: 'navigation'
          label: 'Read the secret note'
          target: 'Secret'
          state_affecting: true
        """,
        """
        format: 'mws/0.3'
        passage_id: 'Well'
        layout: 'event'
        nodes:
        - type: 'text'
          value: 'The well is deep and dark. Something stirs within.'
        - type: 'checkpoint'
          id: 'visited_well'
          display: 'Visited the well'
          diagnostic: 'checkpoint:well'
        - type: 'navigation'
          label: 'Step back'
          target: 'Start'
          state_affecting: true
        """,
        """
        format: 'mws/0.3'
        passage_id: 'Survey'
        layout: 'narration'
        nodes:
        - type: 'text'
          value: 'Before we continue, a quick survey.'
        - type: 'input'
          label: 'Take the survey'
          text: 'How many people are in your group?'
          input: 'number'
          var: 'surveyCount'
          onsubmit: 'SurveyResult'
        """,
        """
        format: 'mws/0.3'
        passage_id: 'SurveyResult'
        layout: 'narration'
        nodes:
        - type: 'text'
          value: 'Thanks! You said there are {surveyCount} people in your group.'
        - type: 'popup'
          label: 'What happens next?'
          button: 'Got it'
          state_affecting: false
          content:
          - type: 'text'
            value: 'This is a generic popup with no named layout — just its content nodes and a dismiss button.'
        - type: 'popup'
          label: 'Cast your vote'
          layout: 'voting'
          state_affecting: true
          onclose: 'Start'
          content:
          - type: 'text'
            value: 'Everyone vote now!'
        - type: 'navigation'
          label: 'Return to town'
          target: 'Start'
          state_affecting: true
        """,
        """
        format: 'mws/0.3'
        passage_id: 'Secret'
        layout: 'private'
        nodes:
        - type: 'text'
          value: 'This note is for one player only — do not let the others see the screen.'
        - type: 'navigation'
          label: 'Fold it back up'
          target: 'Start'
          state_affecting: true
        """,
    ];

    /// <summary>Declares the one session variable this sample scenario writes to.</summary>
    public const string VariablesYaml = """
        standard_variables: []
        variables:
          surveyCount:
            type: 'int'
            default: 0
        """;
}
