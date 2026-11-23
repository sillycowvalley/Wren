namespace Wren.Emu;

/// <summary>Memory as the CPU sees it: 24-bit addresses, one byte at a time.</summary>
public interface IBus
{
    byte Read(int address);
    void Write(int address, byte value);
}

/// <summary>Where the CPU asks for interrupts. Returns the IRQ number to take, or -1.</summary>
public interface IInterruptSource
{
    /// <summary>The IRQ with the highest priority whose software level is above currentLevel, or -1.</summary>
    int Next(int currentLevel);

    /// <summary>The software priority (1, 2 or 3) set for an IRQ in ITC_SPRx.</summary>
    int LevelOf(int irq);

    /// <summary>Called when the CPU takes an IRQ, so edge-triggered requests can be cleared.</summary>
    void Taken(int irq);
}

public enum CpuState { Running, Waiting, Halted }

/// <summary>
/// The STM8 core. Instruction set and flags follow ST's PM0044; the tests compare it with ucsim.
/// Cycle counts are approximate: speed is not a goal, only the order of events.
/// </summary>
public sealed class Cpu
{
    public const byte FlagV = 0x80, FlagI1 = 0x20, FlagH = 0x10, FlagI0 = 0x08, FlagN = 0x04, FlagZ = 0x02, FlagC = 0x01;

    public const int ResetVector = 0x8000;
    public const int TrapVector = 0x8004;
    public const int FirstIrqVector = 0x8008;

    private readonly IBus bus;

    public Cpu(IBus bus, int initialStackPointer)
    {
        this.bus = bus;
        InitialStackPointer = initialStackPointer;
        Reset();
    }

    public byte A { get; set; }
    public ushort X { get; set; }
    public ushort Y { get; set; }
    public ushort SP { get; set; }
    public int PC { get; set; }
    public byte CC { get; set; }

    public int InitialStackPointer { get; }
    public IInterruptSource? Interrupts { get; set; }
    public CpuState State { get; private set; }
    public long Cycles { get; private set; }

    /// <summary>Set when a BREAK instruction runs. The caller clears it.</summary>
    public bool BreakHit { get; set; }

    /// <summary>Set when an opcode that does not exist is fetched. The caller clears it.</summary>
    public string? Fault { get; set; }

    public void Reset()
    {
        A = 0;
        X = 0;
        Y = 0;
        SP = (ushort)InitialStackPointer;
        CC = FlagI1 | FlagI0;
        PC = ResetVector; // The reset vector holds INT addr, which jumps to the program.
        State = CpuState.Running;
        BreakHit = false;
        Fault = null;
    }

    /// <summary>The software priority level held in CC: 0 (main) to 3 (interrupts disabled).</summary>
    public int Level => ((CC & FlagI1) != 0, (CC & FlagI0) != 0) switch
    {
        (true, false) => 0,
        (false, true) => 1,
        (false, false) => 2,
        _ => 3,
    };

    private void SetLevel(int level)
    {
        byte bits = level switch { 0 => FlagI1, 1 => FlagI0, 2 => 0, _ => FlagI1 | FlagI0 };
        CC = (byte)((CC & ~(FlagI1 | FlagI0)) | bits);
    }

    /// <summary>Takes a pending interrupt if one is allowed, or runs one instruction. Returns the cycles used.</summary>
    public int Step()
    {
        long before = Cycles;
        int irq = Interrupts?.Next(Level) ?? -1;
        if (irq >= 0)
        {
            EnterInterrupt(FirstIrqVector + 4 * irq, Interrupts!.LevelOf(irq));
            Interrupts.Taken(irq);
            State = CpuState.Running;
            Cycles += 9;
        }
        else if (State != CpuState.Running)
        {
            Cycles += 1;
        }
        else
        {
            Execute();
        }
        return (int)(Cycles - before);
    }

    private void EnterInterrupt(int vector, int level)
    {
        Push((byte)PC);
        Push((byte)(PC >> 8));
        Push((byte)(PC >> 16));
        Push((byte)Y);
        Push((byte)(Y >> 8));
        Push((byte)X);
        Push((byte)(X >> 8));
        Push(A);
        Push(CC);
        SetLevel(level);
        PC = vector;
    }

    // ---- Memory helpers ----

    private byte Read(int address) => bus.Read(address & 0xFFFFFF);
    private void Write(int address, byte value) => bus.Write(address & 0xFFFFFF, value);
    private ushort Read16(int address) => (ushort)((Read(address) << 8) | Read(address + 1));

