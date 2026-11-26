using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public sealed class VoiceCoverageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mw-voices-test-" + Guid.NewGuid().ToString("n"));
    private readonly string _module;
    private readonly string _pack;

    public VoiceCoverageTests()
    {
        _module = Path.Combine(_root, "module");
        _pack = Path.Combine(_root, "pack");
        Directory.CreateDirectory(Path.Combine(_module, "passages"));
        Directory.CreateDirectory(_pack);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void Write(string directory, string relativePath, string content = "")
    {
        var path = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Narrated(string id, params string[] slugs) =>
        Write(_module, $"passages/{id}.mws.yaml",
            $"format: 'mws/0.6'\npassage_id: '{id}'\nlayout: 'hub'\nnodes:\n" +
            string.Concat(slugs.Select(s => $"- type: 'audio_track'\n  asset: 'audio://vo/{s}'\n")));

    private static VoiceTrack Track(string language, string id) => new() { Language = language, Id = id };

    [Fact]
    public void Narration_CollectsEachPassagesVoSlugs_IncludingNestedAndExpressions()
    {
        Write(_module, "passages/Hub.mws.yaml", """
            passage_id: 'Hub'
            nodes:
            - type: 'section'
              nodes:
              - type: 'audio_track'
                asset: 'audio://vo/first'
            - type: 'audio_track'
              asset: '${x ? "audio://vo/second" : "audio://vo/third"}'
            - type: 'audio_track'
              asset: 'audio://bgm/not_narration'
            """);
        Write(_module, "passages/Silent.mws.yaml", "passage_id: 'Silent'\nnodes: []\n");

        var passage = Assert.Single(VoiceCoverage.CollectNarration(_module));

        Assert.Contains("Hub", passage.Where);
        Assert.Equal(["first", "second", "third"], passage.Slugs);
    }

    [Fact]
    public void Narration_OverriddenBasePassage_IsCountedOnce()
    {
        Narrated("Intro", "intro");
        Write(_module, "passages-override/Intro.mws.yaml", "passage_id: 'Intro'\nnodes:\n- type: 'audio_track'\n  asset: 'audio://vo/intro_v2'\n");

        var passage = Assert.Single(VoiceCoverage.CollectNarration(_module));

        Assert.Equal(["intro_v2"], passage.Slugs);
    }

    [Fact]
    public void Coverage_CountsPassagesWhoseEverySlugHasAFile()
    {
        Narrated("A", "a");
        Narrated("B", "b1", "b2");
        Narrated("C", "c");
        Write(_pack, "assets/audio/vo/en-US/male/a.ogg");
        Write(_pack, "assets/audio/vo/en-US/male/b1.ogg");
        Write(_pack, "assets/audio/vo/en-US/male/c.mp3");
        Write(_pack, "assets/audio/vo/en-US/female/a.ogg");

        var coverage = VoiceCoverage.Coverage(
            [Track("en-US", "male"), Track("en-US", "female")],
            VoiceCoverage.ListVoiceFiles(_pack),
            VoiceCoverage.CollectNarration(_module));

        Assert.Equal([(2, 3), (1, 3)], coverage.Select(c => (c.Covered, c.Total)));
        Assert.Equal("2 of 3 narrated passages", coverage[0].Summary);
    }

    [Fact]
    public void Warnings_FileNoAudioTrackReferences()
    {
        Narrated("A", "a");
        Write(_pack, "assets/audio/vo/en-US/male/a.ogg");
        Write(_pack, "assets/audio/vo/en-US/male/orphan.ogg");

        var warning = Assert.Single(VoiceCoverage.Warnings(
            [Track("en-US", "male")], VoiceCoverage.ListVoiceFiles(_pack), VoiceCoverage.CollectNarration(_module)));

        Assert.Contains("assets/audio/vo/en-US/male/orphan.ogg", warning);
        Assert.Contains("audio://vo/orphan", warning);
    }

    [Fact]
    public void Warnings_FileOutsideATrackFolder()
    {
        Narrated("A", "a");
        Write(_module, "assets/audio/vo/a.ogg");

        var warning = Assert.Single(VoiceCoverage.Warnings(
            [], VoiceCoverage.ListVoiceFiles(_module), VoiceCoverage.CollectNarration(_module)));

        Assert.Contains("assets/audio/vo/a.ogg", warning);
        Assert.Contains("track folder", warning);
    }

    [Fact]
    public void Warnings_FileInAnUndeclaredTracksFolder()
    {
        Narrated("A", "a");
        Write(_module, "assets/audio/vo/en-US/robot/a.ogg");

        var warning = Assert.Single(VoiceCoverage.Warnings(
            [Track("en-US", "male")], VoiceCoverage.ListVoiceFiles(_module), VoiceCoverage.CollectNarration(_module)));

        Assert.Contains("en-US/robot", warning);
    }

    [Fact]
    public void Warnings_NoneWhenEveryFileIsReferenced()
    {
        Narrated("A", "a");
        Write(_module, "assets/audio/vo/en-US/male/a.ogg");

        Assert.Empty(VoiceCoverage.Warnings(
            [Track("en-US", "male")], VoiceCoverage.ListVoiceFiles(_module), VoiceCoverage.CollectNarration(_module)));
    }
}
