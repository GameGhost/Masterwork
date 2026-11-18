using Masterwork.App.Shared.Services;
using Masterwork.App.Theme.Test;

namespace Masterwork.Tests;

/// <summary>The test flavor's config, for services that take a <see cref="WhiteLabelConfig"/>.</summary>
internal static class TestWhiteLabel
{
    public static WhiteLabelConfig Config { get; } = new TestTheme().Config;
}
