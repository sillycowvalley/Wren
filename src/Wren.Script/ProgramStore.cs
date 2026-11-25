namespace Wren.Script;

/// <summary>
/// The program store and its FAT in EEPROM (see docs/Bytecode.md): where an upload's
/// blocks go and what the FAT becomes. Used for the emulator now and Wren.Link later.
/// </summary>
public static class ProgramStore
{
    public const int Blocks = Compiler.StoreBlocks;     // 54
    public const int BlockSize = 64;
    public const int StoreAddress = 0x9280;

    /// <summary>The FAT's version byte for this layout (version 1 was the 47-block store at $9440).</summary>
    public const byte FatVersion = 2;
    public const int FatAddress = 0x4000;
    public const int FatSize = 128;
    public const int ChainOffset = 8;
    public const int CountsOffset = ChainOffset + Blocks;       // $3E
    public const int SettingsOffset = CountsOffset + Blocks;    // $74
    public const int SettingsSize = FatSize - SettingsOffset;   // 12
    public const byte LastBlock = 0xFE;
    public const byte FreeBlock = 0xFF;

    /// <summary>
    /// An upload: the blocks to write, the new FAT, and the program's blocks that are used as
    /// they are because the store already holds exactly their bytes.
    /// </summary>
    public sealed record Upload(IReadOnlyList<(int Block, byte[] Data)> BlockWrites, byte[] Fat, IReadOnlyList<int>? ReusedBlocks = null)
    {
        /// <summary>How many of the program's blocks were already in the store.</summary>
        public int Reused => ReusedBlocks?.Count ?? 0;

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
    /// from EEPROM; null or unformatted for a new device). With store (the whole store as
    /// read, Blocks x 64 bytes), a program block whose bytes some store block already holds,
    /// the current program's or a freed one, is chained as it is and not written: editing
    /// the end of a program, or going back to an earlier one, costs few writes or none.
    /// The other blocks are written to the least-written blocks left; write counts and
    /// program settings are kept.
    /// </summary>
    public static Upload Plan(byte[]? currentFat, byte[] program, byte[]? store = null)
    {
        if (program.Length == 0) throw new ArgumentException("The program is empty", nameof(program));
        int needed = (program.Length + BlockSize - 1) / BlockSize;
        if (needed > Blocks) throw new ArgumentException($"The program needs {needed} blocks; the store has {Blocks}", nameof(program));

        bool formatted = IsFormatted(currentFat);
        var counts = new int[Blocks];
        var settings = new byte[SettingsSize];
        int uploads = 0;
        if (formatted)
        {
            for (int b = 0; b < Blocks; b++) counts[b] = currentFat![CountsOffset + b];
            Array.Copy(currentFat!, SettingsOffset, settings, 0, SettingsSize);
            uploads = (currentFat![6] << 8) | currentFat[7];
        }

        // Blocks the store already holds, matched first (on a formatted device only: a new
        // one's store holds nothing of Wren's); the rest go to the least-written blocks left,
        // ties in block order. A block can be in the chain once, so each match is used once.
        var chosen = new int[needed];
        var reused = new bool[needed];
        var taken = new bool[Blocks];
        if (store != null && formatted)
        {
            if (store.Length != Blocks * BlockSize) throw new ArgumentException($"The store is {Blocks * BlockSize} bytes", nameof(store));
            for (int i = 0; i < needed; i++)
            {
                int length = Math.Min(BlockSize, program.Length - i * BlockSize);
                var part = program.AsSpan(i * BlockSize, length);
                for (int b = 0; b < Blocks; b++)
                {
                    if (taken[b] || !store.AsSpan(b * BlockSize, length).SequenceEqual(part)) continue;
                    chosen[i] = b;
                    reused[i] = taken[b] = true;
                    break;
                }
            }
        }
        var fresh = Enumerable.Range(0, Blocks).Where(b => !taken[b]).OrderBy(b => counts[b]).ThenBy(b => b).GetEnumerator();
        for (int i = 0; i < needed; i++)
        {
            if (reused[i]) continue;
            fresh.MoveNext();
            chosen[i] = fresh.Current;
        }

        var fat = new byte[FatSize];
        fat[0] = FatVersion;
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
        for (int i = 0; i < needed; i++)
            fat[ChainOffset + chosen[i]] = i + 1 < needed ? (byte)chosen[i + 1] : LastBlock;
        for (int i = 0; i < needed; i++) if (!reused[i]) counts[chosen[i]]++;
        for (int b = 0; b < Blocks; b++) fat[CountsOffset + b] = (byte)Math.Min(counts[b], 255);
        Array.Copy(settings, 0, fat, SettingsOffset, SettingsSize);

        var writes = new List<(int, byte[])>();
        for (int i = 0; i < needed; i++)
        {
            if (reused[i]) continue;
            var data = new byte[BlockSize];
            int length = Math.Min(BlockSize, program.Length - i * BlockSize);
            Array.Copy(program, i * BlockSize, data, 0, length);
            writes.Add((chosen[i], data));
        }
        return new Upload(writes, fat, Enumerable.Range(0, needed).Where(i => reused[i]).Select(i => chosen[i]).ToList());
    }

    /// <summary>Write counts per block from a FAT, or zeros for an unformatted one.</summary>
    /// <summary>The store blocks the current program occupies, in order; empty when there is none.</summary>
    public static IReadOnlyList<int> ProgramBlocks(byte[]? fat)
    {
        var blocks = new List<int>();
        if (!IsFormatted(fat)) return blocks;
        int block = fat![1];
        while (block < Blocks && blocks.Count < Blocks && !blocks.Contains(block))
        {
            blocks.Add(block);
            block = fat[ChainOffset + block];
        }
        return blocks;
    }

    public static int[] WriteCounts(byte[]? fat)
    {
        var counts = new int[Blocks];
        if (IsFormatted(fat))
            for (int b = 0; b < Blocks; b++) counts[b] = fat![CountsOffset + b];
        return counts;
    }

    /// <summary>True for a FAT of this layout.</summary>
    public static bool IsFormatted(byte[]? fat) => fat is { Length: FatSize } && fat[0] == FatVersion;

    /// <summary>
    /// The FAT of version 1 (47 blocks at $9440, 26 setting bytes) as this layout: old block n
    /// is new block n + 7 at the same address, so the program stays where it is, with its write
    /// counts; the 7 blocks the old kernel ended in start at 0. Null if fat is not version 1.
    /// </summary>
    public static byte[]? MigrateVersion1(byte[] fat)
    {
        const int OldBlocks = 47, OldCounts = 8 + OldBlocks, OldSettings = OldCounts + OldBlocks;
        int shift = (0x9440 - StoreAddress) / BlockSize;     // 7
        if (fat.Length != FatSize || fat[0] != 1) return null;
        var v2 = new byte[FatSize];
        v2[0] = FatVersion;
        Array.Copy(fat, 2, v2, 2, 6);                         // length, CRC, uploads
        v2[1] = fat[1] < OldBlocks ? (byte)(fat[1] + shift) : FreeBlock;
        for (int b = 0; b < Blocks; b++) v2[ChainOffset + b] = FreeBlock;
        for (int b = 0; b < OldBlocks; b++)
        {
            byte next = fat[8 + b];
            v2[ChainOffset + b + shift] = next < OldBlocks ? (byte)(next + shift) : next;
            v2[CountsOffset + b + shift] = fat[OldCounts + b];
        }
        Array.Copy(fat, OldSettings, v2, SettingsOffset, SettingsSize);
        return v2;
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
