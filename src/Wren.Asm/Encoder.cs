namespace Wren.Asm;

/// <summary>
/// Turns one STM8 instruction into bytes. Encodings follow ST's PM0044 programming manual
/// and are checked against sdasstm8 in the tests.
/// </summary>
public static class Encoder
{
    private const int PDY = 0x90; // use Y instead of X
    private const int PIY = 0x91; // ([short.w],Y) and Y forms of [short.w]
    private const int PIX = 0x92; // [short.w] and ([short.w],X)
    private const int PWSP = 0x72; // long pointers, long-address bit ops and more

    private static readonly Dictionary<string, int> Alu = new()
    {
        ["SUB"] = 0x0, ["CP"] = 0x1, ["SBC"] = 0x2, ["AND"] = 0x4, ["BCP"] = 0x5,
        ["XOR"] = 0x8, ["ADC"] = 0x9, ["OR"] = 0xA, ["ADD"] = 0xB,
    };

    private static readonly Dictionary<string, int> Rmw = new()
    {
        ["NEG"] = 0x0, ["CPL"] = 0x3, ["SRL"] = 0x4, ["RRC"] = 0x6, ["SRA"] = 0x7,
        ["SLL"] = 0x8, ["SLA"] = 0x8, ["RLC"] = 0x9, ["DEC"] = 0xA, ["INC"] = 0xC,
        ["TNZ"] = 0xD, ["SWAP"] = 0xE, ["CLR"] = 0xF,
    };

    private static readonly Dictionary<string, int> Relative = new()
    {
        ["JRA"] = 0x20, ["JRT"] = 0x20, ["JRF"] = 0x21, ["JRUGT"] = 0x22, ["JRULE"] = 0x23,
        ["JRNC"] = 0x24, ["JRUGE"] = 0x24, ["JRC"] = 0x25, ["JRULT"] = 0x25, ["JRNE"] = 0x26,
        ["JREQ"] = 0x27, ["JRNV"] = 0x28, ["JRV"] = 0x29, ["JRPL"] = 0x2A, ["JRMI"] = 0x2B,
        ["JRSGT"] = 0x2C, ["JRSLE"] = 0x2D, ["JRSGE"] = 0x2E, ["JRSLT"] = 0x2F,
        ["CALLR"] = 0xAD,
    };

    private static readonly Dictionary<string, int> RelativeY = new()
    {
        ["JRNH"] = 0x28, ["JRH"] = 0x29, ["JRNM"] = 0x2C, ["JRM"] = 0x2D, ["JRIL"] = 0x2E, ["JRIH"] = 0x2F,
    };

    private static readonly Dictionary<string, int> Inherent = new()
    {
        ["IRET"] = 0x80, ["RET"] = 0x81, ["TRAP"] = 0x83, ["RETF"] = 0x87, ["BREAK"] = 0x8B,
        ["CCF"] = 0x8C, ["HALT"] = 0x8E, ["WFI"] = 0x8F, ["RCF"] = 0x98, ["SCF"] = 0x99,
        ["RIM"] = 0x9A, ["SIM"] = 0x9B, ["RVF"] = 0x9C, ["NOP"] = 0x9D,
    };

    public static bool IsMnemonic(string name)
    {
        string m = name.ToUpperInvariant();
        return Alu.ContainsKey(m) || Rmw.ContainsKey(m) || Rmw.ContainsKey(WordToByte(m))
            || Relative.ContainsKey(m) || RelativeY.ContainsKey(m) || Inherent.ContainsKey(m)
            || m is "LD" or "LDW" or "LDF" or "CPW" or "ADDW" or "SUBW" or "EXG" or "EXGW" or "MOV"
                or "MUL" or "DIV" or "DIVW" or "RRWA" or "RLWA" or "PUSH" or "POP" or "PUSHW" or "POPW"
                or "JP" or "CALL" or "JPF" or "CALLF" or "INT" or "WFE"
                or "BTJT" or "BTJF" or "BSET" or "BRES" or "BCPL" or "BCCM";
    }

