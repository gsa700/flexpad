namespace FlexPad.Core;

/// <summary>
/// Builds the detached helper script the updater launches: it waits for the running app to exit,
/// copies the freshly staged executable over the installed one, and relaunches. A running exe can't
/// overwrite itself, hence the wait-then-swap helper.
///
/// The important correctness detail is the copy check. Both <c>Copy-Item</c> (PowerShell) and <c>cp</c>
/// fail non-fatally, so the original scripts relaunched unconditionally — a failed copy (file locked,
/// no write permission) would relaunch the OLD exe while the UI had already said "restarting to
/// apply…", i.e. a silent false success. Here the copy result is checked: on success the new build
/// relaunches; on failure a marker file is dropped (the app surfaces it on next start) and the old
/// exe is relaunched anyway so the user is never left without a running app.
/// </summary>
public static class UpdateApplyScript
{
    /// <param name="workingDirectory">
    /// Directory the relaunched app starts in — the install directory, never the staging one.
    /// Without this the app inherits the helper's directory, and a directory held as a process's
    /// working directory cannot be deleted: the staging folder then survives, and the *next*
    /// update's clean-up of it throws, so updating twice without a restart in between fails.
    /// </param>
    /// <param name="stageRoot">Staging directory to remove once the swap is done.</param>
    /// <param name="scriptPath">This script, removed last so it doesn't linger in temp.</param>
    public static string Windows(int pid, string stagedExe, string targetExe, string failedMarker,
        string workingDirectory, string stageRoot, string scriptPath) =>
        $"while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}\n" +
        $"Copy-Item -LiteralPath '{stagedExe}' -Destination '{targetExe}' -Force\n" +
        // Copy-Item is non-terminating; $? reflects whether it actually succeeded.
        $"if ($?) {{ Remove-Item -LiteralPath '{failedMarker}' -ErrorAction SilentlyContinue }}\n" +
        $"else {{ New-Item -ItemType File -Path '{failedMarker}' -Force | Out-Null }}\n" +
        $"Start-Process -FilePath '{targetExe}' -WorkingDirectory '{workingDirectory}'\n" +
        $"Remove-Item -LiteralPath '{stageRoot}' -Recurse -Force -ErrorAction SilentlyContinue\n" +
        $"Remove-Item -LiteralPath '{scriptPath}' -Force -ErrorAction SilentlyContinue\n";

    /// <inheritdoc cref="Windows"/>
    public static string Unix(int pid, string stagedExe, string targetExe, string failedMarker,
        string workingDirectory, string stageRoot, string scriptPath) =>
        "#!/bin/sh\n" +
        $"while kill -0 {pid} 2>/dev/null; do sleep 0.3; done\n" +
        // Relaunch of the new build is gated on cp succeeding; the else branch records the failure.
        $"if cp -f '{stagedExe}' '{targetExe}'; then\n" +
        $"  chmod +x '{targetExe}'\n" +
        $"  rm -f '{failedMarker}'\n" +
        "else\n" +
        $"  : > '{failedMarker}'\n" +
        "fi\n" +
        // cd first, for the same reason -WorkingDirectory is set on Windows.
        $"(cd '{workingDirectory}' && '{targetExe}' &)\n" +
        $"rm -rf '{stageRoot}'\n" +
        $"rm -f '{scriptPath}'\n";
}