    private void Write16(int address, ushort value)
    {
        Write(address, (byte)(value >> 8));
        Write(address + 1, (byte)value);
    }

    private byte Fetch()
    {
        byte b = Read(PC);
        PC = (PC + 1) & 0xFFFFFF;
        Cycles++;
        return b;
    }

    private ushort Fetch16() => (ushort)((Fetch() << 8) | Fetch());
    private int Fetch24() => (Fetch() << 16) | (Fetch() << 8) | Fetch();

    private void Push(byte value)
    {
        Write(SP, value);
        SP--;
    }

    private byte Pop()
    {
        SP++;
        return Read(SP);
    }

    private void JumpNear(int address) => PC = (PC & 0xFF0000) | (address & 0xFFFF);

    private void CallNear(int address)
    {
        Push((byte)PC);
        Push((byte)(PC >> 8));
        JumpNear(address);
        Cycles += 2;
    }

    private void Branch(bool condition)
    {
        sbyte offset = (sbyte)Fetch();
        if (condition)
        {
            PC = (PC + offset) & 0xFFFFFF;
            Cycles++;
        }
    }

    // ---- Flags ----

    private bool Flag(byte f) => (CC & f) != 0;

    private void SetFlag(byte f, bool on) => CC = (byte)(on ? CC | f : CC & ~f);

    private void SetNZ(byte value)
    {
        SetFlag(FlagN, (value & 0x80) != 0);
        SetFlag(FlagZ, value == 0);
    }

    private void SetNZ16(ushort value)
    {
        SetFlag(FlagN, (value & 0x8000) != 0);
        SetFlag(FlagZ, value == 0);
    }

    private int Carry => CC & FlagC;

    // ---- Execution ----

    private void Execute()
    {
        int prefix = 0;
        int op = Fetch();
        if (op is 0x72 or 0x90 or 0x91 or 0x92)
        {
            prefix = op;
            op = Fetch();
        }
        int hi = op >> 4;
        int lo = op & 0xF;

        switch (hi)
        {
            case 0x0:
                if (prefix == 0x72) { BitTestJump(lo); return; }
                Group0(prefix, lo);
                return;
            case 0x1:
                if (prefix == 0x72) { BitSetReset(lo); return; }
                if (prefix == 0x90) { BitComplementCopy(lo); return; }
                if (prefix == 0) { Group1(lo); return; }
                break;
            case 0x2:
                RelativeJump(prefix, lo);
                return;
            case 0x3:
            case 0x4:
            case 0x5:
            case 0x6:
            case 0x7:
                if (ReadModifyWriteGroup(prefix, hi, lo)) return;
                break;
            case 0x8:
                if (Group8(prefix, lo)) return;
                break;
            case 0x9:
                if (Group9(prefix, lo)) return;
                break;
            default:
                if (AluGroup(prefix, hi, lo)) return;
                break;
        }
        IllegalOpcode(prefix, op);
    }

    private void IllegalOpcode(int prefix, int op)
    {
        Fault = prefix == 0 ? $"Illegal opcode {op:X2}" : $"Illegal opcode {prefix:X2} {op:X2}";
        State = CpuState.Halted;
    }

    // 0x: (off,SP) read-modify-write, RRWA, RLWA
    private void Group0(int prefix, int lo)
    {
        if (lo is 1 or 2)
        {
            if (prefix is not (0 or 0x90)) { IllegalOpcode(prefix, lo); return; }
            bool y = prefix == 0x90;
            ushort w = y ? Y : X;
            byte xh = (byte)(w >> 8), xl = (byte)w;
            if (lo == 1)
            {
                // RRWA: A -> XH, XH -> XL, XL -> A
                byte a = A;
                A = xl;
                w = (ushort)((a << 8) | xh);
            }
            else
            {
                // RLWA: A -> XL, XL -> XH, XH -> A
                byte a = A;
                A = xh;
                w = (ushort)((xl << 8) | a);
            }
            if (y) Y = w; else X = w;
            SetNZ16(w);
            return;
        }
        if (prefix != 0 || lo is 5 or 0xB) { IllegalOpcode(prefix, lo); return; }
        int ea = (SP + Fetch()) & 0xFFFF;
        ModifyMemory(ea, lo);
    }

