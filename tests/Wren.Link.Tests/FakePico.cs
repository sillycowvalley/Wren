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

    public FakePico(Stm8Device device) => Device = device;

    public Stm8Device Device { get; }
    public bool ResetHeld { get; private set; }
    public bool ResetLetGo { get; private set; }
    public int Srsts { get; private set; }
    public bool Entered { get; private set; }

    /// <summary>When set, SWIM requests answer this status (e.g. no answer from the target).</summary>
    public byte? SwimError { get; set; }

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
        PicoLink.Write => request.Count >= 2 && request.Count == 5 + request[1],
        _ => true,
    };

    private void Reply(byte command, byte status, params byte[] data)
    {
        replies.Enqueue(command);
        replies.Enqueue(status);
        if (status == PicoLink.StatusOk) foreach (byte b in data) replies.Enqueue(b);
        else { replies.Enqueue(0); replies.Enqueue(0); replies.Enqueue(0); }
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
                if (ResetHeld && r[1] != PicoLink.ResetLow) Device.Reset(); // NRST released: the chip restarts
                ResetHeld = r[1] == PicoLink.ResetLow;
                ResetLetGo = r[1] == PicoLink.ResetLetGo;
                Reply(r[0], PicoLink.StatusOk);
                return;
            case PicoLink.Release:
                ResetHeld = false;
                Device.Debug.SwimActive = false;
                Entered = false;
                Reply(r[0], PicoLink.StatusOk);
                return;
            case PicoLink.Entry:
                if (SwimError != null) { Reply(r[0], PicoLink.StatusNoSync); return; }
                Entered = true;
                swimCsr = 0x02; // HSIT offered; the Pico does not take it
                Reply(r[0], PicoLink.StatusOk, 0x3E, 0x80); // 16,000 ns
                return;
            case PicoLink.Srst:
                Srsts++;
                if (swimStatus == PicoLink.StatusOk)
                {
                    if ((swimCsr & 0x20) != 0)                                          // SWIM_DM: SRST resets the chip
                    {
                        if ((swimCsr & 0x04) != 0) { swimCsr = 0; Device.Debug.SwimActive = false; }   // RST: SWIM leaves too
                        Device.Reset();
                    }
                }
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
        _ => Device.Read(address),
    };

    private void WriteTarget(int address, byte value)
    {
        if (address == SwimTarget.SwimCsr)
        {
            swimCsr = (byte)((value & ~0x02) | (swimCsr & 0x02));
            Device.Debug.SwimActive = (swimCsr & 0x20) != 0;
        }
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
