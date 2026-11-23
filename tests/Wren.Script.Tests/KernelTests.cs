using Xunit;

namespace Wren.Script.Tests;

public class KernelTests
{
    [Fact]
    public void OpcodeTableMatchesTheCompiler()
    {
        Assert.Equal(Op.PushByte, Run.Kernel.Symbol("OP_FIRST"));
        Assert.Equal(Op.Flip - Op.PushByte + 1, Run.Kernel.Symbol("OP_COUNT"));
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
    }
}
