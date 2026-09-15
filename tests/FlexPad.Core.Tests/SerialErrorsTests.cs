using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class SerialErrorsTests
{
    [Fact]
    public void Linux_permission_denied_mentions_dialout()
    {
        var msg = SerialErrors.Describe(new UnauthorizedAccessException(), "/dev/ttyUSB0", isLinux: true);
        Assert.Contains("dialout", msg);
        Assert.Contains("/dev/ttyUSB0", msg);
    }

    [Fact]
    public void Windows_access_denied_mentions_another_app()
    {
        var msg = SerialErrors.Describe(new UnauthorizedAccessException(), "COM8", isLinux: false);
        Assert.Contains("COM8", msg);
        Assert.Contains("another app", msg);
        Assert.DoesNotContain("dialout", msg);
    }

    [Fact]
    public void Reconnecting_access_error_suppresses_dialout_hint()
    {
        var msg = SerialErrors.Describe(
            new UnauthorizedAccessException(), "/dev/ttyUSB0", isLinux: true, reconnecting: true);
        Assert.DoesNotContain("dialout", msg);
        Assert.Contains("reconnecting", msg);
        Assert.Contains("/dev/ttyUSB0", msg);
    }

    [Fact]
    public void Reconnecting_access_error_suppresses_in_use_hint_on_windows()
    {
        var msg = SerialErrors.Describe(
            new UnauthorizedAccessException(), "COM8", isLinux: false, reconnecting: true);
        Assert.DoesNotContain("another app", msg);
        Assert.Contains("reconnecting", msg);
        Assert.Contains("COM8", msg);
    }

    [Fact]
    public void Reconnecting_does_not_soften_non_access_errors()
    {
        // A device that's genuinely gone mid-reconnect should still read plainly, not as "reconnecting".
        var msg = SerialErrors.Describe(
            new FileNotFoundException(), "/dev/ttyUSB0", isLinux: true, reconnecting: true);
        Assert.Contains("plugged in", msg);
    }

    [Fact]
    public void Missing_port_asks_if_plugged_in()
    {
        var msg = SerialErrors.Describe(new FileNotFoundException(), "COM8", isLinux: false);
        Assert.Contains("plugged in", msg);
    }

    [Fact]
    public void Unknown_error_falls_back_to_message()
    {
        var msg = SerialErrors.Describe(new InvalidOperationException("boom"), "COM8", isLinux: false);
        Assert.Contains("boom", msg);
    }
}
