using Wren.Asm;

namespace Wren.Link.Tests;

/// <summary>
/// Stand-ins for Digicool's factory firmware, which Wren does not carry: the first 8 bytes
/// (the reset and TRAP vectors) of each of Digicool's images, by which Wren knows them, then
/// 2 KB of filler, a JRA to itself at the reset address and an IRET at the TRAP address.
/// </summary>
public static class DigicoolStandIn
{
    public static readonly byte[] OledV10 = { 0x82, 0x00, 0x8C, 0xC5, 0x82, 0x00, 0x9D, 0x57 },
        OledV11 = { 0x82, 0x00, 0x8C, 0xCB, 0x82, 0x00, 0x9E, 0x7D },
        Led = { 0x82, 0x00, 0x81, 0x50, 0x82, 0x00, 0x85, 0x24 };

    public static MemoryImage Image(byte[] vectors)
    {
        var image = new MemoryImage();
        image.Write(0x8000, vectors);
        image.Write(0x8008, Enumerable.Range(0, 2048).Select(i => (byte)(i * 7 + 3)));
        int reset = (vectors[2] << 8) | vectors[3], trap = (vectors[6] << 8) | vectors[7];
        image.Write(reset, new byte[] { 0x20, 0xFE });       // JRA to itself
        image.Write(trap, new byte[] { 0x80 });              // IRET
        return image;
    }
}
