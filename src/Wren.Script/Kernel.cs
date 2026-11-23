using Wren.Asm;

namespace Wren.Script;

/// <summary>The kernel firmware, assembled from firmware/kernel, with the symbols the debugger uses.</summary>
public sealed class Kernel
{
    private Kernel(AssemblyResult assembly) => Assembly = assembly;

    public AssemblyResult Assembly { get; }
    public MemoryImage Image => Assembly.Image;

    public int Symbol(string name) => (int)Assembly.Symbols[name];

    /// <summary>
    /// Assembles firmware/kernel/Kernel.asm under firmwareFolder. Throws with the
    /// assembler's errors if it fails, or if the kernel runs into the program store.
    /// </summary>
    public static Kernel Assemble(string firmwareFolder)
    {
        string kernelFolder = Path.Combine(firmwareFolder, "kernel");
        string includeFolder = Path.Combine(firmwareFolder, "include");
        var assembler = new Assembler
        {
            IncludeResolver = name =>
            {
                foreach (string folder in new[] { kernelFolder, includeFolder })
                {
                    string path = Path.Combine(folder, name);
                    if (File.Exists(path)) return File.ReadAllText(path);
                }
                return null;
            },
        };
        var result = assembler.Assemble(File.ReadAllText(Path.Combine(kernelFolder, "Kernel.asm")), "Kernel.asm");
        if (!result.Success) throw new InvalidOperationException("The kernel did not assemble:\n" + string.Join("\n", result.Errors));
        int end = result.Image.Bytes.Max(kv => kv.Key);
        if (end >= ProgramStore.StoreAddress)
            throw new InvalidOperationException($"The kernel ends at ${end:X4}, inside the program store at ${ProgramStore.StoreAddress:X4}");
        return new Kernel(result);
    }

    /// <summary>Looks upward from a folder for the repository's firmware folder.</summary>
    public static string? FindFirmwareFolder(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "firmware", "kernel", "Kernel.asm");
            if (File.Exists(candidate)) return Path.Combine(dir.FullName, "firmware");
            dir = dir.Parent;
        }
        return null;
    }
}
