using Wren.Emu;
using Wren.Link;

namespace Wren.Link.Tests;

/// <summary>
/// A Pico running Wren's sketch, in software: it takes the serial requests and answers them
/// from an emulated chip through its debug path, as SWIM does with the CPU stalled.
/// </summary>
public sealed class FakePico : Stream
{
    private readonly List<byte> request = new();
    private readonly Queue<byte> replies = new();
    private byte swimCsr;
    private byte dmCsr2;

    public FakePico(Stm8Device device) => Device = device;

    public Stm8Device Device { get; }
    public bool ResetHeld { get; private set; }
    public bool Entered { get; private set; }
    public int CommsResets { get; private set; }

    /// <summary>When set, SWIM requests answer this status (e.g. no answer from the target).</summary>
    public byte? SwimError { get; set; }

    /// <summary>The line check's data; by default a healthy line (idle high, NRST works, a 90 ns rise, the sync pulse).</summary>
    public byte[] LineCheckReply { get; set; } = { 1, 0, 1, 0x00, 0x5A, 0, 3, 0x00, 0x00, 0x00, 0x14, 0x00, 0xB4 };

    /// <summary>The largest read or write asked for.</summary>
    public int LargestTransfer { get; private set; }

    public override void Write(byte[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            request.Add(buffer[offset + i]);
            if (Complete()) Answer();
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (replies.Count == 0) throw new InvalidOperationException("Read with no reply waiting");
        int n = Math.Min(count, replies.Count);
        for (int i = 0; i < n; i++) buffer[offset + i] = replies.Dequeue();
        return n;
    }

    private bool Complete() => request[0] switch
    {
        PicoLink.Reset => request.Count == 2,
        PicoLink.Read => request.Count == 5,
        PicoLink.ReadTrace => request.Count == 4,
        PicoLink.FrameTrace => request.Count == 2,
        PicoLink.Write => request.Count >= 2 && request.Count == 5 + request[1],
        _ => true,
    };

    private void Reply(byte command, byte status, params byte[] data)
    {
        replies.Enqueue(command);
        replies.Enqueue(status);
        if (status == PicoLink.StatusOk) foreach (byte b in data) replies.Enqueue(b);
        else { replies.Enqueue(1); replies.Enqueue(0x00); replies.Enqueue(0xA0); } // frame 1, 16.0 µs
    }

    private void Answer()
    {
        byte[] r = request.ToArray();
        request.Clear();
        byte swimStatus = SwimError ?? PicoLink.StatusOk;
        switch (r[0])
        {
            case PicoLink.Hello:
                Reply(r[0], PicoLink.StatusOk, (byte)'W', (byte)'R', (byte)'E', (byte)'N', (byte)PicoLink.SketchVersion);
                return;
            case PicoLink.Reset:
                ResetHeld = r[1] != 0;
                Reply(r[0], PicoLink.StatusOk);
                return;
            case PicoLink.ReadTrace: // header acknowledged; the reply 1 then $A1 then parity 0 at 2.75 µs a bit
            {
                var edges = new List<int>();
                int[] bits = { 1, 1, 0, 1, 0, 0, 0, 0, 1, 1 };
                for (int i = 0; i < bits.Length; i++)
                {
                    int start = 300 + i * 275;
                    edges.Add(start);
                    edges.Add(start + (bits[i] == 1 ? 25 : 250));
                }
                var data = new List<byte> { 0, 0, 0x3E, 0x80, 1, 1, (byte)edges.Count };
                foreach (int t in edges) { data.Add((byte)(t >> 8)); data.Add((byte)t); }
                Reply(r[0], PicoLink.StatusOk, data.ToArray());
                return;
            }
            case PicoLink.FrameTrace: // the Pico's five bits of ROTF (0 001 1), then the Thing's ACK
            {
                int[] edges = { 0, 200, 275, 297, 550, 572, 825, 847, 1100, 1122, 1160, 1185 };
                var data = new List<byte> { 0x3F, 0xAC, 1, (byte)edges.Length };
                foreach (int t in edges) { data.Add((byte)(t >> 8)); data.Add((byte)t); }
                Reply(r[0], PicoLink.StatusOk, data.ToArray());
                return;
            }
            case PicoLink.LineCheck:
                Reply(r[0], PicoLink.StatusOk, LineCheckReply);
                return;
            case PicoLink.Release:
                ResetHeld = false;
                Entered = false;
                Reply(r[0], PicoLink.StatusOk);
                return;
            case PicoLink.Entry:
                if (SwimError != null) { Reply(r[0], PicoLink.StatusNoSync); return; }
                Entered = true;
                swimCsr = 0x02; // HSIT offered; the Pico does not take it
                Reply(r[0], PicoLink.StatusOk, 0x3E, 0x80, 0); // 16,000 ns, begun before the release
                return;
            case PicoLink.CommsReset:
                CommsResets++;
                Reply(r[0], PicoLink.StatusOk, 0x3E, 0x80, 1); // 16,000 ns, measured whole
                return;
            case PicoLink.Srst:
                if (swimStatus == PicoLink.StatusOk && (swimCsr & 0x04) != 0) Device.Reset();
                Reply(r[0], swimStatus);
                return;
            case PicoLink.Read:
            {
                int n = r[1], address = (r[2] << 16) | (r[3] << 8) | r[4];
                LargestTransfer = Math.Max(LargestTransfer, n);
                if (n < 1 || n > PicoLink.MaxChunk) { Reply(r[0], PicoLink.StatusBadCommand); return; }
                Reply(r[0], swimStatus, Enumerable.Range(address, n).Select(ReadTarget).ToArray());
                return;
            }
            case PicoLink.Write:
            {
                int n = r[1], address = (r[2] << 16) | (r[3] << 8) | r[4];
                LargestTransfer = Math.Max(LargestTransfer, n);
                if (n < 1 || n > PicoLink.MaxChunk) { Reply(r[0], PicoLink.StatusBadCommand); return; }
                if (swimStatus == PicoLink.StatusOk)
                    for (int i = 0; i < n; i++) WriteTarget(address + i, r[5 + i]);
                Reply(r[0], swimStatus);
                return;
            }
            default:
                Reply(r[0], PicoLink.StatusBadCommand);
                return;
        }
    }

    private byte ReadTarget(int address) => address switch
    {
        SwimTarget.SwimCsr => swimCsr,
        SwimTarget.DmCsr2 => dmCsr2,
        _ => Device.Read(address),
    };

    private void WriteTarget(int address, byte value)
    {
        if (address == SwimTarget.SwimCsr) swimCsr = (byte)((value & ~0x02) | (swimCsr & 0x02));
        else if (address == SwimTarget.DmCsr2) dmCsr2 = value;
        else Device.DebugWrite(address, value);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
