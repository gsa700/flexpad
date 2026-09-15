using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class KnobActionsTests
{
    private static KnobContext Ctx(Dictionary<string, string>? slice = null, string? transmitTune = "0",
        string interlock = "READY", Dictionary<string, IReadOnlyDictionary<string, string>>? amps = null)
    {
        slice ??= new Dictionary<string, string>
        {
            ["in_use"] = "1", ["active"] = "1", ["index_letter"] = "A", ["step"] = "100",
            ["audio_mute"] = "0", ["nr"] = "0", ["agc_mode"] = "med", ["lock"] = "0", ["rit_on"] = "0",
            ["rxant"] = "ANT1", ["txant"] = "ANT1", ["mode"] = "USB",
            ["ant_list"] = "ANT1,ANT2,RX_A,RX_B,XVTA,XVTB", ["tx_ant_list"] = "ANT1,ANT2,XVTA,XVTB",
            ["mode_list"] = "LSB,USB,CW",
        };
        return new KnobContext
        {
            SliceIndex = "0", Slice = slice, LiveSlices = new[] { "0", "1" },
            Transmit = new Dictionary<string, string> { ["tune"] = transmitTune ?? "0" },
            Interlock = new Dictionary<string, string> { ["state"] = interlock },
            Amplifiers = amps ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(),
        };
    }

    [Fact]
    public void Every_catalog_entry_plans_something_or_explains_why_not()
    {
        var amps = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["0x1E5DC72F"] = new Dictionary<string, string> { ["model"] = "PowerGeniusXL", ["state"] = "STANDBY" },
        };
        foreach (var f in KnobActions.Catalog)
        {
            var plan = KnobActions.Plan(f.Code, Ctx(amps: amps));
            Assert.NotNull(plan.Note);
        }
    }

    [Fact]
    public void Toggles_flip_from_the_current_state_and_report_the_new_one()
    {
        var p = KnobActions.Plan("@nr", Ctx());
        Assert.Equal("slice set 0 nr=1", p.Commands[0]);
        Assert.Equal("NR on", p.Note);
        Assert.Equal(("nr", "1"), p.Optimistic);
        var on = Ctx(new Dictionary<string, string> { ["nr"] = "1" });
        Assert.Equal("slice set 0 nr=0", KnobActions.Plan("@nr", on).Commands[0]);
    }

    [Fact]
    public void Tune_and_mox_read_transmit_and_interlock()
    {
        Assert.Equal("transmit tune 1", KnobActions.Plan("@tune", Ctx(transmitTune: "0")).Commands[0]);
        Assert.Equal("transmit tune 0", KnobActions.Plan("@tune", Ctx(transmitTune: "1")).Commands[0]);
        Assert.Equal("xmit 1", KnobActions.Plan("@mox", Ctx(interlock: "READY")).Commands[0]);
        Assert.Equal("xmit 0", KnobActions.Plan("@mox", Ctx(interlock: "TRANSMITTING")).Commands[0]);
    }

    [Fact]
    public void Amp_prefers_the_power_genius_and_uses_its_state()
    {
        var amps = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["0x15ACE487"] = new Dictionary<string, string> { ["model"] = "TunerGeniusXL", ["operate"] = "1" },
            ["0x1E5DC72F"] = new Dictionary<string, string> { ["model"] = "PowerGeniusXL", ["state"] = "STANDBY", ["nickname"] = "" },
        };
        var p = KnobActions.Plan("@amp", Ctx(amps: amps));
        Assert.Equal("amplifier set 0x1E5DC72F operate=1", p.Commands[0]);
        Assert.Equal("PowerGeniusXL operate", p.Note);
        amps["0x1E5DC72F"] = new Dictionary<string, string> { ["model"] = "PowerGeniusXL", ["state"] = "OPERATE" };
        Assert.Equal("amplifier set 0x1E5DC72F operate=0", KnobActions.Plan("@amp", Ctx(amps: amps)).Commands[0]);
        Assert.Throws<SequenceException>(() => KnobActions.Plan("@amp", Ctx()));
    }

    [Fact]
    public void Antenna_and_mode_cycles_walk_the_radios_lists_and_wrap()
    {
        Assert.Equal("slice set 0 rxant=ANT2", KnobActions.Plan("@rx-ant", Ctx()).Commands[0]);
        var last = Ctx(new Dictionary<string, string> { ["rxant"] = "XVTB", ["ant_list"] = "ANT1,ANT2,XVTA,XVTB" });
        Assert.Equal("slice set 0 rxant=ANT1", KnobActions.Plan("@rx-ant", last).Commands[0]);
        Assert.Equal("slice set 0 txant=ANT2", KnobActions.Plan("@tx-ant", Ctx()).Commands[0]);
        Assert.Equal("slice set 0 mode=CW", KnobActions.Plan("@mode", Ctx()).Commands[0]);
    }

    [Fact]
    public void Agc_cycles_and_lock_uses_its_own_commands()
    {
        Assert.Equal("slice set 0 agc_mode=fast", KnobActions.Plan("@agc", Ctx()).Commands[0]);
        Assert.Equal("slice lock 0", KnobActions.Plan("@lock", Ctx()).Commands[0]);
        Assert.Equal("slice unlock 0", KnobActions.Plan("@lock", Ctx(new Dictionary<string, string> { ["lock"] = "1" })).Commands[0]);
    }

    [Fact]
    public void Slice_functions_need_a_slice()
    {
        var none = new KnobContext { SliceIndex = null, Slice = null };
        Assert.Throws<SequenceException>(() => KnobActions.Plan("@nr", none));
        Assert.Throws<SequenceException>(() => KnobActions.Plan("@bogus", Ctx()));
    }
}
