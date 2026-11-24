using Wren.Asm;
using Wren.Script;

namespace Wren.Link;

/// <summary>What the flash holds: nothing, Wren's kernel as this build makes it, or other firmware.</summary>
public enum FlashContents { Empty, WrenKernel, OtherFirmware }

/// <summary>
/// What a Thing (or the development board) holds, as read over SWIM. The program and the
/// wear counts come from Wren's FAT, so they are only meaningful under Wren's kernel.
/// </summary>
public sealed record ThingStatus(
    bool ReadOutProtected,
    int ProtectedBlocks,
    FlashContents Flash,
    int ProgramLength,
    int Uploads,
    IReadOnlyList<int> WriteCounts)
{
    public bool HasKernel => Flash == FlashContents.WrenKernel;
}

/// <summary>
/// Wren's jobs on a real Thing: install the kernel, upload a program through the FAT, read
/// the wear history, run. Blocks that already hold the right bytes are never written, so
/// a Thing's ~100-write flash is only spent where something changed.
/// </summary>
public sealed class ThingProgrammer
{
    public const int OptionRop = 0x4800, OptionUbc = 0x4801, OptionNubc = 0x4802;
    public const int FlashStart = 0x8000;
    public const byte RopOn = 0xAA;
    public const int FlashEnd = 0xA000, EepromStart = 0x4000, EepromSize = 128, OptionSize = 11;

    private readonly SwimTarget target;

    public ThingProgrammer(SwimTarget target) => this.target = target;

    /// <summary>Messages for the user as the work goes.</summary>
    public event Action<string>? Progress;

    private void Say(string message) => Progress?.Invoke(message);

    /// <summary>
    /// Reads the option bytes, the flash's reset vector and the FAT; with a kernel image,
    /// reads the kernel's area too, to tell Wren's kernel from other firmware. Writes nothing.
    /// </summary>
    public ThingStatus ReadStatus(MemoryImage? kernel = null)
    {
        byte rop = target.ReadByte(OptionRop);
        byte ubc = target.ReadByte(OptionUbc);
        byte[] reset = target.Read(FlashStart, 4);
        var flash = reset[0] != 0x82 ? FlashContents.Empty // every STM8 program's reset vector holds INT ($82)
            : kernel != null && Matches(kernel) ? FlashContents.WrenKernel
            : kernel != null ? FlashContents.OtherFirmware
            : FlashContents.WrenKernel; // nothing to compare against: assume Wren's
        byte[] fat = target.Read(ProgramStore.FatAddress, ProgramStore.FatSize);
        bool formatted = flash == FlashContents.WrenKernel && fat[0] == 1;
        return new ThingStatus(
            rop == RopOn,
            ubc,
            flash,
            formatted && fat[1] != ProgramStore.FreeBlock ? (fat[2] << 8) | fat[3] : 0,
            formatted ? (fat[6] << 8) | fat[7] : 0,
            formatted ? ProgramStore.WriteCounts(fat) : new int[ProgramStore.Blocks]);
    }

    private bool Matches(MemoryImage kernel)
    {
        foreach (var (address, data) in kernel.Blocks().Where(b => b.Address >= FlashStart))
        {
            if (!target.Read(address, data.Length).AsSpan().SequenceEqual(data)) return false;
        }
        return true;
    }

    /// <summary>
    /// Reads everything the chip holds: the 8 KB of flash, the 128 bytes of EEPROM and the
    /// option bytes, as an image that can be saved as S-records (which ST Visual Programmer
    /// loads too). Writes nothing.
    /// </summary>
    public MemoryImage ReadEverything()
    {
        var image = new MemoryImage();
        for (int address = FlashStart; address < FlashEnd; address += 1024)
        {
            Say($"Reading flash ${address:X4}");
            image.Write(address, target.Read(address, 1024));
        }
        image.Write(EepromStart, target.Read(EepromStart, EepromSize));
        image.Write(OptionRop, target.Read(OptionRop, OptionSize));
        return image;
    }

