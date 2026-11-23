using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

/// <summary>The kernel's loader: a host sending a program over b0-b7 and PD3.</summary>
public class UploadTests
{
    private const string ProgramA = "DIM a\nON START\n a = 111\nEND ON\n";
    private const string ProgramB = "DIM b, c\nON START\n b = 222\n c = 333\nEND ON\n";

    /// <summary>A Thing with the kernel and, optionally, a program already loaded.</summary>
    private static OledThing Thing(string? existing = null)
    {
        var thing = new OledThing();
        thing.Mcu.Load(Run.Kernel.Image.Bytes);
        if (existing != null) thing.Mcu.Load(ProgramStore.Plan(null, Run.Compile(existing).Image).Bytes());
        return thing;
    }

    /// <summary>Starts the Thing with PD2 held low, so it waits for an upload.</summary>
    private static void EnterLoader(OledThing thing)
    {
        thing.Pd2Low = true;
        thing.PowerOn();
        thing.Mcu.Run(0.02);
        thing.Pd2Low = false;
    }

    /// <summary>Sends bytes as a host would: each on b0-b7, strobed by PD3.</summary>
    private static void Send(OledThing thing, byte[] frame, int programLength)
    {
        for (int i = 0; i < frame.Length; i++)
        {
            thing.SetInputs(frame[i]);
            thing.Mcu.Run(0.000005);
            thing.Pd3Low = true;
            thing.Mcu.Run(0.000010);
            thing.Pd3Low = false;
            thing.Mcu.Run(0.000005);
            if (HostUpload.PauseAfter(i, programLength)) thing.Mcu.Run(HostUpload.BlockMilliseconds / 1000.0);
            Assert.Null(thing.Mcu.Cpu.Fault);
        }
    }

    private static void Upload(OledThing thing, byte[] program)
    {
        Send(thing, HostUpload.Frame(program), program.Length);
        thing.Mcu.Run(0.3);                         // FAT, restart, program
        Assert.Null(thing.Mcu.Cpu.Fault);
    }

    private static byte[] Fat(OledThing thing) =>
        Enumerable.Range(0, ProgramStore.FatSize).Select(i => thing.Mcu.Read(ProgramStore.FatAddress + i)).ToArray();

    private static short Var(OledThing thing, int index)
    {
        int vars = Run.Kernel.Symbol("VARS");
        return (short)((thing.Mcu.Read(vars + 2 * index) << 8) | thing.Mcu.Read(vars + 2 * index + 1));
    }

    [Fact]
    public void ShowsUploadWhilePd2IsHeldAtStart()
    {
        var thing = Thing(ProgramA);
        EnterLoader(thing);
        var p = new Panel();
        p.Text(0, 0, "Upload", 1);
        p.Text(0, 12, "0", 2);
        Assert.Equal(p.ToString(), thing.Oled.PanelText());
        Assert.Equal(0, Var(thing, 0));             // the program did not run
    }

    [Fact]
    public void UploadedProgramRunsAndTheFatIsWritten()
    {
        var thing = Thing();
        EnterLoader(thing);
        var program = Run.Compile(ProgramB).Image;
        Upload(thing, program);

        Assert.Equal(222, Var(thing, 0));
        Assert.Equal(333, Var(thing, 1));
        var fat = Fat(thing);
        Assert.Equal(1, fat[0]);
        Assert.Equal(program.Length, (fat[2] << 8) | fat[3]);
        Assert.Equal(ProgramStore.Crc16(program), (fat[4] << 8) | fat[5]);
        Assert.Equal(1, ProgramStore.WriteCounts(fat).Sum());
    }

    [Fact]
    public void LargeProgramAcrossManyBlocks()
    {
        var body = string.Concat(Enumerable.Range(0, 120).Select(i => $" a = a + {i}\n"));
        var program = Run.Compile($"DIM a\nON START\n{body}END ON\n").Image;
        Assert.True(program.Length > 5 * 64);
        var thing = Thing(ProgramA);
        EnterLoader(thing);
        Upload(thing, program);
        Assert.Equal((short)Enumerable.Range(0, 120).Sum(), Var(thing, 0));
    }

    [Fact]
    public void NewBlocksAvoidTheCurrentProgram()
    {
        var thing = Thing(ProgramA);
        var before = Fat(thing);
        int oldBlock = before[1];
        EnterLoader(thing);
        Upload(thing, Run.Compile(ProgramB).Image);
        var after = Fat(thing);
        Assert.NotEqual(oldBlock, after[1]);
    }

    [Fact]
    public void BadCrcKeepsTheOldProgram()
    {
        var thing = Thing(ProgramA);
        var fatBefore = Fat(thing);
        EnterLoader(thing);
        var program = Run.Compile(ProgramB).Image;
        var frame = HostUpload.Frame(program);
        frame[^1] ^= 0x55;
        Send(thing, frame, program.Length);
        thing.Mcu.Run(0.05);

        var p = new Panel();
        p.Text(0, 0, "Upload", 1);
        p.Text(0, 12, "Failed", 1);
        Assert.Equal(p.ToString(), thing.Oled.PanelText());
        Assert.Equal(fatBefore, Fat(thing));

        thing.Reset();
        thing.Mcu.Run(0.05);
        Assert.Equal(111, Var(thing, 0));           // program A runs again
    }

    [Fact]
    public void NoiseBeforeTheFrameIsIgnored()
    {
        var thing = Thing();
        EnterLoader(thing);
        var program = Run.Compile(ProgramB).Image;
        var frame = new byte[] { 1, 2, (byte)'W', (byte)'R', 7, (byte)'W' }.Concat(HostUpload.Frame(program)).ToArray();
        Send(thing, frame, program.Length + 6);
        thing.Mcu.Run(0.3);
        Assert.Equal(222, Var(thing, 0));
    }

    [Fact]
    public void WearSpreadsOverManyUploads()
    {
        var thing = Thing();
        var program = Run.Compile(ProgramB).Image;  // one block
        for (int i = 0; i < 60; i++)
        {
            EnterLoader(thing);
            Upload(thing, program);
            Assert.Equal(222, Var(thing, 0));
        }
        var counts = ProgramStore.WriteCounts(Fat(thing));
        Assert.Equal(60, counts.Sum());
        Assert.True(counts.Max() - counts.Min() <= 1, string.Join(",", counts));
        Assert.Equal(60, (Fat(thing)[6] << 8) | Fat(thing)[7]);
    }

    [Fact]
    public void TheKernelIsNeverWritten()
    {
        var thing = Thing(ProgramA);
        EnterLoader(thing);
        Upload(thing, Run.Compile(ProgramB).Image);
        int kernelBlocks = (ProgramStore.StoreAddress - 0x8000) / 64;
        Assert.All(thing.Mcu.FlashBlockCycles.Take(kernelBlocks), n => Assert.Equal(0, n));
        Assert.Equal(kernelBlocks, thing.Mcu.ProtectedBlocks);
    }
}
