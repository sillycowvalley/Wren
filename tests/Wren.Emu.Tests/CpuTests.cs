using Wren.Emu;
using Xunit;

namespace Wren.Emu.Tests;

public class CpuTests
{
    private static string ProgramText(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Programs", name));

    /// <summary>Runs until BREAK; fails on a fault or after too many steps.</summary>
    private static void RunToBreak(Cpu cpu, int maxSteps = 100_000)
    {
        for (int i = 0; i < maxSteps; i++)
        {
            cpu.Step();
            Assert.Null(cpu.Fault);
            if (cpu.BreakHit) return;
        }
        Assert.Fail("No BREAK reached");
    }

    [Fact]
    public void CpuMixMatchesUcsim()
    {
        var bus = new TestBus();
        bus.Load(ProgramText("CpuMix.asm"));
        var cpu = new Cpu(bus, 0x3FF);

        RunToBreak(cpu);

        string expected = ProgramText("CpuMix.expected").Trim();
        int count = expected.Split(' ').Length;
        string actual = string.Join(" ", bus.Memory.Skip(0x100).Take(count).Select(b => b.ToString("X2")));
        Assert.Equal(expected, actual);
        Assert.Equal(0x17FF, cpu.SP);
    }

    [Fact]
    public void ResetStartsAtResetVector()
    {
        var bus = new TestBus();
        bus.Load("    org $8000\n    int main\n    org $8080\nmain: ld a,#7\n    break\n");
        var cpu = new Cpu(bus, 0x3FF);
        Assert.Equal(0x28, cpu.CC);
        Assert.Equal(0x3FF, cpu.SP);
        RunToBreak(cpu);
        Assert.Equal(7, cpu.A);
    }

    [Fact]
    public void IllegalOpcodeFaults()
    {
        var bus = new TestBus();
        bus.Load("    org $8000\n    dc.b $05\n");
        var cpu = new Cpu(bus, 0x3FF) { PC = 0x8000 };
        cpu.Step();
        Assert.Equal("Illegal opcode 05", cpu.Fault);
        Assert.Equal(CpuState.Halted, cpu.State);
    }

    [Fact]
    public void WfiWaitsAndSetsLevelZero()
    {
        var bus = new TestBus();
        bus.Load("    org $8000\n    wfi\n");
        var cpu = new Cpu(bus, 0x3FF) { PC = 0x8000 };
        cpu.Step();
        Assert.Equal(CpuState.Waiting, cpu.State);
        Assert.Equal(0, cpu.Level);
    }

    private sealed class OneInterrupt : IInterruptSource
    {
        public bool Pending { get; set; }
        public int Irq { get; init; }
        public int Level { get; init; } = 3;
        public int Next(int currentLevel) => Pending && Level > currentLevel ? Irq : -1;
        public int LevelOf(int irq) => Level;
        public void Taken(int irq) => Pending = false;
    }

    [Fact]
    public void InterruptPushesContextAndIretRestoresIt()
    {
        var bus = new TestBus();
        bus.Load(
            "    org $8000\n" +
            "    int main\n" +
            "    org $8008+4*6\n" +   // IRQ 6 (port D)
            "    int handler\n" +
            "    org $8080\n" +
            "main: rim\n" +
            "    ld a,#$12\n" +
            "    ldw x,#$3456\n" +
            "    ldw y,#$789A\n" +
            "loop: jra loop\n" +
            "handler: ld a,#$FF\n" +
            "    clrw x\n" +
            "    clrw y\n" +
            "    iret\n");
        var irq = new OneInterrupt { Irq = 6 };
        var cpu = new Cpu(bus, 0x3FF) { Interrupts = irq };

        for (int i = 0; i < 6; i++) cpu.Step(); // to the loop
        ushort sp = cpu.SP;
        int loopPc = cpu.PC;

        irq.Pending = true;
        cpu.Step();
        Assert.Equal(0x8008 + 4 * 6, cpu.PC);
        Assert.Equal(sp - 9, cpu.SP);
        Assert.Equal(3, cpu.Level);
        // Stack, from the top: PCL PCH PCE YL YH XL XH A CC
        Assert.Equal((byte)loopPc, bus.Memory[sp]);
        Assert.Equal((byte)(loopPc >> 8), bus.Memory[sp - 1]);
        Assert.Equal(0x9A, bus.Memory[sp - 3]);
        Assert.Equal(0x78, bus.Memory[sp - 4]);
        Assert.Equal(0x56, bus.Memory[sp - 5]);
        Assert.Equal(0x34, bus.Memory[sp - 6]);
        Assert.Equal(0x12, bus.Memory[sp - 7]);

        for (int i = 0; i < 5; i++) cpu.Step(); // INT, handler, IRET
        Assert.Equal(loopPc, cpu.PC);
        Assert.Equal(sp, cpu.SP);
        Assert.Equal(0x12, cpu.A);
        Assert.Equal(0x3456, cpu.X);
        Assert.Equal(0x789A, cpu.Y);
        Assert.Equal(0, cpu.Level);
    }

    [Fact]
    public void InterruptsWaitWhileMasked()
    {
        var bus = new TestBus();
        bus.Load("    org $8000\nloop: jra loop\n");
        var irq = new OneInterrupt { Irq = 3, Pending = true };
        var cpu = new Cpu(bus, 0x3FF) { Interrupts = irq, PC = 0x8000 };
        cpu.Step(); // CC = $28 after reset: level 3, masked
        Assert.Equal(0x8000, cpu.PC);
        Assert.True(irq.Pending);
    }
}
