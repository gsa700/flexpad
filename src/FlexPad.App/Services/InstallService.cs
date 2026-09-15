using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using FlexPad.Core;

namespace FlexPad.App.Services;

/// <summary>What an uninstall should take with it besides the program itself.</summary>
/// <param name="RemoveSettings">
/// Delete <c>config.json</c> (and its <c>.bak</c>). Defaults to false at every call site: settings
/// are cheap to recreate but hold the meter list and each cable's chip-serial pinning, which is
/// fiddly enough to redo that silently discarding it would be rude.
/// </param>
public readonly record struct UninstallOptions(bool RemoveSettings);

/// <summary>Outcome of an install.</summary>
/// <param name="ExePath">The installed executable.</param>
/// <param name="Registered">
/// Whether the desktop integration is in place: the Start Menu shortcut on Windows, the <c>.desktop</c>
/// entry on Linux. The install itself succeeded either way — the program is copied and runs — but
/// when this is false it has no menu entry, which is worth telling the user rather than reporting a
/// clean install.
/// </param>
public readonly record struct InstallResult(string ExePath, bool Registered);

/// <summary>
/// An install could not proceed for a reason the user can act on — almost always because the
/// installed copy is still running. Carries a message meant to be shown as-is.
/// </summary>
public sealed class InstallBlockedException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Installs and removes the per-user copy of the app. Ported from LP-100A Monitor, which
/// established this shape for the station tools; the differences are noted where they occur.
///
/// Per-user by necessity, not preference: <see cref="UpdateService.ApplyAndRestart"/> replaces the
/// running executable in place, which needs no elevation under %LOCALAPPDATA% and would need it on
/// every single update under Program Files. A machine-wide install would quietly break the updater.
///
/// There is deliberately <b>no installed-apps registry entry on Windows</b>, so the app does not appear
/// in Settings → Apps and is removed from its own Setup instead. It used to write one, and from a shell
/// launch the write never reached the registry: Windows' Program Compatibility Assistant attaches a
/// compatibility layer to this unsigned exe whenever Explorer or the updater's helper starts it, and
/// that layer virtualises every registry write — reg.exe and in-process alike, children included — into
/// an overlay the process reads back consistently and loses on exit. So the app wrote the entry,
/// verified it, and reported success, and the real key never changed. A manifest opt-out, in-process
/// writes and a scrubbed relaunch were all tested and none escaped it; the one untested lever is an
/// Authenticode signature. Rather than ship a feature that reports success while doing nothing, the
/// registry was taken out (BACKLOG, 2026-09-04). If the exe is ever signed, the registration code is
/// one commit back in history. Windows integration is therefore shortcuts only, which are files and
/// were never affected.
/// </summary>
public static class InstallService
{
    /// <summary>Display name, used for the installed-apps entry and the Start Menu shortcut.</summary>
    public const string DisplayName = "FlexPad";

    private const string Description = "Programmable command buttons and FlexControl knob for a FlexRadio";

    public static string ExeFileName => OperatingSystem.IsWindows() ? "FlexPad.exe" : "FlexPad";

    /// <summary>Full path of the running executable.</summary>
    public static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot determine the current executable path.");

    public static string ExeDirectory => Path.GetDirectoryName(ExePath)!;

    /// <summary>
    /// Per-user programs directory: <c>%LOCALAPPDATA%\Programs</c> on Windows,
    /// <c>~/.local/share</c> on Linux. <see cref="Environment.SpecialFolder.LocalApplicationData"/>
    /// already resolves to the right base on both; only Windows wants the extra <c>Programs</c>
    /// level, because <c>~/.local/share</c> is itself where per-user application data belongs.
    /// </summary>
    public static string ProgramsDirectory
    {
        get
        {
            var b = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return OperatingSystem.IsWindows() ? Path.Combine(b, "Programs") : b;
        }
    }

    private static string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Where the menu entry goes: <c>~/.local/share/applications</c>.</summary>
    private static string DesktopFilePath =>
        Path.Combine(ProgramsDirectory, "applications", DesktopEntry.FileName);

