namespace Wren.Script;

/// <summary>
/// The program store and its FAT in EEPROM (see docs/Bytecode.md): where an upload's
/// blocks go and what the FAT becomes. Used for the emulator now and Wren.Link later.
/// </summary>
public static class ProgramStore
{
    public const int Blocks = Compiler.StoreBlocks;     // 47
    public const int BlockSize = 64;
    public const int StoreAddress = 0x9440;
    public const int FatAddress = 0x4000;
    public const int FatSize = 128;
    public const int ChainOffset = 8;
    public const int CountsOffset = ChainOffset + Blocks;       // $37
    public const int SettingsOffset = CountsOffset + Blocks;    // $66
    public const int SettingsSize = FatSize - SettingsOffset;   // 26
    public const byte LastBlock = 0xFE;
    public const byte FreeBlock = 0xFF;

    /// <summary>An upload: the blocks to write and the new FAT.</summary>
    public sealed record Upload(IReadOnlyList<(int Block, byte[] Data)> BlockWrites, byte[] Fat)
    {
        /// <summary>Everything as address and byte, ready for a memory image or the emulator.</summary>
        public IEnumerable<KeyValuePair<int, byte>> Bytes()
        {
            foreach (var (block, data) in BlockWrites)
            {
                for (int i = 0; i < data.Length; i++)
                    yield return new KeyValuePair<int, byte>(StoreAddress + block * BlockSize + i, data[i]);
            }
            for (int i = 0; i < Fat.Length; i++)
                yield return new KeyValuePair<int, byte>(FatAddress + i, Fat[i]);
        }
    }

    /// <summary>
    /// Plans an upload of program onto a device whose FAT is currentFat (128 bytes as read
    /// from EEPROM; null or unformatted for a new device). Blocks are taken least-written
    /// first; write counts and program settings are kept.
    /// </summary>
    public static Upload Plan(byte[]? currentFat, byte[] program)
    {
        if (program.Length == 0) throw new ArgumentException("The program is empty", nameof(program));
        int needed = (program.Length + BlockSize - 1) / BlockSize;
        if (needed > Blocks) throw new ArgumentException($"The program needs {needed} blocks; the store has {Blocks}", nameof(program));

        bool formatted = currentFat is { Length: FatSize } && currentFat[0] == 1;
        var counts = new int[Blocks];
        var settings = new byte[SettingsSize];
        int uploads = 0;
        if (formatted)
        {
            for (int b = 0; b < Blocks; b++) counts[b] = currentFat![CountsOffset + b];
            Array.Copy(currentFat!, SettingsOffset, settings, 0, SettingsSize);
            uploads = (currentFat![6] << 8) | currentFat[7];
        }

        // Least-written blocks first; ties in block order.
        var chosen = Enumerable.Range(0, Blocks).OrderBy(b => counts[b]).ThenBy(b => b).Take(needed).ToList();

        var fat = new byte[FatSize];
        fat[0] = 1;
        fat[1] = (byte)chosen[0];
        fat[2] = (byte)(program.Length >> 8);
        fat[3] = (byte)program.Length;
        ushort crc = Crc16(program);
        fat[4] = (byte)(crc >> 8);
        fat[5] = (byte)crc;
        uploads = Math.Min(uploads + 1, 0xFFFF);
        fat[6] = (byte)(uploads >> 8);
        fat[7] = (byte)uploads;
        for (int b = 0; b < Blocks; b++) fat[ChainOffset + b] = FreeBlock;
        for (int i = 0; i < chosen.Count; i++)
            fat[ChainOffset + chosen[i]] = i + 1 < chosen.Count ? (byte)chosen[i + 1] : LastBlock;
        foreach (int b in chosen) counts[b]++;
        for (int b = 0; b < Blocks; b++) fat[CountsOffset + b] = (byte)Math.Min(counts[b], 255);
        Array.Copy(settings, 0, fat, SettingsOffset, SettingsSize);

        var writes = new List<(int, byte[])>();
        for (int i = 0; i < chosen.Count; i++)
        {
            var data = new byte[BlockSize];
            int length = Math.Min(BlockSize, program.Length - i * BlockSize);
            Array.Copy(program, i * BlockSize, data, 0, length);
            writes.Add((chosen[i], data));
        }
        return new Upload(writes, fat);
    }

    /// <summary>Write counts per block from a FAT, or zeros for an unformatted one.</summary>
    public static int[] WriteCounts(byte[]? fat)
    {
        var counts = new int[Blocks];
        if (fat is { Length: FatSize } && fat[0] == 1)
            for (int b = 0; b < Blocks; b++) counts[b] = fat[CountsOffset + b];
        return counts;
    }

    /// <summary>CRC-16/CCITT: start $FFFF, polynomial $1021, as the kernel checks it.</summary>
    public static ushort Crc16(IEnumerable<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}