    private static string WordToByte(string m) => m.Length > 1 && m.EndsWith('W') ? m[..^1] : "";

    /// <summary>
    /// Encodes an instruction placed at address. When final is false, values may still be
    /// unknown and range errors are not reported, so the size can be worked out first.
    /// </summary>
    public static byte[] Encode(string mnemonic, IReadOnlyList<Operand> ops, long address, bool final)
    {
        var e = new Emitter(final, address);
        string m = mnemonic.ToUpperInvariant();

        if (Inherent.TryGetValue(m, out int inherent)) { Count(ops, 0, m); e.Op(0, inherent); }
        else if (m == "WFE") { Count(ops, 0, m); e.Op(PWSP, 0x8F); }
        else if (Alu.TryGetValue(m, out int alu)) EncodeAlu(e, alu, ops, m);
        else if (m == "LD") EncodeLd(e, ops);
        else if (m == "LDW") EncodeLdw(e, ops);
        else if (m == "CPW") EncodeCpw(e, ops);
        else if (m is "ADDW" or "SUBW") EncodeAddSubW(e, m == "ADDW", ops);
        else if (Rmw.TryGetValue(m, out int rmw)) EncodeRmw(e, rmw, ops, m);
        else if (Rmw.TryGetValue(WordToByte(m), out int rmwWord)) EncodeRmwWord(e, rmwWord, ops, m);
        else if (Relative.TryGetValue(m, out int rel)) { Count(ops, 1, m); e.Op(0, rel); e.Relative(Target(ops[0], m)); }
        else if (RelativeY.TryGetValue(m, out int relY)) { Count(ops, 1, m); e.Op(PDY, relY); e.Relative(Target(ops[0], m)); }
        else if (m is "JP" or "CALL") EncodeJump(e, m == "JP" ? 0xC : 0xD, ops, m);
        else if (m is "JPF" or "CALLF") EncodeFar(e, m == "JPF" ? 0xAC : 0x8D, ops, m);
        else if (m == "INT") { Count(ops, 1, m); e.Op(0, 0x82); e.Addr24(Target(ops[0], m).Number); }
        else if (m == "LDF") EncodeLdf(e, ops);
        else if (m is "BTJT" or "BTJF") EncodeBtj(e, m == "BTJF", ops, m);
        else if (m is "BSET" or "BRES" or "BCPL" or "BCCM") EncodeBit(e, m, ops);
        else if (m == "MOV") EncodeMov(e, ops);
        else if (m == "EXG") EncodeExg(e, ops);
        else if (m == "EXGW") { Count(ops, 2, m); Require(ops[0].IsRegister(Register.X) && ops[1].IsRegister(Register.Y), "EXGW X,Y"); e.Op(0, 0x51); }
        else if (m is "MUL" or "DIV") EncodeMulDiv(e, m == "MUL" ? 0x42 : 0x62, ops, m);
        else if (m == "DIVW") { Count(ops, 2, m); Require(ops[0].IsRegister(Register.X) && ops[1].IsRegister(Register.Y), "DIVW X,Y"); e.Op(0, 0x65); }
        else if (m is "RRWA" or "RLWA") EncodeRotateWord(e, m == "RRWA" ? 0x01 : 0x02, ops, m);
        else if (m is "PUSH" or "POP") EncodePushPop(e, m == "PUSH", ops);
        else if (m is "PUSHW" or "POPW") EncodePushPopW(e, m == "PUSHW", ops, m);
        else throw new AsmException($"Unknown instruction '{mnemonic}'");

        return e.Bytes.ToArray();
    }

    // ---- Addressing modes shared by many instructions ----

