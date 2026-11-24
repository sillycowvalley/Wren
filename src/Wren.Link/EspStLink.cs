using System.IO.Ports;

namespace Wren.Link;

/// <summary>
/// A programmer that speaks the esp-stlink serial protocol, on a serial port: in particular
/// a Raspberry Pi Pico (or the Waveshare RP2040 Plus) running pico-swimlib's
/// <c>pico-espstlink</c> firmware (<c>firmware/pico-swimlib</c>), which times SWIM in
/// hardware with the RP2040's PIO. SWIM is on GP16, NRST on GP17.
/// The protocol (rumpeltux/esp-stlink, serial-protocol.md): a request is a command byte and
/// its arguments; a reply echoes the command, then 00 and any data, or FF and a 16-bit error.
/// <list type="bullet">
/// <item>00: SRST. Reply: 00 00.</item>
/// <item>01 n a2 a1 a0: read n bytes at a. Reply: 01 00 n a2 a1 a0 data.</item>
/// <item>02 n a2 a1 a0 data: write n bytes at a. Reply: 02 00 n a2 a1 a0.</item>
/// <item>FD s: NRST: 00 high, 01 low, FF let go. Reply: FD 00.</item>
/// <item>FE: the SWIM entry sequence. Reply: FE 00 and the sync pulse in 80 MHz cycles (2 bytes).</item>
/// <item>FF: version. Reply: FF 00 and the version (2 bytes).</item>
/// </list>
/// </summary>
public sealed class EspStLink : ITargetLink
{
    public const byte Srst = 0x00, Read = 0x01, Write = 0x02, Reset = 0xFD, Entry = 0xFE, Version = 0xFF;
    public const byte ResetHigh = 0x00, ResetLow = 0x01, ResetLetGo = 0xFF;
    public const byte Success = 0x00, Failure = 0xFF;
    public const int ErrorTimeout = 1, ErrorBadTarget = 2, ErrorParity = 3, ErrorNak = 4;

    /// <summary>The most bytes one read asks for (the protocol's count is one byte).</summary>
    public const int MaxRead = 255;

    /// <summary>The most bytes one write carries: a flash block.</summary>
    public const int MaxWrite = 64;

    private readonly Stream stream;
    private readonly IDisposable? owner;

    public EspStLink(Stream stream, IDisposable? owner = null)
    {
        this.stream = stream;
        this.owner = owner;
    }

    /// <summary>The firmware's version, as major.minor.</summary>
    public string FirmwareVersion { get; private set; } = "?";

    /// <summary>The target's sync pulse at the last entry, in microseconds (16 is right).</summary>
    public double SyncMicroseconds { get; private set; }

    public Action<int> Sleep { get; set; } = ms => Thread.Sleep(ms);

    public bool OffersHighSpeed => false;

    /// <summary>
    /// Asks the device on a just-opened serial port for its esp-stlink version. True, with
    /// the link, if it answers as an esp-stlink programmer; false (and the input discarded)
    /// if it answers otherwise or not at all.
    /// </summary>
    public static bool TryOpen(SerialPort port, out EspStLink? link)
    {
        link = null;
        var candidate = new EspStLink(port.BaseStream, port);
        int timeout = port.ReadTimeout;
        try
        {
            port.ReadTimeout = 1000;
            candidate.ReadVersion();
            link = candidate;
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or LinkException)
        {
            Thread.Sleep(100);
            port.DiscardInBuffer();
            return false;
        }
        finally
        {
            port.ReadTimeout = timeout;
        }
    }

    public void ReadVersion()
    {
        byte[] data = Request(new[] { Version }, 2);
        FirmwareVersion = $"{data[0]}.{data[1]}";
    }

    public void EnterSwimMode() => ReadVersion();

    public void LeaveSwimMode() => Request(new[] { Reset, ResetLetGo }, 0);

    public void AssertReset() => Request(new[] { Reset, ResetLow }, 0);

    /// <summary>Lets NRST go rather than driving it high: the target's own pull-up raises it.</summary>
    public void DeassertReset() => Request(new[] { Reset, ResetLetGo }, 0);

