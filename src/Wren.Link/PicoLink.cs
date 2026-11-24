using System.IO.Ports;
using System.Text;

namespace Wren.Link;

/// <summary>
/// A Raspberry Pi Pico (RP2040 or RP2350) running Wren's sketch (<c>firmware/PicoLink</c>),
/// on a USB serial port. It drives SWIM at low speed from GP2 and NRST from GP3.
/// The serial protocol is Wren's own; each request is a command byte and its arguments, and
/// each reply is the command byte echoed, a status byte, then any data. After an error
/// status (sketch version 2 on) come three bytes: the frame of the request that failed
/// (from 1; 0 for the entry) and how long the line was last held low, in 100 ns units:
/// <list type="bullet">
/// <item><c>H</c>: hello. Data: "WREN" and the sketch's version.</item>
/// <item><c>R</c> n: NRST low (n = 1) or released (n = 0).</item>
/// <item><c>E</c>: the SWIM entry sequence. Data: the target's sync pulse in ns (2 bytes, high
/// first), then 1 if it was measured whole (and so calibrated the reading of the target's bits).</item>
/// <item><c>L</c>: SWIM communication reset (the line low over 64 SWIM clocks); the same data.</item>
/// <item><c>S</c>: SWIM's SRST command.</item>
/// <item><c>r</c> n a2 a1 a0: read n (1 to 64) bytes at address a. Data: the bytes.</item>
/// <item><c>w</c> n a2 a1 a0 data: write n (1 to 64) bytes at address a.</item>
/// <item><c>X</c>: let go of SWIM and NRST.</item>
/// <item><c>D</c>: the line check; see <see cref="CheckLine"/>.</item>
/// <item><c>T</c> a2 a1 a0: the read trace; see <see cref="TraceRead"/>.</item>
/// <item><c>F</c> c: the frame trace; see <see cref="TraceFrame"/>.</item>
/// </list>
/// </summary>
public sealed class PicoLink : ITargetLink
{
    public const byte Hello = (byte)'H', Reset = (byte)'R', Entry = (byte)'E', Srst = (byte)'S',
        Read = (byte)'r', Write = (byte)'w', Release = (byte)'X', LineCheck = (byte)'D', CommsReset = (byte)'L', ReadTrace = (byte)'T', FrameTrace = (byte)'F';

    /// <summary>The sketch version this Wren speaks to.</summary>
    public const int SketchVersion = 13;

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

    /// <summary>The sync pulse after the last communication reset, in microseconds.</summary>
    public double ResetSyncMicroseconds { get; private set; }

    /// <summary>Whether that pulse was measured whole, so the sketch calibrated to it.</summary>
    public bool Calibrated { get; private set; }

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
            throw new LinkException($"{portName} did not answer as a programmer: neither pico-swimlib's firmware (firmware/pico-swimlib) nor Wren's PicoLink sketch.", ex);
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

    public void AssertReset() => Request(new[] { Reset, (byte)1 }, 0);

    public void DeassertReset() => Request(new[] { Reset, (byte)0 }, 0);

    public void EnterSequence()
    {
        byte[] data = Request(new[] { Entry }, 3);
        SyncMicroseconds = ((data[0] << 8) | data[1]) / 1000.0;
    }

    /// <summary>
    /// SWIM communication reset: after NRST is released the target's clock is trimmed, and
    /// the sync pulse it answers with calibrates the sketch (UM0470 3.2 step 9).
    /// </summary>
    public void ResetSwim()
    {
        byte[] data = Request(new[] { CommsReset }, 3);
        ResetSyncMicroseconds = ((data[0] << 8) | data[1]) / 1000.0;
        Calibrated = data[2] != 0;
    }

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

