using Wren.Emu;
using Wren.Script;
using Xunit;

namespace Wren.Link.Tests;

/// <summary>
/// The emulator has no debug module, so these only check that the bench checks run to the
/// end, report, and never write flash or EEPROM. What they find matters only on a real Thing.
/// </summary>
public class DebugModuleChecksTests
{
    private static readonly Lazy<Kernel> SharedKernel = new(() =>
        Kernel.Assemble(Path.Combine(AppContext.BaseDirectory, "firmware")));

    [Fact]
    public void RunToTheEndWithoutWritingFlashOrEeprom()
    {
        var thing = new OledThing();
        var link = new ThingLink(new PicoLink(new FakePico(thing.Mcu)), "COM9");
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        link.Programmer.InstallKernel(SharedKernel.Value.Image);
        link.Programmer.UploadProgram(Compiler.Compile("DIM a\nON START\n a = 1\nEND ON\n").Image);
        link.Target.Connect();
        int[] flash = (int[])thing.Mcu.FlashWrites.Clone(), eeprom = (int[])thing.Mcu.EepromWrites.Clone();

        var lines = new List<string>();
        var found = new DebugModuleChecks(link.Target, SharedKernel.Value, lines.Add).Run(hasProgram: true);

        Assert.Equal(flash, thing.Mcu.FlashWrites);
        Assert.Equal(eeprom, thing.Mcu.EepromWrites);
        Assert.Contains(lines, l => l.StartsWith("Done."));
        Assert.False(found.ReadBreakpointOnFlash); // no debug module to stop the CPU
        Assert.NotNull(found.LiveReadsWhileIdle);
        Assert.NotNull(found.StallStopsTheCpu);
        Assert.NotNull(found.Tim4Freezes);
    }
}
