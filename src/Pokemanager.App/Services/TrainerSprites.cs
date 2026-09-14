using System.Net.Http;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;

namespace Pokemanager.App.Services;

/// <summary>
/// Trainer sprites for player profiles, from Pokémon Showdown's public collection (about 1500, 80×80 PNG). The list and
/// every picture used are cached in <c>&lt;data&gt;/trainer-sprites</c>, so they are downloaded once and work offline after.
/// </summary>
public static partial class TrainerSprites
{
    public const string BaseUrl = "https://play.pokemonshowdown.com/sprites/trainers/";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Dictionary<string, Bitmap?> Bitmaps = [];
    private static readonly SemaphoreSlim IndexLock = new(1, 1);
    private static IReadOnlyList<string>? index;

    /// <summary>Some well-known ones to show before searching.</summary>
    public static IReadOnlyList<string> Featured { get; } =
    [
        "red", "leaf", "ethan", "lyra", "brendan", "may", "lucas", "dawn", "hilbert", "hilda", "nate", "rosa",
        "calem", "serena", "elio", "selene", "victor", "gloria", "blue", "cynthia", "steven", "lance", "n", "acetrainer-gen7",
    ];

    private static string CacheDirectory => Path.Combine(AppSettings.DataRoot, "trainer-sprites");

    [GeneratedRegex(@"href=""([a-z0-9\-]+)\.png""")]
    private static partial Regex PngLink();

    /// <summary>Every sprite name: cached list (refreshed after a week), downloaded when there is none.</summary>
    public static async Task<IReadOnlyList<string>> IndexAsync(CancellationToken cancel = default)
    {
        if (index is not null)
            return index;
        await IndexLock.WaitAsync(cancel);
        try
        {
            if (index is not null)
                return index;
            string file = Path.Combine(CacheDirectory, "index.txt");
            bool fresh = File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(7);
            if (!fresh)
            {
                try
                {
                    string html = await Http.GetStringAsync(BaseUrl, cancel);
                    var names = PngLink().Matches(html).Select(m => m.Groups[1].Value).Distinct().Order().ToList();
                    if (names.Count > 0)
                    {
                        Directory.CreateDirectory(CacheDirectory);
                        await File.WriteAllLinesAsync(file, names, cancel);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
                {
                    // Offline: the old list, if any.
                }
            }
            index = File.Exists(file) ? (await File.ReadAllLinesAsync(file, cancel)).Where(n => n.Length > 0).ToList() : [];
            return index;
        }
        finally
        {
            IndexLock.Release();
        }
    }

    /// <summary>Names containing every word of the query ("ace gen7", "cynthia"); featured ones first when the query is empty.</summary>
    public static async Task<IReadOnlyList<string>> SearchAsync(string query, int limit = 60, CancellationToken cancel = default)
    {
        var all = await IndexAsync(cancel);
        string[] words = query.ToLowerInvariant().Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return Featured.Where(f => all.Count == 0 || all.Contains(f)).ToList();
        return all.Where(n => words.All(n.Contains))
            .OrderBy(n => n.StartsWith(words[0], StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(n => n.Length)
            .Take(limit)
            .ToList();
    }

    /// <summary>The sprite as a bitmap (downloaded and cached the first time), or null when it cannot be had.</summary>
    public static async Task<Bitmap?> GetAsync(string? name, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, "^[a-z0-9-]+$"))
            return null;
        lock (Bitmaps)
        {
            if (Bitmaps.TryGetValue(name, out var cached))
                return cached;
        }
        string file = Path.Combine(CacheDirectory, name + ".png");
        Bitmap? bitmap = null;
        try
        {
            if (!File.Exists(file))
            {
                byte[] bytes = await Http.GetByteArrayAsync(BaseUrl + name + ".png", cancel);
                Directory.CreateDirectory(CacheDirectory);
                await File.WriteAllBytesAsync(file, bytes, cancel);
            }
            bitmap = new Bitmap(file);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null; // not cached: try again next time
        }
        lock (Bitmaps)
            Bitmaps[name] = bitmap;
        return bitmap;
    }

    /// <summary>A picture sent by another player (PNG bytes), or null when it is not a picture.</summary>
    public static Bitmap? FromBytes(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
            return null;
        try
        {
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A picture chosen by the user, reduced to fit a profile (at most 96 px, PNG) so it can travel to other players.</summary>
    public static byte[]? ProfileImage(string path)
    {
        try
        {
            using var source = new Bitmap(path);
            double scale = Math.Min(1, 96.0 / Math.Max(source.PixelSize.Width, source.PixelSize.Height));
            var size = new Avalonia.PixelSize(Math.Max(1, (int)(source.PixelSize.Width * scale)), Math.Max(1, (int)(source.PixelSize.Height * scale)));
            using var scaled = source.CreateScaledBitmap(size, BitmapInterpolationMode.HighQuality);
            using var stream = new MemoryStream();
            scaled.Save(stream);
            return stream.Length <= Multiplayer.PlayerProfile.MaxImageBytes ? stream.ToArray() : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
