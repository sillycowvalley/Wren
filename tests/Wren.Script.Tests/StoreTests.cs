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

    private const string Long = "DIM a, b\nON START\n";

    /// <summary>A program of several blocks whose last statement sets b to last.</summary>
    private static string Program(int first, int last) =>
        Long + string.Concat(Enumerable.Range(0, 40).Select(i => $" a = a + {first + i}\n")) + $" b = {last}\nEND ON\n";

    [Fact]
    public void AnEditAtTheEndWritesOnlyTheBlocksThatChanged()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        Assert.True(s.Upload(Program(0, 100)).Success);
        int[] before = ProgramStore.WriteCounts(s.ReadFat());
        var edited = s.Upload(Program(0, 200));
        int[] after = ProgramStore.WriteCounts(s.ReadFat());
        Assert.True(edited.Blocks >= 3);
        Assert.Equal(1, after.Sum() - before.Sum());                 // only the last block written
        s.RunFree(0.05);
        Assert.Equal("200  ($00C8)", s.Globals().Single(v => v.Name == "b").Value);
    }

    [Fact]
    public void GoingBackToAnEarlierProgramWritesNothing()
    {
        var s = new ThingSession(Run.Kernel, DeviceInfo.Stm8s003f3);
        s.Upload(Program(0, 1));                                      // A
        s.Upload(Program(1000, 2));                                   // B: every block different
        int[] before = ProgramStore.WriteCounts(s.ReadFat());
        s.Upload(Program(0, 1));                                      // A again: its blocks are still there
        Assert.Equal(before, ProgramStore.WriteCounts(s.ReadFat()));
        s.RunFree(0.05);
        Assert.Equal("1", s.Globals().Single(v => v.Name == "b").Value);
        Assert.Equal(Enumerable.Range(0, 40).Sum().ToString(), s.Globals().Single(v => v.Name == "a").Value.Split(' ')[0]);
    }

    [Fact]
    public void TwoIdenticalBlocksTakeTwoBlocks()
    {
        var program = new byte[128];                                  // two blocks of zeros
        program[0] = 1;
        program[64] = 1;
        var fat = ProgramStore.Plan(null, program).Fat;
        var store = new byte[ProgramStore.Blocks * ProgramStore.BlockSize];
        store[0] = 1;                                                 // block 0 already holds one of them
        var upload = ProgramStore.Plan(fat, program, store);
        var chain = ProgramStore.ProgramBlocks(upload.Fat);
        Assert.Equal(2, chain.Count);
        Assert.NotEqual(chain[0], chain[1]);
        Assert.Equal(1, upload.Reused);
        Assert.Single(upload.BlockWrites);
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
        fat[0] = ProgramStore.FatVersion;
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

    [Fact]
    public void AVersion1TableMovesToTheNewLayoutWithItsProgram()
    {
        // Version 1: 47 blocks at $9440, 26 setting bytes. A program in old blocks 3 then 5.
        var v1 = new byte[ProgramStore.FatSize];
        v1[0] = 1; v1[1] = 3; v1[2] = 0; v1[3] = 100; v1[4] = 0x12; v1[5] = 0x34; v1[6] = 0; v1[7] = 9;
        for (int b = 0; b < 47; b++) { v1[8 + b] = ProgramStore.FreeBlock; v1[55 + b] = (byte)(b + 1); }
        v1[8 + 3] = 5; v1[8 + 5] = ProgramStore.LastBlock;
        for (int i = 0; i < 26; i++) v1[102 + i] = (byte)(0xA0 + i);

        var v2 = ProgramStore.MigrateVersion1(v1)!;
        Assert.Equal(ProgramStore.FatVersion, v2[0]);
        Assert.Equal(new[] { 10, 12 }, ProgramStore.ProgramBlocks(v2));        // n + 7: the same addresses
        Assert.Equal(0x9440 + 3 * 64, ProgramStore.StoreAddress + 10 * 64);
        Assert.Equal(new byte[] { 0, 100, 0x12, 0x34, 0, 9 }, v2[2..8]);
        var counts = ProgramStore.WriteCounts(v2);
        Assert.Equal(new int[7], counts[..7]);                                   // the old kernel's blocks
        Assert.Equal(Enumerable.Range(1, 47), counts[7..]);
        Assert.Equal(Enumerable.Range(0xA0, ProgramStore.SettingsSize).Select(b => (byte)b), v2[ProgramStore.SettingsOffset..]);
        Assert.Null(ProgramStore.MigrateVersion1(v2));
    }
}
