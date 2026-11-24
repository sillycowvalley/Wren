namespace Wren.Link;

/// <summary>What the first half of the check left behind: the bytes it replaced, for the second half to put back.</summary>
public sealed record EepromProbe(byte[] Original, bool PatternHeld);

/// <summary>
/// Finds out whether a Thing's STM8S003 has the S103's 640 bytes of EEPROM ($4000-$427F)
/// rather than the 128 its datasheet promises ($4000-$407F). The first half writes a pattern
/// to $4080-$427F, checking after every block that Wren's own 128 bytes are untouched (in
/// case the extra area wraps onto them), and reads it back. The second half, after the Thing
/// has been powered off and on, reads it again and puts the original bytes back. Touches no
/// flash, and nothing in $4000-$407F.
/// </summary>
public sealed class EepromSizeCheck
{
    public const int WrenEeprom = 0x4000, WrenSize = 128;
    public const int ExtraStart = 0x4080, ExtraSize = 512;

    private readonly SwimTarget target;
    private readonly Action<string> log;

    public EepromSizeCheck(SwimTarget target, Action<string> log)
    {
        this.target = target;
        this.log = log;
    }

    /// <summary>The test pattern: different in every byte and every block, so a wrap shows.</summary>
    public static byte[] Pattern()
    {
        var data = new byte[ExtraSize];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)((i * 37 + 0xA5) ^ (i >> 6));
        return data;
    }

    /// <summary>
    /// The first half: saves $4080-$427F, writes the pattern block by block, and reads it back.
    /// Stops at once, and puts Wren's 128 bytes back, if a write lands on them.
    /// </summary>
    public EepromProbe WritePattern()
    {
        byte[] wren = target.Read(WrenEeprom, WrenSize);
        byte[] original = target.Read(ExtraStart, ExtraSize);
        byte[] pattern = Pattern();
        log($"Saved $4080-$427F. Writing the test pattern there, one 64-byte block at a time.");
        for (int offset = 0; offset < ExtraSize; offset += SwimTarget.BlockSize)
        {
            int address = ExtraStart + offset;
            try
            {
                target.WriteBlocks(address, pattern[offset..(offset + SwimTarget.BlockSize)], TargetMemory.Eeprom);
            }
            catch (LinkException ex)
            {
                log($"Writing ${address:X4} failed: {ex.Message}");
                return new EepromProbe(original, false);
            }
            if (!target.Read(WrenEeprom, WrenSize).AsSpan().SequenceEqual(wren))
            {
                log($"Writing ${address:X4} changed Wren's own EEPROM at $4000: the extra area wraps onto it. Putting it back.");
                target.WriteBlocks(WrenEeprom, wren, TargetMemory.Eeprom);
                return new EepromProbe(original, false);
            }
        }
        bool held = target.Read(ExtraStart, ExtraSize).AsSpan().SequenceEqual(pattern);
        log(held
            ? "The pattern reads back. Now power the Thing off and on (unplug the programmer's USB and plug it in again), click Connect, then click the button again."
            : "The pattern does not read back: there is no extra EEPROM to use.");
        return new EepromProbe(original, held);
    }

    /// <summary>
    /// The second half, after a power cycle: reads $4080-$427F, says whether the pattern
    /// survived, and puts the original bytes back (only the blocks that differ). True when the
    /// Thing has 640 bytes of EEPROM that keep their contents.
    /// </summary>
    public bool CheckAndRestore(EepromProbe probe)
    {
        byte[] now = target.Read(ExtraStart, ExtraSize);
        bool kept = now.AsSpan().SequenceEqual(Pattern());
        log(kept
            ? "The pattern survived the power cycle: this Thing has 640 bytes of EEPROM ($4000-$427F)."
            : "The pattern did not survive the power cycle: count on 128 bytes only.");
        Restore(probe, now);
        return kept;
    }

    /// <summary>Puts the bytes WritePattern replaced back, writing only the blocks that differ.</summary>
    public void Restore(EepromProbe probe, byte[]? now = null)
    {
        now ??= target.Read(ExtraStart, ExtraSize);
        int restored = 0;
        for (int offset = 0; offset < ExtraSize; offset += SwimTarget.BlockSize)
        {
            var block = probe.Original[offset..(offset + SwimTarget.BlockSize)];
            if (now.AsSpan(offset, SwimTarget.BlockSize).SequenceEqual(block)) continue;
            try
            {
                target.WriteBlocks(ExtraStart + offset, block, TargetMemory.Eeprom);
                restored++;
            }
            catch (LinkException ex)
            {
                log($"Putting ${ExtraStart + offset:X4} back failed: {ex.Message}");
            }
        }
        log($"Put the original bytes back ({restored} block{(restored == 1 ? "" : "s")} written).");
    }
}
