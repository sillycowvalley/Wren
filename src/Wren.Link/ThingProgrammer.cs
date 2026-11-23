using Wren.Asm;
using Wren.Script;

namespace Wren.Link;

/// <summary>What a Thing (or the development board) holds, as read over SWIM.</summary>
public sealed record ThingStatus(
    bool ReadOutProtected,
    int ProtectedBlocks,
    bool HasKernel,
    int ProgramLength,
    int Uploads,
    IReadOnlyList<int> WriteCounts);

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

    private readonly SwimTarget target;

    public ThingProgrammer(SwimTarget target) => this.target = target;

    /// <summary>Messages for the user as the work goes.</summary>
    public event Action<string>? Progress;

    private void Say(string message) => Progress?.Invoke(message);

    public ThingStatus ReadStatus()
    {
        byte rop = target.ReadByte(OptionRop);
        byte ubc = target.ReadByte(OptionUbc);
        byte[] reset = target.Read(FlashStart, 4);
        byte[] fat = target.Read(ProgramStore.FatAddress, ProgramStore.FatSize);
        bool formatted = fat[0] == 1;
        return new ThingStatus(
            rop == RopOn,
            ubc,
            reset[0] == 0x82, // the reset vector holds INT
            formatted && fat[1] != ProgramStore.FreeBlock ? (fat[2] << 8) | fat[3] : 0,
            formatted ? (fat[6] << 8) | fat[7] : 0,
            ProgramStore.WriteCounts(fat));
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