    /// <summary>
    /// The line check: SWIM's level at rest, NRST held and let go, how fast SWIM rises, and
    /// every edge after an entry sequence. Returns lines for the log, each saying what it
    /// means. Leaves NRST let go.
    /// </summary>
    public IReadOnlyList<string> CheckLine()
    {
        if (Version < 3) return new[] { $"The Pico's sketch is version {Version}; the line check needs version 3. Upload the sketch again." };
        byte[] d = Request(new[] { LineCheck }, 7);
        bool swimIdle = d[0] != 0, resetHeld = d[1] != 0, resetFree = d[2] != 0;
        int rise = (d[3] << 8) | d[4];
        int level = d[5], count = d[6];
        byte[] e = ReadExactly(2 * count);
        var edges = Enumerable.Range(0, count).Select(i => ((e[2 * i] << 8) | e[2 * i + 1]) / 10.0).ToList();

        var lines = new List<string> { "--- Line check" };
        lines.Add(swimIdle
            ? "SWIM at rest: high (good)."
            : "SWIM at rest: LOW. Something holds it down: is GP2 on J2 pin 2 (not pin 1, GND)? Is the Thing powered?");
        lines.Add(rise == 0xFFFF
            ? "SWIM rise after letting go: it never rose. The pull-up is missing, or the line is shorted to GND."
            : rise > 1500
                ? $"SWIM rise after letting go: {rise} ns, too slow. Is the 1 kΩ pull-up from GP2 to 3V3(OUT) in place?"
                : rise > 300
                    ? $"SWIM rise after letting go: {rise} ns (works; with 1 kΩ expect nearer 100: check the resistor is brown-black-red, not brown-black-orange)."
                    : $"SWIM rise after letting go: {rise} ns (good).");
        lines.Add(resetHeld
            ? "NRST held low by GP3: it still reads HIGH. Is GP3 wired to J2 pin 3?"
            : "NRST held low by GP3: low (good).");
        lines.Add(resetFree
            ? "NRST let go: high (good)."
            : "NRST let go: it stays LOW. Is the Thing powered at 3.3 V?");
        string trace = count == 0
            ? $"no edges (the line stayed {(level != 0 ? "high" : "low")})"
            : $"starts {(level != 0 ? "high" : "low")}, edges at {string.Join(", ", edges.Select(t => $"{t:F1}"))} µs";
        lines.Add($"After the entry sequence: {trace}.");
        // Pulses are a fall then a rise. A line that starts low and rises within 2 µs is just
        // rising from the Pico letting go; one that stays low longer is the target's pulse,
        // begun before the release and counted from it.
        var pulses = new List<double>();
        bool high = level != 0;
        double? fell = null;
        if (level == 0 && edges.Count > 0 && edges[0] > 2) fell = 0;
        foreach (double t in edges)
        {
            high = !high;
            if (!high) fell = t;
            else if (fell is double f) { pulses.Add(t - f); fell = null; }
        }
        lines.Add(pulses.Count == 0
            ? "The Thing did not answer: no pulse came back. Check SWIM goes to J2 pin 2 and the Thing has 3.3 V on VCC."
            : pulses.Any(p => p is > 12 and < 20)
                ? $"The Thing answered with its sync pulse: {pulses.First(p => p is > 12 and < 20):F1} µs (16 is right)."
                : $"A pulse came back but not 16 µs: {string.Join(", ", pulses.Select(p => $"{p:F1}"))} µs.");
        return lines;
    }

    /// <summary>
    /// The read trace: a communication reset, a read of one byte at the address, and every
    /// edge of the target's reply, decoded here into bits for the log. Shows what the target
    /// actually sends when reads fail.
    /// </summary>
    public IReadOnlyList<string> TraceRead(int address = SwimTarget.SwimCsr)
    {
        byte[] d = Request(new[] { ReadTrace, (byte)(address >> 16), (byte)(address >> 8), (byte)address }, 7);
        byte status = d[0], frame = d[1];
        double sync = ((d[2] << 8) | d[3]) / 1000.0;
        bool clean = d[4] != 0;
        int level = d[5], count = d[6];
        byte[] e = ReadExactly(2 * count);
        var edges = Enumerable.Range(0, count).Select(i => ((e[2 * i] << 8) | e[2 * i + 1]) / 100.0).ToList();

        var lines = new List<string> { $"--- Read trace (one byte at ${address:X4})" };
        lines.Add(sync == 0
            ? "Communication reset: no sync pulse seen after it."
            : $"Communication reset: sync pulse {sync:F1} µs{(clean ? ", measured whole" : ", begun before the release")}.");
        if (status != StatusOk)
        {
            lines.Add($"The read's header failed at frame {frame}: {Describe(status)}");
            return lines;
        }
        lines.Add("The read's header was acknowledged. The reply, from the last ACK:");
        lines.Add(count == 0
            ? $"no edges in 80 µs (the line stayed {(level != 0 ? "high" : "low")})."
            : $"starts {(level != 0 ? "high" : "low")}, edges at {string.Join(", ", edges.Select(t => $"{t:F2}"))} µs.");
        // Low pulses: each is a bit if the line rose between them.
        var pulses = new List<(double Start, double Length)>();
        bool high = level != 0;
        double? fell = high ? null : 0.0;
        foreach (double t in edges)
        {
            high = !high;
            if (!high) fell = t;
            else if (fell is double f) { pulses.Add((f, t - f)); fell = null; }
        }
        if (pulses.Count > 0)
        {
            double unit = sync is > 12 and < 24 && clean ? sync / 128 : 0.125;
            lines.Add("Low pulses (start / length µs, and the bit a length means at a " +
                      $"{unit * 1000:F0} ns SWIM clock): " +
                      string.Join("  ", pulses.Select(p => $"{p.Start:F2}/{p.Length:F2}={(p.Length <= 8.5 * unit ? 1 : 0)}")));
            if (pulses.Count > 1)
                lines.Add("Bit periods (start to start, µs): " +
                          string.Join(" ", pulses.Zip(pulses.Skip(1), (a, b) => $"{b.Start - a.Start:F2}")));
        }
        return lines;
    }

