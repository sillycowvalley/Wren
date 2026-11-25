using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wren.Asm;
using Wren.Script;

namespace Wren.Link;

/// <summary>What a check of Digicool's GitHub found: the files fetched, those already here, and any failure.</summary>
public sealed record DigicoolCheck(IReadOnlyList<string> Downloaded, IReadOnlyList<string> UpToDate, IReadOnlyList<string> Failures);

/// <summary>
/// Digicool's factory firmware, which Wren does not carry: each board's .s19 files are fetched
/// from the Firmware folder of Digicool's GitHub repository into a folder on this machine,
/// where Write image… finds them. A file is fetched when it is not here or differs from
/// Digicool's (compared by git's blob hash, which GitHub lists), so a new version or a changed
/// file arrives the next time the Firmware window opens. Each image fetched is also made
/// known to <see cref="ThingProgrammer.DetectBoard"/>.
/// </summary>
public sealed class DigicoolFirmware
{
    /// <summary>Digicool's two repositories, and the board each one's firmware is for.</summary>
    public static readonly (string Repository, Board Board)[] Sources =
    {
        ("DigicoolThings/8-bit_OLED_Thing", Board.OledThing),
        ("DigicoolThings/8-bit_LED_Thing", Board.LedThing),
    };

    /// <summary>Where fetched firmware is kept: %LocalAppData%\Wren\firmware\digicool, writable wherever Wren is.</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wren", "firmware", "digicool");

    private const string RawPrefix = "https://raw.githubusercontent.com/DigicoolThings/";

    private readonly HttpClient http;

    public string Folder { get; }

    public DigicoolFirmware(HttpClient http, string folder)
    {
        this.http = http;
        Folder = folder;
    }

    /// <summary>A plain HttpClient for GitHub: it asks for a user agent, and a slow network gives up after 20 s.</summary>
    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Wren");
        return client;
    }

    /// <summary>Makes the firmware already in the folder known to DetectBoard; returns the files.</summary>
    public IReadOnlyList<string> LearnFolder()
    {
        if (!Directory.Exists(Folder)) return Array.Empty<string>();
        var files = Directory.GetFiles(Folder, "*.s19").Select(Path.GetFileName).OfType<string>().OrderBy(n => n).ToList();
        foreach (string name in files)
        {
            try
            {
                Learn(MemoryImage.Parse(File.ReadAllText(Path.Combine(Folder, name))), BoardOf(name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
            {
                // A file that cannot be read stays unknown; Write image… will say why if it is chosen.
            }
        }
        return files;
    }

    /// <summary>
    /// Lists Digicool's Firmware folders and fetches every .s19 file that is missing here or
    /// differs. Never throws for a network or disk failure: those are reported in Failures.
    /// </summary>
    public async Task<DigicoolCheck> UpdateAsync(CancellationToken cancel = default)
    {
        var downloaded = new List<string>();
        var upToDate = new List<string>();
        var failures = new List<string>();
        foreach (var (repository, board) in Sources)
        {
            List<Entry> entries;
            try
            {
                string listing = await http.GetStringAsync($"https://api.github.com/repos/{repository}/contents/Firmware", cancel);
                entries = Parse(listing);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                failures.Add($"{repository}: {Reason(ex)}");
                continue;
            }
            foreach (var entry in entries)
            {
                if (!IsFirmwareName(entry.Name)) continue;
                string path = Path.Combine(Folder, entry.Name);
                try
                {
                    if (File.Exists(path) && BlobHash(await File.ReadAllBytesAsync(path, cancel)) == entry.Sha)
                    {
                        upToDate.Add(entry.Name);
                        continue;
                    }
                    if (!entry.DownloadUrl.StartsWith(RawPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"it is not on Digicool's GitHub ({entry.DownloadUrl})");
                    byte[] bytes = await http.GetByteArrayAsync(entry.DownloadUrl, cancel);
                    if (BlobHash(bytes) != entry.Sha) throw new InvalidOperationException("it arrived damaged (its hash differs from GitHub's)");
                    var image = CheckImage(bytes);
                    Directory.CreateDirectory(Folder);
                    string temporary = path + ".download";
                    await File.WriteAllBytesAsync(temporary, bytes, cancel);
                    File.Move(temporary, path, overwrite: true);
                    Learn(image, board);
                    downloaded.Add(entry.Name);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                    or UnauthorizedAccessException or InvalidOperationException or FormatException or ArgumentException)
                {
                    failures.Add($"{entry.Name}: {Reason(ex)}");
                }
            }
        }
        return new DigicoolCheck(downloaded, upToDate, failures);
    }

    private sealed record Entry(string Name, string Sha, string DownloadUrl);

    private static List<Entry> Parse(string listing)
    {
        using var document = JsonDocument.Parse(listing);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("GitHub did not list the folder");
        var entries = new List<Entry>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) && type.GetString() != "file") continue;
            string? name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? sha = item.TryGetProperty("sha", out var h) ? h.GetString() : null;
            string? url = item.TryGetProperty("download_url", out var u) ? u.GetString() : null;
            if (name != null && sha != null && url != null) entries.Add(new Entry(name, sha.ToLowerInvariant(), url));
        }
        return entries;
    }

    /// <summary>A plain .s19 file name: no folders, nothing Windows would refuse.</summary>
    public static bool IsFirmwareName(string name) =>
        name.EndsWith(".s19", StringComparison.OrdinalIgnoreCase)
        && name == Path.GetFileName(name)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0
        && !name.StartsWith('.');

    /// <summary>The image in a fetched file, refused unless it is S-records that lie wholly in the S003's 8 KB of flash.</summary>
    private static MemoryImage CheckImage(byte[] bytes)
    {
        var image = MemoryImage.Parse(Encoding.ASCII.GetString(bytes));
        if (image.Count == 0) throw new InvalidOperationException("it holds no S-records");
        if (image.Bytes.Any(kv => kv.Key < 0x8000 || kv.Key > 0x9FFF))
            throw new InvalidOperationException("it writes outside the Thing's flash ($8000-$9FFF)");
        return image;
    }

    private static void Learn(MemoryImage image, Board board)
    {
        if (board == Board.Unknown || !Enumerable.Range(0x8000, 8).All(image.Contains)) return;
        ThingProgrammer.KnowDigicoolImage(Enumerable.Range(0x8000, 8).Select(a => image[a]).ToArray(), board);
    }

    /// <summary>The board a file in the folder is for, from Digicool's file names ("8-bit_oled_thing…", "8-bit_led_thing…").</summary>
    public static Board BoardOf(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("oled")) return Board.OledThing;
        if (lower.Contains("led")) return Board.LedThing;
        return Board.Unknown;
    }

    /// <summary>Git's hash of a file's contents (SHA-1 of "blob {length}\0" and the bytes), which GitHub lists as its sha.</summary>
    public static string BlobHash(byte[] contents)
    {
        byte[] header = Encoding.ASCII.GetBytes($"blob {contents.Length}\0");
        return Convert.ToHexString(SHA1.HashData(header.Concat(contents).ToArray())).ToLowerInvariant();
    }

    private static string Reason(Exception ex) => ex switch
    {
        TaskCanceledException => "no answer in time",
        HttpRequestException { StatusCode: { } code } => $"GitHub answered {(int)code} ({code})",
        HttpRequestException => "could not reach GitHub (offline?)",
        _ => ex.Message,
    };
}