    // 1x without prefix: A,(off,SP) arithmetic and word operations
    private void Group1(int lo)
    {
        switch (lo)
        {
            case 0x3: { int ea = (SP + Fetch()) & 0xFFFF; Compare16(X, Read16(ea)); return; }
            case 0x6: { int ea = (SP + Fetch()) & 0xFFFF; Y = LoadWord(Read16(ea)); return; }
            case 0x7: { int ea = (SP + Fetch()) & 0xFFFF; Write16(ea, Y); SetNZ16(Y); return; }
            case 0xC: X = Add16(X, Fetch16()); return;
            case 0xD: X = Sub16(X, Fetch16()); return;
            case 0xE: { int ea = (SP + Fetch()) & 0xFFFF; X = LoadWord(Read16(ea)); return; }
            case 0xF: { int ea = (SP + Fetch()) & 0xFFFF; Write16(ea, X); SetNZ16(X); return; }
            default:
            {
                int ea = (SP + Fetch()) & 0xFFFF;
                Arithmetic(lo, Read(ea));
                return;
            }
        }
    }

    private ushort LoadWord(ushort value)
    {
        SetNZ16(value);
        return value;
    }

    // BTJT / BTJF longmem,#n,rel
    private void BitTestJump(int lo)
    {
        int ea = Fetch16();
        int bit = lo >> 1;
        bool set = (Read(ea) & (1 << bit)) != 0;
        SetFlag(FlagC, set);
        bool jumpIfSet = (lo & 1) == 0;
        Branch(set == jumpIfSet);
    }

    // BSET / BRES longmem,#n
    private void BitSetReset(int lo)
    {
        int ea = Fetch16();
        int mask = 1 << (lo >> 1);
        byte v = Read(ea);
        Write(ea, (byte)((lo & 1) == 0 ? v | mask : v & ~mask));
    }

    // BCPL / BCCM longmem,#n
    private void BitComplementCopy(int lo)
    {
        int ea = Fetch16();
        int mask = 1 << (lo >> 1);
        byte v = Read(ea);
        if ((lo & 1) == 0) Write(ea, (byte)(v ^ mask));
        else Write(ea, (byte)(Flag(FlagC) ? v | mask : v & ~mask));
    }

    private void RelativeJump(int prefix, int lo)
    {
        if (prefix == 0x90)
        {
            // JRNH, JRH, JRNM, JRM, JRIL, JRIH. The interrupt-line tests read as low.
            bool? condition = lo switch
            {
                0x8 => !Flag(FlagH),
                0x9 => Flag(FlagH),
                0xC => !InterruptMaskSet,
                0xD => InterruptMaskSet,
                0xE => true,  // JRIL: interrupt line low
                0xF => false, // JRIH
                _ => null,
            };
            if (condition is bool c) { Branch(c); return; }
            IllegalOpcode(prefix, 0x20 | lo);
            return;
        }
        if (prefix != 0) { IllegalOpcode(prefix, 0x20 | lo); return; }

        bool cc = lo switch
        {
            0x0 => true,
            0x1 => false,
            0x2 => !Flag(FlagC) && !Flag(FlagZ),
            0x3 => Flag(FlagC) || Flag(FlagZ),
            0x4 => !Flag(FlagC),
            0x5 => Flag(FlagC),
            0x6 => !Flag(FlagZ),
            0x7 => Flag(FlagZ),
            0x8 => !Flag(FlagV),
            0x9 => Flag(FlagV),
            0xA => !Flag(FlagN),
            0xB => Flag(FlagN),
            0xC => !Flag(FlagZ) && (Flag(FlagN) == Flag(FlagV)),
            0xD => Flag(FlagZ) || (Flag(FlagN) != Flag(FlagV)),
            0xE => Flag(FlagN) == Flag(FlagV),
            _ => Flag(FlagN) != Flag(FlagV),
        };
        Branch(cc);
    }

    /// <summary>JRM / JRNM test the interrupt mask: both I1 and I0 set.</summary>
    private bool InterruptMaskSet => (CC & (FlagI1 | FlagI0)) == (FlagI1 | FlagI0);

