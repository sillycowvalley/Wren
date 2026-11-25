using System.IO.Ports;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Wren.Link;

/// <summary>Serial ports, with the ones belonging to Raspberry Pi boards (USB vendor 2E8A) marked.</summary>
public static class SerialPorts
{
    /// <summary>
    /// Opens a USB serial port the way the Pico boards want it (DTR on), with timeouts. Windows
    /// finishes closing a port after Close returns, so a port Wren has just closed can be refused
    /// for a moment: it is tried again for up to two seconds.
    /// </summary>
    public static SerialPort Open(string portName)
    {
        for (int attempt = 0; ; attempt++)
        {
            var port = new SerialPort(portName, 115200)
            {
                DtrEnable = true, // a Pico's USB serial sends nothing until the port is opened with DTR
                RtsEnable = true,
                ReadTimeout = 2000,
                WriteTimeout = 2000,
            };
            try
            {
                port.Open();
                port.DiscardInBuffer();
                return port;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                port.Dispose();
                if (attempt < 20)
                {
                    Thread.Sleep(100);
                    continue;
                }
                throw new LinkException($"Cannot open {portName}: {ex.Message} (is the Arduino IDE's Serial Monitor or another program using it?)", ex);
            }
        }
    }

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
