using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class CrashReportTests
{
    private static CrashRecord Rec(string detail, int day = 1, string version = "0.8.0-beta") =>
        new(new DateTime(2026, 8, day, 12, 0, 0, DateTimeKind.Utc), version, "win-x64", "unhandled", detail);

    [Fact]
    public void HeaderCarriesTheMetadataAReportHasToBeMatchedBy()
    {
        var header = CrashReport.Format(Rec("boom")).Split('\n')[0];
        Assert.StartsWith(CrashReport.HeaderPrefix, header);
        Assert.Contains("2026-08-01T12:00:00Z", header);
        Assert.Contains("0.8.0-beta", header);
        Assert.Contains("win-x64", header);
        Assert.Contains("unhandled", header);
    }

    [Fact]
    public void BodyLinesAreIndentedSoNoneCanBeMistakenForAHeader()
    {
        // A stack frame from a method whose name begins with the header prefix would otherwise split
        // one report into two when the file is read back.
        var text = CrashReport.Format(Rec("=== not actually a header ===\nsecond line"));
        var reports = CrashReport.Split(text);
        Assert.Single(reports);
        foreach (var line in text.Split('\n').Skip(1).Where(l => l.Length > 0))
            Assert.StartsWith("  ", line);
    }

    [Fact]
    public void TrimKeepsWholeReportsNotLines()
    {
        // The point of the whole format: a stack trace is many lines, so trimming by line count would
        // leave a body with no header saying which build or which exception it came from.
        var file = string.Concat(Enumerable.Range(1, 5)
            .Select(i => CrashReport.Format(Rec($"line one of {i}\nline two\nline three", day: i))));

        var trimmed = CrashReport.Trim(file, keep: 2);
        var kept = CrashReport.Split(trimmed);

        Assert.Equal(2, kept.Count);
        Assert.All(kept, r => Assert.StartsWith(CrashReport.HeaderPrefix, r));
        Assert.Contains("line one of 4", trimmed);
        Assert.Contains("line one of 5", trimmed);
        Assert.DoesNotContain("line one of 3", trimmed);
    }

    [Fact]
    public void TrimIsANoOpBelowTheLimit()
    {
        var file = CrashReport.Format(Rec("only one"));
        Assert.Equal(file, CrashReport.Trim(file, keep: 10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void TrimSurvivesAnEmptyOrBlankFile(string? contents) =>
        Assert.Equal("", CrashReport.Trim(contents));

    [Fact]
    public void ContentBeforeTheFirstHeaderIsDropped()
    {
        // A half-trimmed file, or one somebody edited. A fragment with no header can't be attributed
        // to a build, so it is worth less than the confusion of keeping it.
        var file = "orphaned stack frame\nanother\n" + CrashReport.Format(Rec("real one"));
        var kept = CrashReport.Split(file);
        Assert.Single(kept);
        Assert.Contains("real one", kept[0]);
    }

    [Fact]
    public void AnOverlongDetailIsTruncatedRatherThanWritten()
    {
        // A file too large to open or mail is as useless as no file.
        var huge = new string('x', CrashReport.MaxDetailChars * 3);
        var text = CrashReport.Format(Rec(huge));
        Assert.True(text.Length < CrashReport.MaxDetailChars * 2, $"not truncated: {text.Length} chars");
        Assert.Contains("truncated", text);
    }

    [Fact]
    public void LastCrashReadsTheNewestHeader()
    {
        var file = CrashReport.Format(Rec("first", day: 1)) + CrashReport.Format(Rec("second", day: 3));
        Assert.Equal(new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc), CrashReport.LastCrashUtc(file));
    }

    [Fact]
    public void LastCrashIsNullWhenThereIsNothingToRead()
    {
        Assert.Null(CrashReport.LastCrashUtc(null));
        Assert.Null(CrashReport.LastCrashUtc("just some text with no header"));
    }

    [Fact]
    public void DescribeCarriesTypeMessageAndStack()
    {
        Exception caught;
        try { throw new InvalidOperationException("the meter went away"); }
        catch (Exception ex) { caught = ex; }

        var d = CrashReport.Describe(caught);
        Assert.Contains("System.InvalidOperationException", d);
        Assert.Contains("the meter went away", d);
        Assert.Contains(nameof(DescribeCarriesTypeMessageAndStack), d);   // the stack, not just the message
    }

    [Fact]
    public void DescribeFollowsTheInnerChain()
    {
        var ex = new InvalidOperationException("outer", new IOException("middle", new TimeoutException("root")));
        var d = CrashReport.Describe(ex);
        Assert.Contains("outer", d);
        Assert.Contains("middle", d);
        Assert.Contains("root", d);
    }

    [Fact]
    public void DescribeUnwrapsEveryBranchOfAnAggregate()
    {
        // A faulted task arrives wrapped, and the wrapper's own message says nothing useful.
        var ex = new AggregateException(new IOException("port gone"), new TimeoutException("no reply"));
        var d = CrashReport.Describe(ex);
        Assert.Contains("port gone", d);
        Assert.Contains("no reply", d);
    }

    [Fact]
    public void DescribeSaysSoWhenTheRuntimeHandedUsNoException()
    {
        // AppDomain.UnhandledException carries an object, not necessarily an Exception.
        Assert.Contains("no exception object", CrashReport.Describe(null));
    }

    [Fact]
    public void AWrittenReportRoundTripsBackOutOfTheFile()
    {
        var file = CrashReport.Format(Rec("alpha", day: 1)) +
                   CrashReport.Format(Rec("bravo", day: 2)) +
                   CrashReport.Format(Rec("charlie", day: 3));

        var reports = CrashReport.Split(file);
        Assert.Equal(3, reports.Count);
        Assert.Contains("alpha", reports[0]);
        Assert.Contains("bravo", reports[1]);
        Assert.Contains("charlie", reports[2]);
    }
}
