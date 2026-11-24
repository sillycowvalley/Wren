using Wren.Emu;
using Xunit;

namespace Wren.Emu.Tests;

/// <summary>The debug module as a SWIM debugger uses it (UM0470 chapter 4).</summary>
public class DebugModuleTests
{
    /// <summary>A chip running: $8080 ld a,$9300; $8083 jra $8080.</summary>
    private static Stm8Device Chip(bool swim)
    {
        var mcu = new Stm8Device(DeviceInfo.Stm8s003f3);
        mcu.Load(new Dictionary<int, byte>
        {
            [0x8000] = 0x82, [0x8001] = 0x00, [0x8002] = 0x80, [0x8003] = 0x80,
            [0x8080] = 0xC6, [0x8081] = 0x93, [0x8082] = 0x00,
            [0x8083] = 0x20, [0x8084] = 0xFB,
        });
        mcu.Debug.SwimActive = swim;
        mcu.PowerOn();
        return mcu;
    }

    private const int Cr1 = 0x7F96, Csr1 = 0x7F98, Csr2 = 0x7F99, Enfctr = 0x7F9A;

    private static void SetBreak(Stm8Device mcu, int bk1, int bk2, byte cr1)
    {
        mcu.DebugWrite(0x7F90, (byte)(bk1 >> 16)); mcu.DebugWrite(0x7F91, (byte)(bk1 >> 8)); mcu.DebugWrite(0x7F92, (byte)bk1);
        mcu.DebugWrite(0x7F93, (byte)(bk2 >> 16)); mcu.DebugWrite(0x7F94, (byte)(bk2 >> 8)); mcu.DebugWrite(0x7F95, (byte)bk2);
        mcu.DebugWrite(Cr1, cr1);
    }

    [Fact]
    public void AResetWithSwimActiveWaitsAtTheResetVector()
    {
        var mcu = Chip(swim: true);
        mcu.Run(0.001);
        Assert.Equal(0x8000, mcu.Cpu.PC);
        Assert.Equal(0x08, mcu.Read(Csr2) & 0x08);
        mcu.DebugWrite(Csr2, 0x00);                         // clear STALL: it runs
        mcu.Run(0.001);
        Assert.NotEqual(0x8000, mcu.Cpu.PC);
        Assert.False(Chip(swim: false).Debug.Stalled);    // without SWIM it never stalls
    }

    [Fact]
    public void ADataReadInTheRangeStallsAfterTheInstruction()
    {
        var mcu = Chip(swim: true);
        SetBreak(mcu, 0x9280, 0x9FFF, 0x0C);                // BC = 001, BIR
        mcu.DebugWrite(Csr2, 0x00);
        mcu.Run(0.001);
        Assert.True(mcu.Debug.Stalled);
        Assert.Equal(0x8083, mcu.Cpu.PC);                   // the ld has finished
        Assert.Equal(0x02, mcu.Read(Csr1) & 0x02);          // BK1F
        Assert.Equal(0x83, mcu.Read(0x7F03));               // PCL through the CPU register window
        Assert.Equal(0x80, mcu.Read(0x7F02));               // PCH
    }

    [Fact]
    public void EitherAddressAndOutOfRangeReads()
    {
        var mcu = Chip(swim: true);
        SetBreak(mcu, 0x9000, 0x9300, 0x24);                // BC = 100: on BK1 or BK2
        mcu.DebugWrite(Csr2, 0x00);
        mcu.Run(0.001);
        Assert.True(mcu.Debug.Stalled);
        Assert.Equal(0x04, mcu.Read(Csr1) & 0x06);          // BK2F only

        var quiet = Chip(swim: true);
        SetBreak(quiet, 0x9400, 0x9FFF, 0x0C);              // $9300 is outside
        quiet.DebugWrite(Csr2, 0x00);
        quiet.Run(0.001);
        Assert.False(quiet.Debug.Stalled);
    }

    [Fact]
    public void ClearingStallClearsTheFlagsAndCpuRegistersCanBeSet()
    {
        var mcu = Chip(swim: true);
        mcu.DebugWrite(0x7F00, 0x5A);                       // A, while stalled at reset
        Assert.Equal(0x5A, mcu.Cpu.A);
        SetBreak(mcu, 0x9300, 0x9300, 0x24);
        mcu.DebugWrite(Csr2, 0x00);
        mcu.Run(0.001);
        Assert.Equal(0x06, mcu.Read(Csr1) & 0x06);          // BK1 and BK2 both match
        mcu.DebugWrite(Csr2, 0x00);
        Assert.Equal(0, mcu.Read(Csr1) & 0x06);
        mcu.DebugWrite(0x7F00, 0x11);                       // not stalled now: ignored
        Assert.NotEqual(0x11, mcu.Cpu.A);
    }

    [Fact]
    public void Tim4FreezesWhileStalledOnlyWhenAsked()
    {
        var mcu = Chip(swim: true);                          // stalled at reset
        mcu.DebugWrite(0x5340, 0x01);                       // TIM4 on, no prescaler
        mcu.DebugWrite(0x5348, 0xFF);
        byte a = mcu.Read(0x5346);
        mcu.Run(0.0001);
        Assert.NotEqual(a, mcu.Read(0x5346));               // ENFCTR at reset: TIM4 runs on
        mcu.DebugWrite(Enfctr, 0xFE);                       // ENFCT0 clear: TIM4 frozen
        byte b = mcu.Read(0x5346);
        mcu.Run(0.0001);
        Assert.Equal(b, mcu.Read(0x5346));
    }

    [Fact]
    public void AnInstructionBreakpointStopsBeforeItOnceAPass()
    {
        var mcu = Chip(swim: true);
        SetBreak(mcu, 0x8083, 0x8083, 0x08);                // BC = 001, no BIR/BIW: instruction fetch
        mcu.DebugWrite(Csr2, 0x00);
        mcu.Run(0.0005);
        Assert.True(mcu.Debug.Stalled);
        Assert.Equal(0x8083, mcu.Cpu.PC);                   // before the jra
        mcu.DebugWrite(Csr2, 0x00);                         // resume: the jra runs, then the loop comes round again
        mcu.Step();
        Assert.False(mcu.Debug.Stalled);
        Assert.Equal(0x8080, mcu.Cpu.PC);
        mcu.Run(0.0005);
        Assert.True(mcu.Debug.Stalled);
        Assert.Equal(0x8083, mcu.Cpu.PC);
    }
}
