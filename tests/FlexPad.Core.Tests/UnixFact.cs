using Xunit;

namespace FlexPad.Core.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that skips on Windows, for tests that must really create a symlink.
/// Windows needs Developer Mode or elevation for that, so on a plain developer box these would fail
/// for a reason that has nothing to do with the code under test.
/// </summary>
/// <remarks>
/// Deliberately a skip rather than an early <c>return</c>: a test that quietly passes without
/// asserting anything is the same silent no-op that made the symlink bug survive in the first place.
/// The runner should say it was skipped.
/// </remarks>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Creating a symlink on Windows needs Developer Mode or elevation.";
    }
}
