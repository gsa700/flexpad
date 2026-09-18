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
/// <c>{A}</c>..<c>{H}</c> (slice by letter), <c>{pan}</c> (the active slice's panadapter handle, for
/// <c>display pan set {pan} band=20</c>) and <c>{panA}</c>..<c>{panH}</c> (the panadapter of a slice by letter). Pure, so every rule here is unit-tested.
/// </summary>
public static partial class CommandSequence
{
    [GeneratedRegex(@"\{(slice|tx|pan[A-H]?|[A-H])\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^slices((\s+[A-Ha-h])+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SlicesLine();

    [GeneratedRegex(@"^slice\s+t(?:une)?\s+\{([A-H])\}\s+([0-9.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TuneByLetter();

    [GeneratedRegex(@"^slice\s+s(?:et)?\s+\{([A-H])\}\s+(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SetByLetter();

    /// <summary>Where a slice that has to be opened should be opened: taken from the button's own lines.</summary>
    public sealed record SliceHint(string? Frequency, string? Antenna, string? Mode);

    /// <summary>
    /// Read ahead in a button for what it is about to do to each slice letter: the first
    /// <c>slice tune {B} …</c>, and the first <c>mode=</c> and <c>rxant=</c> in a <c>slice set {B} …</c>.
    /// A <c>slices</c> line uses this to open a missing slice where it is going to live.
    /// </summary>
    public static Dictionary<string, SliceHint> HintsFrom(IEnumerable<string> lines)
    {
        var hints = new Dictionary<string, SliceHint>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (TuneByLetter().Match(line) is { Success: true } t)
            {
                var l = t.Groups[1].Value.ToUpperInvariant();
                var h = hints.GetValueOrDefault(l) ?? new SliceHint(null, null, null);
                hints[l] = h with { Frequency = h.Frequency ?? t.Groups[2].Value };
            }
            else if (SetByLetter().Match(line) is { Success: true } s)
            {
                var l = s.Groups[1].Value.ToUpperInvariant();
                var h = hints.GetValueOrDefault(l) ?? new SliceHint(null, null, null);
                foreach (var (k, v) in FlexProtocol.KeyValues(s.Groups[2].Value))
                {
                    if (k == "mode") h = h with { Mode = h.Mode ?? v };
                    else if (k == "rxant") h = h with { Antenna = h.Antenna ?? v };
                }
                hints[l] = h;
            }
        }
        return hints;
    }

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
            if (key.Length == 4 && key.StartsWith("pan", StringComparison.Ordinal))
            {
                // {panB}: the panadapter slice B lives in, for scope settings in an all-slices button.
                var letter = key[3..];
                var byLetter = slices.ByLetter(letter) ?? throw new SequenceException($"no slice {letter} - cannot fill {{{key}}}");
                if (slices.Get(byLetter) is { } a && a.TryGetValue("pan", out var handle) && handle.Length > 0) return handle;
                throw new SequenceException($"slice {letter} has no panadapter - cannot fill {{{key}}}");
            }
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
    /// follow address the slices by letter.
    /// <para>A new slice is opened where the button is about to put it (<paramref name="hints"/>, read
    /// ahead from the button's own lines), not as a copy of the active slice. That matters: a FLEX-8600M
    /// with its front panel in single-slice view accepts a second slice on the active slice's frequency,
    /// in the same panadapter, and closes it again 80 ms later; opened on its own frequency and antenna
    /// it gets its own panadapter and stays (seen live 2026-09-19 with David's "VHF &amp; UHF" button).
    /// Only a filler, or a slice the button never tunes, still starts as a copy.</para>
    /// </summary>
    /// <returns>False when the radio refused a step, closed the new slice again, or the letters never appeared.</returns>
    public static bool EnsureSlices(IReadOnlyCollection<string> wanted, SliceTable slices,
        Func<string, (int Code, string Text)> send, Action<double> sleep, Action<string>? report = null,
        IReadOnlyDictionary<string, SliceHint>? hints = null)
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
            // The radio hands out the lowest free letter, so that is the slice this create will make.
            var next = "ABCDEFGH".Select(c => c.ToString()).FirstOrDefault(l => !have.Contains(l));
            var hint = next is not null && wanted.Contains(next) ? hints?.GetValueOrDefault(next) : null;
            var create = "slice create " +
                $"freq={hint?.Frequency ?? t?.GetValueOrDefault("RF_frequency") ?? "14.100000"} " +
                $"ant={hint?.Antenna ?? t?.GetValueOrDefault("rxant") ?? "ANT1"} " +
                $"mode={hint?.Mode ?? t?.GetValueOrDefault("mode") ?? "USB"}";
            if (!Do(create, out var reply)) return false;

            string? fresh = null;
            WaitFor(() => (fresh = slices.Live().Except(before).FirstOrDefault()) is not null);
            fresh ??= reply.Trim().Length > 0 && reply.Trim().All(char.IsDigit) ? reply.Trim() : null;
            if (fresh is null || !WaitFor(() => LetterOf(fresh).Length > 0))
            {
                report?.Invoke("error: the radio accepted slice create but never reported the new slice");
                return false;
            }
            // Give the radio a moment, then make sure it kept the slice. It also lets a new
            // panadapter settle before the button's next command lands on it.
            sleep(0.4);
            if (!slices.Live().Contains(fresh))
            {
                report?.Invoke($"error: the radio opened slice {LetterOf(fresh)} and closed it again. It keeps a second slice " +
                               "only on its own panadapter: give the button a 'slice tune {" + LetterOf(fresh) +
                               "} <MHz>' line on another band, or switch the radio to a two-slice view first");
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
        var all = lines as IReadOnlyList<string> ?? lines.ToList();
        Dictionary<string, SliceHint>? hints = null;
        foreach (var raw in all)
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
                    hints ??= HintsFrom(all);
                    if (!EnsureSlices(parsed.Command.Split(' '), slices, send, sleep, report, hints))
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

        // A pinned button brings the front panel and the knob along: its slice becomes the active
        // one (David, 2026-09-19). Last, so a button whose own lines open the slice still works, and
        // a run that stopped on an error leaves the focus where it was.
        if (!string.IsNullOrEmpty(target) && slices.ByLetter(target) is { } own && slices.Active() != own)
        {
            var cmd = $"slice set {own} active=1";
            try
            {
                var (code, text) = send(cmd);
                if (code != 0) { report?.Invoke($"error 0x{code:X} {text} <- {cmd}"); ok = false; }
            }
            catch (Exception ex) { report?.Invoke($"error: {ex.Message}"); ok = false; }
        }
        return ok;
    }
}
