namespace FlexPad.App;

/// <summary>The in-app cheat sheet and the editor's example snippets. Verified against a FLEX-8600M.</summary>
public static class Reference
{
    public const string WikiUrl = "https://github.com/flexradio/smartsdr-api-docs/wiki";

    public static readonly (string Name, string[] Lines)[] Examples =
    {
        ("Tune + mode + antenna + filter", new[]
        {
            "slice tune {slice} 144.200", "slice set {slice} mode=USB",
            "slice set {slice} rxant=XVTA txant=XVTA", "filt {slice} 150 2900",
        }),
        ("Antenna only, active slice", new[] { "slice set {slice} rxant=ANT1 txant=ANT1" }),
        ("Second slice on XVTB", new[] { "slice create freq=432.100 ant=XVTB mode=USB", "wait 0.5",
            "slice set {B} rxant=XVTB txant=XVTB", "filt {B} 150 2900" }),
        ("Move TX to slice A", new[] { "slice set {A} tx=1" }),
        ("Close slice B", new[] { "slice remove {B}" }),
        ("Load a global profile", new[] { "profile global load \"NAME\"" }),
        ("RF power", new[] { "transmit set rfpower=50" }),
        ("Noise reduction on", new[] { "slice set {slice} nr=1 nr_level=50" }),
        ("Comment and pause", new[] { "# what this step is for", "wait 0.5" }),
    };

    /// <summary>Button colours offered as swatches: dark enough to carry white text.</summary>
    public static readonly (string Hex, string Name)[] Palette =
    {
        ("#2d6a4f", "green"), ("#264653", "slate"), ("#1d3557", "navy"), ("#5a189a", "purple"),
        ("#7f1d1d", "maroon"), ("#b45309", "amber"), ("#6b4f2a", "brown"), ("#4b5563", "gray"),
    };

    public const string Text = """
        FLEXPAD QUICK REFERENCE          SmartSDR TCP/IP API, frequencies in MHz

        Placeholders   {slice} active slice    {tx} transmit slice    {A}..{H} slice by letter    {pan} active slice's panadapter
        Runs on        each button follows the active slice, or is pinned to a letter (right-click, Runs on):
                       a pinned button's {slice} and {pan} always mean that slice
        Other lines    wait 0.5  pauses        # starts a comment
                       slices A B   open or close slices until exactly A and B exist (FlexPad does this, then waits)

        TUNING AND MODE
          slice tune {slice} 14.250                  retune (transverter bands need an XVTR definition)
          slice set {slice} mode=USB                 USB LSB CW AM SAM FM NFM DFM DIGU DIGL RTTY
          slice set {slice} step=100                 tuning step, Hz
          slice lock {slice}                         lock tuning; slice unlock {slice} to release

        ANTENNAS
          slice set {slice} rxant=XVTA txant=XVTA    ports on this radio are listed below
          slice set {slice} rxant=RX_A               receive-only port; TX stays put

        FILTER AND DSP
          filt {slice} 150 2900                      RX filter edges, Hz; negative for LSB and CW-L
          slice set {slice} agc_mode=med             off slow med fast
          slice set {slice} agc_threshold=60
          slice set {slice} nr=1 nr_level=50         noise reduction
          slice set {slice} nb=1 nb_level=50         noise blanker
          slice set {slice} wnb=1 wnb_level=50       wideband noise blanker
          slice set {slice} anf=1                    auto notch

        SLICES
          slice create freq=432.100 ant=XVTB mode=USB     open a new slice; ant= is RX only, set txant after
          slice remove {B}                           close slice B
          slice set {A} tx=1                         make A the transmit slice
          slice set {B} active=1                     make B the active slice
          slice set {slice} audio_mute=1             mute (0 unmutes)
          slice set {slice} audio_level=50 audio_pan=50
          slice set {slice} dax=1                    DAX channel (0 = none)

        TRANSMIT
          transmit set rfpower=50                    RF power, 0-100
          transmit set tunepower=10                  tune power
          transmit tune 1                            start tune (0 stops)
          xmit 1                                     MOX on (0 off); a button can key the radio
          atu start                                  tune the internal ATU
          atu bypass

        PROFILES AND MEMORIES
          profile global load "NAME"                 global profile: antennas, slices, everything
          profile tx load "NAME"                     transmit profile
          profile mic load "NAME"                    mic profile
          memory apply 3                             SmartSDR memory by index (it won't set antennas)

        INFO - harmless, the reply shows in the console
          ant list        slice list        info        version        profile global info

        Full reference: the FlexRadio wiki (button below). Pages are named TCPIP-slice,
        TCPIP-filt, TCPIP-transmit, TCPIP-profile, and so on.
        """;
}
