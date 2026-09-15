using System.Globalization;
using System.Text;

namespace FlexPad.Core;

/// <summary>One recorded crash.</summary>
/// <param name="WhenUtc">When it was caught.</param>
/// <param name="Version">App version, so a report can be matched to a build.</param>
/// <param name="Platform">Runtime identifier, e.g. <c>win-x64</c> or <c>linux-arm64</c>.</param>
/// <param name="Source">Which handler caught it — <c>unhandled</c>, <c>task</c>, <c>dispatcher</c>.</param>
/// <param name="Detail">Exception type, message and stack, inner exceptions included.</param>
public readonly record struct CrashRecord(
    DateTime WhenUtc, string Version, string Platform, string Source, string Detail);

/// <summary>
/// Formats crash reports for the file the app keeps beside its config, and trims that file to the
/// last few.
///
/// Written for the tester round: a crash that leaves nothing behind is reported as "it closed",
/// which is the position the registration fault put us in — inferring behaviour from the outside
/// because the code never said what it did. On Linux an unhandled exception prints its stack to
/// stderr, which for an app launched from the desktop menu goes somewhere no user will find; on
/// Windows it goes nowhere at all.
///
/// A report is a **block** of lines, not one line — the stack is the useful part. That is the whole
/// reason <see cref="Trim"/> exists separately from a line count: trimming a multi-line format by
/// lines would saw a report in half and leave a stack trace with no header saying which build or
/// which exception it came from.
/// </summary>
public static class CrashReport
{
    /// <summary>Reports to keep. A crash a tester is asked to send should be near the end of a short file.</summary>
    public const int Keep = 10;

    /// <summary>
    /// Cap on one report's detail. A stack overflow or a deep recursive chain can produce megabytes,
    /// and a file too large to open or mail is as useless as no file. Generous enough that a normal
    /// trace with inners survives whole.
    /// </summary>
    public const int MaxDetailChars = 16_000;

    /// <summary>Marks the start of a report. Every line of a report's body is indented, so this is unambiguous.</summary>
    public const string HeaderPrefix = "=== ";

    /// <summary>
    /// One report: a header line carrying the metadata, then the detail indented by two spaces so no
    /// body line can be mistaken for a header — a stack frame from a method whose name begins with the
    /// prefix would otherwise split the report in two when it is read back.
    /// </summary>
    public static string Format(CrashRecord r)
    {
        var sb = new StringBuilder();
        sb.Append(HeaderPrefix)
          .Append(r.WhenUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
          .Append("  ").Append(Clean(r.Version))
          .Append("  ").Append(Clean(r.Platform))
          .Append("  ").Append(Clean(r.Source))
          .Append(" ===\n");

        var detail = r.Detail ?? "";
        if (detail.Length > MaxDetailChars)
            detail = detail[..MaxDetailChars] + "\n… truncated, report exceeded " + MaxDetailChars + " characters";

        foreach (var line in detail.Replace("\r\n", "\n").Split('\n'))
            sb.Append("  ").Append(line.TrimEnd()).Append('\n');

        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Keep the last <paramref name="keep"/> whole reports from a file's contents. Anything before the
    /// first header is dropped: it is either nothing or the tail of a report already half-trimmed, and
    /// a fragment with no header cannot be attributed to a build.
    /// </summary>
    public static string Trim(string? contents, int keep = Keep)
    {
        if (string.IsNullOrWhiteSpace(contents)) return "";

        var reports = Split(contents);
        if (reports.Count <= keep) return string.Concat(reports);
        return string.Concat(reports.Skip(reports.Count - keep));
    }

    /// <summary>Split a file into whole reports, each still carrying its header and trailing blank line.</summary>
    public static IReadOnlyList<string> Split(string? contents)
    {
        var reports = new List<string>();
        if (string.IsNullOrEmpty(contents)) return reports;

        var lines = contents.Replace("\r\n", "\n").Split('\n');

        // A file ending in a newline splits to a final empty element that is the separator's shadow,
        // not a blank line. Re-emitting it would grow the file by one newline per trim, so a report
        // would not survive a round trip through Split.
        var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;

        var current = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var line = lines[i];
            if (line.StartsWith(HeaderPrefix, StringComparison.Ordinal))
            {
                if (current.Length > 0) reports.Add(current.ToString());
                current.Clear();
            }

            // Before the first header there is nothing to append to — drop it rather than inventing
            // a headerless report.
            if (current.Length > 0 || line.StartsWith(HeaderPrefix, StringComparison.Ordinal))
                current.Append(line).Append('\n');
        }

        if (current.Length > 0) reports.Add(current.ToString());
        return reports;
    }

    /// <summary>Timestamp of the newest report, or null if the file holds none that parse.</summary>
    public static DateTime? LastCrashUtc(string? contents)
    {
        DateTime? newest = null;
        foreach (var report in Split(contents))
        {
            var header = report.Split('\n')[0];
            var fields = header[HeaderPrefix.Length..].Split("  ", StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            if (DateTime.TryParse(fields[0], CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)
                && (newest is null || when > newest)) newest = when;
        }
        return newest;
    }

    /// <summary>
    /// Flatten an exception into the report body: type, message and stack for the exception and every
    /// inner one, and every branch of an <see cref="AggregateException"/> — a faulted task reaches the
    /// handler wrapped, and the wrapper's own message says nothing useful.
    /// </summary>
    public static string Describe(Exception? ex, int depth = 0)
    {
        if (ex is null) return "(no exception object — the runtime reported a non-Exception throw)";

        var sb = new StringBuilder();
        var indent = new string(' ', depth * 2);
        sb.Append(indent).Append(ex.GetType().FullName).Append(": ").Append(ex.Message).Append('\n');
        if (!string.IsNullOrWhiteSpace(ex.StackTrace)) sb.Append(ex.StackTrace).Append('\n');

        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
            {
                sb.Append(indent).Append("--- inner ---\n");
                sb.Append(Describe(inner, depth + 1));
            }
        }
        else if (ex.InnerException is { } single)
        {
            sb.Append(indent).Append("--- inner ---\n");
            sb.Append(Describe(single, depth + 1));
        }

        return sb.ToString();
    }

    private static string Clean(string? s) =>
        (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
