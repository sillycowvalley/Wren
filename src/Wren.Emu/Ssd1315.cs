namespace Wren.Emu;

/// <summary>
/// The SSD1315 OLED controller (command set as the SSD1306) with a 128x64 display RAM,
/// and the 0.49" 64x32 panel that shows part of it.
///
/// I2C: after the address comes a control byte. Bit 6 (D/C) chooses data or commands;
/// with bit 7 (Co) clear, every byte after it is of that kind until STOP; with Co set, one
/// byte follows and then another control byte.
///
/// The panel shows segments ColumnOffset to ColumnOffset+63 and COM rows 0 to 31.
/// How the real 0.49" module is wired is still to be confirmed on hardware.
/// </summary>
public sealed class Ssd1315 : II2cDevice
{
    public const int RamColumns = 128;
    public const int RamPages = 8;
    public const int PanelWidth = 64;
    public const int PanelHeight = 32;

    private readonly byte[,] ram = new byte[RamPages, RamColumns];

    // Command parsing
    private readonly List<byte> command = new();
    private int parametersWanted;

    // I2C state
    private bool expectControl;
    private bool continuous;
    private bool dataMode;

    public Ssd1315()
    {
        Reset();
    }

    /// <summary>First RAM column the panel shows (before segment remap).</summary>
    public int ColumnOffset { get; set; } = 32;

    /// <summary>
    /// The module is mounted rotated by 180° against the controller's own orientation, as on
    /// the OLED Thing: there, segment remap ($A1) and reversed COM scan ($C8) give an
    /// upright picture, as Digicool's firmware and Wren's kernel set it up.
    /// </summary>
    public bool MountedRotated { get; set; } = true;

    public bool DisplayOn { get; private set; }
    public bool ChargePump { get; private set; }
    public bool Inverse { get; private set; }
    public bool EntireOn { get; private set; }
    public bool SegmentRemap { get; private set; }
    public bool ComReverse { get; private set; }
    public int MuxRatio { get; private set; }
    public int DisplayOffset { get; private set; }
    public int StartLine { get; private set; }
    public int Contrast { get; private set; }
    public int AddressingMode { get; private set; }
    public int Column { get; private set; }
    public int Page { get; private set; }
    public int ColumnStart { get; private set; }
    public int ColumnEnd { get; private set; }
    public int PageStart { get; private set; }
    public int PageEnd { get; private set; }

    /// <summary>Goes up by one on every RAM write or setting change, so a view knows to redraw.</summary>
    public long Version { get; private set; }

    /// <summary>Command bytes that are not recognised, for the user to see.</summary>
    public List<byte> UnknownCommands { get; } = new();

    public void Reset()
    {
        Array.Clear(ram);
        command.Clear();
        parametersWanted = 0;
        DisplayOn = false;
        ChargePump = false;
        Inverse = false;
        EntireOn = false;
        SegmentRemap = false;
        ComReverse = false;
        MuxRatio = 64;
        DisplayOffset = 0;
        StartLine = 0;
        Contrast = 0x7F;
        AddressingMode = 2;
        Column = 0;
        Page = 0;
        ColumnStart = 0;
        ColumnEnd = 127;
        PageStart = 0;
        PageEnd = 7;
        Version++;
    }

    public byte RamByte(int page, int column) => ram[page, column];

    // ---- I2C ----

    public void Start(bool read)
    {
        expectControl = true;
        continuous = false;
    }

    public bool Write(byte value)
    {
        if (expectControl)
        {
            continuous = (value & 0x80) == 0;
            dataMode = (value & 0x40) != 0;
            expectControl = false;
            return true;
        }
        if (dataMode) WriteData(value);
        else WriteCommand(value);
        if (!continuous) expectControl = true;
        return true;
    }

    /// <summary>Status read: bit 6 is set while the display is off.</summary>
    public byte Read() => (byte)(DisplayOn ? 0x00 : 0x40);

    public void Stop() => expectControl = true;

    // ---- Data ----

    public void WriteData(byte value)
    {
        ram[Page & 7, Column & 127] = value;
        Version++;
        switch (AddressingMode)
        {
            case 0: // horizontal
                if (++Column > ColumnEnd)
                {
                    Column = ColumnStart;
                    if (++Page > PageEnd) Page = PageStart;
                }
                break;
            case 1: // vertical
                if (++Page > PageEnd)
                {
                    Page = PageStart;
                    if (++Column > ColumnEnd) Column = ColumnStart;
                }
                break;
            default: // page
                if (++Column > 127) Column = ColumnStart;
                break;
        }
    }

    // ---- Commands ----