    // 3x-7x: read-modify-write on memory and registers, plus the odd ones out
    private bool ReadModifyWriteGroup(int prefix, int hi, int lo)
    {
        // Special opcodes that share these rows.
        if (prefix == 0)
        {
            switch (hi, lo)
            {
                case (0x3, 0x1): { int ea = Fetch16(); byte v = Read(ea); Write(ea, A); A = v; return true; }        // EXG A,long
                case (0x3, 0x2): { int ea = Fetch16(); Write(ea, Pop()); return true; }                              // POP long
                case (0x3, 0x5): { byte v = Fetch(); int ea = Fetch16(); Write(ea, v); return true; }                // MOV long,#
                case (0x3, 0xB): { int ea = Fetch16(); Push(Read(ea)); return true; }                                // PUSH long
                case (0x4, 0x1): { byte v = (byte)X; X = (ushort)((X & 0xFF00) | A); A = v; return true; }          // EXG A,XL
                case (0x4, 0x2): Multiply(false); return true;                                                        // MUL X,A
                case (0x4, 0x5): { byte src = Fetch(); byte dst = Fetch(); Write(dst, Read(src)); return true; }     // MOV short,short
                case (0x4, 0xB): Push(Fetch()); return true;                                                          // PUSH #
                case (0x5, 0x1): { ushort t = X; X = Y; Y = t; return true; }                                         // EXGW X,Y
                case (0x5, 0x2): SP = (ushort)(SP - Fetch()); return true;                                            // SUB SP,#
                case (0x5, 0x5): { int src = Fetch16(); int dst = Fetch16(); Write(dst, Read(src)); return true; }   // MOV long,long
                case (0x5, 0xB): SP = (ushort)(SP + Fetch()); return true;                                            // ADDW SP,#
                case (0x6, 0x1): { byte v = (byte)Y; Y = (ushort)((Y & 0xFF00) | A); A = v; return true; }          // EXG A,YL
                case (0x6, 0x2): Divide(false); return true;                                                          // DIV X,A
                case (0x6, 0x5): DivideWord(); return true;                                                           // DIVW X,Y
                case (0x6, 0xB): { int ea = (SP + Fetch()) & 0xFFFF; Write(ea, A); SetNZ(A); return true; }         // LD (off,SP),A
                case (0x7, 0xB): { int ea = (SP + Fetch()) & 0xFFFF; A = Read(ea); SetNZ(A); return true; }         // LD A,(off,SP)
            }
        }
        if (prefix == 0x90)
        {
            switch (hi, lo)
            {
                case (0x4, 0x2): Multiply(true); return true;   // MUL Y,A
                case (0x6, 0x2): Divide(true); return true;     // DIV Y,A
            }
        }
        if (lo is 1 or 2 or 5 or 0xB) return false;

        // Registers: A, X and Y.
        if (hi == 0x4 && prefix == 0) { A = ModifyByte(A, lo); return true; }
        if (hi == 0x5 && prefix == 0) { X = ModifyWord(X, lo); return true; }
        if (hi == 0x5 && prefix == 0x90) { Y = ModifyWord(Y, lo); return true; }

        int? target = (prefix, hi) switch
        {
            (0, 0x3) => Fetch(),
            (0x72, 0x5) => Fetch16(),
            (0, 0x7) => X,
            (0, 0x6) => Fetch() + X,
            (0x72, 0x4) => Fetch16() + X,
            (0x90, 0x7) => Y,
            (0x90, 0x6) => Fetch() + Y,
            (0x90, 0x4) => Fetch16() + Y,
            (0x92, 0x3) => Read16(Fetch()),
            (0x72, 0x3) => Read16(Fetch16()),
            (0x92, 0x6) => Read16(Fetch()) + X,
            (0x72, 0x6) => Read16(Fetch16()) + X,
            (0x91, 0x6) => Read16(Fetch()) + Y,
            _ => null,
        };
        if (target is not int address) return false;
        ModifyMemory(address, lo);
        return true;
    }

    private void ModifyMemory(int address, int lo)
    {
        byte v = Read(address);
        byte r = ModifyByte(v, lo);
        if (lo != 0xD) Write(address, r); // TNZ only reads
        Cycles++;
    }

