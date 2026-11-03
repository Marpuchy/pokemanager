using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Mods;

/// <summary>
/// What the emulator's plugin folder holds for one plugin mod: whether the file is there, and what else is there that
/// would stop it from loading.
/// </summary>
/// <param name="Directory">
/// The folder itself: <c>&lt;emulator user folder&gt;/sdmc/luma/plugins/&lt;TitleID&gt;</c>. Never worked out here —
/// see the remarks on <see cref="ModPluginInstaller"/>.
/// </param>
/// <param name="FileName">The plugin's own file name, as the author ships it (<c>Gen7FieldFollower.3gx</c>).</param>
/// <param name="Path">The installed file, or null when it is not installed.</param>
/// <param name="Bytes">Its size, or 0 when it is not installed.</param>
/// <param name="Sha256">
/// The installed file's hash, upper-case hex, or null when it is not installed. Measured, not promised: the catalogue
/// has no hash for this mod's downloads, so this is the only record of what is actually in the folder.
/// </param>
/// <param name="OtherPlugins">
/// Other <c>.3gx</c> files in the same folder. <b>The emulator loads one plugin per title</b>, so any of these means
/// this one will not load — and the honest thing is to say which files, measured in the folder, instead of warning in
/// the abstract.
/// </param>
public sealed record PluginInstallation(
    string Directory,
    string FileName,
    string? Path,
    long Bytes,
    string? Sha256,
    IReadOnlyList<string> OtherPlugins)
{
    public bool Installed => Path is not null;

    /// <summary>Another plugin is in the folder, so this one will not be the one the emulator loads.</summary>
    public bool HasConflict => OtherPlugins.Count > 0;
}

/// <summary>
/// Installs and removes a <see cref="ModKind.EmulatorPlugin"/> mod: one file copied into the emulator's own
/// <c>sdmc/luma/plugins/&lt;TitleID&gt;</c> folder, and taken back out again.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing of the ROM is involved.</b> A 3GX plugin has no romfs, no exefs and no <c>code.bin</c>: there is nothing
/// to pack, nothing to randomize and nothing to build. Installing it is a file copy, and the only thing that makes it
/// take effect is the emulator's own 3GX plugin loader, which Pokemanager cannot switch on — see
/// <see cref="ModDefinition.SetupNote"/>.
/// </para>
/// <para>
/// <b>The destination folder is a parameter and is never worked out here.</b> Finding the emulator's user folder is
/// <c>Pokemanager.Bridge</c>'s job (<c>EmulatorUserFolders.PluginDirectory</c>) and this assembly does not reference
/// it; the caller — the application — resolves it and hands it over, exactly as it already does for
/// <c>Build.ModInstaller</c>'s LayeredFS folder. The same goes for which emulators are running: knowing that means
/// looking at processes, so the caller looks and passes the answer in.
/// </para>
/// <para>
/// <b>It refuses to write while the emulator is open</b>, like everything else here that touches the emulator's files
/// (the save writer, the level-cap pass, starting a run over): the emulator reads its plugin folder when it loads a
/// title, and writing under a running one is at best ignored and at worst half read.
/// </para>
/// </remarks>
public static class ModPluginInstaller
{
    /// <summary>The extension of a 3GX plugin, which is also how another one in the folder is recognised.</summary>
    public const string Extension = ".3gx";

    /// <summary>
    /// What that folder holds for this mod right now. Reads and hashes; writes nothing, and a folder that does not
    /// exist is simply "not installed" rather than an error.
    /// </summary>
    public static PluginInstallation Inspect(string pluginDirectory, ModDefinition mod)
    {
        string directory = Path.GetFullPath(pluginDirectory);
        string name = FileNameOf(mod);
        string target = Path.Combine(directory, name);

        var others = new List<string>();
        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*" + Extension, SearchOption.TopDirectoryOnly))
            {
                string leaf = Path.GetFileName(file);
                if (!string.Equals(leaf, name, StringComparison.OrdinalIgnoreCase))
                    others.Add(leaf);
            }
            others.Sort(StringComparer.OrdinalIgnoreCase);
        }

