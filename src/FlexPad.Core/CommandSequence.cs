// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

using System.Text.RegularExpressions;

namespace FlexPad.Core;

public sealed class SequenceException(string message) : Exception(message);

/// <summary>What one line of a button's command list means.</summary>
public enum LineType { Skip, Wait, Slices, Command }

/// <param name="Command">The command text; for a <see cref="LineType.Slices"/> line, the wanted
/// letters separated by spaces ("A B").</param>
public readonly record struct SequenceLine(LineType Type, string Command, double WaitSeconds);

/// <summary>
/// The button language: one command per line, <c>#</c> comments, <c>wait N</c> pauses,
/// <c>slices A B</c> (open or close slices until exactly those letters exist), and the
/// placeholders <c>{slice}</c> (active slice index), <c>{tx}</c> (transmit slice),
/// <c>{A}</c>..<c>{H}</c> (slice by letter) and <c>{pan}</c> (the active slice's panadapter
/// handle, for <c>display pan set {pan} band=20</c>). Pure, so every rule here is unit-tested.
/// </summary>
public static partial class CommandSequence
{
    [GeneratedRegex(@"\{(slice|tx|pan|[A-H])\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^slices((\s+[A-Ha-h])+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SlicesLine();

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
        // "slices" with an s is ours; the radio's own commands all start "slice ".
        if (line.Equals("slices", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("slices ", StringComparison.OrdinalIgnoreCase))
        {
            var m = SlicesLine().Match(line);
            if (!m.Success) throw new SequenceException($"bad slices line (want letters, e.g. slices A B): {line}");
            var letters = m.Groups[1].Value.ToUpperInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().OrderBy(l => l);
            return new SequenceLine(LineType.Slices, string.Join(' ', letters), 0);
        }
        return new SequenceLine(LineType.Command, line, 0);
    }

    /// <summary>Fill placeholders from the live slice table. Throws when one can't be resolved.</summary>
    /// <param name="target">The slice letter the button is pinned to, or null for "whichever is
    /// active". A pinned button's <c>{slice}</c> and <c>{pan}</c> mean that slice, so a memory made
    /// for slice A still lands on A after a second slice has been opened and become active
    /// (David, 2026-09-19: "even the single memories need to be slice aware").</param>
    public static string Substitute(string command, SliceTable slices, string? target = null)
    {
        string Own(string placeholder)
        {
            if (string.IsNullOrEmpty(target))
                return slices.Active() ?? throw new SequenceException($"no active slice - cannot fill {{{placeholder}}}");
            return slices.ByLetter(target) ?? throw new SequenceException(
                $"this button runs on slice {target}, which is not open (right-click the button, Runs on, to change that)");
        }
        return Placeholder().Replace(command, m =>
        {
            var key = m.Groups[1].Value;
            if (key == "pan")
            {
                var active = Own("pan");
                var attrs = slices.Get(active);
                if (attrs is not null && attrs.TryGetValue("pan", out var pan) && pan.Length > 0) return pan;
                throw new SequenceException("that slice has no panadapter - cannot fill {pan}");
            }
            var (idx, what) = key switch
            {
                "slice" => (Own("slice"), "no active slice"),
                "tx" => (slices.Tx(), "no transmit slice"),
                _ => (slices.ByLetter(key), $"no slice {key}"),
            };
            return idx ?? throw new SequenceException($"{what} - cannot fill {{{key}}}");
        });
    }

    /// <summary>
    /// Make exactly the wanted slice letters exist: close every open slice that is not wanted, then
    /// open slices until every wanted letter is there. The radio gives a new slice the lowest free
    /// letter, so a wanted letter above a gap (A and C, no B) means opening a filler and closing it
    /// afterwards. Each step waits for the radio's status before the next, because the lines that
    /// follow address the slices by letter. A new slice starts as a copy of the active one's
    /// frequency, RX antenna and mode; the button's own lines then set it up.
    /// </summary>
    /// <returns>False when the radio refused a step or the letters never appeared.</returns>
    public static bool EnsureSlices(IReadOnlyCollection<string> wanted, SliceTable slices,
        Func<string, (int Code, string Text)> send, Action<double> sleep, Action<string>? report = null)
    {
        bool Do(string cmd, out string text)
        {
            text = "";
            try
            {
                var (code, reply) = send(cmd);
                text = reply;
                if (code == 0) return true;
                report?.Invoke($"error 0x{code:X} {reply} <- {cmd}");
            }
            catch (Exception ex) { report?.Invoke($"error: {ex.Message}"); }
            return false;
        }
        bool WaitFor(Func<bool> done)
        {
            for (var i = 0; i < 30 && !done(); i++) sleep(0.1);
            return done();
        }
        string LetterOf(string idx) => slices.Get(idx)?.GetValueOrDefault("index_letter") ?? "";
        bool Remove(string idx)
        {
            if (!Do($"slice remove {idx}", out _)) return false;
            slices.Set(idx, "in_use", "0");   // the reply is proof enough; don't depend on the status line
            return true;
        }

        foreach (var idx in slices.Live())
            if (!wanted.Contains(LetterOf(idx)) && !Remove(idx)) return false;

        var fillers = new List<string>();
        for (var guard = 0; guard < 8; guard++)
        {
            var have = slices.Live().Select(LetterOf).ToHashSet();
            if (wanted.All(have.Contains)) break;

            var before = slices.Live();
            var like = slices.Active() ?? before.FirstOrDefault();
            var t = like is null ? null : slices.Get(like);
            var create = t is null
                ? "slice create freq=14.100000 ant=ANT1 mode=USB"
                : $"slice create freq={t.GetValueOrDefault("RF_frequency", "14.100000")} " +
                  $"ant={t.GetValueOrDefault("rxant", "ANT1")} mode={t.GetValueOrDefault("mode", "USB")}";
            if (!Do(create, out var reply)) return false;

            string? fresh = null;
            WaitFor(() => (fresh = slices.Live().Except(before).FirstOrDefault()) is not null);
            fresh ??= reply.Trim().Length > 0 && reply.Trim().All(char.IsDigit) ? reply.Trim() : null;
            if (fresh is null || !WaitFor(() => LetterOf(fresh).Length > 0))
            {
                report?.Invoke("error: the radio accepted slice create but never reported the new slice");
                return false;
            }
            if (!wanted.Contains(LetterOf(fresh))) fillers.Add(fresh);
        }
        foreach (var idx in fillers)
            if (!Remove(idx)) return false;

        var now = slices.Live().Select(LetterOf).ToHashSet();
        var missing = wanted.Where(l => !now.Contains(l)).ToList();
        if (missing.Count == 0) return true;
        report?.Invoke($"error: could not open slice {string.Join(", ", missing)}");
        return false;
    }

    /// <summary>
    /// Run a button's lines in order. <paramref name="send"/> is synchronous and returns the radio's
    /// reply code and text; a non-zero code is an error. Returns true when every line succeeded.
    /// </summary>
    /// <param name="report">Called with each error message as it happens.</param>
    public static bool Run(IEnumerable<string> lines, SliceTable slices,
        Func<string, (int Code, string Text)> send, bool stopOnError, Action<string>? report = null,
        Action<double>? sleep = null, string? target = null)
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
                case LineType.Slices:
                    if (!EnsureSlices(parsed.Command.Split(' '), slices, send, sleep, report))
                    {
                        ok = false;
                        if (stopOnError) return false;
                    }
                    continue;
            }

            string cmd;
            try { cmd = Substitute(parsed.Command, slices, target); }
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
