using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class LanguageNamesTests
{
    [Theory]
    [InlineData("en-US", "English (United States)")]
    [InlineData("fr-CA", "Français (Canada)")]
    [InlineData("cs-CZ", "Čeština (Česko)")]
    [InlineData("de", "Deutsch")]
    public void Native_IsTheLanguagesOwnNameCapitalized(string code, string expected) =>
        Assert.Equal(expected, LanguageNames.Native(code));

    [Theory]
    [InlineData("xx-YY")]
    [InlineData("not a code")]
    [InlineData("")]
    public void Native_UnknownCode_IsTheCode(string code) =>
        Assert.Equal(code, LanguageNames.Native(code));
}
