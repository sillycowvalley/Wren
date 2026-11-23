using Wren.Asm;
using Wren.Emu;

namespace Wren;

/// <summary>
/// What the window works on: the board, the loaded program and its source file.
/// Kept free of UI code.
/// </summary>
public sealed class Session
{
    public Session()
    {
        Board = new OledThing(Chip);
    }

    public DeviceInfo Chip { get; private set; } = DeviceInfo.Stm8s003f3;
    public OledThing Board { get; private set; }
    public string? ProgramPath { get; private set; }
    public AssemblyResult? Assembly { get; private set; }
    public List<string> Messages { get; } = new();

    /// <summary>Extra folders searched for INCLUDE files, after the source file's own folder.</summary>
    public List<string> IncludeFolders { get; } = new();

    /// <summary>Switches between the OLED Thing (STM8S003F3) and the development board (STM8S103F3).</summary>
    public void SetChip(DeviceInfo chip)
    {
        byte inputs = Board.Inputs;
        bool hex = Board.HexLow, bar = Board.BarLow, inv = Board.InvLow;
        Chip = chip;
        Board = new OledThing(chip) { HexLow = hex, BarLow = bar, InvLow = inv };
        Board.SetInputs(inputs);
        if (ProgramPath != null) Load(ProgramPath);
    }

    /// <summary>Loads an assembly source (.asm), or an image (.s19, .hex), and powers the board on.</summary>
    public bool Load(string path)
    {
        Messages.Clear();
        ProgramPath = path;
        Assembly = null;
        MemoryImage image;

        try
        {
            string text = File.ReadAllText(path);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".s19" or ".s28" or ".hex" or ".ihx" or ".mot")
            {
                image = MemoryImage.Parse(text);
            }
            else
            {
                Assembly = Assemble(path, text);
                foreach (var error in Assembly.Errors) Messages.Add(error.ToString());
                if (!Assembly.Success) return false;
                image = Assembly.Image;
            }

            Board.Mcu.Erase();
            Board.Mcu.Load(image.Bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            Messages.Add(ex.Message);
            return false;
        }

        int flashBytes = image.Bytes.Count(kv => kv.Key >= Stm8Device.FlashStart);
        Messages.Add($"Loaded {Path.GetFileName(path)}: {flashBytes} bytes of flash of {Chip.FlashSize}.");
        Board.PowerOn();
        return true;
    }

    private AssemblyResult Assemble(string path, string text)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var assembler = new Assembler
        {
            IncludeResolver = name =>
            {
                var folders = new List<string> { directory, Path.Combine(directory, "include"), Path.Combine(directory, "..", "include") };
                folders.AddRange(IncludeFolders);
                foreach (string folder in folders)
                {
                    string candidate = Path.Combine(folder, name);
                    if (File.Exists(candidate)) return File.ReadAllText(candidate);
                }
                return null;
            },
        };
        return assembler.Assemble(text, Path.GetFileName(path));
    }

    /// <summary>Looks upward from the app for the repository's firmware folder, so the demo opens on first run.</summary>
    public static string? FindDemo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "firmware", "demo", "HexDemo.asm");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>The CPU registers as text, one per line.</summary>
    public string RegisterText()
    {
        var c = Board.Mcu.Cpu;
        string flags = string.Concat(
            "V-IHINZC".Select((name, i) => (c.CC & (0x80 >> i)) != 0 ? name : '.'));
        return
            $"PC  {c.PC:X6}\n" +
            $"A   {c.A:X2}\n" +
            $"X   {c.X:X4}\n" +
            $"Y   {c.Y:X4}\n" +
            $"SP  {c.SP:X4}\n" +
            $"CC  {c.CC:X2}  {flags}\n" +
            $"\n{c.State switch { CpuState.Waiting => "Waiting for an interrupt", CpuState.Halted => "Halted", _ => "Executing" }}";
    }

    /// <summary>Flash and EEPROM writes made by the running firmware, against the rated endurance.</summary>
    public string WearText()
    {
        var m = Board.Mcu;
        int flash = m.FlashWrites.Max();
        int eeprom = m.EepromWrites.Max();
        return
            $"Flash   most writes to one byte: {flash} (rated {Chip.FlashEndurance:N0})\n" +
            $"EEPROM  most writes to one byte: {eeprom} (rated {Chip.EepromEndurance:N0})";
    }
}