    /// <summary>
    /// The prefix and high opcode nibble for the memory modes of the A,src family
    /// (and LD, JP, CALL, LDW, CPW). Returns false when the operand is not one of them.
    /// </summary>
    private static bool MemoryMode(Operand o, out int prefix, out int high)
    {
        prefix = 0;
        high = 0;
        switch (o.Kind)
        {
            case OperandKind.Direct: high = o.Short ? 0xB : 0xC; return true;
            case OperandKind.IndexX: high = 0xF; return true;
            case OperandKind.OffsetX: high = o.Short ? 0xE : 0xD; return true;
            case OperandKind.IndexY: prefix = PDY; high = 0xF; return true;
            case OperandKind.OffsetY: prefix = PDY; high = o.Short ? 0xE : 0xD; return true;
            case OperandKind.Pointer when !o.ExtendedPointer: prefix = o.Short ? PIX : PWSP; high = 0xC; return true;
            case OperandKind.PointerX when !o.ExtendedPointer: prefix = o.Short ? PIX : PWSP; high = 0xD; return true;
            case OperandKind.PointerY when !o.ExtendedPointer:
                if (!o.Short) throw new AsmException($"([long.w],Y) does not exist: '{o.Text}'");
                prefix = PIY; high = 0xD; return true;
            default: return false;
        }
    }

    /// <summary>Writes the address bytes that follow the opcode for a memory mode.</summary>
    private static void MemoryBytes(Emitter e, Operand o)
    {
        switch (o.Kind)
        {
            case OperandKind.IndexX:
            case OperandKind.IndexY:
                return;
            case OperandKind.OffsetSP:
                e.Byte(o.Number, "stack offset", unsigned: true);
                return;
            default:
                if (o.Short) e.Byte(o.Number, "short address", unsigned: true);
                else e.Addr16(o.Number);
                return;
        }
    }

    /// <summary>The index register a memory mode uses, or None.</summary>
    private static Register IndexOf(Operand o) => o.Kind switch
    {
        OperandKind.IndexX or OperandKind.OffsetX or OperandKind.PointerX => Register.X,
        OperandKind.IndexY or OperandKind.OffsetY or OperandKind.PointerY => Register.Y,
        _ => Register.None,
    };

    // ---- Byte arithmetic: op A,src ----

    private static void EncodeAlu(Emitter e, int low, IReadOnlyList<Operand> ops, string m)
    {
        Count(ops, 2, m);
        if (m == "SUB" && ops[0].IsRegister(Register.SP))
        {
            Require(ops[1].Kind == OperandKind.Immediate, "SUB SP,#byte");
            e.Op(0, 0x52);
            e.Byte(ops[1].Number, "immediate", unsigned: true);
            return;
        }
        Require(ops[0].IsRegister(Register.A), $"{m} A,src");
        Operand src = ops[1];

        if (src.Kind == OperandKind.Immediate) { e.Op(0, 0xA0 | low); e.Byte(src.Number, "immediate"); return; }
        if (src.Kind == OperandKind.OffsetSP) { e.Op(0, 0x10 | low); MemoryBytes(e, src); return; }
        if (MemoryMode(src, out int prefix, out int high)) { e.Op(prefix, (high << 4) | low); MemoryBytes(e, src); return; }
        throw BadOperands(m, ops);
    }

