using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class VersionOrderTests
{
    [Theory]
    [InlineData("0.10.0-beta", "0.9.0-beta")]     // the numeric trap: 10 > 9, not "1" < "9"
    [InlineData("1.0.0-beta1", "0.10.0-beta")]
    [InlineData("0.9.1-beta", "0.9.0-beta")]
    [InlineData("2.0.0", "1.9.9")]
    public void HigherNumbersWin(string newer, string older) =>
        Assert.True(VersionOrder.IsNewer(newer, older), $"{newer} should be newer than {older}");

    [Theory]
    [InlineData("1.0.0-beta2", "1.0.0-beta1")]
    [InlineData("1.0.0-beta10", "1.0.0-beta2")]   // lexically "beta10" < "beta2"; numerically it isn't
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1")]  // dotted form, the strict-semver spelling
    [InlineData("1.0.0-beta1", "1.0.0-alpha9")]
    public void PreReleaseSuffixesOrderByTrailingNumber(string newer, string older) =>
        Assert.True(VersionOrder.IsNewer(newer, older), $"{newer} should be newer than {older}");

    [Fact]
    public void TheRealReleaseBeatsItsOwnPreReleases()
    {
        // Whoever is left on the last beta has to be offered the final build.
        Assert.True(VersionOrder.IsNewer("1.0.0", "1.0.0-beta1"));
        Assert.True(VersionOrder.IsNewer("1.0.0", "1.0.0-beta9"));
        Assert.False(VersionOrder.IsNewer("1.0.0-beta1", "1.0.0"));
    }

    [Fact]
    public void ThisIsTheCaseTheOldComparerGotWrong()
    {
        // Truncating at the dash made all three of these equal, so a tester on beta1 was never
        // offered anything again. Pinned so it cannot regress.
        Assert.True(VersionOrder.IsNewer("1.0.0-beta2", "1.0.0-beta1"));
        Assert.True(VersionOrder.IsNewer("1.0.0", "1.0.0-beta2"));
    }

    [Fact]
    public void UpgradingFromTheShippedSchemeStillWorks()
    {
        // Every release to date is X.Y.Z-beta. Those users must be offered 1.0.0-beta1.
        foreach (var installed in new[] { "0.6.2-beta", "0.7.1-beta", "0.8.0-beta", "0.9.0-beta" })
            Assert.True(VersionOrder.IsNewer("1.0.0-beta1", installed), $"from {installed}");
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("v1.0.0", "1.0.0")]               // tags carry a leading v, assemblies don't
    [InlineData("1.0.0-beta1", "v1.0.0-beta1")]
    [InlineData("1.0", "1.0.0")]                  // a missing field is zero
    [InlineData("1.0.0+abc123", "1.0.0")]         // build metadata is not part of the order
    [InlineData("1.0.0-beta1+abc", "1.0.0-beta1")]
    public void EquivalentSpellingsAreEqual(string a, string b)
    {
        Assert.Equal(0, VersionOrder.Compare(a, b));
        Assert.False(VersionOrder.IsNewer(a, b));
        Assert.False(VersionOrder.IsNewer(b, a));
    }

    [Fact]
    public void NobodyIsOfferedTheVersionTheyAlreadyHave()
    {
        Assert.False(VersionOrder.IsNewer("0.9.0-beta", "0.9.0-beta"));
        Assert.False(VersionOrder.IsNewer("1.0.0-beta1", "1.0.0-beta1"));
    }

    [Fact]
    public void ABareSuffixPrecedesANumberedOne() =>
        Assert.True(VersionOrder.IsNewer("1.0.0-beta1", "1.0.0-beta"));

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("1.0.0", null)]
    [InlineData("", "1.0.0")]
    [InlineData("not-a-version", "1.0.0")]
    [InlineData("1.0.0", "garbage")]
    public void UnparseableIsUnknownRatherThanNewer(string? a, string? b)
    {
        // A wrong "newer" offers everyone a downgrade; a missed one just delays an update.
        Assert.Null(VersionOrder.Compare(a, b));
        Assert.False(VersionOrder.IsNewer(a, b));
    }

    [Fact]
    public void OrdersAWholeBetaSeriesTheWayAPersonWould()
    {
        var released = new[]
        {
            "0.9.0-beta", "0.10.0-beta", "1.0.0-beta1", "1.0.0-beta2",
            "1.0.0-beta10", "1.0.0-rc1", "1.0.0", "1.0.1",
        };
        for (var i = 1; i < released.Length; i++)
            Assert.True(VersionOrder.IsNewer(released[i], released[i - 1]),
                $"{released[i]} should follow {released[i - 1]}");
    }
}
