using Wren.Emu;
using Wren.Link;

namespace Wren.Link.Tests;

/// <summary>
/// An esp-stlink programmer (such as a Pico running pico-swimlib) in software: it takes the
/// serial requests and answers them from an emulated chip through its debug path, as SWIM
/// does with the CPU stalled. Reply layouts follow pico-swimlib's pico-espstlink example.
/// </summary>
public sealed class FakeEspStLink : Stream
{
    private readonly List<byte> request = new();
    private readonly Queue<byte> replies = new();
    private byte swimCsr;
    private byte dmCsr2;

    public FakeEspStLink(Stm8Device device) => Device = device;

    public Stm8Device Device { get; }
    public bool ResetHeld { get; private set; }
    public bool Entered { get; private set; }
    public int Srsts { get; private set; }
    public int LargestRead { get; private set; }
    public int LargestWrite { get; private set; }

    /// <summary>When set, reads and writes fail with this esp-stlink error code.</summary>
    public int? Error { get; set; }

    /// <summary>When set, the chip has read-out protection: SWIM_CSR shows NO_ACCESS and memory reads time out.</summary>
    public bool ReadOutProtected { get; set; }

    /// <summary>When set, the device answers nothing (like a port with something else on it).</summary>
    public bool Silent { get; set; }

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
        if (replies.Count == 0) throw new TimeoutException("Nothing to read");
        int n = Math.Min(count, replies.Count);
        for (int i = 0; i < n; i++) buffer[offset + i] = replies.Dequeue();
        return n;
    }

    private bool Complete() => request[0] switch
    {
        EspStLink.Read => request.Count == 5,
        EspStLink.Write => request.Count >= 2 && request.Count == 5 + request[1],
        EspStLink.Reset => request.Count == 2,
        _ => true,
    };

    private void Reply(params byte[] bytes)
    {
        foreach (byte b in bytes) replies.Enqueue(b);
    }

    private void Fail(byte command, int error) => Reply(command, EspStLink.Failure, (byte)(error >> 8), (byte)error);

    private void Answer()
    {
        byte[] r = request.ToArray();
        request.Clear();
        if (Silent) return;
        switch (r[0])
        {
            case EspStLink.Version:
                Reply(r[0], EspStLink.Success, 0, 1);
                return;
            case EspStLink.Reset:
                ResetHeld = r[1] == EspStLink.ResetLow;
                Reply(r[0], EspStLink.Success);
                return;
            case EspStLink.Entry:
                Entered = true;
                swimCsr = (byte)(0x02 | (ReadOutProtected ? 0x40 : 0)); // HSIT offered; this programmer does not take it
                Reply(r[0], EspStLink.Success, 0x05, 0x00); // 1280 cycles at 80 MHz: 16 µs
                return;
            case EspStLink.Srst:
                Srsts++;
                if ((swimCsr & 0x24) == 0x24) Device.Reset(); // SWIM_DM and RST set
                Reply(r[0], EspStLink.Success);
                return;
            case EspStLink.Read:
            {
                int n = r[1], address = (r[2] << 16) | (r[3] << 8) | r[4];
                LargestRead = Math.Max(LargestRead, n);
                if (Error is int e) { Fail(r[0], e); return; }
                if (ReadOutProtected && address != SwimTarget.SwimCsr) { Fail(r[0], EspStLink.ErrorTimeout); return; }
                Reply(r[0], EspStLink.Success, r[1], r[2], r[3], r[4]);
                Reply(Enumerable.Range(address, n).Select(ReadTarget).ToArray());
                return;
            }
            case EspStLink.Write:
            {
                int n = r[1], address = (r[2] << 16) | (r[3] << 8) | r[4];
                LargestWrite = Math.Max(LargestWrite, n);
                if (Error is int e) { Fail(r[0], e); return; }
                for (int i = 0; i < n; i++) WriteTarget(address + i, r[5 + i]);
                Reply(r[0], EspStLink.Success, r[1], r[2], r[3], r[4]);
                return;
            }
        }
        // Unknown commands get no answer, as in pico-swimlib.
    }

    private byte ReadTarget(int address) => address switch
    {
        SwimTarget.SwimCsr => swimCsr,
        SwimTarget.DmCsr2 => dmCsr2,
        _ => Device.Read(address),
    };

    private void WriteTarget(int address, byte value)
    {
        if (address == SwimTarget.SwimCsr) swimCsr = (byte)((value & ~0x42) | (swimCsr & 0x42)); // HSIT and NO_ACCESS are read-only
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
