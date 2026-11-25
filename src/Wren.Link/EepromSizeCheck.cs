namespace Wren.Link;

/// <summary>
/// Finds out whether a Thing's STM8S003 has the S103's 640 bytes of EEPROM ($4000-$427F)
/// rather than the 128 its datasheet promises ($4000-$407F), for Install kernel. EEPROM is
/// non-volatile, so a block that takes a write and reads it back is real EEPROM: no power
/// cycle is needed (one was tried on the bench, and the pattern survived it).
/// </summary>
public static class EepromSizeCheck
{
    public const int WrenEeprom = 0x4000, WrenSize = 128;
    public const int ExtraStart = 0x4080, ExtraSize = 512;


    /// <summary>
    /// Writes one block of the pattern at $4080, reads it back, checks Wren's 128 bytes are
    /// untouched (putting them back if not: the extra area could wrap onto them), and restores
    /// the block. True when the extra EEPROM is there. Touches no flash.
    /// </summary>
    public static bool HasExtraEeprom(SwimTarget target)
    {
        byte[] wren = target.Read(WrenEeprom, WrenSize);
        byte[] original = target.Read(ExtraStart, SwimTarget.BlockSize);
        byte[] pattern = Pattern()[..SwimTarget.BlockSize];
        try
        {
            target.WriteBlocks(ExtraStart, pattern, TargetMemory.Eeprom);
        }
        catch (LinkException)
        {
            return false;
        }
        bool held = target.Read(ExtraStart, SwimTarget.BlockSize).AsSpan().SequenceEqual(pattern);
        if (!target.Read(WrenEeprom, WrenSize).AsSpan().SequenceEqual(wren))
        {
            target.WriteBlocks(WrenEeprom, wren, TargetMemory.Eeprom);
            return false;
        }
        if (held) target.WriteBlocks(ExtraStart, original, TargetMemory.Eeprom);
        return held;
    }

    /// <summary>The test pattern: different in every byte and every block, so a wrap shows.</summary>
    public static byte[] Pattern()
    {
        var data = new byte[ExtraSize];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)((i * 37 + 0xA5) ^ (i >> 6));
        return data;
    }
}
