using System.Text.Json.Serialization;
using Pokemanager.Model.Mods;

namespace Pokemanager.Model.Projects;

/// <summary>
/// The community mods this project plays with, by id (see <c>Mods.ModCatalog</c>). Part of the project file, like the
/// shops and the game options: the seed does not touch them, and turning one on or off needs a ROM build and not a new
/// roll.
/// </summary>
/// <remarks>
/// Only ids travel. The files themselves live in the user's own library (<c>Mods.ModLibrary</c>), so a shared project
/// still weighs kilobytes and still contains no byte of the game or of anybody's mod.
/// </remarks>
public sealed class ModSettings
{
    /// <summary>
    /// Mod ids in priority order, highest first: the first one's files win where two mods replace the same file, and
    /// they all sit over the dump. Empty — the ordinary case — means the application behaves exactly as it did before
    /// mods existed.
    /// </summary>
    public List<string> Enabled { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Enabled.Count == 0;

    /// <summary>
    /// The enabled ids that really change the ROM, in the same order. <b>This, and not <see cref="Enabled"/>, is what
    /// the read layers, the modded base ROM and the history's fingerprint are made of</b>: a plugin mod
    /// (<see cref="ModKind.EmulatorPlugin"/>) is a file the emulator loads, so it changes no byte of the game and
    /// must not become a layer, a cache key or a reason to build.
    /// </summary>
    /// <remarks>
    /// An id this version does not know counts as a ROM mod, so the build still refuses with its name instead of
    /// quietly ignoring it — the same thing it did before plugins existed.
    /// </remarks>
    [JsonIgnore]
    public IEnumerable<string> EnabledRomMods =>
        Enabled.Where(id => ModCatalog.Find(id)?.ChangesTheRom ?? true);

    /// <summary>Nothing enabled here puts a single file in the ROM, so there is no base to pack and no build to ask for.</summary>
    [JsonIgnore]
    public bool HasNoRomMods => !EnabledRomMods.Any();

    public bool Has(string modId) => Enabled.Contains(modId, StringComparer.OrdinalIgnoreCase);

    /// <summary>Turns a mod on (highest priority) or off, without ever repeating an id.</summary>
    public void Set(string modId, bool enabled)
    {
        Enabled.RemoveAll(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase));
        if (enabled)
            Enabled.Insert(0, modId);
    }

    /// <summary>
    /// Moves an enabled mod up (<paramref name="delta"/> &lt; 0, more priority) or down among the enabled ones; returns
    /// whether it moved. The first one's files win.
    /// </summary>
    public bool Move(string modId, int delta)
    {
        int at = Enabled.FindIndex(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase));
        int to = at + delta;
        if (at < 0 || to < 0 || to >= Enabled.Count || to == at)
            return false;
        string id = Enabled[at];
        Enabled.RemoveAt(at);
        Enabled.Insert(to, id);
        return true;
    }

    public ModSettings Clone() => new() { Enabled = [.. Enabled] };
}