    /// <summary>
    /// Installs the kernel: lifts the UBC lock if it is set, writes the flash blocks that
    /// differ, sets the option bytes the kernel image carries (UBC), and checks it all.
    /// Returns how many flash blocks were written.
    /// </summary>
    public int InstallKernel(MemoryImage kernel)
    {
        var flash = kernel.Bytes.Where(kv => kv.Key >= FlashStart).ToList();
        var options = kernel.Bytes.Where(kv => kv.Key is >= OptionRop and < OptionRop + 0x80).ToList();

        if (target.ReadByte(OptionUbc) != 0)
        {
            Say("Lifting the UBC lock so the kernel's blocks can be written");
            target.WriteOptionByte(OptionUbc, 0x00);
            target.WriteOptionByte(OptionNubc, 0xFF);
        }

        int written = WriteChangedBlocks(flash, TargetMemory.Flash, "kernel");

        foreach (var (address, value) in options)
        {
            if (target.ReadByte(address) == value) continue;
            Say($"Option byte ${address:X4} = ${value:X2}");
            target.WriteOptionByte(address, value);
        }

        Verify(flash.Concat(options), "kernel");
        Say(written == 0 ? "The kernel was already installed: nothing written" : $"Kernel installed: {written} blocks written");
        return written;
    }

    /// <summary>
    /// Uploads a compiled program through the FAT: least-written blocks, data first and the
    /// FAT last, then everything read back.
    /// </summary>
    public void UploadProgram(byte[] program)
    {
        byte[] fat = target.Read(ProgramStore.FatAddress, ProgramStore.FatSize);
        var upload = ProgramStore.Plan(fat, program);

        foreach (var (block, data) in upload.BlockWrites)
        {
            int address = ProgramStore.StoreAddress + block * ProgramStore.BlockSize;
            Say($"Program block {block} at ${address:X4}");
            target.WriteBlocks(address, data, TargetMemory.Flash);
        }
        var fatBytes = Enumerable.Range(0, ProgramStore.FatSize)
            .Select(i => new KeyValuePair<int, byte>(ProgramStore.FatAddress + i, upload.Fat[i])).ToList();
        WriteChangedBlocks(fatBytes, TargetMemory.Eeprom, "FAT");
        Verify(upload.Bytes(), "program");
        Say($"Uploaded {program.Length} bytes in {upload.BlockWrites.Count} blocks");
    }

    /// <summary>Resets the Thing and lets it run.</summary>
    public void Run() => target.Run();

    /// <summary>
    /// Writes the 64-byte blocks the bytes fall in, but only those whose contents differ.
    /// Bytes of a block the image does not cover keep what the target holds.
    /// </summary>
    private int WriteChangedBlocks(IReadOnlyCollection<KeyValuePair<int, byte>> bytes, TargetMemory memory, string what)
    {
        int written = 0;
        foreach (var group in bytes.GroupBy(kv => kv.Key & ~(SwimTarget.BlockSize - 1)).OrderBy(g => g.Key))
        {
            int start = group.Key;
            byte[] current = target.Read(start, SwimTarget.BlockSize);
            byte[] wanted = (byte[])current.Clone();
            foreach (var (address, value) in group) wanted[address - start] = value;
            if (wanted.AsSpan().SequenceEqual(current)) continue;
            Say($"Writing {what} block at ${start:X4}");
            target.WriteBlocks(start, wanted, memory);
            written++;
        }
        return written;
    }

    private void Verify(IEnumerable<KeyValuePair<int, byte>> bytes, string what)
    {
        foreach (var group in bytes.GroupBy(kv => kv.Key & ~(SwimTarget.BlockSize - 1)))
        {
            byte[] read = target.Read(group.Key, SwimTarget.BlockSize);
            foreach (var (address, value) in group)
            {
                if (read[address - group.Key] != value)
                    throw new LinkException($"Checking the {what}: ${address:X4} holds ${read[address - group.Key]:X2}, not ${value:X2}");
            }
        }
    }
}