    private byte ModifyByte(byte v, int lo)
    {
        byte r;
        switch (lo)
        {
            case 0x0: // NEG
                r = (byte)(-v);
                SetFlag(FlagV, v == 0x80);
                SetFlag(FlagC, r != 0);
                break;
            case 0x3: // CPL
                r = (byte)~v;
                SetFlag(FlagC, true);
                break;
            case 0x4: // SRL
                SetFlag(FlagC, (v & 1) != 0);
                r = (byte)(v >> 1);
                break;
            case 0x6: // RRC
                r = (byte)((v >> 1) | (Carry << 7));
                SetFlag(FlagC, (v & 1) != 0);
                break;
            case 0x7: // SRA
                r = (byte)((v >> 1) | (v & 0x80));
                SetFlag(FlagC, (v & 1) != 0);
                break;
            case 0x8: // SLL
                r = (byte)(v << 1);
                SetFlag(FlagC, (v & 0x80) != 0);
                break;
            case 0x9: // RLC
                r = (byte)((v << 1) | Carry);
                SetFlag(FlagC, (v & 0x80) != 0);
                break;
            case 0xA: // DEC
                r = (byte)(v - 1);
                SetFlag(FlagV, v == 0x80);
                break;
            case 0xC: // INC
                r = (byte)(v + 1);
                SetFlag(FlagV, v == 0x7F);
                break;
            case 0xD: // TNZ
                r = v;
                break;
            case 0xE: // SWAP
                r = (byte)((v << 4) | (v >> 4));
                break;
            case 0xF: // CLR
                r = 0;
                break;
            default:
                throw new InvalidOperationException();
        }
        SetNZ(r);
        return r;
    }

    private ushort ModifyWord(ushort v, int lo)
    {
        ushort r;
        switch (lo)
        {
            case 0x0: // NEGW
                r = (ushort)(-v);
                SetFlag(FlagV, v == 0x8000);
                SetFlag(FlagC, r != 0);
                break;
            case 0x3: // CPLW
                r = (ushort)~v;
                SetFlag(FlagC, true);
                break;
            case 0x4: // SRLW
                SetFlag(FlagC, (v & 1) != 0);
                r = (ushort)(v >> 1);
                break;
            case 0x6: // RRCW
                r = (ushort)((v >> 1) | (Carry << 15));
                SetFlag(FlagC, (v & 1) != 0);
                break;
            case 0x7: // SRAW
                r = (ushort)((v >> 1) | (v & 0x8000));
                SetFlag(FlagC, (v & 1) != 0);
                break;
            case 0x8: // SLLW
                r = (ushort)(v << 1);
                SetFlag(FlagC, (v & 0x8000) != 0);
                break;
            case 0x9: // RLCW
                r = (ushort)((v << 1) | Carry);
                SetFlag(FlagC, (v & 0x8000) != 0);
                break;
            case 0xA: // DECW
                r = (ushort)(v - 1);
                SetFlag(FlagV, v == 0x8000);
                break;
            case 0xC: // INCW
                r = (ushort)(v + 1);
                SetFlag(FlagV, v == 0x7FFF);
                break;
            case 0xD: // TNZW
                r = v;
                break;
            case 0xE: // SWAPW
                r = (ushort)((v << 8) | (v >> 8));
                break;
            case 0xF: // CLRW
                r = 0;
                break;
            default:
                throw new InvalidOperationException();
        }
        SetNZ16(r);
        return r;
    }

    private void Multiply(bool y)
    {
        ushort w = y ? Y : X;
        ushort r = (ushort)((w & 0xFF) * A);
        if (y) Y = r; else X = r;
        SetFlag(FlagH, false);
        SetFlag(FlagC, false);
        Cycles += 3;
    }

    private void Divide(bool y)
    {
        Cycles += 10;
        if (A == 0)
        {
            SetFlag(FlagC, true);
            return;
        }
        ushort w = y ? Y : X;
        ushort q = (ushort)(w / A);
        byte rem = (byte)(w % A);
        if (y) Y = q; else X = q;
        A = rem;
        SetFlag(FlagV, false);
        SetFlag(FlagH, false);
        SetFlag(FlagN, false);
        SetFlag(FlagZ, q == 0);
        SetFlag(FlagC, false);
    }

    private void DivideWord()
    {
        Cycles += 10;
        if (Y == 0)
        {
            SetFlag(FlagC, true);
            return;
        }
        ushort q = (ushort)(X / Y);
        ushort rem = (ushort)(X % Y);
        X = q;
        Y = rem;
        SetFlag(FlagV, false);
        SetFlag(FlagH, false);
        SetFlag(FlagN, false);
        SetFlag(FlagZ, q == 0);
        SetFlag(FlagC, false);
    }