    private static void EncodeLd(Emitter e, IReadOnlyList<Operand> ops)
    {
        Count(ops, 2, "LD");
        Operand dst = ops[0], src = ops[1];

        if (dst.IsRegister(Register.A) && src.Kind == OperandKind.Register)
        {
            switch (src.Register)
            {
                case Register.XL: e.Op(0, 0x9F); return;
                case Register.XH: e.Op(0, 0x9E); return;
                case Register.YL: e.Op(PDY, 0x9F); return;
                case Register.YH: e.Op(PDY, 0x9E); return;
            }
        }
        if (src.IsRegister(Register.A) && dst.Kind == OperandKind.Register)
        {
            switch (dst.Register)
            {
                case Register.XL: e.Op(0, 0x97); return;
                case Register.XH: e.Op(0, 0x95); return;
                case Register.YL: e.Op(PDY, 0x97); return;
                case Register.YH: e.Op(PDY, 0x95); return;
            }
        }
        if (dst.IsRegister(Register.A))
        {
            if (src.Kind == OperandKind.Immediate) { e.Op(0, 0xA6); e.Byte(src.Number, "immediate"); return; }
            if (src.Kind == OperandKind.OffsetSP) { e.Op(0, 0x7B); MemoryBytes(e, src); return; }
            if (MemoryMode(src, out int p, out int h)) { e.Op(p, (h << 4) | 0x6); MemoryBytes(e, src); return; }
        }
        if (src.IsRegister(Register.A))
        {
            if (dst.Kind == OperandKind.OffsetSP) { e.Op(0, 0x6B); MemoryBytes(e, dst); return; }
            if (MemoryMode(dst, out int p, out int h)) { e.Op(p, (h << 4) | 0x7); MemoryBytes(e, dst); return; }
        }
        throw BadOperands("LD", ops);
    }

    // ---- Word loads and compares ----

    private static void EncodeLdw(Emitter e, IReadOnlyList<Operand> ops)
    {
        Count(ops, 2, "LDW");
        Operand dst = ops[0], src = ops[1];

        // Register to register.
        if (dst.Kind == OperandKind.Register && src.Kind == OperandKind.Register)
        {
            switch (dst.Register, src.Register)
            {
                case (Register.X, Register.Y): e.Op(0, 0x93); return;
                case (Register.Y, Register.X): e.Op(PDY, 0x93); return;
                case (Register.X, Register.SP): e.Op(0, 0x96); return;
                case (Register.Y, Register.SP): e.Op(PDY, 0x96); return;
                case (Register.SP, Register.X): e.Op(0, 0x94); return;
                case (Register.SP, Register.Y): e.Op(PDY, 0x94); return;
            }
            throw BadOperands("LDW", ops);
        }

        if (dst.Kind == OperandKind.Register && dst.Register is Register.X or Register.Y)
        {
            Register reg = dst.Register;
            if (src.Kind == OperandKind.Immediate) { e.Op(reg == Register.Y ? PDY : 0, 0xAE); e.Word(src.Number); return; }
            if (src.Kind == OperandKind.OffsetSP) { e.Op(0, reg == Register.X ? 0x1E : 0x16); MemoryBytes(e, src); return; }
            if (WordMemory(src, reg, sameRegisterAsIndex: true, out int p, out int h))
            {
                e.Op(p, (h << 4) | 0xE);
                MemoryBytes(e, src);
                return;
            }
        }
        if (src.Kind == OperandKind.Register && src.Register is Register.X or Register.Y)
        {
            Register reg = src.Register;
            if (dst.Kind == OperandKind.OffsetSP) { e.Op(0, reg == Register.X ? 0x1F : 0x17); MemoryBytes(e, dst); return; }
            if (WordMemory(dst, reg, sameRegisterAsIndex: false, out int p, out int h))
            {
                e.Op(p, (h << 4) | 0xF);
                MemoryBytes(e, dst);
                return;
            }
        }
        throw BadOperands("LDW", ops);
    }

    private static void EncodeCpw(Emitter e, IReadOnlyList<Operand> ops)
    {
        Count(ops, 2, "CPW");
        Operand dst = ops[0], src = ops[1];
        Require(dst.Kind == OperandKind.Register && dst.Register is Register.X or Register.Y, "CPW X,src or CPW Y,src");
        Register reg = dst.Register;

        if (src.Kind == OperandKind.Immediate) { e.Op(reg == Register.Y ? PDY : 0, 0xA3); e.Word(src.Number); return; }
        if (src.Kind == OperandKind.OffsetSP)
        {
            Require(reg == Register.X, "CPW X,(off,SP)");
            e.Op(0, 0x13);
            MemoryBytes(e, src);
            return;
        }
        if (WordMemory(src, reg, sameRegisterAsIndex: false, out int p, out int h))
        {
            e.Op(p, (h << 4) | 0x3);
            MemoryBytes(e, src);
            return;
        }
        throw BadOperands("CPW", ops);
    }

