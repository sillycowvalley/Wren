using System.Net;
using System.Text;
using System.Text.Json;
using Wren.Emu;
using Wren.Script;

namespace Wren.Link.Tests;

/// <summary>Fetching Digicool's firmware from a stand-in for GitHub (no network).</summary>
public class DigicoolFirmwareTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "WrenDigicoolTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    /// <summary>GitHub as far as the fetch sees it: each repository's Firmware listing and the raw files.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public readonly Dictionary<string, Dictionary<string, byte[]>> Folders = new()
        {
            ["DigicoolThings/8-bit_OLED_Thing"] = new(),
            ["DigicoolThings/8-bit_LED_Thing"] = new(),
        };
        public readonly List<string> Fetched = new();
        public bool Offline;
        public string? WrongShaFor, ElsewhereFor;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (Offline) throw new HttpRequestException("No such host is known.");
            string url = request.RequestUri!.ToString();
            foreach (var (repository, files) in Folders)
            {
                if (url == $"https://api.github.com/repos/{repository}/contents/Firmware")
                {
                    var listing = files.Select(f => new Dictionary<string, string>
                    {
                        ["name"] = f.Key,
                        ["type"] = "file",
                        ["sha"] = f.Key == WrongShaFor ? new string('0', 40) : DigicoolFirmware.BlobHash(f.Value),
                        ["download_url"] = f.Key == ElsewhereFor
                            ? $"https://example.com/{f.Key}"
                            : $"https://raw.githubusercontent.com/{repository}/master/Firmware/{f.Key}",
                    }).Append(new Dictionary<string, string> { ["name"] = "README.md", ["type"] = "file", ["sha"] = "ab", ["download_url"] = "x" });
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(listing)) });
                }
                string prefix = $"https://raw.githubusercontent.com/{repository}/master/Firmware/";
                if (url.StartsWith(prefix) && files.TryGetValue(url[prefix.Length..], out var bytes))
                {
                    Fetched.Add(url[prefix.Length..]);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static byte[] S19(byte[] vectors) => Encoding.ASCII.GetBytes(DigicoolStandIn.Image(vectors).ToSRecords());

    private (FakeGitHub GitHub, DigicoolFirmware Firmware) Make()
    {
        var github = new FakeGitHub();
        github.Folders["DigicoolThings/8-bit_OLED_Thing"]["8-bit_oled_thing_v1.1.s19"] = S19(DigicoolStandIn.OledV11);
        github.Folders["DigicoolThings/8-bit_LED_Thing"]["8-bit_led_thing.s19"] = S19(DigicoolStandIn.Led);
        return (github, new DigicoolFirmware(new HttpClient(github), folder));
    }

    [Fact]
    public void GitsBlobHashIsGitHubsSha()
    {
        Assert.Equal("ce013625030ba8dba906f756967f9e9ca394464a", DigicoolFirmware.BlobHash(Encoding.ASCII.GetBytes("hello\n")));
    }

    [Fact]
    public async Task TheFirstCheckFetchesEveryImageThenNothingTillOneChanges()
    {
        var (github, firmware) = Make();
        var first = await firmware.UpdateAsync();
        Assert.Empty(first.Failures);
        Assert.Equal(new[] { "8-bit_oled_thing_v1.1.s19", "8-bit_led_thing.s19" }, first.Downloaded);
        Assert.Equal(S19(DigicoolStandIn.Led), File.ReadAllBytes(Path.Combine(folder, "8-bit_led_thing.s19")));

        var second = await firmware.UpdateAsync();
        Assert.Empty(second.Downloaded);
        Assert.Equal(2, second.UpToDate.Count);
        Assert.Equal(2, github.Fetched.Count);

        // Digicool changes the LED file under the same name: it is fetched again.
        byte[] changed = S19(new byte[] { 0x82, 0x00, 0x81, 0x60, 0x82, 0x00, 0x85, 0x30 });
        github.Folders["DigicoolThings/8-bit_LED_Thing"]["8-bit_led_thing.s19"] = changed;
        var third = await firmware.UpdateAsync();
        Assert.Equal(new[] { "8-bit_led_thing.s19" }, third.Downloaded);
        Assert.Equal(changed, File.ReadAllBytes(Path.Combine(folder, "8-bit_led_thing.s19")));
        Assert.False(File.Exists(Path.Combine(folder, "8-bit_led_thing.s19.download")));
    }

    [Fact]
    public async Task ANewVersionIsFetchedAndItsThingsAreKnown()
    {
        var (github, firmware) = Make();
        byte[] v12 = { 0x82, 0x00, 0x8D, 0x10, 0x82, 0x00, 0x9F, 0x00 };
        github.Folders["DigicoolThings/8-bit_OLED_Thing"]["8-bit_oled_thing_v1.2.s19"] = S19(v12);

        var thing = new Stm8Device(DeviceInfo.Stm8s003f3);    // no display answering: known by its firmware alone
        thing.Load(DigicoolStandIn.Image(v12).Bytes);
        var link = new ThingLink(new PicoLink(new FakePico(thing)), "COM9");
        link.Link.Sleep = _ => { };
        link.Target.Connect();
        Assert.Equal(Board.Unknown, link.Programmer.DetectBoard().Board);

        var check = await firmware.UpdateAsync();
        Assert.Contains("8-bit_oled_thing_v1.2.s19", check.Downloaded);
        Assert.Equal(Board.OledThing, link.Programmer.DetectBoard().Board);
    }

    [Fact]
    public async Task OfflineIsReportedNotThrown()
    {
        var (github, firmware) = Make();
        github.Offline = true;
        var check = await firmware.UpdateAsync();
        Assert.Empty(check.Downloaded);
        Assert.Equal(2, check.Failures.Count);
        Assert.Contains("offline", check.Failures[0]);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task DamagedForeignOrOutOfFlashFilesAreRefused()
    {
        var (github, firmware) = Make();
        var oled = github.Folders["DigicoolThings/8-bit_OLED_Thing"];
        github.WrongShaFor = "8-bit_oled_thing_v1.1.s19";
        oled["elsewhere.s19"] = S19(DigicoolStandIn.OledV10);
        github.ElsewhereFor = "elsewhere.s19";
        var outside = DigicoolStandIn.Image(DigicoolStandIn.OledV10);
        outside.Write(0x4800, new byte[] { 0xAA });            // read-out protection on
        oled["options.s19"] = Encoding.ASCII.GetBytes(outside.ToSRecords());
        oled["../escape.s19"] = S19(DigicoolStandIn.OledV10);

        var check = await firmware.UpdateAsync();
        Assert.Equal(new[] { "8-bit_led_thing.s19" }, check.Downloaded);
        Assert.Contains(check.Failures, f => f.StartsWith("8-bit_oled_thing_v1.1.s19") && f.Contains("damaged"));
        Assert.Contains(check.Failures, f => f.StartsWith("elsewhere.s19") && f.Contains("not on Digicool's GitHub"));
        Assert.Contains(check.Failures, f => f.StartsWith("options.s19") && f.Contains("outside"));
        Assert.Equal(new[] { "8-bit_led_thing.s19" }, Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("8-bit_oled_thing_v1.1.s19", true)]
    [InlineData("8-bit_led_thing.S19", true)]
    [InlineData("../x.s19", false)]
    [InlineData("a\\x.s19", false)]
    [InlineData("README.md", false)]
    [InlineData(".s19", false)]
    public void OnlyPlainS19NamesAreFetched(string name, bool fetched) =>
        Assert.Equal(fetched, DigicoolFirmware.IsFirmwareName(name));

    [Fact]
    public void BoardsComeFromDigicoolsFileNames()
    {
        Assert.Equal(Board.OledThing, DigicoolFirmware.BoardOf("8-bit_oled_thing_v1.0.s19"));
        Assert.Equal(Board.LedThing, DigicoolFirmware.BoardOf("8-bit_led_thing.s19"));
        Assert.Equal(Board.Unknown, DigicoolFirmware.BoardOf("other.s19"));
    }
}