    // 8x: returns, calls, stack, interrupts
    private bool Group8(int prefix, int lo)
    {
        if (prefix == 0x90)
        {
            switch (lo)
            {
                case 0x5: Y = (ushort)((Pop() << 8) | Pop()); return true;   // POPW Y
                case 0x9: Push((byte)Y); Push((byte)(Y >> 8)); return true;   // PUSHW Y
            }
            return false;
        }
        if (prefix == 0x92 && lo == 0xD)
        {
            // CALLF [ptr.e]
            int ptr = Fetch16();
            int target = (Read(ptr) << 16) | (Read(ptr + 1) << 8) | Read(ptr + 2);
            CallFar(target);
            return true;
        }
        if (prefix == 0x72 && lo == 0xF)
        {
            State = CpuState.Waiting; // WFE
            return true;
        }
        if (prefix != 0) return false;

        switch (lo)
        {
            case 0x0: // IRET
                CC = Pop();
                A = Pop();
                X = (ushort)((Pop() << 8) | Pop());
                Y = (ushort)((Pop() << 8) | Pop());
                PC = (Pop() << 16) | (Pop() << 8) | Pop();
                Cycles += 10;
                return true;
            case 0x1: // RET
                JumpNear((Pop() << 8) | Pop());
                Cycles += 3;
                return true;
            case 0x2: // INT ext
                PC = Fetch24();
                return true;
            case 0x3: // TRAP
                EnterInterrupt(TrapVector, 3);
                Cycles += 9;
                return true;
            case 0x4: A = Pop(); return true;
            case 0x5: X = (ushort)((Pop() << 8) | Pop()); return true;
            case 0x6: CC = Pop(); return true;
            case 0x7: // RETF
                PC = (Pop() << 16) | (Pop() << 8) | Pop();
                Cycles += 4;
                return true;
            case 0x8: Push(A); return true;
            case 0x9: Push((byte)X); Push((byte)(X >> 8)); return true;
            case 0xA: Push(CC); return true;
            case 0xB: BreakHit = true; return true;
            case 0xC: CC ^= FlagC; return true; // CCF
            case 0xD: CallFar(Fetch24()); return true;
            case 0xE: SetLevel(0); State = CpuState.Halted; return true;   // HALT
            case 0xF: SetLevel(0); State = CpuState.Waiting; return true;  // WFI
        }
        return false;
    }

    private void CallFar(int target)
    {
        Push((byte)PC);
        Push((byte)(PC >> 8));
        Push((byte)(PC >> 16));
        PC = target & 0xFFFFFF;
        Cycles += 3;
    }

    // 9x: register transfers and flag operations
    private bool Group9(int prefix, int lo)
    {
        bool y = prefix == 0x90;
        if (prefix != 0 && !y) return false;
        switch (lo)
        {
            case 0x3: if (y) Y = X; else X = Y; return true;            // LDW X,Y / LDW Y,X
            case 0x4: SP = y ? Y : X; return true;                       // LDW SP,X
            case 0x5: SetHigh(y, A); return true;                        // LD XH,A
            case 0x6: if (y) Y = SP; else X = SP; return true;           // LDW X,SP
            case 0x7: SetLow(y, A); return true;                         // LD XL,A
            case 0xE: A = (byte)((y ? Y : X) >> 8); return true;         // LD A,XH
            case 0xF: A = (byte)(y ? Y : X); return true;                // LD A,XL
        }
        if (y) return false;
        switch (lo)
        {
            case 0x8: SetFlag(FlagC, false); return true;  // RCF
            case 0x9: SetFlag(FlagC, true); return true;   // SCF
            case 0xA: SetLevel(0); return true;            // RIM
            case 0xB: SetLevel(3); return true;            // SIM
            case 0xC: SetFlag(FlagV, false); return true;  // RVF
            case 0xD: return true;                         // NOP
        }
        return false;
    }

    private void SetHigh(bool y, byte v)
    {
        if (y) Y = (ushort)((Y & 0x00FF) | (v << 8));
        else X = (ushort)((X & 0x00FF) | (v << 8));
    }

    private void SetLow(bool y, byte v)
    {
        if (y) Y = (ushort)((Y & 0xFF00) | v);
        else X = (ushort)((X & 0xFF00) | v);
    }

