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

    /// <summary>Through an esp-stlink programmer, such as a Pico running pico-swimlib.</summary>
    public ThingLink(EspStLink esp, string port)
    {
        esp.ReadVersion();
        Link = esp;
        About = new[] { $"esp-stlink programmer on {port} (pico-swimlib firmware {esp.FirmwareVersion}). SWIM at low speed on GP16, NRST on GP17; the Thing must run at 3.3 V." };
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
                // pico-swimlib's firmware answers the esp-stlink version request; anything
                // else on the port is tried as Wren's own PicoLink sketch.
                var port = SerialPorts.Open(serial.Port);
                ITargetLink? opened = null;
                try
                {
                    if (EspStLink.TryOpen(port, out EspStLink? esp))
                    {
                        opened = esp;
                        return new ThingLink(esp!, serial.Port);
                    }
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
            default:
                throw new ArgumentException("Unknown programmer", nameof(programmer));
        }
    }

    public void Dispose() => Link.Dispose();
}
