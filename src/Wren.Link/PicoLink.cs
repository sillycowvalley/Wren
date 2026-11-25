using System.IO.Ports;
using System.Text;

namespace Wren.Link;

/// <summary>
/// A Raspberry Pi Pico, or a Pico-pinout RP2040 board such as the Waveshare RP2040 Plus,
/// running Wren's sketch (<c>firmware/PicoLink</c>) on a USB serial port. The sketch times
/// SWIM with the RP2040's PIO by pico-swimlib's method: SWIM on GP16, NRST on GP17, as
/// pico-swimlib wires it.
/// The serial protocol is Wren's own; each request is a command byte and its arguments, and
/// each reply is the command byte echoed, a status byte, then any data. After an error status
/// come three zero bytes.
/// <list type="bullet">
/// <item><c>H</c>: hello. Data: "WREN" and the sketch's version.</item>
/// <item><c>R</c> s: NRST driven high (s = 0), driven low (1) or let go (FF).</item>
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

    public const byte ResetHigh = 0x00, ResetLow = 0x01, ResetLetGo = 0xFF;

    /// <summary>The sketch version this Wren speaks to.</summary>
    public const int SketchVersion = 16;

    public const byte StatusOk = 0, StatusNoAnswer = 1, StatusNack = 2, StatusParity = 3,
        StatusBadFrame = 4, StatusNoSync = 5, StatusBadCommand = 6, StatusBusy = 7;

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
    public static PicoLink Open(string portName) => Open(SerialPorts.Open(portName), portName);

    /// <summary>Takes over an open serial port and checks that it runs Wren's sketch.</summary>
    public static PicoLink Open(SerialPort port, string portName)
    {
        var link = new PicoLink(port.BaseStream, port);
        try
        {
            link.SayHello();
            return link;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or LinkException)
        {
            link.Dispose();
            throw new LinkException($"{portName} did not answer as Wren's PicoLink sketch (firmware/PicoLink/PicoLink.uf2). " + ex.Message, ex);
        }
    }

    /// <summary>Checks the sketch is there and reads its version.</summary>
    public void SayHello()
    {
        byte[] data = Request(new[] { Hello }, 5);
        if (Encoding.ASCII.GetString(data, 0, 4) != "WREN") throw new LinkException("The serial device is not a Wren Pico programmer");
        Version = data[4];
        if (Version != SketchVersion)
            throw new LinkException($"The Pico runs sketch version {Version}; this Wren needs version {SketchVersion}. Upload firmware/PicoLink/PicoLink.ino again.");
    }

    public void EnterSwimMode() => SayHello();

    public void LeaveSwimMode() => Request(new[] { Release }, 0);

    public void AssertReset() => Request(new[] { Reset, ResetLow }, 0);

    /// <summary>
    /// Drives NRST high: an RP2040 pin let go can leave a Thing's NRST part way up, and the
    /// chip in reset. Safe because the Thing runs from the same 3.3 V.
    /// </summary>
    public void DeassertReset() => Request(new[] { Reset, ResetHigh }, 0);

    /// <summary>The entry sequence, then an SRST before the first write, as pico-swimlib does.</summary>
    public void EnterSequence()
    {
        byte[] data = Request(new[] { Entry }, 2);
        SyncMicroseconds = ((data[0] << 8) | data[1]) / 1000.0;
        Request(new[] { Srst }, 0);
    }

    /// <summary>Nothing to do: SWIM stays at low speed, and pico-swimlib's sequence needs no reset.</summary>
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
        if (head[1] != StatusOk)
        {
            ReadExactly(3);
            throw new LinkException(Describe(head[1]));
        }
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
        StatusNoAnswer => "SWIM: the target did not answer in time (is it powered at 3.3 V, and are SWIM on GP16 with the 470 Ω pull-up and NRST on GP17?)",
        StatusNack => "SWIM: the target kept refusing a frame (NACK)",
        StatusParity => "SWIM: a byte from the target kept arriving damaged (parity)",
        StatusBadFrame => "SWIM: the target's reply did not start like a SWIM frame",
        StatusNoSync => "SWIM: the target did not answer the entry sequence (is it powered at 3.3 V, and are SWIM and NRST wired?)",
        StatusBusy => "The Pico was still busy with the previous SWIM transfer",
        StatusBadCommand => "The Pico did not understand a request: is its sketch the same version as Wren?",
        _ => $"The Pico reported error {status}",
    };

    public void Dispose()
    {
        if (owner != null) owner.Dispose();
        else stream.Dispose();
    }
}
