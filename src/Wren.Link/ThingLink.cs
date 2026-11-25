namespace Wren.Link;

/// <summary>A programmer Wren can use: an ST-LINK on USB, or a serial port that may hold a Pico.</summary>
public abstract record ProgrammerInfo;

/// <summary>A serial port; <see cref="LikelyPico"/> when its USB device is a Raspberry Pi board.</summary>
public sealed record SerialProgrammerInfo(string Port, bool LikelyPico) : ProgrammerInfo
{
    public override string ToString() => LikelyPico ? $"Pico on {Port}" : $"{Port} (serial port)";
}

/// <summary>An open connection to a Thing: the programmer, the SWIM target and Wren's jobs on it.</summary>
public sealed class ThingLink : IDisposable
{
    /// <summary>Through an ST-LINK on a USB pipe.</summary>
    public ThingLink(IUsbPipe pipe)
    {
        var stLink = new StLink(pipe);
        Version = stLink.ReadVersion();
        double? volts = stLink.ReadTargetVoltage(Version);
        Link = stLink;
        About = new[]
        {
            Version.ToString(),
            volts is double v ? $"Target voltage: {v:F2} V" : "Target voltage: this ST-LINK cannot measure it",
        };
        Target = new SwimTarget(Link);
        Programmer = new ThingProgrammer(Target);
    }

    /// <summary>Through a Pico running Wren's sketch.</summary>
    public ThingLink(PicoLink pico, string port)
    {
        pico.SayHello();
        Link = pico;
        About = new[] { $"Wren Pico programmer on {port}, sketch version {pico.Version}. SWIM at low speed; the Thing must run at 3.3 V." };
        Target = new SwimTarget(Link);
        Programmer = new ThingProgrammer(Target);
    }

    public ITargetLink Link { get; }

    /// <summary>The ST-LINK's version; null for a Pico.</summary>
    public StLinkVersion? Version { get; }

    /// <summary>Lines describing the programmer, for the log.</summary>
    public IReadOnlyList<string> About { get; }

    public SwimTarget Target { get; }
    public ThingProgrammer Programmer { get; }

    /// <summary>
    /// Programmers attached to this computer: ST-LINKs (Windows only, for now) and serial
    /// ports, Raspberry Pi boards first. Why, when ST-LINKs cannot be looked for here.
    /// </summary>
    public static IReadOnlyList<ProgrammerInfo> Find(out string? why)
    {
        why = null;
        var found = new List<ProgrammerInfo>();
        if (OperatingSystem.IsWindows()) found.AddRange(WinUsbPipe.Find());
        else why = "ST-LINKs are found on Windows only for now (Mac and Linux need libusb); a Pico works anywhere.";
        found.AddRange(SerialPorts.Find());
        return found;
    }

    /// <summary>
    /// Which of the programmers found to select: the one named last (its ToString), else one of
    /// the same kind (a Pico may come back on another port), else the first; -1 when none.
    /// </summary>
    public static int Preferred(IReadOnlyList<ProgrammerInfo> found, string? last)
    {
        if (found.Count == 0) return -1;
        if (string.IsNullOrEmpty(last)) return 0;
        for (int i = 0; i < found.Count; i++)
            if (found[i].ToString() == last) return i;
        for (int i = 0; i < found.Count; i++)
            if (Kind(found[i]) is string kind && last.StartsWith(kind, StringComparison.Ordinal)) return i;
        return 0;
    }

    /// <summary>How a programmer's name starts, for the kinds worth matching across ports.</summary>
    private static string? Kind(ProgrammerInfo programmer) => programmer switch
    {
        UsbDeviceInfo => "ST-LINK",
        SerialProgrammerInfo { LikelyPico: true } => "Pico",
        _ => null,
    };

    /// <summary>Opens a programmer found by Find.</summary>
    public static ThingLink Open(ProgrammerInfo programmer)
    {
        switch (programmer)
        {
            case UsbDeviceInfo usb:
                if (!OperatingSystem.IsWindows()) throw new LinkException("ST-LINKs work on Windows only for now.");
                return new ThingLink(WinUsbPipe.Open(usb));
            case SerialProgrammerInfo serial:
            {
                lock (Held)
                {
                    if (Held.Contains(serial.Port))
                        throw new LinkException($"{serial.Port} is already open in Wren: stop debugging in the main window (Shift+F5), or click Disconnect in the Firmware window.");
                    Held.Add(serial.Port);
                }
                try
                {
                    var link = OpenSerial(serial);
                    link.heldPort = serial.Port;
                    return link;
                }
                catch
                {
                    lock (Held) Held.Remove(serial.Port);
                    throw;
                }
            }
            default:
                throw new ArgumentException("Unknown programmer", nameof(programmer));
        }
    }

    /// <summary>The serial ports Wren holds open now, so a second open names Wren as the holder.</summary>
    private static readonly HashSet<string> Held = new(StringComparer.OrdinalIgnoreCase);

    private string? heldPort;

    /// <summary>The serial port this link holds; null for an ST-LINK.</summary>
    public string? PortName => heldPort;

    /// <summary>
    /// True when the link's serial port is no longer listed: its programmer was unplugged.
    /// (An ST-LINK unplugged shows as a failed transfer instead.)
    /// </summary>
    public bool Vanished => heldPort != null && !SerialPorts.Names().Contains(heldPort);

    private static ThingLink OpenSerial(SerialProgrammerInfo serial)
    {
        // A serial port is tried as Wren's PicoLink sketch.
        var port = SerialPorts.Open(serial.Port);
        ITargetLink? opened = null;
        try
        {
            var pico = PicoLink.Open(port, serial.Port);
            opened = pico;
            return new ThingLink(pico, serial.Port);
        }
        catch
        {
            if (opened != null) opened.Dispose();
            else port.Dispose();
            throw;
        }
    }

    private bool disposed;

    /// <summary>
    /// Closes the programmer's port (once; later calls do nothing) and lets Wren open it again.
    /// Never throws: a port whose programmer was unplugged has nothing left to close.
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            Link.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or LinkException
            or TimeoutException or System.ComponentModel.Win32Exception)
        {
        }
        finally
        {
            if (heldPort != null) lock (Held) Held.Remove(heldPort);
        }
    }
}
