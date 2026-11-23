namespace Wren.Link;

/// <summary>An open connection to a Thing: the ST-LINK, the SWIM target and the programmer.</summary>
public sealed class ThingLink : IDisposable
{
    public ThingLink(IUsbPipe pipe)
    {
        StLink = new StLink(pipe);
        Version = StLink.ReadVersion();
        Target = new SwimTarget(StLink);
        Programmer = new ThingProgrammer(Target);
    }

    public StLink StLink { get; }
    public StLinkVersion Version { get; }
    public SwimTarget Target { get; }
    public ThingProgrammer Programmer { get; }

    /// <summary>ST-LINKs attached to this computer. Empty, with a reason, where Wren.Link cannot look yet.</summary>
    public static IReadOnlyList<UsbDeviceInfo> Find(out string? why)
    {
        why = null;
        if (OperatingSystem.IsWindows()) return WinUsbPipe.Find();
        why = "Wren.Link talks to the ST-LINK on Windows for now (Mac and Linux need libusb, still to come).";
        return Array.Empty<UsbDeviceInfo>();
    }

    /// <summary>Opens an ST-LINK found by Find.</summary>
    public static ThingLink Open(UsbDeviceInfo device)
    {
        if (!OperatingSystem.IsWindows()) throw new LinkException("Wren.Link talks to the ST-LINK on Windows for now.");
        return new ThingLink(WinUsbPipe.Open(device));
    }

    public void Dispose() => StLink.Dispose();
}
