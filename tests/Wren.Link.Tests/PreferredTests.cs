using Xunit;

namespace Wren.Link.Tests;

public class PreferredTests
{
    private static readonly ProgrammerInfo StLink = new UsbDeviceInfo(@"\\?\usb#vid_0483&pid_3748#1", 0x3748);
    private static readonly ProgrammerInfo PicoCom5 = new SerialProgrammerInfo("COM5", true);
    private static readonly ProgrammerInfo PicoCom7 = new SerialProgrammerInfo("COM7", true);
    private static readonly ProgrammerInfo Com1 = new SerialProgrammerInfo("COM1", false);

    [Fact]
    public void NothingFound() => Assert.Equal(-1, ThingLink.Preferred(Array.Empty<ProgrammerInfo>(), "Pico on COM5"));

    [Fact]
    public void NothingSavedPicksTheFirst() => Assert.Equal(0, ThingLink.Preferred(new[] { StLink, PicoCom5 }, null));

    [Fact]
    public void PicksTheOneUsedLast() => Assert.Equal(2, ThingLink.Preferred(new[] { StLink, PicoCom7, PicoCom5 }, "Pico on COM5"));

    [Fact]
    public void PicksTheStLinkUsedLast() => Assert.Equal(1, ThingLink.Preferred(new[] { PicoCom5, StLink }, StLink.ToString()));

    [Fact]
    public void APicoOnANewPortStillCounts() => Assert.Equal(2, ThingLink.Preferred(new[] { StLink, Com1, PicoCom7 }, "Pico on COM5"));

    [Fact]
    public void AnotherStLinkModelStillCounts() =>
        Assert.Equal(1, ThingLink.Preferred(new[] { PicoCom5, StLink }, "ST-LINK V2.1 (PID 374B)"));

    [Fact]
    public void APlainSerialPortIsMatchedOnlyByName()
    {
        Assert.Equal(1, ThingLink.Preferred(new[] { StLink, Com1 }, "COM1 (serial port)"));
        Assert.Equal(0, ThingLink.Preferred(new[] { StLink, Com1 }, "COM3 (serial port)"));
    }

    [Fact]
    public void GoneAndNoSameKindPicksTheFirst() => Assert.Equal(0, ThingLink.Preferred(new[] { Com1, StLink }, "Pico on COM5"));
}