    // Ax-Fx: A,src arithmetic, loads, stores, jumps, calls, word loads and compares
    private bool AluGroup(int prefix, int hi, int lo)
    {
        // Word arithmetic with the 72 prefix.
        if (prefix == 0x72 && hi is 0xA or 0xB or 0xF && lo is 0x0 or 0x2 or 0x9 or 0xB)
            return WordArithmetic(hi, lo);

        // LDF, JPF, CALLR: far and relative forms in the A and B rows.
        if (hi == 0xA && lo is 0x7 or 0xF) return LoadFarIndexed(prefix, lo == 0xF);
        if (hi == 0xB && lo is 0xC or 0xD) return LoadFarDirect(prefix, lo == 0xC);
        if (hi == 0xA && lo == 0xC)
        {
            if (prefix == 0) { PC = Fetch24(); return true; }
            if (prefix == 0x92) { int ptr = Fetch16(); PC = (Read(ptr) << 16) | (Read(ptr + 1) << 8) | Read(ptr + 2); return true; }
            return false;
        }
        if (hi == 0xA && lo == 0xD)
        {
            if (prefix != 0) return false;
            sbyte offset = (sbyte)Fetch();
            CallNear((PC + offset) & 0xFFFF);
            return true;
        }

        bool yPrefix = prefix is 0x90 or 0x91;

        if (hi == 0xA)
        {
            // Immediate.
            if (prefix is not (0 or 0x90)) return false;
            switch (lo)
            {
                case 0x3: Compare16(yPrefix ? Y : X, Fetch16()); return true;
                case 0x6: if (prefix != 0) return false; A = Fetch(); SetNZ(A); return true;
                case 0xE:
                {
                    ushort v = LoadWord(Fetch16());
                    if (yPrefix) Y = v; else X = v;
                    return true;
                }
                default:
                    if (prefix != 0 || lo is 0x7) return false;
                    Arithmetic(lo, Fetch());
                    return true;
            }
        }

        int? ea = EffectiveAddress(prefix, hi);
        if (ea is not int address) return false;

        bool indexed = hi is 0xD or 0xE or 0xF;
        // The index register is Y with the 90 and 91 prefixes. For non-indexed modes the
        // same prefixes select Y as the word register.
        bool indexIsY = yPrefix;
        bool baseIsY = yPrefix;

        switch (lo)
        {
            case 0x3: // CPW
            {
                bool useY = indexed ? !indexIsY : baseIsY;
                Compare16(useY ? Y : X, Read16(address));
                return true;
            }
            case 0x6: // LD A,mem
                if (!ByteModeAllowed(prefix, hi)) return false;
                A = Read(address);
                SetNZ(A);
                return true;
            case 0x7: // LD mem,A
                if (!ByteModeAllowed(prefix, hi)) return false;
                Write(address, A);
                SetNZ(A);
                return true;
            case 0xC: // JP
                if (hi == 0xB || !ByteModeAllowed(prefix, hi)) return false;
                JumpNear(address);
                return true;
            case 0xD: // CALL
                if (hi == 0xB || !ByteModeAllowed(prefix, hi)) return false;
                CallNear(address);
                return true;
            case 0xE: // LDW reg,mem
            {
                bool useY = indexed ? indexIsY : baseIsY;
                ushort v = LoadWord(Read16(address));
                if (useY) Y = v; else X = v;
                return true;
            }
            case 0xF: // LDW mem,reg
            {
                bool useY = indexed ? !indexIsY : baseIsY;
                ushort v = useY ? Y : X;
                Write16(address, v);
                SetNZ16(v);
                return true;
            }
            default:
                if (!ByteModeAllowed(prefix, hi)) return false;
                Arithmetic(lo, Read(address));
                return true;
        }
    }

    /// <summary>
    /// Byte operations exist for every memory mode except the Y-register forms of [ptr] and
    /// (short)/(long) with prefix 90 or 91, which only the word instructions use.
    /// </summary>
    private static bool ByteModeAllowed(int prefix, int hi) => (prefix, hi) switch
    {
        (0x90, 0xB) or (0x90, 0xC) or (0x91, 0xC) => false,
        _ => true,
    };

    private int? EffectiveAddress(int prefix, int hi) => (prefix, hi) switch
    {
        (0 or 0x90, 0xB) => Fetch(),
        (0 or 0x90, 0xC) => Fetch16(),
        (0x92 or 0x91, 0xC) => Read16(Fetch()),
        (0x72, 0xC) => Read16(Fetch16()),
        (0, 0xD) => Fetch16() + X,
        (0x90, 0xD) => Fetch16() + Y,
        (0x92, 0xD) => Read16(Fetch()) + X,
        (0x91, 0xD) => Read16(Fetch()) + Y,
        (0x72, 0xD) => Read16(Fetch16()) + X,
        (0, 0xE) => Fetch() + X,
        (0x90, 0xE) => Fetch() + Y,
        (0, 0xF) => X,
        (0x90, 0xF) => Y,
        _ => null,
    };

