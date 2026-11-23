using Wren.Asm;
using Wren.Emu;
using Xunit;

namespace Wren.Emu.Tests;

public class PeripheralTests
{
    private static OledThing Run(string body, double seconds = 0.01, Action<OledThing>? before = null)
    {
        string source =
            "    include \"stm8s003.inc\"\n" +
            "    org $8000\n" +
            "    int start\n" +
            "    int start\n" +
            "    org $8008+4*IRQ_EXTI_D\n" +
            "    int exti_d\n" +
            "    org $8008+4*IRQ_TIM4\n" +
            "    int tim4\n" +
            "    org $8080\n" +
            body;
        var assembler = new Assembler
        {
            IncludeResolver = name => File.ReadAllText(Path.Combine(Firmware.Folder, "include", name)),
        };
        var result = assembler.Assemble(source);
        Assert.True(result.Success, string.Join("\n", result.Errors));

        var thing = new OledThing();
        thing.Mcu.Load(result.Image.Bytes);
        before?.Invoke(thing);
        thing.PowerOn();
        thing.Mcu.Run(seconds);
        Assert.Null(thing.Mcu.Cpu.Fault);
        return thing;
    }

    private const string NoHandlers = "exti_d:\ntim4:\n    iret\n";

    [Fact]
    public void Tim4InterruptCounts()
    {
        // 16 MHz / 128 / 125 = 1 kHz. In 10 ms that is about 10 interrupts.
        var thing = Run(
            "start: clr CLK_CKDIVR\n" +
            "    mov TIM4_PSCR,#7\n" +
            "    mov TIM4_ARR,#124\n" +
            "    bset TIM4_IER,#0\n" +
            "    bset TIM4_CR1,#0\n" +
            "    rim\n" +
            "idle: wfi\n" +
            "    jra idle\n" +
            "tim4: bres TIM4_SR,#0\n" +
            "    inc $10\n" +
            "    iret\n" +
            "exti_d: iret\n", 0.0105);
        byte ticks = thing.Mcu.Read(0x10);
        Assert.InRange(ticks, 9, 11);
    }

    [Fact]
    public void Pd3FallingEdgeLatchesInputs()
    {
        // PD3 as an interrupt input with pull-up; falling edges copy b0-b4 (port C) to RAM.
        var thing = Run(
            "start: clr CLK_CKDIVR\n" +
            "    bset PD_CR1,#3\n" +
            "    bset PD_CR2,#3\n" +
            "    mov EXTI_CR1,#$80\n" +     // port D: falling edge only
            "    rim\n" +
            "idle: wfi\n" +
            "    jra idle\n" +
            "exti_d: ld a,PC_IDR\n" +
            "    ld $10,a\n" +
            "    inc $11\n" +
            "    iret\n" +
            "tim4: iret\n");

        Assert.Equal(0, thing.Mcu.Read(0x11));
        thing.SetInputs(0b0000_0001);   // b0 = PC7
        thing.Pd3Low = true;            // falling edge
        thing.Mcu.Run(0.001);
        Assert.Equal(1, thing.Mcu.Read(0x11));
        Assert.Equal(0x80, thing.Mcu.Read(0x10) & 0xF8);

        thing.SetInputs(0b0000_0010);   // changes while PD3 stays low: no new latch
        thing.Mcu.Run(0.001);
        Assert.Equal(0x80, thing.Mcu.Read(0x10) & 0xF8);

        thing.Pd3Low = false;           // rising edge: ignored
        thing.Mcu.Run(0.001);
        thing.Pd3Low = true;            // second falling edge
        thing.Mcu.Run(0.001);
        Assert.Equal(2, thing.Mcu.Read(0x11));
        Assert.Equal(0x40, thing.Mcu.Read(0x10) & 0xF8);
    }

    [Fact]
    public void EepromWritesNeedTheKeysAndAreCounted()
    {
        var thing = Run(
            "start: mov EEPROM,#$11\n" +          // locked: ignored
            "    mov FLASH_DUKR,#$AE\n" +
            "    mov FLASH_DUKR,#$56\n" +
            "    mov EEPROM+1,#$22\n" +
            "    mov EEPROM+1,#$23\n" +
            "    mov $9000,#$33\n" +             // flash is still locked
            "    break\n" + NoHandlers);
        Assert.Equal(0x00, thing.Mcu.Read(0x4000));
        Assert.Equal(0x23, thing.Mcu.Read(0x4001));
        Assert.Equal(2, thing.Mcu.EepromWrites[1]);
        Assert.Equal(0x00, thing.Mcu.Read(0x9000));
        Assert.Equal(0, thing.Mcu.FlashWrites[0x1000]);
    }

