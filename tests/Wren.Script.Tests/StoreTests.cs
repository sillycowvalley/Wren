using Wren.Emu;
using Xunit;

namespace Wren.Script.Tests;

public class StoreTests
{
    [Fact]
    public void WearStaysEvenOverManyUploads()
    {
        byte[]? fat = null;
        var rnd = new Random(1);
        for (int i = 0; i < 1000; i++)
        {
            var program = new byte[rnd.Next(1, 700)];
            var upload = ProgramStore.Plan(fat, program);
            fat = upload.Fat;
        }
        var counts = ProgramStore.WriteCounts(fat);
        Assert.True(counts.Max() - counts.Min() <= 1, string.Join(",", counts));
        Assert.Equal(1000, (fat![6] << 8) | fat[7]);
    }

    [Fact]
    public void ChainHoldsTheProgramInOrder()
    {
        var program = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        var upload = ProgramStore.Plan(null, program);
        var fat = upload.Fat;
        var rebuilt = new List<byte>();
        int block = fat[1];
        while (true)
        {
            rebuilt.AddRange(upload.BlockWrites.Single(w => w.Block == block).Data);
            byte next = fat[ProgramStore.ChainOffset + block];
            if (next == ProgramStore.LastBlock) break;
            block = next;
        }
        Assert.Equal(program, rebuilt.Take(200));
        Assert.Equal(ProgramStore.Crc16(program), (fat[4] << 8) | fat[5]);
    }

    [Fact]
    public void CrcMatchesTheStandardCheckValue()
    {
        Assert.Equal(0x29B1, ProgramStore.Crc16("123456789"u8.ToArray()));
    }

    [Fact]
    public void KernelRunsAProgramScatteredAcrossBlocks()
    {
        // Wear the low blocks so a 3-block program lands on scattered ones.
        var fat = new byte[ProgramStore.FatSize];
        fat[0] = 1;
        for (int b = 0; b < ProgramStore.Blocks; b++) fat[ProgramStore.CountsOffset + b] = (byte)(b % 3 == 0 ? 0 : 5);
        var body = string.Concat(Enumerable.Range(0, 40).Select(i => $" a = a + {i}\n"));
        var program = Run.Compile($"DIM a\nON START\n{body}END ON\n");
        Assert.True(program.Blocks >= 3);
        var upload = ProgramStore.Plan(fat, program.Image);
        Assert.All(upload.BlockWrites, w => Assert.Equal(0, w.Block % 3));

        var thing = new OledThing();
        thing.Mcu.Load(Run.Kernel.Image.Bytes);
        thing.Mcu.Load(upload.Bytes());
        thing.PowerOn();
        thing.Mcu.Run(0.05);
        int vars = Run.Kernel.Symbol("VARS");
        Assert.Equal(Enumerable.Range(0, 40).Sum(), (thing.Mcu.Read(vars) << 8) | thing.Mcu.Read(vars + 1));
    }

    [Fact]
    public void BadCrcShowsNoProgram()
    {
        var program = Run.Compile("DIM a\nON START\n a = 1\nEND ON\n");
        var upload = ProgramStore.Plan(null, program.Image);
        upload.Fat[5] ^= 1;
        var thing = new OledThing();
        thing.Mcu.Load(Run.Kernel.Image.Bytes);
        thing.Mcu.Load(upload.Bytes());
        thing.PowerOn();
        thing.Mcu.Run(0.05);
        int vars = Run.Kernel.Symbol("VARS");
        Assert.Equal(0, thing.Mcu.Read(vars + 1));
        var p = new Panel();
        p.Text(2, 22, "No program", 1);
        Assert.Equal(string.Join("\n", p.ToString().Split('\n').Skip(22).Take(8)),
            string.Join("\n", thing.Oled.PanelText().Split('\n').Skip(22).Take(8)));
    }

    [Fact]
    public void EmptyDeviceShowsNoProgram()
    {
        var thing = new OledThing();
        thing.Mcu.Load(Run.Kernel.Image.Bytes);
        thing.PowerOn();
        thing.Mcu.Run(0.05);
        Assert.True(thing.Oled.Lit);
        Assert.Contains('#', thing.Oled.PanelText());
    }
}
