using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class SemVerTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.2.0", "1.1.0", 1)]
    [InlineData("1.1.0", "1.2.0", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.0.10", "1.0.9", 1)]
    public void Compare_OrdersVersionsCorrectly(string a, string b, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(SemVer.Compare(a, b)));
    }

    [Fact]
    public void MajorOrMinorDiffers_FalseForPatchOnlyChange()
    {
        Assert.False(SemVer.MajorOrMinorDiffers("1.2.0", "1.2.5"));
    }

    [Theory]
    [InlineData("2.0.0", "1.0.0")]
    [InlineData("1.3.0", "1.2.0")]
    public void MajorOrMinorDiffers_TrueForMajorOrMinorChange(string a, string b)
    {
        Assert.True(SemVer.MajorOrMinorDiffers(a, b));
    }
}