    public void WriteCommand(byte value)
    {
        command.Add(value);
        if (command.Count == 1)
        {
            parametersWanted = ParameterCount(value);
            if (parametersWanted < 0)
            {
                UnknownCommands.Add(value);
                parametersWanted = 0;
            }
        }
        if (command.Count - 1 < parametersWanted) return;

        Execute(command);
        command.Clear();
        Version++;
    }

    private static int ParameterCount(byte c) => c switch
    {
        <= 0x1F => 0,                        // column address in page mode
        0x20 => 1,                           // addressing mode
        0x21 or 0x22 => 2,                   // column / page range
        0x26 or 0x27 => 6,                   // horizontal scroll set-up
        0x29 or 0x2A => 5,                   // vertical and horizontal scroll set-up
        0x2C or 0x2D => 6,                   // content scroll
        0x2E or 0x2F => 0,                   // scroll off / on
        >= 0x40 and <= 0x7F => 0,            // start line
        0x81 => 1,                           // contrast
        0x82 => 1,                           // (SSD1315) brightness for colour bank
        0x8D => 1,                           // charge pump
        0xA0 or 0xA1 => 0,                   // segment remap
        0xA3 => 2,                           // vertical scroll area
        0xA4 or 0xA5 or 0xA6 or 0xA7 => 0,
        0xA8 => 1,                           // multiplex ratio
        0xAD => 1,                           // (SSD1315) internal IREF
        0xAE or 0xAF => 0,                   // display off / on
        >= 0xB0 and <= 0xB7 => 0,            // page in page mode
        0xC0 or 0xC8 => 0,                   // COM scan direction
        0xD3 or 0xD5 or 0xD8 or 0xD9 or 0xDA or 0xDB => 1,
        0xE3 => 0,                           // NOP
        _ => -1,
    };

    private void Execute(List<byte> c)
    {
        byte op = c[0];
        switch (op)
        {
            case <= 0x0F: Column = (Column & 0xF0) | op; ColumnStart = AddressingMode == 2 ? Column : ColumnStart; break;
            case <= 0x1F: Column = (Column & 0x0F) | ((op & 0x0F) << 4); ColumnStart = AddressingMode == 2 ? Column : ColumnStart; break;
            case 0x20: AddressingMode = c[1] & 3; break;
            case 0x21:
                ColumnStart = c[1] & 127;
                ColumnEnd = c[2] & 127;
                Column = ColumnStart;
                break;
            case 0x22:
                PageStart = c[1] & 7;
                PageEnd = c[2] & 7;
                Page = PageStart;
                break;
            case >= 0x40 and <= 0x7F: StartLine = op & 0x3F; break;
            case 0x81: Contrast = c[1]; break;
            case 0x8D: ChargePump = (c[1] & 0x04) != 0; break;
            case 0xA0: SegmentRemap = false; break;
            case 0xA1: SegmentRemap = true; break;
            case 0xA4: EntireOn = false; break;
            case 0xA5: EntireOn = true; break;
            case 0xA6: Inverse = false; break;
            case 0xA7: Inverse = true; break;
            case 0xA8: MuxRatio = Math.Max(16, (c[1] & 0x3F) + 1); break;
            case 0xAE: DisplayOn = false; break;
            case 0xAF: DisplayOn = true; break;
            case >= 0xB0 and <= 0xB7: Page = op & 7; break;
            case 0xC0: ComReverse = false; break;
            case 0xC8: ComReverse = true; break;
            case 0xD3: DisplayOffset = c[1] & 0x3F; break;
        }
    }

    // ---- What the panel shows ----

    /// <summary>True when the panel is lit at all: display on and charge pump running.</summary>
    public bool Lit => DisplayOn && ChargePump;

    /// <summary>Whether panel pixel (x, y) is lit. (0, 0) is the top-left corner.</summary>
    public bool Pixel(int x, int y)
    {
        if (!Lit) return false;
        if (EntireOn) return true;

        if (MountedRotated)
        {
            x = PanelWidth - 1 - x;
            y = PanelHeight - 1 - y;
        }
        int segment = ColumnOffset + x;
        int column = SegmentRemap ? 127 - segment : segment;
        int com = ComReverse ? MuxRatio - 1 - y : y;
        if (com < 0) return false;
        int row = (com + DisplayOffset + StartLine) & 63;

        bool on = (ram[row >> 3, column & 127] & (1 << (row & 7))) != 0;
        return on != Inverse;
    }

    /// <summary>The whole panel, row by row: '#' lit, '.' dark. Handy for tests.</summary>
    public string PanelText()
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < PanelHeight; y++)
        {
            for (int x = 0; x < PanelWidth; x++) sb.Append(Pixel(x, y) ? '#' : '.');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
