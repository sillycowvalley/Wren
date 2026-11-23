namespace Wren.Script;

/// <summary>
/// Uploading a program from a host (the Reggie, a Teensy) over the Thing's pins, as the
/// kernel's loader expects it (firmware/kernel/Loader.inc, docs/Bytecode.md):
///
/// 1. Hold PD2 low while the Thing starts (power on or NRST). It shows "Upload" and 0.
///    Release PD2 once it does: the kernel only looks at PD2 at start-up.
/// 2. For each byte of the frame: put it on b0-b7, then pull PD3 low and let it go high.
///    The byte is read at the falling edge. Allow ByteMicroseconds per byte.
/// 3. After every 64th program byte, and after the last one, wait BlockMilliseconds while
///    the Thing writes a flash block and shows its progress.
///
/// On success the new program starts at once. On a bad CRC or length the Thing shows
/// "Failed" and keeps its previous program; reset it to run that, or upload again.
/// </summary>
public static class HostUpload
{
    public const int ByteMicroseconds = 20;
    public const int BlockMilliseconds = 25;
    public const int HeaderLength = 6; // "WREN" and the length

    /// <summary>The bytes to send: "WREN", the length (high byte first), the program, its CRC-16.</summary>
    public static byte[] Frame(byte[] program)
    {
        if (program.Length is 0 or > ProgramStore.Blocks * ProgramStore.BlockSize)
            throw new ArgumentException($"A program must be 1 to {ProgramStore.Blocks * ProgramStore.BlockSize} bytes", nameof(program));
        ushort crc = ProgramStore.Crc16(program);
        var frame = new List<byte>(program.Length + 8) { (byte)'W', (byte)'R', (byte)'E', (byte)'N' };
        frame.Add((byte)(program.Length >> 8));
        frame.Add((byte)program.Length);
        frame.AddRange(program);
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)crc);
        return frame.ToArray();
    }

    /// <summary>True when the host must pause BlockMilliseconds after sending frame byte i.</summary>
    public static bool PauseAfter(int index, int programLength)
    {
        int data = index - HeaderLength + 1; // program bytes sent so far
        return data > 0 && data <= programLength && (data % ProgramStore.BlockSize == 0 || data == programLength);
    }
}
