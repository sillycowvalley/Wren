using System.IO.Ports;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Wren.Link;

/// <summary>Serial ports, with the ones belonging to Raspberry Pi boards (USB vendor 2E8A) marked.</summary>
public static class SerialPorts
{
    public static IReadOnlyList<SerialProgrammerInfo> Find()
    {
        string[] names;
        try
        {
            names = SerialPort.GetPortNames();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return Array.Empty<SerialProgrammerInfo>();
        }
        var pico = OperatingSystem.IsWindows() ? RaspberryPiPorts() : new HashSet<string>();
        return names.Distinct()
            .Select(n => new SerialProgrammerInfo(n, pico.Contains(n) || n.Contains("ttyACM", StringComparison.Ordinal) || n.Contains("usbmodem", StringComparison.Ordinal)))
            .OrderByDescending(p => p.LikelyPico)
            .ThenBy(p => p.Port.Length).ThenBy(p => p.Port, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>COM ports whose USB device has Raspberry Pi's vendor id, from the registry's USB enumeration.</summary>
    [SupportedOSPlatform("windows")]
    private static HashSet<string> RaspberryPiPorts()
    {
        var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb == null) return ports;
            foreach (string device in usb.GetSubKeyNames().Where(n => n.StartsWith("VID_2E8A", StringComparison.OrdinalIgnoreCase)))
            {
                using var deviceKey = usb.OpenSubKey(device);
                if (deviceKey == null) continue;
                foreach (string instance in deviceKey.GetSubKeyNames())
                {
                    using var parameters = deviceKey.OpenSubKey(instance + @"\Device Parameters");
                    if (parameters?.GetValue("PortName") is string port) ports.Add(port);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return ports;
    }
}
