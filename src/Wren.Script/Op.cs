namespace Wren.Script;

/// <summary>Opcodes. See docs/Bytecode.md; the kernel's Vm.inc must agree.</summary>
public static class Op
{
    public const byte PushSmall = 0x00;     // + n (0-15)
    public const byte PushByte = 0x10;
    public const byte PushNegative = 0x11;
    public const byte PushWord = 0x12;
    public const byte Value = 0x13;
    public const byte LoadGlobal = 0x20;    // + n (0-15)
    public const byte StoreGlobal = 0x30;   // + n (0-15)
    public const byte LoadGlobalByte = 0x40;
    public const byte StoreGlobalByte = 0x41;
    public const byte LoadLocal = 0x42;
    public const byte StoreLocal = 0x43;
    public const byte LoadArrayWord = 0x44;
    public const byte StoreArrayWord = 0x45;
    public const byte LoadArrayByte = 0x46;
    public const byte StoreArrayByte = 0x47;
    public const byte Add = 0x50, Sub = 0x51, Mul = 0x52, Div = 0x53, Mod = 0x54, And = 0x55, Or = 0x56,
        Xor = 0x57, Not = 0x58, Neg = 0x59, Shl = 0x5A, Shr = 0x5B, Eq = 0x5C, Ne = 0x5D, Lt = 0x5E,
        Le = 0x5F, Gt = 0x60, Ge = 0x61, Drop = 0x62;
    public const byte Jump = 0x70, JumpZero = 0x71, JumpNotZero = 0x72;
    public const byte JumpLong = 0x73, JumpZeroLong = 0x74, JumpNotZeroLong = 0x75;
    public const byte Call = 0x76, Enter = 0x77, Return = 0x78, ReturnValue = 0x79;
    public const byte Cls = 0x80, Show = 0x81, Pixel = 0x82, Line = 0x83, Box = 0x84, Fill = 0x85,
        Invert = 0x86, Contrast = 0x87, Inverse = 0x88, Text = 0x89, Hex = 0x8A, Dec = 0x8B, Bin = 0x8C,
        Bits = 0x8D, Bar = 0x8E, Wait = 0x8F, EeWrite = 0x90, Inputs = 0x91, Pin = 0x92, Analog = 0x93,
        Millis = 0x94, EeRead = 0x95, Rnd = 0x96, Abs = 0x97, Min = 0x98, Max = 0x99, Lost = 0x9A, Flip = 0x9B;

    /// <summary>Event numbers in the handler table.</summary>
    public const int EventStart = 0, EventChange = 1, EventPins = 2, EventLatch = 3, EventPress = 8,
        EventRelease = 13, EventEvery = 18;
}
