using System;
using System.IO;
using System.Threading.Tasks;
using FlexPad.Core;

namespace FlexPad.App.Services;

/// <summary>
/// Records unhandled exceptions to a file beside the config, so a crash leaves something a user can
/// send instead of the report being "it closed".
///
/// Wired as early as <c>Main</c> allows, before Avalonia starts, so a failure during startup — the
/// part a tester on an unfamiliar distribution is most likely to hit — is caught as well.
///
/// <b>Write on crash, tidy on start.</b> The crash path only ever appends: the process is already
/// dying, may have moments to live, and reading the file back to trim it is work that can fail
/// halfway and lose the report that mattered. Trimming reads and rewrites the whole file, so it runs
/// at startup, where there is time and nothing at stake.
/// </summary>
public static class CrashLog
{
    /// <summary>The file testers are asked to attach. Named in the README for that reason.</summary>
    public static string FilePath => Path.Combine(ConfigStore.DataDir, "crash.log");

    private static bool _installed;

    /// <summary>
    /// Attach the handlers and tidy last run's file. Safe to call twice; does nothing the second time.
    /// </summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        // Fires for anything that reaches the top of any thread, including the UI thread, and is the
        // handler that catches the crashes users actually see.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("unhandled", e.ExceptionObject as Exception);

        // A faulted task nobody awaited. This app launches several deliberately (`_ = SomeAsync()`),
        // and while each has its own try/catch, an exception thrown outside those would otherwise be
        // swallowed by the finalizer with nothing recorded anywhere.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("task", e.Exception);
            e.SetObserved();   // recorded — don't let it escalate on top of having been logged
        };

        Tidy();
    }

    /// <summary>Append one report. Deliberately the simplest thing that can work — see the class remarks.</summary>
    public static void Write(string source, Exception? ex)
    {
        try
        {
            var record = new CrashRecord(
                DateTime.UtcNow,
                UpdateService.CurrentVersion,
                UpdateService.Rid(),
                source,
                CrashReport.Describe(ex));

            File.AppendAllText(FilePath, CrashReport.Format(record));
        }
        catch
        {
            // Nothing useful is left to do: the process is on its way out, and a handler that throws
            // during shutdown replaces a recorded crash with an unrecorded one.
        }
    }

    /// <summary>Keep the file to the last few reports. Startup only, where a rewrite is safe.</summary>
    private static void Tidy()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var contents = File.ReadAllText(FilePath);
            var trimmed = CrashReport.Trim(contents);
            if (trimmed.Length != contents.Length) File.WriteAllText(FilePath, trimmed);
        }
        catch (IOException) { /* a fat log is not worth failing a launch over */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// When the newest recorded crash happened, or null if there are none. Read at startup, before
    /// this run can add to it, so it answers "did the last run end badly".
    /// </summary>
    public static DateTime? LastCrashUtc()
    {
        try
        {
            return File.Exists(FilePath) ? CrashReport.LastCrashUtc(File.ReadAllText(FilePath)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