    private bool LoadFarIndexed(int prefix, bool load)
    {
        int address = prefix switch
        {
            0 => Fetch24() + X,
            0x90 => Fetch24() + Y,
            0x92 => ReadPointer24(Fetch16()) + X,
            0x91 => ReadPointer24(Fetch16()) + Y,
            _ => -1,
        };
        if (address < 0) return false;
        LoadStoreFar(address, load);
        return true;
    }

    private bool LoadFarDirect(int prefix, bool load)
    {
        int address = prefix switch
        {
            0 => Fetch24(),
            0x92 => ReadPointer24(Fetch16()),
            _ => -1,
        };
        if (address < 0) return false;
        LoadStoreFar(address, load);
        return true;
    }

    private int ReadPointer24(int ptr) => (Read(ptr) << 16) | (Read(ptr + 1) << 8) | Read(ptr + 2);

    private void LoadStoreFar(int address, bool load)
    {
        if (load) A = Read(address);
        else Write(address, A);
        SetNZ(A);
    }

    private bool WordArithmetic(int hi, int lo)
    {
        ushort operand = hi switch
        {
            0xA => Fetch16(),
            0xB => Read16(Fetch16()),
            _ => Read16((SP + Fetch()) & 0xFFFF),
        };
        switch (lo)
        {
            case 0x0: X = Sub16(X, operand); return true;  // SUBW X
            case 0x2: Y = Sub16(Y, operand); return true;  // SUBW Y
            case 0x9: Y = Add16(Y, operand); return true;  // ADDW Y
            case 0xB: X = Add16(X, operand); return true;  // ADDW X
        }
        return false;
    }

    private void Arithmetic(int lo, byte m)
    {
        switch (lo)
        {
            case 0x0: A = Sub8(A, m, 0); return;           // SUB
            case 0x1: Sub8(A, m, 0); return;               // CP
            case 0x2: A = Sub8(A, m, Carry); return;       // SBC
            case 0x4: A &= m; SetNZ(A); return;            // AND
            case 0x5: SetNZ((byte)(A & m)); return;        // BCP
            case 0x8: A ^= m; SetNZ(A); return;            // XOR
            case 0x9: A = Add8(A, m, Carry); return;       // ADC
            case 0xA: A |= m; SetNZ(A); return;            // OR
            case 0xB: A = Add8(A, m, 0); return;           // ADD
        }
        throw new InvalidOperationException($"Not an arithmetic operation: {lo:X}");
    }

    private byte Add8(byte a, byte b, int c)
    {
        int r = a + b + c;
        SetFlag(FlagC, r > 0xFF);
        SetFlag(FlagH, ((a & 0xF) + (b & 0xF) + c) > 0xF);
        SetFlag(FlagV, ((a ^ r) & (b ^ r) & 0x80) != 0);
        SetNZ((byte)r);
        return (byte)r;
    }

    private byte Sub8(byte a, byte b, int c)
    {
        int r = a - b - c;
        SetFlag(FlagC, r < 0);
        SetFlag(FlagV, ((a ^ b) & (a ^ r) & 0x80) != 0);
        SetNZ((byte)r);
        return (byte)r;
    }

    private ushort Add16(ushort a, ushort b)
    {
        int r = a + b;
        SetFlag(FlagC, r > 0xFFFF);
        SetFlag(FlagH, ((a & 0xFF) + (b & 0xFF)) > 0xFF);
        SetFlag(FlagV, ((a ^ r) & (b ^ r) & 0x8000) != 0);
        SetNZ16((ushort)r);
        return (ushort)r;
    }

    private ushort Sub16(ushort a, ushort b)
    {
        int r = a - b;
        SetFlag(FlagC, r < 0);
        SetFlag(FlagH, (a & 0xFF) < (b & 0xFF));
        SetFlag(FlagV, ((a ^ b) & (a ^ r) & 0x8000) != 0);
        SetNZ16((ushort)r);
        return (ushort)r;
    }

    private void Compare16(ushort a, ushort b)
    {
        int r = a - b;
        SetFlag(FlagC, r < 0);
        SetFlag(FlagV, ((a ^ b) & (a ^ r) & 0x8000) != 0);
        SetNZ16((ushort)r);
    }
}
