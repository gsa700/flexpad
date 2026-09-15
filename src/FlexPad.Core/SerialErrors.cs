namespace FlexPad.Core;

/// <summary>
/// Turns raw serial exceptions into actionable, platform-aware messages. The big one on
/// Linux/Pi is permission denied on /dev/tty* when the user isn't in the 'dialout' group;
/// on Windows the same exception usually means another app already holds the port.
/// </summary>
public static class SerialErrors
{
    public static string Describe(Exception ex, string port, bool isLinux, bool reconnecting = false)
    {
        // A transient access error mid-reconnect just means the device is still re-enumerating after a
        // replug (on Linux udev hasn't re-applied the 'dialout' perms yet; on Windows the old handle is
        // still tearing down) — not a real permissions problem. Skip the alarming dialout / "another app"
        // hint and show a calm status; a genuine first-connect denial (reconnecting: false) still gets it.
        if (reconnecting && ex is UnauthorizedAccessException)
            return $"{port} reconnecting…";

        return ex switch
        {
            UnauthorizedAccessException when isLinux =>
                $"Permission denied on {port}. Add your user to the 'dialout' group: " +
                "sudo usermod -aG dialout $USER  (then log out and back in).",
            UnauthorizedAccessException =>
                $"{port} is in use or access denied — another app may have it open.",
            FileNotFoundException =>
                $"{port} not found — is the FlexControl plugged in?",
            System.IO.IOException =>
                $"{port} could not be opened — check the cable.",
            _ => $"Error: {ex.Message}",
        };
    }
}
