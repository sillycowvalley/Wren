using Xunit;

namespace Wren.Script.Tests;

public class KernelTests
{
    [Fact]
    public void OpcodeTableMatchesTheCompiler()
    {
        Assert.Equal(Op.PushByte, Run.Kernel.Symbol("OP_FIRST"));
        Assert.Equal(Op.Pullups - Op.PushByte + 1, Run.Kernel.Symbol("OP_COUNT"));
    }

    [Fact]
    public void KernelEndsBelowTheProgramStore()
    {
        int end = Run.Kernel.Image.Bytes.Max(kv => kv.Key);
        Assert.True(end < ProgramStore.StoreAddress, $"The kernel ends at ${end:X4}");
    }

    [Fact]
    public void FatLayoutMatchesTheKernel()
    {
        Assert.Equal(ProgramStore.StoreAddress, Run.Kernel.Symbol("STORE"));
        Assert.Equal(ProgramStore.Blocks, Run.Kernel.Symbol("STORE_BLOCKS"));
        Assert.Equal(ProgramStore.FatAddress + ProgramStore.SettingsOffset, Run.Kernel.Symbol("SETTINGS"));
        Assert.Equal(ProgramStore.FatVersion, Run.Kernel.Symbol("FAT_FORMAT"));
        Assert.Equal(KernelInfo.Address, Run.Kernel.Symbol("INFO"));
    }

    [Fact]
    public void TheInfoBlockDescribesTheKernel()
    {
        var info = KernelInfo.Of(Run.Kernel.Image)!;
        Assert.Equal(Board.OledThing, info.Board);
        Assert.Equal(Run.Kernel.Symbol("KERNEL_VERSION"), info.KernelVersion);
        Assert.Equal(ProgramStore.FatVersion, info.FatVersion);
        Assert.Equal(ProgramStore.StoreAddress, info.StoreAddress);
        Assert.Equal(ProgramStore.Blocks, info.StoreBlocks);
        Assert.Equal(128, info.EepromSize);
        Assert.Equal(KernelInfo.SmallSettingsAddress, info.SettingsAddress);
        Assert.Equal(ProgramStore.SettingsSize, info.SettingsSize);

        var large = KernelInfo.Of(KernelInfo.ForEeprom(Run.Kernel.Image, 640))!;
        Assert.Equal(640, large.EepromSize);
        Assert.Equal(0x4080, large.SettingsAddress);
        Assert.Equal(512, large.SettingsSize);
    }
}
