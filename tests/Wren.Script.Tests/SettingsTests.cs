namespace Wren.Script.Tests;

public class SettingsTests
{
    private static readonly ScreenRect[] OneScreen = { new(0, 0, 1920, 1040) };

    [Fact]
    public void RoundTripsThroughJson()
    {
        var settings = new AppSettings
        {
            MainWindow = new WindowPlacement { X = 100, Y = 50, Width = 1300, Height = 800, Maximized = true },
            ProgrammerWindow = new WindowPlacement { X = -1500, Y = 200, Width = 700, Height = 500 },
            SidePanelWidth = 412.5,
            Board = BoardChoice.DevelopmentBoard,
            LastFile = @"C:\Programs\Blink.bas",
            LastProgrammer = "Pico on COM5",
        };
        var back = AppSettings.FromJson(settings.ToJson());
        Assert.Equal(100, back.MainWindow!.X);
        Assert.Equal(50, back.MainWindow.Y);
        Assert.Equal(1300, back.MainWindow.Width);
        Assert.Equal(800, back.MainWindow.Height);
        Assert.True(back.MainWindow.Maximized);
        Assert.Equal(-1500, back.ProgrammerWindow!.X);
        Assert.False(back.ProgrammerWindow.Maximized);
        Assert.Equal(412.5, back.SidePanelWidth);
        Assert.Equal(BoardChoice.DevelopmentBoard, back.Board);
        Assert.Equal(@"C:\Programs\Blink.bas", back.LastFile);
        Assert.Equal("Pico on COM5", back.LastProgrammer);
    }

    [Fact]
    public void BoardIsWrittenByName() =>
        Assert.Contains("\"DevelopmentBoard\"", new AppSettings { Board = BoardChoice.DevelopmentBoard }.ToJson());

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"MainWindow\": 12 }")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    [InlineData("{ \"Board\": \"Toaster\" }")]
    [InlineData("{ \"Board\": 7 }")]
    public void BadJsonGivesDefaults(string json)
    {
        var settings = AppSettings.FromJson(json);
        Assert.Null(settings.MainWindow);
        Assert.Null(settings.SidePanelWidth);
        Assert.Equal(BoardChoice.Thing, settings.Board);
        Assert.Null(settings.LastFile);
    }

    [Fact]
    public void UnknownFieldsAreIgnored() =>
        Assert.Equal("a.bas", AppSettings.FromJson("{ \"Colour\": \"Blue\", \"LastFile\": \"a.bas\" }").LastFile);

    [Fact]
    public void SavesAndLoadsAFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "WrenSettingsTest" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "Wren", "Settings.json");
        try
        {
            Assert.Null(AppSettings.Load(path).MainWindow);   // missing: defaults
            var settings = new AppSettings { MainWindow = new WindowPlacement { X = 1, Y = 2, Width = 3, Height = 4 } };
            Assert.True(settings.Save(path));
            Assert.Equal(3, AppSettings.Load(path).MainWindow!.Width);
            Assert.False(File.Exists(path + ".tmp"));

            File.WriteAllText(path, "{ corrupt");
            Assert.Null(AppSettings.Load(path).MainWindow);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(0, 0, true)]
    [InlineData(1900, 100, false)]      // only 20 pixels of the title bar on screen
    [InlineData(1800, 100, true)]       // 120 pixels on screen
    [InlineData(-1100, 100, true)]      // mostly off the left, 100 pixels on
    [InlineData(-1200, 100, false)]
    [InlineData(100, 1030, false)]      // title bar below the working area
    [InlineData(100, -20, false)]       // only 10 pixels of the title bar on screen
    [InlineData(2500, 100, false)]      // on a monitor that is gone
    public void OnScreenCheck(int x, int y, bool expected) =>
        Assert.Equal(expected, AppSettings.IsOnScreen(x, y, 1200, OneScreen));

    [Fact]
    public void SecondMonitorCounts()
    {
        var two = new[] { new ScreenRect(0, 0, 1920, 1040), new ScreenRect(-1280, 0, 1280, 984) };
        Assert.True(AppSettings.IsOnScreen(-1000, 100, 800, two));
        Assert.False(AppSettings.IsOnScreen(-1000, 100, 800, OneScreen.Take(1)));
    }

    [Fact]
    public void NoScreensMeansOffScreen() =>
        Assert.False(AppSettings.IsOnScreen(0, 0, 800, Array.Empty<ScreenRect>()));
}
