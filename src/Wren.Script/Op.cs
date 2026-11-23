namespace Wren.Script;

/// <summary>Opcodes. See docs/Bytecode.md; the kernel's Vm.inc must agree.</summary>
public static class Op
{
    public const byte PushSmall = 0x00;     // + n (0-15)
    public const byte LoadGlobal = 0x10;    // + n (0-15)
    public const byte StoreGlobal = 0x20;   // + n (0-15)
    public const byte PushByte = 0x30;
    public const byte PushNegative = 0x31;
    public const byte PushWord = 0x32;
    public const byte Value = 0x33;
    public const byte LoadGlobalByte = 0x34;
    public const byte StoreGlobalByte = 0x35;
    public const byte LoadLocal = 0x36;
    public const byte StoreLocal = 0x37;
    public const byte LoadArrayWord = 0x38;
    public const byte StoreArrayWord = 0x39;
    public const byte LoadArrayByte = 0x3A;
    public const byte StoreArrayByte = 0x3B;
    public const byte Add = 0x3C, Sub = 0x3D, Mul = 0x3E, Div = 0x3F, Mod = 0x40, And = 0x41, Or = 0x42,
        Xor = 0x43, Not = 0x44, Neg = 0x45, Shl = 0x46, Shr = 0x47, Eq = 0x48, Ne = 0x49, Lt = 0x4A,
        Le = 0x4B, Gt = 0x4C, Ge = 0x4D, Drop = 0x4E;
    public const byte Jump = 0x4F, JumpZero = 0x50, JumpNotZero = 0x51;
    public const byte JumpLong = 0x52, JumpZeroLong = 0x53, JumpNotZeroLong = 0x54;
    public const byte Call = 0x55, Enter = 0x56, Return = 0x57, ReturnValue = 0x58;
    public const byte Cls = 0x59, Show = 0x5A, Pixel = 0x5B, Line = 0x5C, Box = 0x5D, Fill = 0x5E,
        Invert = 0x5F, Contrast = 0x60, Inverse = 0x61, Text = 0x62, Hex = 0x63, Dec = 0x64, Bin = 0x65,
        Bits = 0x66, Bar = 0x67, Wait = 0x68, EeWrite = 0x69, Inputs = 0x6A, Pin = 0x6B, Analog = 0x6C,
        Millis = 0x6D, EeRead = 0x6E, Rnd = 0x6F, Abs = 0x70, Min = 0x71, Max = 0x72, Lost = 0x73, Flip = 0x74;

    /// <summary>Event numbers in the handler table.</summary>
    public const int EventStart = 0, EventChange = 1, EventPins = 2, EventLatch = 3, EventPress = 8,
        EventRelease = 13, EventEvery = 18;
}
