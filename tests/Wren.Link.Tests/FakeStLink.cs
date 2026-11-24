using Wren.Emu;
using Wren.Link;

namespace Wren.Link.Tests;

/// <summary>
/// An ST-LINK V2 in software: it takes the same USB packets and answers them by reading
/// and writing an emulated chip through its debug path, as SWIM does with the CPU stalled.
/// </summary>
public sealed class FakeStLink : IUsbPipe
{
    private readonly Queue<byte> replies = new();
    private byte[] readBuffer = Array.Empty<byte>();
    private int pendingAddress;
    private int pendingBytes;
    private byte swimCsr;
    private byte dmCsr2;

    public FakeStLink(Stm8Device device) => Device = device;

    public Stm8Device Device { get; }
    public byte Mode { get; private set; } = StLink.ModeDfu;
    public bool HighSpeed { get; private set; }
    public bool Connected { get; private set; }

    /// <summary>READSTATUS answers busy this many times before each OK, to exercise the polling.</summary>
    public int BusyPolls { get; set; }

    /// <summary>When set, READSTATUS answers this error (e.g. no response from the target).</summary>
    public byte? StatusError { get; set; }

    public List<string> Log { get; } = new();

    private int busyLeft;

    public void Write(ReadOnlySpan<byte> data)
    {
        if (pendingBytes > 0)
        {
            // The rest of a WRITEMEM's data, after its command packet.
            foreach (byte b in data)
            {
                WriteTarget(pendingAddress++, b);
                pendingBytes--;
            }
            return;
        }
        if (data.Length != 16) throw new InvalidOperationException($"A command packet is 16 bytes, not {data.Length}");
        Command(data.ToArray());
    }

    public void Read(Span<byte> buffer)
    {
        if (replies.Count < buffer.Length) throw new InvalidOperationException($"Read {buffer.Length} bytes, but only {replies.Count} are waiting");
        for (int i = 0; i < buffer.Length; i++) buffer[i] = replies.Dequeue();
    }

    public void Dispose() { }

    private void Reply(params byte[] bytes)
    {
        foreach (byte b in bytes) replies.Enqueue(b);
    }

    private void Command(byte[] c)
    {
        switch (c[0])
        {
            case StLink.GetVersion:
            {
                int v = (2 << 12) | (37 << 6) | 7;
                Reply((byte)(v >> 8), (byte)v, 0x83, 0x04, 0x48, 0x37);
                return;
            }
            case StLink.GetVdd: // 3.3 V: 2 x 1.2 V x 1375 / 1000
                Reply(BitConverter.GetBytes(1000u).Concat(BitConverter.GetBytes(1375u)).ToArray());
                return;
            case StLink.GetCurrentMode:
                Reply(Mode, 0);
                return;
            case StLink.Dfu when c[1] == StLink.DfuExit:
            case StLink.Debug when c[1] == StLink.DebugExit:
                Mode = StLink.ModeMass;
                return;
            case StLink.Swim:
                Swim(c);
                return;
        }
        throw new InvalidOperationException($"Unknown command {c[0]:X2} {c[1]:X2}");
    }

    private void Swim(byte[] c)
    {
        Log.Add($"SWIM {c[1]:X2}");
        switch (c[1])
        {
            case StLink.SwimEnter:
                Mode = StLink.ModeSwim;
                return;
            case StLink.SwimExit:
                Mode = StLink.ModeDfu;
                Connected = false;
                return;
            case StLink.SwimReadBufSize:
                Reply(0x00, 0x18); // 6144
                return;
            case StLink.SwimReadCap:
                Reply(new byte[8]);
                return;
            case StLink.SwimReadStatus:
                if (StatusError is byte error) Reply(error, 0, 0, 0);
                else if (busyLeft > 0) { busyLeft--; Reply(StLink.StatusBusy, 0, 0, 0); }
                else Reply(StLink.StatusOk, 0, 0, 0);
                return;
            case StLink.SwimAssertReset:
            case StLink.SwimDeassertReset:
            case StLink.SwimReset:
                break;
            case StLink.SwimEnterSeq:
                Connected = true;
                swimCsr = 0x02; // HSIT: high speed offered
                break;
            case StLink.SwimSpeed:
                HighSpeed = c[2] != 0;
                break;
            case StLink.SwimGenRst:
                if ((swimCsr & 0x04) != 0) Device.Reset();
                break;
            case StLink.SwimWriteMem:
            {
                int length = (c[2] << 8) | c[3];
                int address = (c[4] << 24) | (c[5] << 16) | (c[6] << 8) | c[7];
                int inPacket = Math.Min(8, length);
                for (int i = 0; i < inPacket; i++) WriteTarget(address + i, c[8 + i]);
                pendingAddress = address + inPacket;
                pendingBytes = length - inPacket;
                break;
            }
            case StLink.SwimReadMem:
            {
                int length = (c[2] << 8) | c[3];
                int address = (c[4] << 24) | (c[5] << 16) | (c[6] << 8) | c[7];
                readBuffer = Enumerable.Range(address, length).Select(ReadTarget).ToArray();
                break;
            }
            case StLink.SwimReadBuf:
                Reply(readBuffer);
                return;
            default:
                throw new InvalidOperationException($"Unknown SWIM command {c[1]:X2}");
        }
        busyLeft = BusyPolls;
    }

    private byte ReadTarget(int address) => address switch
    {
        SwimTarget.SwimCsr => swimCsr,
        SwimTarget.DmCsr2 => dmCsr2,
        _ => Device.Read(address),
    };

    private void WriteTarget(int address, byte value)
    {
        if (address == SwimTarget.SwimCsr) swimCsr = (byte)((value & ~0x02) | (swimCsr & 0x02)); // HSIT is read-only
        else if (address == SwimTarget.DmCsr2) dmCsr2 = value;
        else Device.DebugWrite(address, value);
    }
}
