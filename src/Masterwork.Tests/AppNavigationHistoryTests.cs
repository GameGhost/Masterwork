using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class AppNavigationHistoryTests
{
    [Fact]
    public void TryGoBack_NothingTracked_ReturnsFalse()
    {
        var history = new AppNavigationHistory();

        Assert.False(history.TryGoBack(out var previous));
        Assert.Null(previous);
    }

    [Fact]
    public void TryGoBack_AfterOneTrack_NothingToGoBackToYet()
    {
        // A single Track call just records the starting route — there's nothing *before* it.
        var history = new AppNavigationHistory();
        history.Track("new-game");

        Assert.False(history.TryGoBack(out _));
    }

    [Fact]
    public void TryGoBack_AfterTwoTracks_ReturnsTheFirst()
    {
        var history = new AppNavigationHistory();
        history.Track("");
        history.Track("new-game");

        Assert.True(history.TryGoBack(out var previous));
        Assert.Equal("", previous);
    }

    [Fact]
    public void TryGoBack_PopsMostRecentFirst()
    {
        var history = new AppNavigationHistory();
        history.Track("");
        history.Track("new-game");
        history.Track("continue");

        Assert.True(history.TryGoBack(out var first));
        Assert.Equal("new-game", first);
        Assert.True(history.TryGoBack(out var second));
        Assert.Equal("", second);
        Assert.False(history.TryGoBack(out _));
    }

    [Fact]
    public void Track_SameRouteTwiceInARow_DoesNotPushDuplicate()
    {
        var history = new AppNavigationHistory();
        history.Track("");
        history.Track("new-game");
        history.Track("new-game"); // e.g. a redundant LocationChanged notification

        Assert.True(history.TryGoBack(out var previous));
        Assert.Equal("", previous);
        Assert.False(history.TryGoBack(out _));
    }

    [Fact]
    public void Track_PlayRoute_NeverBecomesABackTarget()
    {
        // The route entered *before* play is still a valid back-target once the player leaves —
        // only "play" itself is excluded from ever being something to return to.
        var history = new AppNavigationHistory();
        history.Track("new-game");
        history.Track("play");
        history.Track("continue");

        Assert.True(history.TryGoBack(out var previous));
        Assert.Equal("new-game", previous);
        Assert.False(history.TryGoBack(out _));
    }

    [Fact]
    public void Track_PlayRouteIsCaseInsensitiveAndLeadingSlashInsensitive()
    {
        var history = new AppNavigationHistory();
        history.Track("new-game");
        history.Track("/Play");
        history.Track("continue");

        Assert.True(history.TryGoBack(out var previous));
        Assert.Equal("new-game", previous);
        Assert.False(history.TryGoBack(out _));
    }
}