    /// <summary>
    /// Memory modes for LDW and CPW. Indexed modes pair a fixed register with the index:
    /// LDW X,(..,X) loads the index register itself; LDW (..,X),Y and CPW Y,(..,X) use the other one.
    /// </summary>
    private static bool WordMemory(Operand o, Register reg, bool sameRegisterAsIndex, out int prefix, out int high)
    {
        if (!MemoryMode(o, out prefix, out high)) return false;

        Register index = IndexOf(o);
        if (index != Register.None)
        {
            bool same = index == reg;
            if (same != sameRegisterAsIndex) throw new AsmException($"That register cannot be used with '{o.Text}'");
            return true;
        }

        if (reg == Register.Y)
        {
            if (o.Kind == OperandKind.Direct) prefix = PDY;
            else if (o.Kind == OperandKind.Pointer && prefix == PIX) prefix = PIY;
            else throw new AsmException($"Y cannot be used with '{o.Text}'");
        }
        return true;
    }

    private static void EncodeAddSubW(Emitter e, bool add, IReadOnlyList<Operand> ops)
    {
        string m = add ? "ADDW" : "SUBW";
        Count(ops, 2, m);
        Operand dst = ops[0], src = ops[1];

        if (add && dst.IsRegister(Register.SP))
        {
            Require(src.Kind == OperandKind.Immediate, "ADDW SP,#byte");
            e.Op(0, 0x5B);
            e.Byte(src.Number, "immediate", unsigned: true);
            return;
        }
        Require(dst.Kind == OperandKind.Register && dst.Register is Register.X or Register.Y, $"{m} X,src or {m} Y,src");
        bool y = dst.Register == Register.Y;

        // Opcodes: immediate, long address, (off,SP)
        (int prefix, int op) code = (src.Kind, add, y) switch
        {
            (OperandKind.Immediate, true, false) => (0, 0x1C),
            (OperandKind.Immediate, false, false) => (0, 0x1D),
            (OperandKind.Immediate, true, true) => (PWSP, 0xA9),
            (OperandKind.Immediate, false, true) => (PWSP, 0xA2),
            (OperandKind.Direct, true, false) => (PWSP, 0xBB),
            (OperandKind.Direct, false, false) => (PWSP, 0xB0),
            (OperandKind.Direct, true, true) => (PWSP, 0xB9),
            (OperandKind.Direct, false, true) => (PWSP, 0xB2),
            (OperandKind.OffsetSP, true, false) => (PWSP, 0xFB),
            (OperandKind.OffsetSP, false, false) => (PWSP, 0xF0),
            (OperandKind.OffsetSP, true, true) => (PWSP, 0xF9),
            (OperandKind.OffsetSP, false, true) => (PWSP, 0xF2),
            _ => throw BadOperands(m, ops),
        };
        e.Op(code.prefix, code.op);
        if (src.Kind == OperandKind.Immediate) e.Word(src.Number);
        else if (src.Kind == OperandKind.Direct) e.Addr16(src.Number);
        else MemoryBytes(e, src);
    }

    // ---- Read-modify-write ----

