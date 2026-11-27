using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class VoicePackInstallRulesTests
{
    private static VoicePackManifest Pack(string id, string minVersion = "1.0.0", string moduleId = "test.module", params (string Language, string Track)[] tracks) =>
        new()
        {
            Id = id,
            Title = $"Pack {id}",
            Version = "1.0.0",
            For = new VoicePackTarget { ModuleId = moduleId, MinVersion = minVersion },
            Voices = [.. tracks.Select(t => new VoiceTrack { Language = t.Language, Id = t.Track })],
        };

    private static InstalledModule Module(string version) => new("test.module", version, "Test Module", "", ["en-US"], "hash");

    [Fact]
    public void APackForTheInstalledModuleWithNewTracks_IsAccepted()
    {
        var candidate = Pack("voices.en", tracks: ("en-US", "male"));

        Assert.Null(VoicePackInstallRules.Check(candidate, Module("1.0.0"), installed: []));
    }

    [Fact]
    public void APackNeedingANewerModule_IsRefused_NamingTheVersionNeeded()
    {
        var candidate = Pack("voices.en", minVersion: "1.2.0", tracks: ("en-US", "male"));

        var refusal = VoicePackInstallRules.Check(candidate, Module("1.1.9"), installed: []);

        Assert.Equal(VoicePackRefusalKind.NeedsModuleVersion, refusal?.Kind);
        Assert.Equal("1.2.0", refusal?.ModuleVersion);
    }

    [Fact]
    public void OnlyTheMinimumIsChecked_NotTheNextMajorVersion()
    {
        var candidate = Pack("voices.en", minVersion: "1.0.0", tracks: ("en-US", "male"));

        Assert.Null(VoicePackInstallRules.Check(candidate, Module("3.0.0"), installed: []));
    }

    [Fact]
    public void ATrackAnotherInstalledPackAlreadyAdds_IsRefused()
    {
        var other = Pack("voices.other", tracks: ("en-US", "male"));
        var candidate = Pack("voices.en", tracks: [("en-US", "female"), ("EN-us", "male")]);

        var refusal = VoicePackInstallRules.Check(candidate, Module("1.0.0"), installed: [other]);

        Assert.Equal(VoicePackRefusalKind.TrackAlreadyInstalled, refusal?.Kind);
        Assert.Equal("male", refusal?.Track?.Id);
        Assert.Equal("Pack voices.other", refusal?.InstalledBy);
    }

    [Fact]
    public void AnUpdateOfTheSamePack_MayKeepItsTracks()
    {
        var installed = Pack("voices.en", tracks: ("en-US", "male"));
        var update = Pack("voices.en", tracks: [("en-US", "male"), ("en-US", "female")]);

        Assert.Null(VoicePackInstallRules.Check(update, Module("1.0.0"), installed: [installed]));
    }

    [Fact]
    public void TracksOfPacksForOtherModules_DontConflict()
    {
        var other = Pack("voices.elsewhere", moduleId: "other.module", tracks: ("en-US", "male"));
        var candidate = Pack("voices.en", tracks: ("en-US", "male"));

        Assert.Null(VoicePackInstallRules.Check(candidate, Module("1.0.0"), installed: [other]));
    }

    [Fact]
    public void WithItsModuleNotInstalled_OnlyTheTrackRuleApplies()
    {
        var candidate = Pack("voices.en", minVersion: "9.0.0", tracks: ("en-US", "male"));

        Assert.Null(VoicePackInstallRules.Check(candidate, module: null, installed: []));
    }
}