    /// <summary>
    /// The entry sequence, then an SRST, as pico-swimlib's own example does before its first
    /// write: harmless (SRST resets nothing until SWIM_CSR.SWIM_DM is set), and a first
    /// command to settle the link on.
    /// </summary>
    public void EnterSequence()
    {
        byte[] data = Request(new[] { Entry }, 2);
        SyncMicroseconds = ((data[0] << 8) | data[1]) / 80.0;
        Request(new[] { Srst }, 0);
    }

    /// <summary>Nothing to do: the protocol has no communication reset and stays at low speed.</summary>
    public void ResetSwim() { }

    public void GenerateReset() => Request(new[] { Srst }, 0);

    public void SetHighSpeed(bool high)
    {
        if (high) throw new LinkException("This programmer speaks SWIM at low speed only");
    }

    public void WriteMemory(int address, ReadOnlySpan<byte> data)
    {
        for (int done = 0; done < data.Length; done += MaxWrite)
        {
            int n = Math.Min(MaxWrite, data.Length - done);
            int a = address + done;
            var request = new byte[5 + n];
            request[0] = Write;
            request[1] = (byte)n;
            request[2] = (byte)(a >> 16);
            request[3] = (byte)(a >> 8);
            request[4] = (byte)a;
            data.Slice(done, n).CopyTo(request.AsSpan(5));
            byte[] echo = Request(request, 4);
            CheckEcho(echo, n, a);
        }
    }

    public byte[] ReadMemory(int address, int length)
    {
        var result = new byte[length];
        for (int done = 0; done < length; done += MaxRead)
        {
            int n = Math.Min(MaxRead, length - done);
            int a = address + done;
            byte[] reply = Request(new[] { Read, (byte)n, (byte)(a >> 16), (byte)(a >> 8), (byte)a }, 4 + n);
            CheckEcho(reply, n, a);
            Array.Copy(reply, 4, result, done, n);
        }
        return result;
    }

    private static void CheckEcho(byte[] reply, int count, int address)
    {
        int echoed = (reply[1] << 16) | (reply[2] << 8) | reply[3];
        if (reply[0] != (byte)count || echoed != (address & 0xFFFFFF))
            throw new LinkException($"The programmer's reply was for {reply[0]} bytes at ${echoed:X6}, not {count} at ${address:X6}");
    }

    /// <summary>Sends a request; reads the echoed command and the status, then dataLength bytes, or the error.</summary>
    private byte[] Request(byte[] request, int dataLength)
    {
        stream.Write(request, 0, request.Length);
        stream.Flush();
        byte[] head = ReadExactly(2);
        if (head[0] != request[0])
            throw new LinkException($"The programmer answered command {head[0]:X2} to {request[0]:X2}: out of step. Disconnect and connect again.");
        if (head[1] == Failure)
        {
            byte[] error = ReadExactly(2);
            throw new LinkException(Describe((error[0] << 8) | error[1]));
        }
        if (head[1] != Success) throw new LinkException($"The programmer sent status {head[1]:X2}");
        return ReadExactly(dataLength);
    }

    private byte[] ReadExactly(int length)
    {
        var buffer = new byte[length];
        int done = 0;
        while (done < length)
        {
            int got = stream.Read(buffer, done, length - done);
            if (got <= 0) throw new LinkException("The programmer stopped answering");
            done += got;
        }
        return buffer;
    }

    public static string Describe(int error) => error switch
    {
        ErrorTimeout => "SWIM: the target did not answer in time (is it powered at 3.3 V, and are SWIM on GP16 and NRST on GP17?)",
        ErrorBadTarget => "SWIM: the target's reply did not start like a SWIM frame",
        ErrorParity => "SWIM: a byte from the target arrived damaged (parity)",
        ErrorNak => "SWIM: the target kept refusing a frame (NACK)",
        _ => $"The programmer reported error {error}",
    };

    public void Dispose()
    {
        if (owner != null) owner.Dispose();
        else stream.Dispose();
    }
}