    [Fact]
    public void EepromSizeFollowsTheChip()
    {
        Assert.Equal(128, new OledThing().Mcu.Info.EepromSize);
        Assert.Equal(640, new OledThing(DeviceInfo.Stm8s103f3).Mcu.Info.EepromSize);
    }

    [Fact]
    public void UnmodelledPeripheralsAreReported()
    {
        var thing = Run("start: mov $5231,#$55\n    break\n" + NoHandlers);
        Assert.Contains("UART1", thing.Mcu.UnmodelledPeripherals);
    }

    [Fact]
    public void OledStaysDarkWithoutChargePump()
    {
        var oled = new Ssd1315();
        oled.WriteCommand(0xAF);
        oled.WriteCommand(0xA5); // all pixels on
        Assert.False(oled.Pixel(0, 0));
        oled.WriteCommand(0x8D);
        oled.WriteCommand(0x14);
        Assert.True(oled.Pixel(0, 0));
    }
}

public class FlashProgrammingTests
{
    private static Stm8Device Run(string body, byte ubc = 0)
    {
        var assembler = new Wren.Asm.Assembler
        {
            IncludeResolver = name => File.ReadAllText(Path.Combine(Firmware.Folder, "include", name)),
        };
        var r = assembler.Assemble("    include \"stm8s003.inc\"\n    org $8000\n    int start\n    org $8080\n" + body);
        Assert.True(r.Success, string.Join("\n", r.Errors));
        var d = new Stm8Device(DeviceInfo.Stm8s003f3);
        d.Load(r.Image.Bytes);
        d.Load(new[] { new KeyValuePair<int, byte>(0x4801, ubc), new KeyValuePair<int, byte>(0x4802, (byte)~ubc) });
        d.PowerOn();
        d.Run(0.01);
        return d;
    }

    private const string Unlock = "start: mov FLASH_PUKR,#$56\n    mov FLASH_PUKR,#$AE\n";

    [Fact]
    public void UbcProtectsTheBootArea()
    {
        var d = Run(Unlock + "    mov $8100,#$12\n    mov $9000,#$34\n    break\n", ubc: 32);  // $8000-$87FF protected
        Assert.NotEqual(0x12, d.Read(0x8100));
        Assert.Equal(0x34, d.Read(0x9000));
        Assert.Equal(32, d.ProtectedBlocks);
    }

    [Fact]
    public void BlockProgrammingFromFlashIsAFault()
    {
        var d = Run(Unlock + "    mov FLASH_CR2,#$01\n    mov FLASH_NCR2,#$FE\n    mov $9000,#$55\nhere: jra here\n");
        Assert.Equal("Block programming of flash must run from RAM", d.Cpu.Fault);
    }

    [Fact]
    public void BlockProgrammingFromRamIsOneCycle()
    {
        // Copy a 64-byte writer loop to RAM at $0100 and run it there.
        var d = Run(Unlock +
            "    ldw x,#writer\n    ldw y,#$0100\ncopy: ld a,(x)\n    ld (y),a\n    incw x\n    incw y\n    cpw x,#writer_end\n    jrne copy\n" +
            "    call $0100\n    break\n" +
            "writer: mov FLASH_CR2,#$01\n    mov FLASH_NCR2,#$FE\n    ldw x,#$9000\n    ld a,#64\nwloop: ld (x),a\n    incw x\n    dec a\n    jrne wloop\n" +
            "wait: btjf FLASH_IAPSR,#2,wait\n    ret\nwriter_end:\n");
        Assert.Null(d.Cpu.Fault);
        Assert.Equal(64, d.Read(0x9000));
        Assert.Equal(1, d.Read(0x903F));
        Assert.Equal(1, d.FlashBlockCycles[(0x9000 - 0x8000) / 64]);
    }
}
