namespace Wren.Emu;

/// <summary>
/// The flash and EEPROM programming controller. Writes to program flash need the PUKR keys
/// ($56 then $AE); writes to data EEPROM and option bytes need the DUKR keys ($AE then $56).
/// Each write completes at once and sets EOP. Writes while locked are ignored and set WR_PG_DIS.
/// Block programming (FLASH_CR2 PRG or FPRG, with FLASH_NCR2 the complement) collects 64
/// bytes and programs them as one block; the device checks that it runs from RAM.
/// </summary>
public sealed class FlashController : Peripheral
{
    public const int Address = 0x505A;

    // Offsets from 0x505A
    private const int Cr1 = 0, Cr2 = 1, Ncr2 = 2, Fpr = 3, Nfpr = 4, Iapsr = 5, Pukr = 8, Dukr = 10;

    private const byte WrPgDis = 0x01, Pul = 0x02, Eop = 0x04, Dul = 0x08, Hvoff = 0x40;

    private byte cr1, cr2, ncr2, fpr, nfpr, iapsr;
    private int pukrStage, dukrStage;

    public FlashController(InterruptController itc) : base("FLASH", Address, 11)
    {
        itc.Connect(Irq.Flash, () => (cr1 & 0x02) != 0 && (iapsr & (Eop | WrPgDis)) != 0);
        Reset();
    }

    public bool ProgramUnlocked => (iapsr & Pul) != 0;

    /// <summary>Standard or fast block programming is selected.</summary>
    public bool BlockMode => (cr2 & 0x11) != 0 && (ncr2 & 0x11) == (~cr2 & 0x11);

    /// <summary>Called when a block has been programmed: the mode bits clear, EOP is set.</summary>
    public void BlockDone()
    {
        cr2 &= unchecked((byte)~0x11);
        ncr2 |= 0x11;
        iapsr |= Eop | Hvoff;
    }

    /// <summary>A write to a protected area (the UBC): ignored, WR_PG_DIS set.</summary>
    public void Refuse() => iapsr |= WrPgDis;
    public bool DataUnlocked => (iapsr & Dul) != 0;

    /// <summary>Called by the device after a write into flash or EEPROM; returns whether it is allowed.</summary>
    public bool TryWrite(bool program)
    {
        if (program ? ProgramUnlocked : DataUnlocked)
        {
            iapsr |= Eop | Hvoff;
            return true;
        }
        iapsr |= WrPgDis;
        return false;
    }

    public override byte Read(int offset)
    {
        switch (offset)
        {
            case Cr1: return cr1;
            case Cr2: return cr2;
            case Ncr2: return ncr2;
            case Fpr: return fpr;
            case Nfpr: return nfpr;
            case Iapsr:
                byte v = iapsr;
                iapsr &= unchecked((byte)~(Eop | WrPgDis)); // cleared by reading
                return v;
            default: return 0;
        }
    }

    public override void Write(int offset, byte value)
    {
        switch (offset)
        {
            case Cr1: cr1 = (byte)(value & 0x0F); break;
            case Cr2: cr2 = value; break;
            case Ncr2: ncr2 = value; break;
            case Iapsr:
                // Writing 0 to PUL or DUL locks again.
                if ((value & Pul) == 0) iapsr &= unchecked((byte)~Pul);
                if ((value & Dul) == 0) iapsr &= unchecked((byte)~Dul);
                break;
            case Pukr:
                if (pukrStage == 0 && value == 0x56) pukrStage = 1;
                else if (pukrStage == 1 && value == 0xAE) { iapsr |= Pul; pukrStage = 0; }
                else pukrStage = 0;
                break;
            case Dukr:
                if (dukrStage == 0 && value == 0xAE) dukrStage = 1;
                else if (dukrStage == 1 && value == 0x56) { iapsr |= Dul; dukrStage = 0; }
                else dukrStage = 0;
                break;
        }
    }

    public override void Reset()
    {
        cr1 = 0;
        cr2 = 0;
        ncr2 = 0xFF;
        fpr = 0;
        nfpr = 0xFF;
        iapsr = Hvoff;
        pukrStage = 0;
        dukrStage = 0;
    }
}
