using Wren.Asm;
using Wren.Script;

namespace Wren.Link;

/// <summary>Which board a Thing is, and how Wren knows.</summary>
public sealed record BoardFinding(Board Board, string How);

/// <summary>The program store's wear, read from a Thing's program table.</summary>
public sealed record StoreWear(IReadOnlyList<int> WriteCounts, IReadOnlyList<int> ProgramBlocks, int Uploads, int ProgramLength);

/// <summary>What the flash holds: nothing, Wren's kernel as this build makes it, or other firmware.</summary>
/// <summary>
/// What the flash holds: nothing; Wren's kernel as this build makes it; Wren's kernel from
/// another build (most of its blocks match: a kernel update writes only the rest); or other
/// firmware.
/// </summary>
public enum FlashContents { Empty, WrenKernel, WrenKernelOtherBuild, WrenKernelOlder, OtherFirmware }

/// <summary>
/// What a Thing (or the development board) holds, as read over SWIM. The program and the
/// wear counts come from Wren's FAT, so they are only meaningful under Wren's kernel.
/// </summary>
public sealed record ThingStatus(
    bool ReadOutProtected,
    int ProtectedBlocks,
    FlashContents Flash,
    int KernelBlocksDiffering,
    int ProgramLength,
    int Uploads,
    IReadOnlyList<int> WriteCounts,
    KernelInfo? Info = null)
{
    /// <summary>Wren's kernel, any build: the program table is Wren's.</summary>
    public bool HasKernel => Flash is FlashContents.WrenKernel or FlashContents.WrenKernelOtherBuild or FlashContents.WrenKernelOlder;
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
    /// each of the 54 store blocks, the blocks the current program occupies, and the number of
    /// uploads. Only meaningful under Wren's kernel. Reads only.
    /// </summary>
    public StoreWear ReadWear()
    {
        byte[] fat = ReadFat();
        bool formatted = ProgramStore.IsFormatted(fat);
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
        byte[] vectors = target.Read(FlashStart, WrenVectors.Length);
        var info = ReadInfo();
        int differing = 0;
        FlashContents flash;
        if (vectors[0] != 0x82) flash = FlashContents.Empty; // every STM8 program's reset vector holds INT ($82)
        else if (kernel == null) flash = FlashContents.WrenKernel; // nothing to compare against: assume Wren's
        else
        {
            // Compare with the kernel as Wren installed it here: its info block carries this Thing's EEPROM.
            var installed = info != null ? KernelInfo.ForEeprom(kernel, info.EepromSize) : kernel;
            var (blocks, differ) = CompareBlocks(installed);
            differing = differ;
            // Another build of Wren's kernel shares nearly all its blocks; an older layout
            // (before the info block) still starts with Wren's vectors; other firmware shares neither.
            flash = differ == 0 ? FlashContents.WrenKernel
                : differ * 4 <= blocks ? FlashContents.WrenKernelOtherBuild
                : info == null && vectors.AsSpan().SequenceEqual(WrenVectors) ? FlashContents.WrenKernelOlder
                : FlashContents.OtherFirmware;
        }
        byte[] fat = ReadFat();
        bool formatted = flash is not (FlashContents.Empty or FlashContents.OtherFirmware) && ProgramStore.IsFormatted(fat);
        return new ThingStatus(
            rop == RopOn,
            ubc,
            flash,
            differing,
            formatted && fat[1] != ProgramStore.FreeBlock ? (fat[2] << 8) | fat[3] : 0,
            formatted ? (fat[6] << 8) | fat[7] : 0,
            formatted ? ProgramStore.WriteCounts(fat) : new int[ProgramStore.Blocks],
            info);
    }

    /// <summary>The first 8 bytes of every Wren kernel: reset to start ($8081), TRAP to an IRET ($8080).</summary>
    private static readonly byte[] WrenVectors = { 0x82, 0x00, 0x80, 0x81, 0x82, 0x00, 0x80, 0x80 };

    /// <summary>The program table, an older version 1 table read as this layout (its program in place).</summary>
    private byte[] ReadFat()
    {
        byte[] fat = target.Read(ProgramStore.FatAddress, ProgramStore.FatSize);
        return ProgramStore.MigrateVersion1(fat) ?? fat;
    }

    /// <summary>The kernel's info block, or null when the flash holds no Wren kernel with one.</summary>
    public KernelInfo? ReadInfo() => KernelInfo.Parse(target.Read(KernelInfo.Address, KernelInfo.Size));

    /// <summary>
    /// Digicool's factory images by their first 8 bytes (the reset and TRAP vectors): the three
    /// published when this was written (OLED Thing v1.0 and v1.1, LED Thing), and any fetched
    /// since (<see cref="DigicoolFirmware"/> adds them).
    /// </summary>
    private static readonly Dictionary<string, Board> DigicoolImages = new()
    {
        ["82008CC582009D57"] = Board.OledThing,
        ["82008CCB82009E7D"] = Board.OledThing,
        ["8200815082008524"] = Board.LedThing,
    };

    /// <summary>Remembers a Digicool image's first 8 bytes, so a Thing holding it is known as this board.</summary>
    public static void KnowDigicoolImage(ReadOnlySpan<byte> vectors, Board board)
    {
        if (vectors.Length < 8 || board == Board.Unknown) return;
        lock (DigicoolImages) DigicoolImages[Convert.ToHexString(vectors[..8])] = board;
    }

    private static Board DigicoolBoard(byte[] vectors)
    {
        lock (DigicoolImages) return DigicoolImages.TryGetValue(Convert.ToHexString(vectors), out var board) ? board : Board.Unknown;
    }

    /// <summary>
    /// Which board this is: from Digicool's factory firmware if it is there, else from a Wren
    /// kernel's info block, else by asking the I2C bus whether an OLED answers at $3C (the OLED
    /// Thing's display; on an LED Thing, PB4/PB5 are inputs b6 and b7). Writes no flash or EEPROM.
    /// </summary>
    public BoardFinding DetectBoard()
    {
        switch (DigicoolBoard(target.Read(FlashStart, 8)))
        {
            case Board.OledThing: return new BoardFinding(Board.OledThing, "it holds Digicool's OLED Thing firmware");
            case Board.LedThing: return new BoardFinding(Board.LedThing, "it holds Digicool's LED Thing firmware");
        }
        var info = ReadInfo();
        if (info != null && info.Board != Board.Unknown) return new BoardFinding(info.Board, $"it holds Wren's {info}");
        return OledAnswers()
            ? new BoardFinding(Board.OledThing, "an OLED answered at I2C address $3C")
            : new BoardFinding(Board.Unknown, "no OLED answered at I2C address $3C: an LED Thing, or an OLED Thing whose display is not answering");
    }

    private const int I2cCr1 = 0x5210, I2cCr2 = 0x5211, I2cFreqr = 0x5212, I2cOarh = 0x5214, I2cDr = 0x5216,
        I2cSr1 = 0x5217, I2cSr2 = 0x5218, I2cSr3 = 0x5219, I2cCcrl = 0x521B, I2cCcrh = 0x521C, I2cTriser = 0x521D,
        PbIdr = 0x5006;

    /// <summary>
    /// Sends the OLED's address ($3C) on the I2C bus from the stalled chip and reports whether
    /// anything acknowledged. Only tried when SCL and SDA both read high: the OLED module pulls
    /// them up, so a low one means no OLED, and nothing is driven.
    /// </summary>
    public bool OledAnswers()
    {
        if ((target.ReadByte(PbIdr) & 0x30) != 0x30) return false;
        target.WriteByte(SwimTarget.ClkCkdivr, 0x00);          // 16 MHz
        target.WriteByte(I2cCr1, 0x00);
        target.WriteByte(I2cFreqr, 16);
        target.WriteByte(I2cCcrl, 80);                         // 100 kHz
        target.WriteByte(I2cCcrh, 0);
        target.WriteByte(I2cTriser, 17);
        target.WriteByte(I2cOarh, 0x40);
        target.WriteByte(I2cCr1, 0x01);                        // PE
        try
        {
            target.WriteByte(I2cCr2, 0x01);                    // START
            if (!Poll(I2cSr1, 0x01)) return false;             // SB
            target.WriteByte(I2cDr, 0x78);                     // $3C, write
            for (int i = 0; i < 10; i++)
            {
                if ((target.ReadByte(I2cSr1) & 0x02) != 0)     // ADDR: acknowledged
                {
                    target.ReadByte(I2cSr3);
                    return true;
                }
                if ((target.ReadByte(I2cSr2) & 0x04) != 0) return false;   // AF: no one there
                target.Link.Sleep(1);
            }
            return false;
        }
        finally
        {
            target.WriteByte(I2cCr2, 0x02);                    // STOP
            target.Link.Sleep(1);
            target.WriteByte(I2cSr2, 0x00);
            target.WriteByte(I2cCr1, 0x00);
        }
    }

    private bool Poll(int register, byte mask)
    {
        for (int i = 0; i < 10; i++)
        {
            if ((target.ReadByte(register) & mask) != 0) return true;
            target.Link.Sleep(1);
        }
        return false;
    }

    /// <summary>The kernel's 64-byte flash blocks, and how many of them the Thing holds differently.</summary>
    private (int Blocks, int Differing) CompareBlocks(MemoryImage kernel)
    {
        int blocks = 0, differing = 0;
        foreach (var group in kernel.Bytes.Where(kv => kv.Key >= FlashStart).GroupBy(kv => kv.Key & ~(SwimTarget.BlockSize - 1)))
        {
            blocks++;
            byte[] held = target.Read(group.Key, SwimTarget.BlockSize);
            if (group.Any(kv => held[kv.Key - group.Key] != kv.Value)) differing++;
        }
        return (blocks, differing);
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
    public int InstallKernel(MemoryImage kernel) => InstallKernel(new[] { kernel });

    /// <summary>
    /// Installs the kernel for this Thing's board, chosen from kernels by the board byte in each
    /// one's info block. A board Wren cannot tell gets nothing: a blank LED Thing is told
    /// from a blank OLED Thing only by its missing display, so it is refused rather than guessed.
    /// </summary>
    public int InstallKernel(IEnumerable<MemoryImage> kernels)
    {
        var found = DetectBoard();
        Say($"The board: {found.How}.");
        if (found.Board == Board.Unknown)
            throw new LinkException("Wren could not tell which Thing this is, so it installs nothing: " + found.How +
                ". On an LED Thing, write Digicool's LED Thing firmware first (Write image…), then Install kernel.");
        var kernel = kernels.FirstOrDefault(k => KernelInfo.Of(k)?.Board == found.Board)
            ?? throw new LinkException($"This Wren has no kernel for the {(found.Board == Board.LedThing ? "LED Thing" : "OLED Thing")}.");
        bool large = EepromSizeCheck.HasExtraEeprom(target);
        Say(large ? "EEPROM: 640 bytes, so the program settings get 512 bytes at $4080." : $"EEPROM: 128 bytes, so the program settings get {ProgramStore.SettingsSize}.");
        int written = WriteImage(KernelInfo.ForEeprom(kernel, large ? 640 : 128), "kernel");
        byte[] fat = target.Read(ProgramStore.FatAddress, ProgramStore.FatSize);
        if (ProgramStore.MigrateVersion1(fat) is byte[] moved)
        {
            var bytes = Enumerable.Range(0, ProgramStore.FatSize)
                .Select(i => new KeyValuePair<int, byte>(ProgramStore.FatAddress + i, moved[i])).ToList();
            WriteChangedBlocks(bytes, TargetMemory.Eeprom, "FAT");
            Say("The program table is in the new layout: the program stays where it was, with its blocks' write counts.");
        }
        return written;
    }

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
    /// Write image: replaces the Thing's firmware with another (such as Digicool's factory
    /// firmware), without read-out protection: removes the protection first if the chip
    /// refuses memory access (which erases it), writes the image, clears the EEPROM (Wren's
    /// program table means nothing to other firmware) and leaves ST's default option bytes.
    /// Only blocks that differ are written.
    /// </summary>
    public void ReplaceFirmware(MemoryImage image, string name)
    {
        if (target.NoAccess) RemoveReadOutProtection();
        WriteImage(image, name);
        var eeprom = Enumerable.Range(EepromStart, EepromSize).Select(a => new KeyValuePair<int, byte>(a, 0)).ToList();
        int cleared = WriteChangedBlocks(eeprom, TargetMemory.Eeprom, "EEPROM");
        if (cleared > 0) Say($"EEPROM cleared: {cleared} blocks");
        for (int i = 0; i < DefaultOptions.Length; i++)
        {
            if (target.ReadByte(OptionUbc + i) != DefaultOptions[i]) target.WriteOptionByte(OptionUbc + i, DefaultOptions[i]);
        }
        Verify(eeprom, "EEPROM");
        Say($"{name} written, without read-out protection. Disconnect (or close this window) and it runs.");
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
        var info = ReadInfo();
        if (info == null || info.FatVersion != ProgramStore.FatVersion || info.StoreAddress != ProgramStore.StoreAddress)
            throw new LinkException("The Thing's kernel is older than this Wren's: click Install kernel first.");
        byte[] fat = target.Read(ProgramStore.FatAddress, ProgramStore.FatSize);
        // Read before writing: blocks the store already holds are chained, not written.
        var store = new byte[ProgramStore.Blocks * ProgramStore.BlockSize];
        for (int offset = 0; offset < store.Length; offset += 1024)
        {
            int length = Math.Min(1024, store.Length - offset);
            target.Read(ProgramStore.StoreAddress + offset, length).CopyTo(store, offset);
        }
        var upload = ProgramStore.Plan(fat, program, store);

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
        Say(upload.Reused == 0
            ? $"Uploaded {program.Length} bytes in {upload.BlockWrites.Count} blocks"
            : $"Uploaded {program.Length} bytes: {upload.BlockWrites.Count} block{(upload.BlockWrites.Count == 1 ? "" : "s")} written, " +
              $"{upload.Reused} already in the store");
    }

    /// <summary>
    /// True when the Thing already holds exactly this program: same length, and the same bytes
    /// in the blocks its program table chains. Reads only.
    /// </summary>
    public bool HoldsProgram(byte[] program)
    {
        byte[] fat = target.Read(ProgramStore.FatAddress, ProgramStore.FatSize);
        if (!ProgramStore.IsFormatted(fat) || fat[1] == ProgramStore.FreeBlock || ((fat[2] << 8) | fat[3]) != program.Length) return false;
        var blocks = ProgramStore.ProgramBlocks(fat);
        if (blocks.Count * ProgramStore.BlockSize < program.Length) return false;
        for (int i = 0; i * ProgramStore.BlockSize < program.Length; i++)
        {
            int n = Math.Min(ProgramStore.BlockSize, program.Length - i * ProgramStore.BlockSize);
            byte[] held = target.Read(ProgramStore.StoreAddress + blocks[i] * ProgramStore.BlockSize, n);
            if (!held.AsSpan().SequenceEqual(program.AsSpan(i * ProgramStore.BlockSize, n))) return false;
        }
        return true;
    }

    /// <summary>
    /// Uploads the program unless the Thing already holds it, so starting the same program
    /// again costs no flash writes. True when it uploaded.
    /// </summary>
    public bool UploadIfChanged(byte[] program)
    {
        if (HoldsProgram(program))
        {
            Say("The Thing already holds this program: nothing uploaded, no flash written.");
            return false;
        }
        UploadProgram(program);
        return true;
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