        if (!File.Exists(target))
            return new PluginInstallation(directory, name, null, 0, null, others);
        return new PluginInstallation(directory, name, target, new FileInfo(target).Length, ModLibrary.Hash(target), others);
    }

    /// <summary>
    /// Copies the plugin from the user's own library into the emulator's folder, through a <c>.partial</c> that is
    /// renamed once it is whole — the same rule every other write here follows, so a copy that is interrupted is
    /// never read as a plugin.
    /// </summary>
    /// <param name="pluginDirectory"><c>&lt;emulator user folder&gt;/sdmc/luma/plugins/&lt;TitleID&gt;</c>.</param>
    /// <param name="mod">The plugin mod. Anything else is refused: there is no file to copy.</param>
    /// <param name="library">Where the user's own copy of the plugin is.</param>
    /// <param name="runningEmulators">
    /// The emulators the caller found running. <b>Not empty means nothing is written</b> and the message names them.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// An emulator is open, the mod is not a plugin mod, or the library has not got the plugin yet. The message says
    /// which, so the interface can show it unchanged.
    /// </exception>
    public static PluginInstallation Install(
        string pluginDirectory,
        ModDefinition mod,
        ModLibrary library,
        IReadOnlyList<string>? runningEmulators = null)
    {
        var plugin = Required(mod);
        Refuse(runningEmulators);

        string source = library.PathFor(mod.Id, plugin.ServedPath);
        if (!File.Exists(source))
            throw new InvalidOperationException(string.Format(Strings.Mods_Plugin_NotInLibrary, mod.Name, source));

        string directory = Path.GetFullPath(pluginDirectory);
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, plugin.Path);
        string partial = target + ".partial";
        File.Copy(source, partial, overwrite: true);
        File.Move(partial, target, overwrite: true);

        return Inspect(directory, mod);
    }

    /// <summary>
    /// Takes the plugin back out of the emulator's folder, and nothing else: another <c>.3gx</c> that was already
    /// there is somebody else's and is left alone. An empty folder left behind is removed.
    /// </summary>
    /// <returns>What the folder holds afterwards, so the caller can say it is gone.</returns>
    /// <exception cref="InvalidOperationException">An emulator is open, or the mod is not a plugin mod.</exception>
    public static PluginInstallation Uninstall(
        string pluginDirectory,
        ModDefinition mod,
        IReadOnlyList<string>? runningEmulators = null)
    {
        var plugin = Required(mod);
        Refuse(runningEmulators);

        string directory = Path.GetFullPath(pluginDirectory);
        string target = Path.Combine(directory, plugin.Path);
        if (File.Exists(target))
            File.Delete(target);

        // A folder this put the one file in is not worth keeping once it is empty; one with anything else in it is.
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty folder left behind is harmless; what mattered was removing the file.
        }

        return Inspect(directory, mod);
    }

    /// <summary>The plugin's own file name, as the author ships it: <c>Gen7FieldFollower.3gx</c>.</summary>
    /// <exception cref="InvalidOperationException">The mod is not a plugin mod, so it has no plugin file.</exception>
    public static string FileNameOf(ModDefinition mod) => Required(mod).Path;

    /// <summary>The plugin file of a plugin mod, or a refusal naming the mod: no other kind has one to copy.</summary>
    private static ModFile Required(ModDefinition mod) =>
        mod.IsPlugin && mod.PluginFile is { } plugin
            ? plugin
            : throw new InvalidOperationException(string.Format(Strings.Mods_Plugin_NotAPluginMod, mod.Name));

    private static void Refuse(IReadOnlyList<string>? runningEmulators)
    {
        if (runningEmulators is { Count: > 0 })
            throw new InvalidOperationException(
                string.Format(Strings.Mods_Plugin_EmulatorOpen, string.Join(", ", runningEmulators)));
    }
}
