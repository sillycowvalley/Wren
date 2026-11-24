namespace Wren.Link;

/// <summary>
/// A programmer that speaks SWIM to the target: an ST-LINK V2 (<see cref="StLink"/>) or a
/// Raspberry Pi Pico running Wren's sketch (<see cref="PicoLink"/>). <see cref="SwimTarget"/>
/// does the rest through these few operations.
/// </summary>
public interface ITargetLink : IDisposable
{
    /// <summary>Waits between steps; tests set it to do nothing.</summary>
    Action<int> Sleep { get; set; }

    /// <summary>Whether this programmer can switch SWIM to high speed when the target offers it.</summary>
    bool OffersHighSpeed { get; }

    /// <summary>Gets the programmer ready for SWIM.</summary>
    void EnterSwimMode();

    /// <summary>Lets go of the target's lines.</summary>
    void LeaveSwimMode();

    /// <summary>Holds NRST low.</summary>
    void AssertReset();

    /// <summary>Releases NRST.</summary>
    void DeassertReset();

    /// <summary>The SWIM entry sequence and the target's synchronisation reply.</summary>
    void EnterSequence();

    /// <summary>Resets SWIM communication (before a change of speed).</summary>
    void ResetSwim();

    /// <summary>SWIM's SRST command: resets the target if SWIM_CSR allows it.</summary>
    void GenerateReset();

    void SetHighSpeed(bool high);

    /// <summary>Writes bytes to the target's memory (a block at most).</summary>
    void WriteMemory(int address, ReadOnlySpan<byte> data);

    byte[] ReadMemory(int address, int length);
}
