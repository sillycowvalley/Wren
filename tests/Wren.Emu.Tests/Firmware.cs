using Wren.Asm;

namespace Wren.Emu.Tests;

/// <summary>Assembles firmware from the repository's firmware folder (copied next to the tests).</summary>
public static class Firmware
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "firmware");

    public static AssemblyResult Assemble(string relativePath)
    {
        string path = Path.Combine(Folder, relativePath);
        string directory = Path.GetDirectoryName(path)!;
        var assembler = new Assembler
        {
            IncludeResolver = name =>
            {
                foreach (string dir in new[] { directory, Path.Combine(Folder, "include") })
                {
                    string candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate)) return File.ReadAllText(candidate);
                }
                return null;
            },
        };
        var result = assembler.Assemble(File.ReadAllText(path), Path.GetFileName(path));
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Errors));
        return result;
    }
}
