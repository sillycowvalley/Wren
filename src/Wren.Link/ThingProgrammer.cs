using Wren.Asm;
using Wren.Script;

namespace Wren.Link;

/// <summary>The program store's wear, read from a Thing's program table.</summary>
public sealed record StoreWear(IReadOnlyList<int> WriteCounts, IReadOnlyList<int> ProgramBlocks, int Uploads, int ProgramLength);

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
    /// The program store's wear, as the Thing's own program table records it: the writes to
    /// each of the 47 store blocks, the blocks the current program occupies, and the number of
    /// uploads. Only meaningful under Wren's kernel. Reads only.
    /// </summary>
    public StoreWear ReadWear()
    {
        byte[] fat = target.Read(ProgramStore.FatAddress, ProgramStore.FatSize);
        bool formatted = fat[0] == 1;
        return new StoreWear(
            formatted ? ProgramStore.WriteCounts(fat) : new int[ProgramStore.Blocks],
            ProgramStore.ProgramBlocks(fat),
            formatted ? (fat[6] << 8) | fat[7] : 0,
            formatted && fat[1] != ProgramStore.FreeBlock ? (fat[2] << 8) | fat[3] : 0);
    }

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
    public int InstallKernel(MemoryImage kernel) => WriteImage(kernel, "kernel");

    /// <summary>
    /// Writes any firmware image (a .s19 or .hex, such as a Thing's factory firmware) the way
    /// the kernel is installed: lifts the UBC lock, writes the flash blocks that differ, sets
    /// any option bytes the image carries, and checks it all. An image without option bytes
    /// leaves them as they are, so read-out protection stays off. Returns the blocks written.
    /// </summary>
    public int WriteImage(MemoryImage image, string what = "image")
    {
        var flash = image.Bytes.Where(kv => kv.Key >= FlashStart).ToList();
        var options = image.Bytes.Where(kv => kv.Key is >= OptionRop and < OptionRop + 0x80).ToList();

        if (target.ReadByte(OptionUbc) != 0)
        {
            // The chip loads its option bytes at reset, so the lifted lock counts only after one.
            Say("Lifting the UBC lock so the blocks can be written, then resetting the chip so it takes effect");
            target.WriteOptionByte(OptionUbc, 0x00);
            target.WriteOptionByte(OptionNubc, 0xFF);
            target.Run();
            target.Link.Sleep(50);
            target.Connect();
        }

        int written = WriteChangedBlocks(flash, TargetMemory.Flash, what);

        foreach (var (address, value) in options)
        {
            if (target.ReadByte(address) == value) continue;
            Say($"Option byte ${address:X4} = ${value:X2}");
            target.WriteOptionByte(address, value);
        }

        Verify(flash.Concat(options), what);
        Say(written == 0 ? $"The {what} was already there: nothing written" : $"The {what} is written: {written} blocks");
        return written;
    }

    /// <summary>
    /// Puts a Thing back to its factory behaviour, without read-out protection: removes the
    /// protection first if the chip refuses memory access (which erases it), writes the
    /// factory firmware, clears the EEPROM (where Wren keeps its program table) and leaves
    /// ST's default option bytes. Only blocks that differ are written.
    /// </summary>
    public void RestoreFactory(MemoryImage factory)
    {
        if (target.NoAccess) RemoveReadOutProtection();
        WriteImage(factory, "factory firmware");
        var eeprom = Enumerable.Range(EepromStart, EepromSize).Select(a => new KeyValuePair<int, byte>(a, 0)).ToList();
        int cleared = WriteChangedBlocks(eeprom, TargetMemory.Eeprom, "EEPROM");
        if (cleared > 0) Say($"EEPROM cleared: {cleared} blocks");
        for (int i = 0; i < DefaultOptions.Length; i++)
        {
            if (target.ReadByte(OptionUbc + i) != DefaultOptions[i]) target.WriteOptionByte(OptionUbc + i, DefaultOptions[i]);
        }
        Verify(eeprom, "EEPROM");
        Say("Factory firmware restored, without read-out protection. Click Run to start it.");
    }

    /// <summary>ST's default option bytes from $4801 on: everything off, each with its complement.</summary>
    public static readonly byte[] DefaultOptions = { 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF };

    /// <summary>
    /// Removes read-out protection: writes $00 to the ROP option byte, which makes the chip
    /// erase all its flash and EEPROM (the hardware's own rule); resets it, connects again and
    /// writes ST's default option bytes. The chip is left blank and unprotected. As
    /// esp-stlink's readout_protection.py and factory_reset.py do it.
    /// </summary>
    public void RemoveReadOutProtection()
    {
        Say("Writing $00 to the read-out protection byte: the chip now erases its flash and EEPROM");
        target.WriteOptionByte(OptionRop, 0x00);
        target.Link.Sleep(100);
        Say("Resetting and connecting again");
        target.Run();
        target.Link.Sleep(50);
        target.Connect();
        if (target.NoAccess) throw new LinkException("The chip still refuses memory access after removing the protection");
        Say("Writing the default option bytes");
        for (int i = 0; i < DefaultOptions.Length; i++)
        {
            if (target.ReadByte(OptionUbc + i) != DefaultOptions[i]) target.WriteOptionByte(OptionUbc + i, DefaultOptions[i]);
        }
        Say(target.ReadByte(OptionRop) == 0x00 ? "Read-out protection is off; the chip is blank" : "The read-out protection byte did not read back as $00");
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
