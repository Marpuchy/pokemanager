using System.Text.Json;
using System.Text.Json.Serialization;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Dump;

/// <summary>
/// What an imported game folder holds, written as <c>pokemanager-game.json</c> next to <c>romfs</c> and <c>exefs</c>. The
/// title comes from the ROM's program ID, so the folder never has to be inspected to know the game.
/// </summary>
public sealed record DumpManifest(
    int Format,
    GameTitle Game,
    string TitleId,
    string SourceRom,
    long SourceSize,
    DateTime SourceModified,
    DateTime Imported,
    List<string> Files)
{
    public const string FileName = "pokemanager-game.json";
    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The manifest of a game folder, or null for a folder that was not imported by Pokemanager.</summary>
    public static DumpManifest? TryLoad(string directory)
    {
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<DumpManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(string directory) =>
        File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Whether this folder was made from exactly that ROM file (same path, size and date).</summary>
    public bool IsFrom(string romPath)
    {
        var info = new FileInfo(romPath);
        return string.Equals(Path.GetFullPath(SourceRom), info.FullName, StringComparison.OrdinalIgnoreCase)
               && info.Exists && info.Length == SourceSize && info.LastWriteTimeUtc == SourceModified.ToUniversalTime();
    }
}

/// <summary>Result of <see cref="GameImporter.Import"/>.</summary>
public sealed record ImportedGame(string Directory, GameTitle Game, bool Reused);

/// <summary>
/// Turns a decrypted ROM into a game folder the application can open: detects the game from the ROM, and copies only
/// what Pokemanager reads (data tables, game texts in every language, Pokémon icons, badge/trial images, the decompressed
/// <c>code.bin</c> and the icon). A few tens of megabytes instead of the whole ROM. The ROM is only read.
/// </summary>
public static class GameImporter
{
    /// <summary>The game of a ROM without importing it.</summary>
    /// <exception cref="RomReadException">Not a usable ROM, or not a supported game.</exception>
    public static GameTitle Detect(string romPath)
    {
        using var rom = RomReader.Open(romPath);
        return GameTitleExtensions.FromTitleId(rom.TitleId)
               ?? throw new RomReadException(string.Format(Strings.Import_UnsupportedGame, Path.GetFileName(romPath), rom.TitleId.ToString("X16")));
    }

    /// <summary>
    /// Imports <paramref name="romPath"/> into a new folder under <paramref name="gamesRoot"/>, named after the ROM. A folder
    /// already imported from the same ROM file is reused (and completed if files are missing).
    /// </summary>
    /// <exception cref="RomReadException">Not a usable ROM, or not a supported game.</exception>
    public static ImportedGame Import(string romPath, string gamesRoot, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        romPath = Path.GetFullPath(romPath);
        using var rom = RomReader.Open(romPath);
        var game = GameTitleExtensions.FromTitleId(rom.TitleId)
                   ?? throw new RomReadException(string.Format(Strings.Import_UnsupportedGame, Path.GetFileName(romPath), rom.TitleId.ToString("X16")));

        string directory = FolderFor(romPath, gamesRoot, out bool reused);
        progress?.Report(Strings.Import_Looking);
        var files = RequiredFiles(game).ToList();
        if (PickIcons(rom, game) is { } icons)
            files.Add(icons);
        if (PickMilestones(rom, game) is { } milestones)
            files.Add(milestones);
        // Item icons: the game's own Z-crystals for the trials of Generation 7.
        foreach (string itemIcons in game.Layout().ItemIcons.Where(p => rom.RomFsFileSize(p) > 0).Take(1))
            files.Add(itemIcons);
        Directory.CreateDirectory(directory);

        string romfs = Path.Combine(directory, "romfs");
        var copied = new List<string>();
        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(romfs, files[i].Replace('/', Path.DirectorySeparatorChar));
            long size = rom.RomFsFileSize(files[i]) ?? throw new RomReadException(string.Format(Strings.Import_MissingFile, Path.GetFileName(romPath), files[i]));
            if (!File.Exists(target) || new FileInfo(target).Length != size)
            {
                progress?.Report(string.Format(Strings.Import_Copying, files[i], i + 1, files.Count));
                rom.CopyRomFs(files[i], target);
            }
            copied.Add(files[i]);
        }

        string exefs = Path.Combine(directory, "exefs");
        Directory.CreateDirectory(exefs);
        progress?.Report(Strings.Import_Code);
        WriteIfDifferent(Path.Combine(exefs, "code.bin"), rom.ReadCode());
        if (rom.ReadExeFs("icon") is { } icon)
            WriteIfDifferent(Path.Combine(exefs, "icon.bin"), icon);
        WriteIfDifferent(Path.Combine(directory, "exheader.bin"), rom.ReadExtendedHeader());

        var info = new FileInfo(romPath);
        new DumpManifest(DumpManifest.CurrentFormat, game, rom.TitleId.ToString("X16"), romPath, info.Length, info.LastWriteTimeUtc,
            DateTime.UtcNow, copied).Save(directory);
        return new ImportedGame(directory, game, reused);
    }

    /// <summary>
    /// Completes a folder imported by an older version of the application: when it lacks a file this one reads and the
    /// source ROM is still where it was, the game is imported again, which only copies what is missing. Any problem (ROM
    /// moved, changed or unreadable) simply leaves the folder as it is.
    /// </summary>
    /// <returns>Whether anything was imported.</returns>
    public static bool Complete(string directory, IProgress<string>? progress = null)
    {
        if (DumpManifest.TryLoad(directory) is not { } manifest)
            return false;
        if (!Incomplete(manifest))
            return false;
        if (Path.GetDirectoryName(directory) is not { } gamesRoot || !File.Exists(manifest.SourceRom) || !manifest.IsFrom(manifest.SourceRom))
            return false;
        try
        {
            Import(manifest.SourceRom, gamesRoot, progress);
            return true;
        }
        catch (Exception ex) when (ex is RomReadException or IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the folder lacks a file this version of the application reads: one of the required files (trainers were
    /// added in 3.0), or the item icons of Generation 7 (added in 2.0, and only when the game has them at all).
    /// </summary>
    private static bool Incomplete(DumpManifest manifest)
    {
        if (RequiredFiles(manifest.Game).Any(f => !manifest.Files.Contains(f)))
            return true;
        string[] itemIcons = manifest.Game.Layout().ItemIcons;
        return itemIcons.Length > 0 && !itemIcons.Any(manifest.Files.Contains);
    }

    /// <summary>
    /// <c>&lt;games&gt;/&lt;ROM name&gt;</c>; when that folder holds another ROM's game, <c>&lt;ROM name&gt; (2)</c> and so on.
    /// </summary>
    public static string FolderFor(string romPath, string gamesRoot, out bool reused)
    {
        string name = Path.GetFileNameWithoutExtension(romPath).Trim();
        name = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (name.Length == 0)
            name = "game";
        for (int n = 1; ; n++)
        {
            string candidate = Path.Combine(gamesRoot, n == 1 ? name : $"{name} ({n})");
            var manifest = DumpManifest.TryLoad(candidate);
            if (manifest is not null && manifest.IsFrom(romPath))
            {
                reused = true;
                return candidate;
            }
            if (!Directory.Exists(candidate) || (manifest is null && !Directory.EnumerateFileSystemEntries(candidate).Any()))
            {
                reused = false;
                return candidate;
            }
        }
    }

    /// <summary>RomFS files every game has and the application reads: data tables and game texts in every language.</summary>
    public static IEnumerable<string> RequiredFiles(GameTitle game)
    {
        var layout = game.Layout();
        yield return layout.Personal;
        yield return layout.Moves;
        yield return layout.LevelUp;
        yield return layout.Evolution;
        yield return layout.TrainerData;
        yield return layout.TrainerPokemon;
        for (int language = 0; language < layout.LanguageCount; language++)
            yield return GarcPath(layout.GameText + language);
    }

    /// <summary>The first icon archive candidate that looks like one (hundreds of entries); null when none does.</summary>
    private static string? PickIcons(RomReader rom, GameTitle game) =>
        game.Layout().Icons.FirstOrDefault(path =>
        {
            try
            {
                return rom.ReadRomFs(path) is { } data && new pk3DS.Core.CTR.GARC.MemGARC(data).FileCount > game.Layout().SpeciesCount;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or EndOfStreamException or FormatException)
            {
                return false;
            }
        });

    /// <summary>The first badge/trial image archive candidate that holds the first image; null when none does.</summary>
    private static string? PickMilestones(RomReader rom, GameTitle game)
    {
        foreach (var candidate in game.Layout().Milestones)
        {
            try
            {
                if (rom.ReadRomFs(candidate.Archive) is not { } data)
                    continue;
                if (Data.MilestoneIcons.ReadArchive(data, candidate.File) is { } files && Data.MilestoneIcons.Find(files, candidate.Names[0]) is not null)
                    return candidate.Archive;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or EndOfStreamException or FormatException)
            {
            }
        }
        return null;
    }

    /// <summary>GARC number → path (<c>72</c> → <c>a/0/7/2</c>).</summary>
    public static string GarcPath(int number) => $"a/{number / 100 % 10}/{number / 10 % 10}/{number % 10}";

    private static void WriteIfDifferent(string path, byte[] data)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(data))
            return;
        File.WriteAllBytes(path, data);
    }
}
