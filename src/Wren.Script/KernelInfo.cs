using Wren.Asm;

namespace Wren.Script;

/// <summary>The boards a Wren kernel drives (the info block's board byte).</summary>
public enum Board : byte
{
    Unknown = 0,
    OledThing = 1,
    LedThing = 2,
}

/// <summary>
/// The kernel's info block: 16 bytes at a fixed address just below the program store, which
/// Wren reads on Connect (docs/Bytecode.md). Wren fills in the EEPROM bytes for each Thing as
/// it installs the kernel.
/// </summary>
public sealed record KernelInfo(int Format, int KernelVersion, Board Board, int FatVersion,
    int StoreAddress, int StoreBlocks, int EepromSize, int SettingsAddress, int SettingsSize)
{
    public const int Address = ProgramStore.StoreAddress - Size;
    public const int Size = 16;

    /// <summary>The settings in a 128-byte EEPROM: what is left of it after the FAT.</summary>
    public const int SmallSettingsAddress = ProgramStore.FatAddress + ProgramStore.SettingsOffset;

    /// <summary>The settings in a 640-byte EEPROM: all of $4080-$427F.</summary>
    public const int LargeSettingsAddress = 0x4080, LargeSettingsSize = 512;

    /// <summary>The block as read from a Thing, or null when it is not a Wren kernel's (an older kernel, other firmware).</summary>
    public static KernelInfo? Parse(byte[] block)
    {
        if (block.Length < Size || block[0] != 'W' || block[1] != 'R' || block[2] != 'E' || block[3] != 'N') return null;
        return new KernelInfo(block[4], block[5], (Board)block[6], block[7],
            (block[8] << 8) | block[9], block[10], block[11] * 64,
            (block[12] << 8) | block[13], (block[14] << 8) | block[15]);
    }

    /// <summary>The kernel image's own block.</summary>
    public static KernelInfo? Of(MemoryImage kernel) =>
        Parse(Enumerable.Range(Address, Size).Select(a => kernel.Contains(a) ? kernel[a] : (byte)0).ToArray());

    /// <summary>
    /// A copy of the kernel image with its EEPROM bytes set for a Thing with eepromSize bytes
    /// (128 or 640): a 640-byte EEPROM keeps the program settings at $4080, 512 of them.
    /// </summary>
    public static MemoryImage ForEeprom(MemoryImage kernel, int eepromSize)
    {
        var copy = new MemoryImage();
        foreach (var (a, v) in kernel.Bytes) copy[a] = v;
        bool large = eepromSize >= 640;
        int settings = large ? LargeSettingsAddress : SmallSettingsAddress;
        int size = large ? LargeSettingsSize : ProgramStore.SettingsSize;
        copy[Address + 11] = (byte)((large ? 640 : 128) / 64);
        copy[Address + 12] = (byte)(settings >> 8);
        copy[Address + 13] = (byte)settings;
        copy[Address + 14] = (byte)(size >> 8);
        copy[Address + 15] = (byte)size;
        return copy;
    }

    public override string ToString() =>
        $"kernel version {KernelVersion} for the {(Board == Board.OledThing ? "OLED Thing" : Board == Board.LedThing ? "LED Thing" : "unknown board")}, " +
        $"{EepromSize} bytes of EEPROM ({SettingsSize} setting bytes)";
}