    /// <summary>
    /// The frame trace: after a communication reset, the Pico sends one command frame (ROTF
    /// by default) from a timetable and records every edge on the line, its own pulses
    /// included, then 300 µs more. Each low pulse is listed with who most likely made it.
    /// </summary>
    public IReadOnlyList<string> TraceFrame(byte command = 1)
    {
        byte[] d = Request(new[] { FrameTrace, command }, 4);
        double sync = ((d[0] << 8) | d[1]) / 1000.0;
        int level = d[2], count = d[3];
        byte[] e = ReadExactly(2 * count);
        var edges = Enumerable.Range(0, count).Select(i => ((e[2 * i] << 8) | e[2 * i + 1]) / 100.0).ToList();

        var lines = new List<string> { $"--- Frame trace (command {Convert.ToString(command, 2).PadLeft(3, '0')})" };
        lines.Add(sync == 0 ? "Communication reset: no sync pulse seen." : $"Communication reset: sync pulse {sync:F1} µs.");
        lines.Add(count == 0
            ? "No edges at all: the Pico's own pulses did not show on the line."
            : $"Starts {(level != 0 ? "high" : "low")}, {count} edges (µs from the frame's start): {string.Join(", ", edges.Select(t => $"{t:F2}"))}.");
        // The Pico's five bits start every 2.75 µs; a low that starts near one of those is its own.
        int[] bits = { 0, (command >> 2) & 1, (command >> 1) & 1, command & 1, 0 };
        bits[4] = bits[1] ^ bits[2] ^ bits[3];
        var pulses = new List<string>();
        bool high = level != 0;
        double? fell = high ? null : 0.0;
        foreach (double t in edges)
        {
            high = !high;
            if (!high) fell = t;
            else if (fell is double f)
            {
                int nearest = (int)Math.Round(f / 2.75);
                bool host = nearest is >= 0 and < 5 && Math.Abs(f - nearest * 2.75) < 0.3;
                string who = host ? $"Pico bit {nearest} ({bits[nearest]}, expect {(bits[nearest] == 1 ? 0.22 : 2.0):F2})" : "Thing";
                pulses.Add($"{f:F2}+{t - f:F2} {who}");
                fell = null;
            }
        }
        if (fell is double open) pulses.Add($"{open:F2}+ (still low at the end)");
        lines.Add("Low pulses (start+length µs): " + string.Join("; ", pulses));
        return lines;
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
            string detail = "";
            if (Version >= 2)
            {
                byte[] d = ReadExactly(3);
                double low = ((d[1] << 8) | d[2]) / 10.0;
                detail = d[0] switch
                {
                    0xF1 => $" [the line stayed low {low:F1} µs after the Pico let go]",
                    0xF2 => " [no pulse came back]",
                    0xF3 => $" [a pulse of {low:F1} µs came back, not 16]",
                    0 => $" [line low {low:F1} µs]",
                    _ => $" [frame {d[0]}, line low {low:F1} µs]",
                };
            }
            throw new LinkException(Describe(head[1]) + detail);
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
        StatusNoAnswer => "SWIM: the target did not answer (is it powered at 3.3 V, and is SWIM wired to GP2 with the pull-up (two 1 kΩ side by side)?)",
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
