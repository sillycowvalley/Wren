using System.IO.Ports;
using System.Text;

namespace Wren.Link;

/// <summary>
/// A Raspberry Pi Pico (RP2040 or RP2350) running Wren's sketch (<c>firmware/PicoLink</c>),
/// on a USB serial port. It drives SWIM at low speed from GP2 and NRST from GP3.
/// The serial protocol is Wren's own; each request is a command byte and its arguments, and
/// each reply is the command byte echoed, a status byte, then any data:
/// <list type="bullet">
/// <item><c>H</c>: hello. Data: "WREN" and the sketch's version.</item>
/// <item><c>R</c> n: NRST low (n = 1) or released (n = 0).</item>
/// <item><c>E</c>: the SWIM entry sequence. Data: the target's sync pulse in ns (2 bytes, high first).</item>
/// <item><c>S</c>: SWIM's SRST command.</item>
/// <item><c>r</c> n a2 a1 a0: read n (1 to 64) bytes at address a. Data: the bytes.</item>
/// <item><c>w</c> n a2 a1 a0 data: write n (1 to 64) bytes at address a.</item>
/// <item><c>X</c>: let go of SWIM and NRST.</item>
/// </list>
/// </summary>
public sealed class PicoLink : ITargetLink
{
    public const byte Hello = (byte)'H', Reset = (byte)'R', Entry = (byte)'E', Srst = (byte)'S',
        Read = (byte)'r', Write = (byte)'w', Release = (byte)'X';

    public const byte StatusOk = 0, StatusNoAnswer = 1, StatusNack = 2, StatusParity = 3,
        StatusBadFrame = 4, StatusNoSync = 5, StatusBadCommand = 6;

    /// <summary>The most bytes one read or write carries: a flash block.</summary>
    public const int MaxChunk = 64;

    private readonly Stream stream;
    private readonly IDisposable? owner;

    /// <summary>Talks to a Pico over a stream (a serial port's, or a test's).</summary>
    public PicoLink(Stream stream, IDisposable? owner = null)
    {
        this.stream = stream;
        this.owner = owner;
    }

    /// <summary>The sketch's version, from the hello.</summary>
    public int Version { get; private set; }

    /// <summary>The target's sync pulse at the last entry, in microseconds (16 is right).</summary>
    public double SyncMicroseconds { get; private set; }

    public Action<int> Sleep { get; set; } = ms => Thread.Sleep(ms);

    public bool OffersHighSpeed => false;

    /// <summary>Opens a Pico on a serial port and checks that it runs Wren's sketch.</summary>
    public static PicoLink Open(string portName)
    {
        var port = new SerialPort(portName, 115200)
        {
            DtrEnable = true, // the Pico's USB serial sends nothing until the port is opened with DTR
            RtsEnable = true,
            ReadTimeout = 2000,
            WriteTimeout = 2000,
        };
        try
        {
            port.Open();
            port.DiscardInBuffer();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            port.Dispose();
            throw new LinkException($"Cannot open {portName}: {ex.Message} (is the Arduino IDE's Serial Monitor using it?)", ex);
        }
        var link = new PicoLink(port.BaseStream, port);
        try
        {
            link.SayHello();
            return link;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or LinkException)
        {
            link.Dispose();
            throw new LinkException($"{portName} did not answer as a Wren Pico programmer. Is the PicoLink sketch on it?", ex);
        }
    }

    /// <summary>Checks the sketch is there and reads its version.</summary>
    public void SayHello()
    {
        byte[] data = Request(new[] { Hello }, 5);
        if (Encoding.ASCII.GetString(data, 0, 4) != "WREN") throw new LinkException("The serial device is not a Wren Pico programmer");
        Version = data[4];
    }

    public void EnterSwimMode() => SayHello();

    public void LeaveSwimMode() => Request(new[] { Release }, 0);

    public void AssertReset() => Request(new[] { Reset, (byte)1 }, 0);

    public void DeassertReset() => Request(new[] { Reset, (byte)0 }, 0);

    public void EnterSequence()
    {
        byte[] data = Request(new[] { Entry }, 2);
        SyncMicroseconds = ((data[0] << 8) | data[1]) / 1000.0;
    }

    /// <summary>Nothing to do: the Pico stays at low speed, where no reset is needed.</summary>
    public void ResetSwim() { }

    public void GenerateReset() => Request(new[] { Srst }, 0);

    public void SetHighSpeed(bool high)
    {
        if (high) throw new LinkException("The Pico programmer speaks SWIM at low speed only");
    }

    public void WriteMemory(int address, ReadOnlySpan<byte> data)
    {
        for (int done = 0; done < data.Length; done += MaxChunk)
        {
            int n = Math.Min(MaxChunk, data.Length - done);
            int a = address + done;
            var request = new byte[5 + n];
            request[0] = Write;
            request[1] = (byte)n;
            request[2] = (byte)(a >> 16);
            request[3] = (byte)(a >> 8);
            request[4] = (byte)a;
            data.Slice(done, n).CopyTo(request.AsSpan(5));
            Request(request, 0);
        }
    }

    public byte[] ReadMemory(int address, int length)
    {
        var result = new byte[length];
        for (int done = 0; done < length; done += MaxChunk)
        {
            int n = Math.Min(MaxChunk, length - done);
            int a = address + done;
            byte[] data = Request(new[] { Read, (byte)n, (byte)(a >> 16), (byte)(a >> 8), (byte)a }, n);
            data.CopyTo(result, done);
        }
        return result;
    }

    /// <summary>Sends a request and reads its reply: the echoed command, the status, then dataLength bytes.</summary>
    private byte[] Request(byte[] request, int dataLength)
    {
        stream.Write(request, 0, request.Length);
        stream.Flush();
        byte[] head = ReadExactly(2);
        if (head[0] != request[0])
            throw new LinkException($"The Pico answered '{(char)head[0]}' to '{(char)request[0]}': out of step. Disconnect and connect again.");
        if (head[1] != StatusOk) throw new LinkException(Describe(head[1]));
        return ReadExactly(dataLength);
    }

    private byte[] ReadExactly(int length)
    {
        var buffer = new byte[length];
        int done = 0;
        while (done < length)
        {
            int got = stream.Read(buffer, done, length - done);
            if (got <= 0) throw new LinkException("The Pico stopped answering");
            done += got;
        }
        return buffer;
    }

    public static string Describe(byte status) => status switch
    {
        StatusNoAnswer => "SWIM: the target did not answer (is it powered at 3.3 V, and is SWIM wired to GP2 with the 1 kΩ pull-up?)",
        StatusNack => "SWIM: the target kept refusing a frame",
        StatusParity => "SWIM: a byte from the target arrived damaged (parity); check the wiring is short",
        StatusBadFrame => "SWIM: the target's reply was not a SWIM frame",
        StatusNoSync => "SWIM: the target did not answer the entry sequence (is it powered at 3.3 V, and are SWIM and NRST wired?)",
        StatusBadCommand => "The Pico did not understand a request: is its sketch the same version as Wren?",
        _ => $"The Pico reported error {status}",
    };

    public void Dispose()
    {
        if (owner != null) owner.Dispose();
        else stream.Dispose();
    }
}
