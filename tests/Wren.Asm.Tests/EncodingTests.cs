using Wren.Asm;
using Xunit;

namespace Wren.Asm.Tests;

/// <summary>Every instruction form, checked against bytes produced by sdasstm8 (see Data/Encodings.txt).</summary>
public class EncodingTests
{
    public static IEnumerable<object[]> Cases()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "Encodings.txt");
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || line.Trim().Length == 0) continue;
            string[] parts = line.Split('|');
            yield return new object[] { parts[2], parts[0], parts[1] };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EncodesLikeSdas(string instruction, string address, string expected)
    {
        string source = $"    org ${address}\n    {instruction}\n";
        var result = new Assembler().Assemble(source);

        Assert.True(result.Success, string.Join("\n", result.Errors));
        int start = Convert.ToInt32(address, 16);
        string actual = string.Join(" ", result.Image.Bytes.Select(kv => kv.Value.ToString("X2")));
        Assert.Equal(start, result.Image.Bytes.First().Key);
        Assert.Equal(expected, actual);
    }
}
