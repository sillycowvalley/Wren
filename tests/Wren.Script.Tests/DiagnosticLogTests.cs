namespace Wren.Script.Tests;

public class DiagnosticLogTests
{
    [Fact]
    public void WritesOnlyWhileOnAndKeepsEveryLine()
    {
        string folder = Path.Combine(Path.GetTempPath(), "WrenLogTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            DiagnosticLog.Write("before");
            DiagnosticLog.Start(folder);
            DiagnosticLog.Write("one\ntwo");
            string text = DiagnosticLog.Text;
            DiagnosticLog.Stop();
            DiagnosticLog.Write("after");
            Assert.Contains(" one", text);
            Assert.Contains(" two", text);
            Assert.DoesNotContain("before", text);
            Assert.DoesNotContain("after", File.ReadAllText(DiagnosticLog.FilePath!));
            Assert.False(DiagnosticLog.Enabled);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
