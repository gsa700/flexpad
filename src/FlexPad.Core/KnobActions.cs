// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

namespace FlexPad.Core;

/// <summary>One function a FlexControl button can be bound to.</summary>
public sealed record KnobFunction(string Code, string Label);

/// <summary>What the radio currently reports, as the planner needs it.</summary>
public sealed class KnobContext
{
    public string? SliceIndex { get; init; }
    public IReadOnlyDictionary<string, string>? Slice { get; init; }
    public IReadOnlyList<string> LiveSlices { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Transmit { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Interlock { get; init; } = new Dictionary<string, string>();
    /// <summary>Amplifier handle to its attributes, e.g. model=PowerGeniusXL state=STANDBY.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Amplifiers { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<int> Steps { get; init; } = new[] { 10, 100, 1000, 10000 };
}

/// <summary>The commands to send for one press, a line for the console, and any value to assume
/// locally before the radio's status echo arrives.</summary>
public sealed record KnobPlan(IReadOnlyList<string> Commands, string Note, (string Key, string Value)? Optimistic = null);

/// <summary>
/// The functions a FlexControl button can perform, and the pure decision of which command each one
/// sends given the radio's current state. Every command here was accepted by a FLEX-8600M when sent
/// with its current value (2026-09-14).
/// </summary>
public static class KnobActions
{
    public static readonly KnobFunction[] Catalog =
    {
        new(KnobPolicy.ActionStep, "Cycle tuning step"),
        new(KnobPolicy.ActionNextSlice, "Next slice (move the active flag)"),
        new(KnobPolicy.ActionMute, "Mute / unmute audio"),
        new(KnobPolicy.ActionTx, "TX on this slice"),
        new("@tune", "TUNE on / off"),
        new("@mox", "MOX on / off"),
        new("@atu", "ATU tune"),
        new("@atu-bypass", "ATU bypass"),
        new("@amp", "AMP operate / standby"),
        new("@rx-ant", "Cycle RX antenna"),
        new("@tx-ant", "Cycle TX antenna"),
        new("@mode", "Cycle mode"),
        new("@agc", "Cycle AGC (off / slow / med / fast)"),
        new("@nr", "NR on / off"),
        new("@nb", "NB on / off"),
        new("@wnb", "WNB on / off"),
        new("@anf", "ANF on / off"),
        new("@lock", "Lock / unlock tuning"),
        new("@rit", "RIT on / off"),
        new("@xit", "XIT on / off"),
    };

    public static bool IsKnown(string code) => Catalog.Any(f => f.Code == code);

    private static readonly string[] AgcCycle = { "off", "slow", "med", "fast" };

    /// <exception cref="SequenceException">The function needs something the radio isn't reporting.</exception>
    public static KnobPlan Plan(string action, KnobContext ctx)
    {
        string Slice() => ctx.SliceIndex ?? throw new SequenceException("no active slice");
        string Attr(string key, string fallback = "") =>
            ctx.Slice is not null && ctx.Slice.TryGetValue(key, out var v) ? v : fallback;
        bool On(string key) => Attr(key) == "1";
        KnobPlan Toggle(string key, string name)
        {
            var i = Slice();
            var on = !On(key);
            return new KnobPlan(new[] { $"slice set {i} {key}={(on ? 1 : 0)}" }, $"{name} {(on ? "on" : "off")}", (key, on ? "1" : "0"));
        }
        KnobPlan Cycle(string listKey, string attrKey, string setKey, string name)
        {
            var i = Slice();
            var list = ButtonBuilder.ListFrom(ctx.Slice, listKey, Array.Empty<string>());
            if (list.Count == 0) throw new SequenceException($"the radio hasn't reported its {name} list");
            var cur = list.IndexOf(Attr(attrKey));
            var next = list[(cur + 1) % list.Count];
            return new KnobPlan(new[] { $"slice set {i} {setKey}={next}" }, $"{name} {next}", (attrKey, next));
        }

        switch (action)
        {
            case KnobPolicy.ActionStep:
            {
                var i = Slice();
                var cur = int.TryParse(Attr("step"), out var st) ? st : 0;
                var next = KnobPolicy.NextStep(ctx.Steps, cur);
                return new KnobPlan(new[] { $"slice set {i} step={next}" }, $"tuning step {next} Hz", ("step", next.ToString()));
            }
            case KnobPolicy.ActionNextSlice:
            {
                var next = KnobPolicy.NextSlice(ctx.LiveSlices, ctx.SliceIndex);
                return next is null
                    ? new KnobPlan(Array.Empty<string>(), "only one slice open")
                    : new KnobPlan(new[] { $"slice set {next} active=1" }, $"slice {next} active");
            }
            case KnobPolicy.ActionMute: return Toggle("audio_mute", "mute");
            case KnobPolicy.ActionTx:
            {
                var i = Slice();
                return new KnobPlan(new[] { $"slice set {i} tx=1" }, "TX on this slice");
            }
            case "@tune":
            {
                var on = ctx.Transmit.TryGetValue("tune", out var t) && t == "1";
                return new KnobPlan(new[] { $"transmit tune {(on ? 0 : 1)}" }, on ? "TUNE off" : "TUNE on");
            }
            case "@mox":
            {
                // READY means not transmitting; anything else (TRANSMITTING, PTT_REQUESTED, ...) means
                // a key-up is the right answer. Never key without a slice to transmit on.
                var ready = ctx.Interlock.TryGetValue("state", out var st) && st == "READY";
                return new KnobPlan(new[] { $"xmit {(ready ? 1 : 0)}" }, ready ? "MOX on" : "MOX off");
            }
            case "@atu": return new KnobPlan(new[] { "atu start" }, "ATU tuning");
            case "@atu-bypass": return new KnobPlan(new[] { "atu bypass" }, "ATU bypassed");
            case "@amp":
            {
                // Prefer a Power Genius; a Tuner Genius also appears as an amplifier object.
                var amp = ctx.Amplifiers
                    .OrderByDescending(a => a.Value.TryGetValue("model", out var m) && m.Contains("PowerGenius", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                if (amp.Key is null) throw new SequenceException("no amplifier reported by the radio");
                var operating = (amp.Value.TryGetValue("state", out var s) && s.Equals("OPERATE", StringComparison.OrdinalIgnoreCase))
                             || (amp.Value.TryGetValue("operate", out var o) && o == "1");
                var name = amp.Value.TryGetValue("nickname", out var n) && n.Length > 0 ? n
                         : amp.Value.TryGetValue("model", out var mo) ? mo : "amplifier";
                return new KnobPlan(new[] { $"amplifier set {amp.Key} operate={(operating ? 0 : 1)}" },
                    $"{name} {(operating ? "standby" : "operate")}");
            }
            case "@rx-ant": return Cycle("ant_list", "rxant", "rxant", "RX antenna");
            case "@tx-ant": return Cycle("tx_ant_list", "txant", "txant", "TX antenna");
            case "@mode": return Cycle("mode_list", "mode", "mode", "mode");
            case "@agc":
            {
                var i = Slice();
                var cur = Array.IndexOf(AgcCycle, Attr("agc_mode").ToLowerInvariant());
                var next = AgcCycle[(cur + 1) % AgcCycle.Length];
                return new KnobPlan(new[] { $"slice set {i} agc_mode={next}" }, $"AGC {next}", ("agc_mode", next));
            }
            case "@nr": return Toggle("nr", "NR");
            case "@nb": return Toggle("nb", "NB");
            case "@wnb": return Toggle("wnb", "WNB");
            case "@anf": return Toggle("anf", "ANF");
            case "@lock":
            {
                var i = Slice();
                var locked = On("lock");
                return new KnobPlan(new[] { locked ? $"slice unlock {i}" : $"slice lock {i}" },
                    locked ? "tuning unlocked" : "tuning locked", ("lock", locked ? "0" : "1"));
            }
            case "@rit": return Toggle("rit_on", "RIT");
            case "@xit": return Toggle("xit_on", "XIT");
            default:
                throw new SequenceException($"unknown knob function {action}");
        }
    }
}
