namespace Wren.Link;

/// <summary>The two bulk endpoints of an ST-LINK: commands and data out, replies in.</summary>
public interface IUsbPipe : IDisposable
{
    /// <summary>Sends all of data on the OUT endpoint.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Reads exactly buffer.Length bytes from the IN endpoint.</summary>
    void Read(Span<byte> buffer);
}

/// <summary>A failure talking to the ST-LINK or, through it, to the target.</summary>
public sealed class LinkException : Exception
{
    public LinkException(string message) : base(message) { }
    public LinkException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>ST-LINK models Wren knows. V2 uses endpoints 2 (out) and 1 (in); the others use 1 for both.</summary>
public enum StLinkModel { V2, V21, V3 }

public sealed record StLinkVersion(int StLink, int Jtag, int Swim, int VendorId, int ProductId)
{
    public override string ToString() => $"ST-LINK V{StLink}, JTAG {Jtag}, SWIM {Swim} (USB {VendorId:X4}:{ProductId:X4})";
}

/// <summary>
/// The ST-LINK's USB commands for SWIM. Every command is a 16-byte packet; a SWIM command's
/// result is fetched with READSTATUS until it is no longer busy. Protocol as used by the
/// open-source stm8flash and OpenOCD (Wren's own code, written from them).
/// </summary>
public sealed class StLink : ITargetLink
{
    // Command classes
    public const byte GetVersion = 0xF1, Debug = 0xF2, Dfu = 0xF3, Swim = 0xF4, GetCurrentMode = 0xF5, GetVdd = 0xF7;
    // Sub-commands
    public const byte DebugExit = 0x21, DfuExit = 0x07;
    public const byte SwimEnter = 0x00, SwimExit = 0x01, SwimReadCap = 0x02, SwimSpeed = 0x03, SwimEnterSeq = 0x04,
        SwimGenRst = 0x05, SwimReset = 0x06, SwimAssertReset = 0x07, SwimDeassertReset = 0x08, SwimReadStatus = 0x09,
        SwimWriteMem = 0x0A, SwimReadMem = 0x0B, SwimReadBuf = 0x0C, SwimReadBufSize = 0x0D;
    // Modes
    public const byte ModeDfu = 0x00, ModeMass = 0x01, ModeDebug = 0x02, ModeSwim = 0x03, ModeBootloader = 0x04;
    // SWIM status
    public const byte StatusOk = 0x00, StatusBusy = 0x01, StatusNoResponse = 0x04, StatusBadState = 0x05;

    private readonly IUsbPipe pipe;

    public StLink(IUsbPipe pipe) => this.pipe = pipe;

    /// <summary>How long a SWIM command may stay busy before it is a failure.</summary>
    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Waits between status polls; tests set it to zero.</summary>
    public Action<int> Sleep { get; set; } = ms => Thread.Sleep(ms);

    public bool OffersHighSpeed => true;

    /// <summary>The ST-LINK's buffer for SWIM reads, from READBUFSIZE.</summary>
    public int ReadBufferSize { get; private set; } = 6144;

    // ---- Packets ----

    /// <summary>Sends a command packet: the bytes given, then zeros to 16; data beyond 16 follows separately.</summary>
    public void Command(params byte[] bytes)
    {
        var packet = new byte[16];
        Array.Copy(bytes, packet, Math.Min(16, bytes.Length));
        pipe.Write(packet);
        if (bytes.Length > 16) pipe.Write(bytes.AsSpan(16));
    }

    public byte[] Receive(int length)
    {
        var buffer = new byte[length];
        pipe.Read(buffer);
        return buffer;
    }

    /// <summary>A SWIM command, then status polls until it is done.</summary>
    public void SwimCommand(params byte[] bytes)
    {
        Command(bytes);
        WaitForSwim();
    }

