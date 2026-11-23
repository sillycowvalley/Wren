namespace Wren.Asm;

/// <summary>An error in the source being assembled. The assembler adds the file and line.</summary>
public sealed class AsmException : Exception
{
    public AsmException(string message) : base(message) { }
}

/// <summary>One error reported by the assembler.</summary>
public sealed record Diagnostic(string File, int Line, string Message)
{
    public override string ToString() => $"{File}({Line}): {Message}";
}