    /// <summary>Icon path in the XDG hicolor theme, at the 256px size the embedded PNG carries.</summary>
    private static string IconFilePath => Path.Combine(
        ProgramsDirectory, "icons", "hicolor", "256x256", "apps", "flexpad.png");

    /// <summary>
    /// Convenience symlink so <c>flexpad</c> works from a terminal. <c>~/.local/bin</c> is on
    /// PATH by default on Raspberry Pi OS and most desktop distributions.
    /// </summary>
    private static string SymlinkPath =>
        Path.Combine(HomeDirectory, ".local", "bin", "flexpad");

    public static string InstallDirectory => InstallLayout.InstallDirectoryUnder(ProgramsDirectory);

    public static string InstalledExePath => Path.Combine(InstallDirectory, ExeFileName);

    /// <summary>Directories accepted as installed — the canonical one plus pre-installer hand-installs.</summary>
    public static IEnumerable<string> InstalledDirectories =>
        InstallLayout.InstalledDirectoriesUnder(ProgramsDirectory);

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", DisplayName + ".lnk");

    /// <summary>
    /// The user's desktop directory, or null if there isn't one. On Windows this is a known folder
    /// and always exists. On Linux it comes from <c>XDG_DESKTOP_DIR</c> in
    /// <c>~/.config/user-dirs.dirs</c>, because <c>~/Desktop</c> is an assumption: the directory is
    /// localised, and a user can disable it entirely. Null means "don't put a shortcut anywhere".
    /// </summary>
    /// <remarks>
    /// .NET's <see cref="Environment.SpecialFolder.DesktopDirectory"/> is deliberately not trusted on
    /// Linux — it returns <c>$HOME/Desktop</c> whether or not that is where the desktop is, or whether
    /// one exists at all. The v0.7.0-beta symlink bug came from taking a BCL call's Linux behaviour on
    /// faith, so this reads the file the desktop environment actually uses.
    /// </remarks>
    private static string? DesktopDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var d = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return string.IsNullOrEmpty(d) ? null : d;
            }

            var conf = Path.Combine(HomeDirectory, ".config", "user-dirs.dirs");
            var dir = XdgUserDirs.Resolve(TryReadAllText(conf), XdgUserDirs.DesktopKey, HomeDirectory);

            // No user-dirs.dirs at all is the minimal-install case rather than a refusal, so fall back
            // to ~/Desktop — but only if it is already there. Creating it would invent a desktop on a
            // machine that hasn't got one.
            if (dir is null)
            {
                var guess = Path.Combine(HomeDirectory, "Desktop");
                return Directory.Exists(guess) ? guess : null;
            }
            return dir;
        }
    }

    /// <summary>Desktop shortcut this installer owns.</summary>
    private static string? DesktopShortcutPath => DesktopDirectory is { } d
        ? Path.Combine(d, OperatingSystem.IsWindows() ? DisplayName + ".lnk" : DesktopEntry.FileName)
        : null;

    /// <summary>
    /// Desktop launchers from the retired <c>install-desktop-shortcut.sh</c> that shipped in the
    /// pre-installer zips. Adopted rather than ignored: left alone they sit beside the installer's own
    /// launcher as a second, identical-looking icon pointing at a download folder that no longer
    /// exists — the same duplicate trap <see cref="InstallLayout.LegacyFolders"/> avoids for install
    /// directories.
    /// </summary>
    private static IEnumerable<string> LegacyDesktopShortcuts()
    {
        if (DesktopDirectory is not { } d) yield break;
        if (OperatingSystem.IsWindows()) yield break;
        yield return Path.Combine(d, "flexpad.desktop");   // no hyphen: what the old script wrote
    }

    /// <summary>How this copy is running. Derived from its path every time — never cached or stored.</summary>
    public static InstallMode Mode => InstallLayout.Detect(
        ExeDirectory,
        File.Exists(Path.Combine(ExeDirectory, InstallLayout.PortableMarker)),
        InstallDirectory,
        InstalledDirectories);

    /// <summary>
    /// Copy this executable into the install directory and register it. Returns the path of the
    /// installed copy, which the caller should launch before exiting.
    /// </summary>
    /// <remarks>
    /// Copying only the executable is sufficient because the published build is self-contained and
    /// single-file — there is no payload beside it to keep in step. Settings already live in
    /// <see cref="ConfigStore.DataDir"/>, so an install picks up whatever was there before and an
    /// uninstall can leave it behind.
    /// </remarks>
    public static InstallResult Install()
    {
        Directory.CreateDirectory(InstallDirectory);

        var target = InstalledExePath;
        if (!InstallLayout.SamePath(ExeDirectory, InstallDirectory))
        {
            try
            {
                File.Copy(ExePath, target, overwrite: true);
            }
            catch (IOException ex)
            {
                // Running a newly downloaded copy while the installed one is still open is an
                // ordinary thing to do, and Windows will not let the open executable be replaced.
                // Say that, rather than surfacing a raw sharing violation from File.Copy.
                throw new InstallBlockedException(
                    $"{DisplayName} is already running from the install folder. "
                    + "Close it and try installing again.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InstallBlockedException(
                    $"Could not write to {InstallDirectory}. Check the folder's permissions.", ex);
            }
        }

        // A copied executable arrives without its executable bit on Unix; without this the
        // installed copy and the menu entry both silently fail to launch.
        if (!OperatingSystem.IsWindows()) MakeExecutable(target);

        return new InstallResult(target, Register(target));
    }

    /// <summary>
    /// Give the copy at <paramref name="exePath"/> its desktop integration: Start Menu and desktop
    /// shortcuts on Windows; a <c>.desktop</c> entry, icon, <c>~/.local/bin</c> symlink and desktop
    /// launcher on Linux. Safe to call repeatedly — everything overwrites or skips rather than
    /// duplicating.
    /// </summary>
    /// <returns>Whether the menu entry — the one piece that makes the app launchable — is on disk afterwards.</returns>
    public static bool Register(string exePath) =>
        OperatingSystem.IsWindows() ? RegisterWindows(exePath) : RegisterUnix(exePath);

    /// <returns>Whether the desktop entry is on disk afterwards — the icon and the
    /// <c>~/.local/bin</c> symlink are conveniences, but without the entry there is no menu item.</returns>
    /// <remarks>
    /// Runs on every launch now, so each step skips itself when the result is already correct. The
    /// cost that mattered was <c>update-desktop-database</c>, which is a process spawn and a directory
    /// rebuild; it now runs only when the entry's contents actually changed.
    /// </remarks>
    private static bool RegisterUnix(string exePath)
    {
        // Write the icon first: the entry should not reference a file that isn't there yet. Only if
        // it's missing — the bytes are baked into this build and can't have drifted.
        string? icon = null;
        try
        {
            if (File.Exists(IconFilePath))
            {
                icon = IconFilePath;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IconFilePath)!);
                using var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("app-icon.png");
                if (src is not null)
                {
                    using var dst = File.Create(IconFilePath);
                    src.CopyTo(dst);
                    icon = IconFilePath;
                }
            }
        }
        catch (IOException) { /* an entry without an icon still launches */ }
        catch (UnauthorizedAccessException) { }

        var wanted = DesktopEntry.Build(DisplayName, exePath, icon, Description);
        var current = TryReadAllText(DesktopFilePath);
        if (current != wanted)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DesktopFilePath)!);
            File.WriteAllText(DesktopFilePath, wanted);

            // Some environments only notice a changed entry once the database is rebuilt; others watch
            // the directory. Best effort, and harmless where the tool isn't installed.
            Run("update-desktop-database", [Path.GetDirectoryName(DesktopFilePath)!]);
        }

        try
        {
            // Replaces the link only if it's missing or aimed somewhere else — an install directory
            // that moved, say. Note this catch guards only the *creation*: probing for an absent link
            // used to throw FileNotFoundException from in here, which — being an IOException — landed
            // in the handler below and skipped the create, so on Linux the symlink was never made on
            // any launch. Symlink.ResolveTarget answers "no link" with null for that reason.
            Symlink.Ensure(SymlinkPath, exePath);
        }
        catch (IOException) { /* the menu entry is the point; the symlink is a convenience */ }
        catch (UnauthorizedAccessException) { }

        EnsureDesktopShortcut(exePath, Path.GetDirectoryName(exePath)!);

        return File.Exists(DesktopFilePath);
    }

    /// <summary>
    /// Put a launcher on the desktop, unless something is already there. Returns a short note on
    /// what it did, for diagnostics.
    /// </summary>
    /// <remarks>
    /// **Never overwrites.** An existing file at that path is the user's — they may have moved it,
    /// renamed the target, pointed it at a different profile, or made it by hand — and replacing it on
    /// every launch would undo that silently and repeatedly, since this runs at every start. The only
    /// file it will replace is a legacy launcher from the retired shell script, which is adopted
    /// precisely so it stops being a second dead icon beside the real one.
    ///
    /// A desktop <c>.desktop</c> file must be executable or the desktop treats it as untrusted and
    /// offers to run it as a program instead of launching it — the confirmation prompt that made the
    /// stale CM5 shortcut so puzzling. Nothing here is worth failing an install over.
    /// </remarks>
    private static string EnsureDesktopShortcut(string exePath, string workingDirectory)
    {
        if (DesktopShortcutPath is not { } shortcut) return "no desktop directory";

        try
        {
            // Adopt first: a legacy launcher is replaced by ours at the canonical name, so the user
            // ends up with one working icon rather than one working and one dead.
            var adopted = false;
            foreach (var legacy in LegacyDesktopShortcuts())
            {
                if (!File.Exists(legacy) || InstallLayout.SamePath(legacy, shortcut)) continue;
                TryDelete(legacy);
                adopted = true;
            }

            if (File.Exists(shortcut))
                return adopted ? "desktop shortcut kept, legacy one removed" : "desktop shortcut already there";

            if (OperatingSystem.IsWindows())
            {
                CreateShortcut(shortcut, exePath, workingDirectory, Description);
            }
            else
            {
                var icon = File.Exists(IconFilePath) ? IconFilePath : null;
                File.WriteAllText(shortcut, DesktopEntry.Build(DisplayName, exePath, icon, Description));
                MakeExecutable(shortcut);
            }

            return File.Exists(shortcut)
                ? adopted ? "desktop shortcut created, legacy one removed" : "desktop shortcut created"
                : "desktop shortcut could not be created";
        }
        catch (IOException ex) { return $"desktop shortcut failed: {ex.Message}"; }
        catch (UnauthorizedAccessException ex) { return $"desktop shortcut failed: {ex.Message}"; }
    }

    private static string? TryReadAllText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Windows integration is shortcuts only — Start Menu and desktop. There is deliberately no
    /// installed-apps registry entry; see the class remarks. Returns whether the Start Menu shortcut,
    /// the one that makes the app findable, is on disk afterwards.
    /// </summary>
    private static bool RegisterWindows(string exePath)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var dir = Path.GetDirectoryName(exePath)!;
        CreateShortcut(StartMenuShortcut, exePath, dir, Description);
        EnsureDesktopShortcut(exePath, dir);
        return File.Exists(StartMenuShortcut);
    }

    /// <summary>
    /// Called at every startup of an installed copy: re-asserts the Start Menu shortcut on Windows and
    /// the menu entry, icon and symlink on Linux, so a copy adopted from a pre-installer folder gets its
    /// integration without being reinstalled. Loose and portable copies are left alone.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (Mode != InstallMode.Installed) return;
        Register(ExePath);
    }

    /// <summary>
    /// Whether the copy is launchable from the desktop environment's menu: the Start Menu shortcut on
    /// Windows, the <c>.desktop</c> entry on Linux.
    /// </summary>
    public static bool IsRegistered() =>
        File.Exists(OperatingSystem.IsWindows() ? StartMenuShortcut : DesktopFilePath);

    /// <summary>
    /// Remove the registrations, then hand off to a detached helper that deletes the install
    /// directory once this process has exited. The caller must exit immediately after.
    /// </summary>
    /// <remarks>
    /// A running executable cannot delete itself, which is the same constraint
    /// <see cref="UpdateService.ApplyAndRestart"/> works around; this uses the same trampoline
    /// shape. The helper is written to the temp directory rather than the install directory,
    /// because the install directory is what it is about to remove.
    /// </remarks>
    public static void Uninstall(UninstallOptions options)
    {
        Unregister();

        var toDelete = new List<string>();

        // Only ever remove a directory the app owns. Mode is derived from the path, so a copy being
        // run from a download folder is Loose and its directory is not ours — and if someone
        // extracted the exe straight into Downloads, ExeDirectory *is* Downloads. Deleting it
        // recursively because the user asked to uninstall a program would be indefensible. An
        // Installed copy's directory is private to the app (canonical or an adopted legacy folder),
        // so that one can go whole; a shared directory such as ~/.local/bin never can, which is why
        // Unregister removes its items one file at a time.
        // (LP-100A had deleted ExeDirectory unconditionally; it took this guard in v0.9.19-beta.)
        if (Mode == InstallMode.Installed) toDelete.Add(ExeDirectory);

        toDelete.AddRange(DataFilesToRemove(options));

        var pid = Environment.ProcessId;

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), "flexpad-uninstall.ps1");
            var lines = new List<string>
            {
                $"while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}",
            };
            // Retried for up to ten seconds rather than attempted once. The wait loop above sees the
            // process id vanish a moment before the executable's mapping is released, and a single
            // Remove-Item in that gap fails on the locked exe — silently, because the helper has no
            // window and no one to tell. (Ported from LP-100A, 2026-09-04.)
            lines.AddRange(toDelete.Select(p =>
            {
                var q = p.Replace("'", "''");
                return $"for ($i = 0; $i -lt 40 -and (Test-Path -LiteralPath '{q}'); $i++) {{ " +
                       $"Remove-Item -LiteralPath '{q}' -Recurse -Force -ErrorAction SilentlyContinue; " +
                       $"if (Test-Path -LiteralPath '{q}') {{ Start-Sleep -Milliseconds 250 }} }}";
            }));
            // Take the helper with it, so an uninstall doesn't leave its own tooling behind in temp.
            lines.Add($"Remove-Item -LiteralPath '{script.Replace("'", "''")}' -Force -ErrorAction SilentlyContinue");

            File.WriteAllText(script, string.Join("\n", lines) + "\n");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                // The helper must not inherit this process's working directory: an installed copy
                // runs with its own folder as the working directory, and Windows will not remove a
                // directory that is any live process's current directory — including the one doing
                // the removing. Without this the helper deletes the files and then fails on the
                // folder itself, every time, from inside it. (Ported from LP-100A, 2026-09-04.)
                WorkingDirectory = Path.GetTempPath(),
            });
        }
        else
        {
            var script = Path.Combine(Path.GetTempPath(), "flexpad-uninstall.sh");
            var lines = new List<string>
            {
                "#!/bin/sh",
                $"while kill -0 {pid} 2>/dev/null; do sleep 0.3; done",
            };
            lines.AddRange(toDelete.Select(p => $"rm -rf {ShellQuote(p)}"));
            lines.Add($"rm -f {ShellQuote(script)}");

            File.WriteAllText(script, string.Join("\n", lines) + "\n");
            MakeExecutable(script);
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { script },
                UseShellExecute = false,
                // Same reason as the Windows branch. Linux will unlink a directory that is a process's
                // cwd, but the helper then sits in a deleted directory, and nothing about that is
                // worth keeping.
                WorkingDirectory = Path.GetTempPath(),
            });
        }
    }

    /// <summary>
    /// Wrap a path in single quotes for /bin/sh, closing and reopening around any single quote it
    /// contains. Paths come from the environment, so they are not assumed to be tame.
    /// </summary>
    private static string ShellQuote(string path) => "'" + path.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Which files under the data directory an uninstall should take. The directory itself is never
    /// removed wholesale — only the files actually consented to are listed, so anything a future
    /// version puts there is not swept up by an uninstall that predates it.
    /// </summary>
    public static IEnumerable<string> DataFilesToRemove(UninstallOptions options)
    {
        if (!options.RemoveSettings) yield break;

        var config = ConfigStore.ConfigFilePath;
        if (File.Exists(config)) yield return config;

        // AtomicFile keeps the last readable config beside it when a load fails; leaving that behind
        // would resurrect the meter list on the next install.
        var bak = config + ".bak";
        if (File.Exists(bak)) yield return bak;
    }

    private static void Unregister()
    {
        // The desktop directory is the user's, shared with everything else they keep there — so this
        // is a named file, never a sweep. Legacy launchers go too: leaving one behind after an
        // uninstall means a dead icon pointing at a program that isn't there any more.
        if (DesktopShortcutPath is { } shortcut) TryDelete(shortcut);
        foreach (var legacy in LegacyDesktopShortcuts()) TryDelete(legacy);

        if (OperatingSystem.IsWindows())
        {
            TryDelete(StartMenuShortcut);
            return;
        }

        // Each removed as a single file. ~/.local/bin and the icon theme are shared directories:
        // nothing here may delete a directory it does not own.
        TryDelete(DesktopFilePath);
        TryDelete(IconFilePath);
        TryDelete(SymlinkPath);
        Run("update-desktop-database", [Path.GetDirectoryName(DesktopFilePath)!]);
    }

    private static void TryDelete(string path)
    {
        try
        {
            // Ask the link itself as well as File.Exists: whether File.Exists follows a dangling
            // symlink varies by runtime (measured true on .NET 10 / linux-arm64, false on the
            // runtime this was first written against), so neither question alone reliably finds a
            // link whose target is already gone.
            if (File.Exists(path) || Symlink.ResolveTarget(path) is not null)
                File.Delete(path);
        }
        catch (IOException) { /* a locked or vanished file is not worth failing an uninstall over */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Launch a copy of the app detached from this process.</summary>
    /// <remarks>
    /// The working directory is set explicitly, and must stay that way. A child process otherwise
    /// inherits this one's, and after an install that is the folder the user just installed FROM —
    /// which Windows then refuses to delete, because a live process's current directory cannot be
    /// removed. The install appears to finish and the download folder becomes undeletable for as
    /// long as the app runs, with nothing on screen connecting the two.
    /// </remarks>
    public static void LaunchDetached(string exePath) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = true,
        });

    /// <summary>Run a console tool with no window and return its exit code (-1 if it wouldn't start).</summary>
    private static int Run(string fileName, IEnumerable<string> arguments) =>
        RunCaptured(fileName, arguments).Code;

    /// <summary>
    /// Run a console tool and return its exit code together with everything it printed. Both streams
    /// are drained before waiting: redirecting a pipe and never reading it is how a child that prints
    /// more than the buffer holds ends up blocked on write while the parent blocks in WaitForExit.
    /// reg.exe prints far too little for that today, which is exactly the kind of "fine until it
    /// isn't" that is cheaper to fix than to remember.
    ///
    /// The output matters for diagnosis: reg.exe writes "The operation completed successfully." to
    /// **stderr**, so stderr carrying text is not by itself a failure signal.
    /// </summary>
    private static (int Code, string Output) RunCaptured(string fileName, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (-1, "");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, (stdout + " " + stderr).Trim());
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, ""); }
    }

    /// <summary>
    /// Write a .lnk via Windows Script Host. Reached by reflection rather than a <c>dynamic</c>
    /// call so nothing depends on the C# runtime binder being present in a single-file build.
    /// A missing shortcut is not worth failing an install over, so every failure here is swallowed.
    /// </summary>
    private static void CreateShortcut(string lnkPath, string target, string workingDirectory, string description)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;

            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);

            var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [lnkPath]);
            if (link is null) return;

            var linkType = link.GetType();
            void Set(string property, object value) =>
                linkType.InvokeMember(property, BindingFlags.SetProperty, null, link, [value]);

            Set("TargetPath", target);
            Set("WorkingDirectory", workingDirectory);
            Set("IconLocation", target + ",0");
            Set("Description", description);
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        catch (Exception)
        {
            // Windows Script Host can be disabled by policy. The app is fully usable without a
            // Start Menu entry, so this must not take the install down with it.
        }
    }
}
