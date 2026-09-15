// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

using System.Text.RegularExpressions;

namespace FlexPad.Core;

public sealed class SequenceException(string message) : Exception(message);

/// <summary>What one line of a button's command list means.</summary>
public enum LineType { Skip, Wait, Command }

public readonly record struct SequenceLine(LineType Type, string Command, double WaitSeconds);

/// <summary>
/// The button language: one command per line, <c>#</c> comments, <c>wait N</c> pauses, and the
/// placeholders <c>{slice}</c> (active slice index), <c>{tx}</c> (transmit slice) and
/// <c>{A}</c>..<c>{H}</c> (slice by letter). Pure, so every rule here is unit-tested.
/// </summary>
public static partial class CommandSequence
{
    [GeneratedRegex(@"\{(slice|tx|[A-H])\}")]
    private static partial Regex Placeholder();

    public static SequenceLine Classify(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) return new SequenceLine(LineType.Skip, "", 0);
        if (line.StartsWith("wait ", StringComparison.OrdinalIgnoreCase))
        {
            var arg = line[5..].Trim();
            if (!double.TryParse(arg, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var secs) || secs < 0)
                throw new SequenceException($"bad wait line: {line}");
            return new SequenceLine(LineType.Wait, "", secs);
        }
        return new SequenceLine(LineType.Command, line, 0);
    }

    /// <summary>Fill placeholders from the live slice table. Throws when one can't be resolved.</summary>
    public static string Substitute(string command, SliceTable slices)
    {
        return Placeholder().Replace(command, m =>
        {
            var key = m.Groups[1].Value;
            var (idx, what) = key switch
            {
                "slice" => (slices.Active(), "no active slice"),
                "tx" => (slices.Tx(), "no transmit slice"),
                _ => (slices.ByLetter(key), $"no slice {key}"),
            };
            return idx ?? throw new SequenceException($"{what} - cannot fill {{{key}}}");
        });
    }

    /// <summary>
    /// Run a button's lines in order. <paramref name="send"/> is synchronous and returns the radio's
    /// reply code and text; a non-zero code is an error. Returns true when every line succeeded.
    /// </summary>
    /// <param name="report">Called with each error message as it happens.</param>
    public static bool Run(IEnumerable<string> lines, SliceTable slices,
        Func<string, (int Code, string Text)> send, bool stopOnError, Action<string>? report = null,
        Action<double>? sleep = null)
    {
        sleep ??= s => Thread.Sleep(TimeSpan.FromSeconds(s));
        var ok = true;
        foreach (var raw in lines)
        {
            SequenceLine parsed;
            try { parsed = Classify(raw); }
            catch (SequenceException ex)
            {
                report?.Invoke(ex.Message);
                ok = false;
                if (stopOnError) return false;
                continue;
            }
            switch (parsed.Type)
            {
                case LineType.Skip:
                    continue;
                case LineType.Wait:
                    sleep(parsed.WaitSeconds);
                    continue;
            }

            string cmd;
            try { cmd = Substitute(parsed.Command, slices); }
            catch (SequenceException ex)
            {
                report?.Invoke($"error: {ex.Message}");
                ok = false;
                if (stopOnError) return false;
                continue;
            }

            (int code, string text) reply;
            try { reply = send(cmd); }
            catch (Exception ex)
            {
                report?.Invoke($"error: {ex.Message}");
                ok = false;
                if (stopOnError) return false;
                continue;
            }
            if (reply.code != 0)
            {
                report?.Invoke($"error 0x{reply.code:X} {reply.text} <- {cmd}");
                ok = false;
                if (stopOnError) return false;
            }
        }
        return ok;
    }
}
