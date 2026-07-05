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
    /// <summary>Two passages: a <c>hub</c> start passage with a location header, and an <c>event</c> passage without one.</summary>
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
        """,
        """
        format: 'mws/0.3'
        passage_id: 'Well'
        layout: 'event'
        nodes:
        - type: 'text'
          value: 'The well is deep and dark. Something stirs within.'
        - type: 'navigation'
          label: 'Step back'
          target: 'Start'
          state_affecting: true
        """,
    ];

    /// <summary>No session variables are needed for this sample scenario.</summary>
    public const string VariablesYaml = """
        standard_variables: []
        variables: {}
        """;
}
