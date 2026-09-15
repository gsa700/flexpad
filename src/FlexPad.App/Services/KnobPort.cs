using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using FlexPad.Core;

namespace FlexPad.App.Services;

/// <summary>
/// Finds the FlexControl by its USB identity, so no other serial device is ever opened by mistake
/// (this station has a port grabber or two). Windows asks WMI for the PnP id; Linux reads
/// <c>/dev/serial/by-id</c>, where the name carries the vendor string.
/// </summary>
public static partial class KnobPort
{
    [GeneratedRegex(@"\((COM\d+)\)")]
    private static partial Regex ComName();

    public static string? Find()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return FindWindows();
            if (OperatingSystem.IsLinux()) return FindLinux();
        }
        catch { /* fall through: not found */ }
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? FindWindows()
    {
        var want = $"VID_{KnobProtocol.VendorId:X4}&PID_{KnobProtocol.ProductId:X4}";
        using var searcher = new System.Management.ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass='Ports'");
        foreach (var o in searcher.Get())
        {
            var id = o["PNPDeviceID"]?.ToString() ?? "";
            if (!id.Contains(want, StringComparison.OrdinalIgnoreCase)) continue;
            var m = ComName().Match(o["Name"]?.ToString() ?? "");
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    private static string? FindLinux()
    {
        const string dir = "/dev/serial/by-id";
        if (!Directory.Exists(dir)) return null;
        foreach (var link in Directory.GetFiles(dir))
        {
            var name = Path.GetFileName(link);
            if (!name.Contains("FlexControl", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("FlexRadio", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var target = new FileInfo(link).ResolveLinkTarget(returnFinalTarget: true);
                return target?.FullName ?? link;
            }
            catch { return link; }
        }
        return null;
    }
}