    private static void EncodeRmw(Emitter e, int low, IReadOnlyList<Operand> ops, string m)
    {
        Count(ops, 1, m);
        Operand o = ops[0];
        (int prefix, int high) mode = o.Kind switch
        {
            OperandKind.Register when o.Register == Register.A => (0, 0x4),
            OperandKind.Direct => o.Short ? (0, 0x3) : (PWSP, 0x5),
            OperandKind.IndexX => (0, 0x7),
            OperandKind.OffsetX => o.Short ? (0, 0x6) : (PWSP, 0x4),
            OperandKind.IndexY => (PDY, 0x7),
            OperandKind.OffsetY => o.Short ? (PDY, 0x6) : (PDY, 0x4),
            OperandKind.OffsetSP => (0, 0x0),
            OperandKind.Pointer when !o.ExtendedPointer => o.Short ? (PIX, 0x3) : (PWSP, 0x3),
            OperandKind.PointerX when !o.ExtendedPointer => o.Short ? (PIX, 0x6) : (PWSP, 0x6),
            OperandKind.PointerY when !o.ExtendedPointer && o.Short => (PIY, 0x6),
            _ => throw BadOperands(m, ops),
        };
        e.Op(mode.prefix, (mode.high << 4) | low);
        if (o.Kind != OperandKind.Register) MemoryBytes(e, o);
    }

    private static void EncodeRmwWord(Emitter e, int low, IReadOnlyList<Operand> ops, string m)
    {
        Count(ops, 1, m);
        if (ops[0].IsRegister(Register.X)) e.Op(0, 0x50 | low);
        else if (ops[0].IsRegister(Register.Y)) e.Op(PDY, 0x50 | low);
        else throw BadOperands(m, ops);
    }

    // ---- Jumps ----

    private static void EncodeJump(Emitter e, int low, IReadOnlyList<Operand> ops, string m)
    {
        Count(ops, 1, m);
        Operand o = ops[0];
        if (o.Kind == OperandKind.Direct)
        {
            e.Op(0, 0xC0 | low);
            e.Addr16(o.Number);
            return;
        }
        if (MemoryMode(o, out int prefix, out int high))
        {
            e.Op(prefix, (high << 4) | low);
            MemoryBytes(e, o);
            return;
        }
        throw BadOperands(m, ops);
    }

    private static void EncodeFar(Emitter e, int op, IReadOnlyList<Operand> ops, string m)
    {
        Count(ops, 1, m);
        Operand o = ops[0];
        if (o.Kind == OperandKind.Direct) { e.Op(0, op); e.Addr24(o.Number); return; }
        if (o.Kind == OperandKind.Pointer && o.ExtendedPointer) { e.Op(PIX, op); e.Addr16(o.Number); return; }
        throw BadOperands(m, ops);
    }

    private static void EncodeLdf(Emitter e, IReadOnlyList<Operand> ops)
    {
        Count(ops, 2, "LDF");
        bool load = ops[0].IsRegister(Register.A);
        Operand mem = load ? ops[1] : ops[0];
        Require(load || ops[1].IsRegister(Register.A), "LDF A,mem or LDF mem,A");

        // (prefix, opcode for load, opcode for store), then the address width.
        (int prefix, int ld, int st, bool wide) code = mem.Kind switch
        {
            OperandKind.Direct => (0, 0xBC, 0xBD, true),
            OperandKind.OffsetX => (0, 0xAF, 0xA7, true),
            OperandKind.OffsetY => (PDY, 0xAF, 0xA7, true),
            OperandKind.Pointer when mem.ExtendedPointer => (PIX, 0xBC, 0xBD, false),
            OperandKind.PointerX when mem.ExtendedPointer => (PIX, 0xAF, 0xA7, false),
            OperandKind.PointerY when mem.ExtendedPointer => (PIY, 0xAF, 0xA7, false),
            _ => throw BadOperands("LDF", ops),
        };
        e.Op(code.prefix, load ? code.ld : code.st);
        if (code.wide) e.Addr24(mem.Number);
        else e.Addr16(mem.Number);
    }

    // ---- Bit operations ----