    private void WaitForSwim()
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            Command(Swim, SwimReadStatus);
            byte[] status = Receive(4);
            if (status[0] == StatusOk) return;
            if (status[0] != StatusBusy)
            {
                string what = status[0] switch
                {
                    StatusNoResponse => "the target did not answer (is it powered, and is SWIM wired to PD1?)",
                    StatusBadState => "the ST-LINK is in the wrong state",
                    _ => $"error {status[0]:X2}",
                };
                throw new LinkException($"SWIM: {what}");
            }
            if (DateTime.UtcNow - started > BusyTimeout) throw new LinkException("SWIM: the target stayed busy");
            Sleep(1);
        }
    }

    // ---- ST-LINK ----

    public StLinkVersion ReadVersion()
    {
        Command(GetVersion);
        byte[] b = Receive(6);
        int v = (b[0] << 8) | b[1];
        return new StLinkVersion((v >> 12) & 0x0F, (v >> 6) & 0x3F, v & 0x3F, b[2] | (b[3] << 8), b[4] | (b[5] << 8));
    }

    public byte ReadMode()
    {
        Command(GetCurrentMode);
        return Receive(2)[0];
    }

    /// <summary>Target voltage in volts, or null when this ST-LINK cannot measure it (many clones).</summary>
    public double? ReadTargetVoltage(StLinkVersion version)
    {
        if (version.Jtag < 13) return null;
        Command(GetVdd);
        byte[] b = Receive(8);
        uint reference = BitConverter.ToUInt32(b, 0), target = BitConverter.ToUInt32(b, 4);
        return reference == 0 ? null : 2.0 * target * 1.2 / reference;
    }

    /// <summary>Leaves DFU, mass-storage or debug mode and enters SWIM mode.</summary>
    public void EnterSwimMode()
    {
        byte mode = ReadMode();
        if (mode == ModeDebug) Command(Debug, DebugExit);
        else if (mode is ModeDfu or ModeMass or ModeBootloader) Command(Dfu, DfuExit);
        if (mode != ModeSwim) Command(Swim, SwimEnter);

        Command(Swim, SwimReadBufSize);
        byte[] size = Receive(2);
        int n = size[0] | (size[1] << 8);
        if (n > 0) ReadBufferSize = n;

        Command(Swim, SwimReadCap, 0x01);
        Receive(8);
    }

    public void LeaveSwimMode() => Command(Swim, SwimExit);

    // ---- SWIM ----

    public void AssertReset() => SwimCommand(Swim, SwimAssertReset);
    public void DeassertReset() => SwimCommand(Swim, SwimDeassertReset);
    public void EnterSequence() => SwimCommand(Swim, SwimEnterSeq);
    public void ResetSwim() => SwimCommand(Swim, SwimReset);
    public void GenerateReset() => SwimCommand(Swim, SwimGenRst);
    public void SetHighSpeed(bool high) => SwimCommand(Swim, SwimSpeed, (byte)(high ? 1 : 0));

    /// <summary>Writes bytes to the target's memory (at most a block at a time is sensible).</summary>
    public void WriteMemory(int address, ReadOnlySpan<byte> data)
    {
        var bytes = new byte[8 + data.Length];
        bytes[0] = Swim;
        bytes[1] = SwimWriteMem;
        bytes[2] = (byte)(data.Length >> 8);
        bytes[3] = (byte)data.Length;
        bytes[4] = (byte)(address >> 24);
        bytes[5] = (byte)(address >> 16);
        bytes[6] = (byte)(address >> 8);
        bytes[7] = (byte)address;
        data.CopyTo(bytes.AsSpan(8));
        // The first 8 data bytes travel in the command packet, the rest follow it.
        SwimCommand(bytes);
    }

    /// <summary>Reads bytes from the target's memory, in pieces as large as the ST-LINK's buffer.</summary>
    public byte[] ReadMemory(int address, int length)
    {
        var result = new byte[length];
        int done = 0;
        while (done < length)
        {
            int size = Math.Min(ReadBufferSize, length - done);
            int a = address + done;
            SwimCommand(Swim, SwimReadMem, (byte)(size >> 8), (byte)size,
                (byte)(a >> 24), (byte)(a >> 16), (byte)(a >> 8), (byte)a);
            Command(Swim, SwimReadBuf);
            pipe.Read(result.AsSpan(done, size));
            done += size;
        }
        return result;
    }

    public void Dispose() => pipe.Dispose();
}
