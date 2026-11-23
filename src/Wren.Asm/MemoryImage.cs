using System.Text;

namespace Wren.Asm;

/// <summary>The bytes produced by the assembler, by address. Gaps are not filled.</summary>
public sealed class MemoryImage
{
    private readonly SortedDictionary<int, byte> bytes = new();

    public int Count => bytes.Count;

    public IEnumerable<KeyValuePair<int, byte>> Bytes => bytes;

    public bool Contains(int address) => bytes.ContainsKey(address);

    public byte this[int address]
    {
        get => bytes.TryGetValue(address, out byte b) ? b : (byte)0;
        set => bytes[address] = value;
    }

    /// <summary>Copies a block of bytes into the image.</summary>
    public void Write(int address, IEnumerable<byte> data)
    {
        foreach (byte b in data) bytes[address++] = b;
    }

    /// <summary>Contiguous runs of bytes, in address order.</summary>
    public IEnumerable<(int Address, byte[] Data)> Blocks()
    {
        var current = new List<byte>();
        int start = -1;
        int next = -1;
        foreach (var (address, value) in bytes)
        {
            if (address != next && current.Count > 0)
            {
                yield return (start, current.ToArray());
                current.Clear();
            }
            if (current.Count == 0) start = address;
            current.Add(value);
            next = address + 1;
        }
        if (current.Count > 0) yield return (start, current.ToArray());
    }

    /// <summary>Intel HEX, 16 bytes per record.</summary>
    public string ToIntelHex()
    {
        var sb = new StringBuilder();
        int upper = 0;
        foreach (var (address, data) in Chunks(16))
        {
            if ((address >> 16) != upper)
            {
                upper = address >> 16;
                AppendHexRecord(sb, 0, 4, new[] { (byte)(upper >> 8), (byte)upper });
            }
            AppendHexRecord(sb, address & 0xFFFF, 0, data);
        }
        AppendHexRecord(sb, 0, 1, Array.Empty<byte>());
        return sb.ToString();
    }

    /// <summary>Motorola S-records (S1, or S2 above 64 KB), 16 bytes per record. STVP loads these.</summary>
    public string ToSRecords()
    {
        var sb = new StringBuilder();
        bool wide = bytes.Count > 0 && bytes.Keys.Max() > 0xFFFF;
        foreach (var (address, data) in Chunks(16))
        {
            var record = new List<byte>();
            if (wide) record.Add((byte)(address >> 16));
            record.Add((byte)(address >> 8));
            record.Add((byte)address);
            record.AddRange(data);
            AppendSRecord(sb, wide ? '2' : '1', record);
        }
        AppendSRecord(sb, wide ? '8' : '9', wide ? new List<byte> { 0, 0, 0 } : new List<byte> { 0, 0 });
        return sb.ToString();
    }

    private IEnumerable<(int Address, byte[] Data)> Chunks(int size)
    {
        foreach (var (address, data) in Blocks())
        {
            for (int i = 0; i < data.Length; i += size)
            {
                int start = address + i;
                // Do not let a record cross a 64 KB boundary.
                int length = Math.Min(size, data.Length - i);
                int toBoundary = 0x10000 - (start & 0xFFFF);
                length = Math.Min(length, toBoundary);
                yield return (start, data.AsSpan(i, length).ToArray());
                if (length < Math.Min(size, data.Length - i)) i -= size - length;
            }
        }
    }

    private static void AppendHexRecord(StringBuilder sb, int address, int type, byte[] data)
    {
        int sum = data.Length + (address >> 8) + (address & 0xFF) + type;
        sb.Append(':').Append(data.Length.ToString("X2")).Append(address.ToString("X4")).Append(type.ToString("X2"));
        foreach (byte b in data)
        {
            sb.Append(b.ToString("X2"));
            sum += b;
        }
        sb.Append(((-sum) & 0xFF).ToString("X2")).Append("\r\n");
    }

    private static void AppendSRecord(StringBuilder sb, char type, List<byte> body)
    {
        int count = body.Count + 1;
        int sum = count;
        sb.Append('S').Append(type).Append(count.ToString("X2"));
        foreach (byte b in body)
        {
            sb.Append(b.ToString("X2"));
            sum += b;
        }
        sb.Append((~sum & 0xFF).ToString("X2")).Append("\r\n");
    }

    /// <summary>Reads Intel HEX or S-records, whichever the text holds.</summary>
    public static MemoryImage Parse(string text)
    {
        var image = new MemoryImage();
        int upper = 0;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;

            if (line[0] == ':')
            {
                byte[] r = Convert.FromHexString(line[1..]);
                int count = r[0];
                int address = (r[1] << 8) | r[2];
                int type = r[3];
                if (type == 0) image.Write((upper << 16) | address, r.AsSpan(4, count).ToArray());
                else if (type == 4) upper = (r[4] << 8) | r[5];
                else if (type == 1) break;
            }
            else if (line[0] == 'S' && line.Length >= 4)
            {
                char type = line[1];
                byte[] r = Convert.FromHexString(line[2..]);
                int addressBytes = type switch { '1' => 2, '2' => 3, '3' => 4, _ => 0 };
                if (addressBytes == 0) continue;
                int address = 0;
                for (int i = 0; i < addressBytes; i++) address = (address << 8) | r[1 + i];
                int dataLength = r[0] - addressBytes - 1;
                image.Write(address, r.AsSpan(1 + addressBytes, dataLength).ToArray());
            }
        }
        return image;
    }
}