    private static void EncodeBtj(Emitter e, bool ifFalse, IReadOnlyList<Operand> ops, string m)
    {
        Count(ops, 3, m);
        Require(ops[0].Kind == OperandKind.Direct && ops[1].Kind == OperandKind.Immediate, $"{m} addr,#bit,label");
        int bit = BitNumber(e, ops[1]);
        e.Op(PWSP, (bit << 1) | (ifFalse ? 1 : 0));
        e.Addr16(ops[0].Number);
        e.Relative(Target(ops[2], m));
    }

    private static void EncodeBit(Emitter e, string m, IReadOnlyList<Operand> ops)
    {
        Count(ops, 2, m);
        Require(ops[0].Kind == OperandKind.Direct && ops[1].Kind == OperandKind.Immediate, $"{m} addr,#bit");
        int bit = BitNumber(e, ops[1]);
        (int prefix, int odd) code = m switch
        {
            "BSET" => (PWSP, 0),
            "BRES" => (PWSP, 1),
            "BCPL" => (PDY, 0),
            _ => (PDY, 1), // BCCM
        };
        e.Op(code.prefix, 0x10 | (bit << 1) | code.odd);
        e.Addr16(ops[0].Number);
    }

    private static int BitNumber(Emitter e, Operand o)
    {
        long bit = o.Number;
        if (bit is < 0 or > 7)
        {
            if (e.Final) throw new AsmException($"Bit number must be 0 to 7: '{o.Text}'");
            return 0;
        }
        return (int)bit;
    }

    // ---- Moves, exchanges, multiply and divide ----

    private static void EncodeMov(Emitter e, IReadOnlyList<Operand> ops)
    {
        Count(ops, 2, "MOV");
        Operand dst = ops[0], src = ops[1];
        Require(dst.Kind == OperandKind.Direct, "MOV dst,src");

        if (src.Kind == OperandKind.Immediate)
        {
            e.Op(0, 0x35);
            e.Byte(src.Number, "immediate");
            e.Addr16(dst.Number);
            return;
        }
        Require(src.Kind == OperandKind.Direct, "MOV dst,src");
        if (src.Short && dst.Short)
        {
            e.Op(0, 0x45);
            e.Byte(src.Number, "short address", unsigned: true);
            e.Byte(dst.Number, "short address", unsigned: true);
            return;
        }
        e.Op(0, 0x55);
        e.Addr16(src.Number);
        e.Addr16(dst.Number);
    }

    private static void EncodeExg(Emitter e, IReadOnlyList<Operand> ops)
    {
        Count(ops, 2, "EXG");
        Require(ops[0].IsRegister(Register.A), "EXG A,XL / EXG A,YL / EXG A,addr");
        Operand o = ops[1];
        if (o.IsRegister(Register.XL)) { e.Op(0, 0x41); return; }
        if (o.IsRegister(Register.YL)) { e.Op(0, 0x61); return; }
        if (o.Kind == OperandKind.Direct) { e.Op(0, 0x31); e.Addr16(o.Number); return; }
        throw BadOperands("EXG", ops);
    }

    private static void EncodeMulDiv(Emitter e, int op, IReadOnlyList<Operand> ops, string m)
    {
        Count(ops, 2, m);
        Require(ops[1].IsRegister(Register.A), $"{m} X,A or {m} Y,A");
        if (ops[0].IsRegister(Register.X)) e.Op(0, op);
        else if (ops[0].IsRegister(Register.Y)) e.Op(PDY, op);
        else throw BadOperands(m, ops);
    }

    private static void EncodeRotateWord(Emitter e, int op, IReadOnlyList<Operand> ops, string m)
    {
        // RRWA X,A and RRWA X are both accepted.
        Require(ops.Count is 1 or 2 && (ops.Count == 1 || ops[1].IsRegister(Register.A)), $"{m} X,A or {m} Y,A");
        if (ops[0].IsRegister(Register.X)) e.Op(0, op);
        else if (ops[0].IsRegister(Register.Y)) e.Op(PDY, op);
        else throw BadOperands(m, ops);
    }

