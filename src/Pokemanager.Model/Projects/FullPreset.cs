using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Mods;

namespace Pokemanager.Model.Projects;

/// <summary>
/// Everything of the Options page that is the application's and not UPR ZX's, carried at the end of a <c>.rnqs</c>
/// (user, 2026-11-03: "the preset has to bring everything"). The levels, the level cap, the shiny odds, the item and
/// shop options, the difficulty and the mods.
/// </summary>
/// <remarks>
/// <para>
/// <b>The file is still a UPR ZX preset.</b> UPR writes a version, a length and that many bytes of settings
/// (<c>Settings.write</c>) and reads exactly that back (<c>Settings.read</c>), so anything after it is never looked at:
/// the same file opens in UPR ZX, and a plain preset from UPR opens here too, simply without this part. The block is a
/// magic, a big-endian length and JSON, so it can grow without breaking either side.
/// </para>
/// <para>
/// What a profile or a sweep <i>wrote</i> into the tables is not here: those are the project's edits and travel in the
/// data files. The difficulty travels as its name and is applied again on import, which writes the same edits.
/// </para>
/// </remarks>
public sealed class FullPreset
{
    /// <summary>What marks the block after the UPR ZX part.</summary>
    public static ReadOnlySpan<byte> Magic => "PKMANAGER-OPTIONS\0"u8;

    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;

    /// <summary>Levels, level cap, shiny odds, difficulty (as its name), original starters.</summary>
    public GameSettings Tweaks { get; init; } = new();

    /// <summary>Free Rare Candies and the Mega Stones on sale.</summary>
    public ShopSettings Shops { get; init; } = new();

    /// <summary>The two item options of the randomization that are ours: every item, and the Mega Stones in the pool.</summary>
    public bool AllowAllItems { get; init; }

    public bool MegaStonesInPool { get; init; }

    /// <summary>
    /// The ROM mods that are on, by id. Emulator plugins are not here: they are installed on this computer, not part of
    /// how the run is set up.
    /// </summary>
    public List<string> Mods { get; init; } = [];

    public static FullPreset FromProject(Project project) => new()
    {
        Tweaks = project.Tweaks.Clone(),
        Shops = project.Shops.Clone(),
        AllowAllItems = project.Randomization.AllowAllItems,
        MegaStonesInPool = project.Randomization.MegaStonesInPool,
        Mods = [.. project.Mods.EnabledRomMods],
    };

    /// <summary>
    /// The UPR ZX preset followed by this block. <paramref name="uprPreset"/> is trimmed to its own length first, so
    /// exporting a file that was imported with a block does not stack two.
    /// </summary>
    public static byte[] Write(byte[] uprPreset, Project project)
    {
        byte[] upr = UprPart(uprPreset);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(FromProject(project), Options);
        var file = new byte[upr.Length + Magic.Length + 4 + json.Length];
        upr.CopyTo(file, 0);
        Magic.CopyTo(file.AsSpan(upr.Length));
        BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(upr.Length + Magic.Length), json.Length);
        json.CopyTo(file, upr.Length + Magic.Length + 4);
        return file;
    }

    /// <summary>
    /// The UPR ZX part of a preset file and, when the file has one, the block after it. A plain UPR preset gives
    /// itself back and null.
    /// </summary>
    /// <exception cref="InvalidDataException">The block is there but cannot be read.</exception>
    public static (byte[] Upr, FullPreset? Options) Read(byte[] file)
    {
        byte[] upr = UprPart(file);
        var rest = file.AsSpan(upr.Length);
        if (rest.Length < Magic.Length + 4 || !rest[..Magic.Length].SequenceEqual(Magic))
            return (upr, null);
        int length = BinaryPrimitives.ReadInt32BigEndian(rest[Magic.Length..]);
        if (length < 0 || length > rest.Length - Magic.Length - 4)
            throw new InvalidDataException(Resources.Strings.Preset_OptionsBroken);
        try
        {
            var options = JsonSerializer.Deserialize<FullPreset>(rest.Slice(Magic.Length + 4, length), Options)
                          ?? throw new InvalidDataException(Resources.Strings.Preset_OptionsBroken);
            if (options.Format > CurrentFormat)
                throw new InvalidDataException(string.Format(Resources.Strings.Data_NewerFormat, options.Format, CurrentFormat));
            return (upr, options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Resources.Strings.Preset_OptionsBroken, ex);
        }
    }

    /// <summary>
    /// The application's options of the file into the project, all but the mods (<see cref="ModsFor"/>: turning one on
    /// reopens the game data, which is the caller's to do). What describes the ROM on disk (the installed level cap)
    /// stays the project's: the file is how to play, not what was built.
    /// </summary>
    public void ApplyTo(Project project)
    {
        int? installedCap = project.Tweaks.InstalledLevelCap;
        project.Tweaks = Tweaks.Clone();
        project.Tweaks.InstalledLevelCap = installedCap;
        project.Shops = Shops.Clone();
        project.Randomization.AllowAllItems = AllowAllItems;
        project.Randomization.MegaStonesInPool = MegaStonesInPool;
    }

    /// <summary>
    /// The ROM mods of the file that this version knows for <paramref name="game"/>, and the ones it does not (a newer
    /// version's, another game's), which are left off.
    /// </summary>
    public (IReadOnlyList<ModDefinition> Known, IReadOnlyList<string> Unknown) ModsFor(GameTitle game)
    {
        var known = new List<ModDefinition>();
        var unknown = new List<string>();
        foreach (string id in Mods.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ModCatalog.Find(id) is { } mod && mod.Supports(game) && mod.ChangesTheRom)
                known.Add(mod);
            else
                unknown.Add(id);
        }
        return (known, unknown);
    }

    /// <summary>The bytes UPR ZX itself reads: version, length and that many bytes. The whole file when it is shorter.</summary>
    public static byte[] UprPart(byte[] file)
    {
        if (file.Length < 8)
            return file;
        int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(4));
        return length >= 0 && 8L + length <= file.Length ? file[..(8 + length)] : file;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