    private static void EncodePushPop(Emitter e, bool push, IReadOnlyList<Operand> ops)
    {
        string m = push ? "PUSH" : "POP";
        Count(ops, 1, m);
        Operand o = ops[0];
        if (o.IsRegister(Register.A)) { e.Op(0, push ? 0x88 : 0x84); return; }
        if (o.IsRegister(Register.CC)) { e.Op(0, push ? 0x8A : 0x86); return; }
        if (push && o.Kind == OperandKind.Immediate) { e.Op(0, 0x4B); e.Byte(o.Number, "immediate"); return; }
        if (o.Kind == OperandKind.Direct) { e.Op(0, push ? 0x3B : 0x32); e.Addr16(o.Number); return; }
        throw BadOperands(m, ops);
    }

    private static void EncodePushPopW(Emitter e, bool push, IReadOnlyList<Operand> ops, string m)
    {
        Count(ops, 1, m);
        int op = push ? 0x89 : 0x85;
        if (ops[0].IsRegister(Register.X)) e.Op(0, op);
        else if (ops[0].IsRegister(Register.Y)) e.Op(PDY, op);
        else throw BadOperands(m, ops);
    }

    // ---- Helpers ----

    private static Operand Target(Operand o, string m)
    {
        if (o.Kind != OperandKind.Direct) throw new AsmException($"{m} needs a target address, not '{o.Text}'");
        return o;
    }

    private static void Count(IReadOnlyList<Operand> ops, int count, string m)
    {
        if (ops.Count != count)
            throw new AsmException($"{m} takes {count} operand{(count == 1 ? "" : "s")}, not {ops.Count}");
    }

    private static void Require(bool condition, string form)
    {
        if (!condition) throw new AsmException($"Expected {form}");
    }

    private static AsmException BadOperands(string m, IReadOnlyList<Operand> ops) =>
        new($"{m} cannot take '{string.Join(",", ops.Select(o => o.Text))}'");

    private sealed class Emitter
    {
        public Emitter(bool final, long address)
        {
            Final = final;
            Address = address;
        }

        public bool Final { get; }
        public long Address { get; }
        public List<byte> Bytes { get; } = new();

        public void Op(int prefix, int opcode)
        {
            if (prefix != 0) Bytes.Add((byte)prefix);
            Bytes.Add((byte)opcode);
        }

        public void Byte(long value, string what, bool unsigned = false)
        {
            long min = unsigned ? 0 : -128;
            if (Final && (value < min || value > 255)) throw new AsmException($"{Capital(what)} out of range: {value}");
            Bytes.Add((byte)value);
        }

        public void Word(long value)
        {
            if (Final && (value < -32768 || value > 65535)) throw new AsmException($"Immediate out of range: {value}");
            Bytes.Add((byte)(value >> 8));
            Bytes.Add((byte)value);
        }

        public void Addr16(long value)
        {
            if (Final && (value < 0 || value > 0xFFFF)) throw new AsmException($"Address out of range: ${value:X}");
            Bytes.Add((byte)(value >> 8));
            Bytes.Add((byte)value);
        }

        public void Addr24(long value)
        {
            if (Final && (value < 0 || value > 0xFFFFFF)) throw new AsmException($"Address out of range: ${value:X}");
            Bytes.Add((byte)(value >> 16));
            Bytes.Add((byte)(value >> 8));
            Bytes.Add((byte)value);
        }

        /// <summary>A relative branch offset, from the end of the instruction.</summary>
        public void Relative(Operand target)
        {
            long next = Address + Bytes.Count + 1;
            long offset = target.Number - next;
            if (Final && (offset < -128 || offset > 127))
                throw new AsmException($"Branch target out of range ({offset} bytes): '{target.Text}'");
            Bytes.Add((byte)offset);
        }

        private static string Capital(string s) => char.ToUpperInvariant(s[0]) + s[1..];
    }
}
