using Pokemanager.App.Resources;
using Pokemanager.Bridge;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;
using Pokemanager.Save;

namespace Pokemanager.App.Services;

/// <summary>
/// Brings back to the cap of the built ROM whatever got past it in game (a Rare Candy ignores the cap), for one
/// project. The application runs it whenever the emulator closes and when a project is shown with the emulator closed,
/// whichever page is open (2026-11-04, user: a Pokémon candied to 25 over a cap of 20 stayed there — the watch lived in
/// the editor, and the game had been played from the start page).
/// </summary>
public static class LevelCapGuard
{
    /// <summary>What a pass did: how many Pokémon came back, to what cap, or why it wrote nothing.</summary>
    public sealed record Outcome(int Trimmed, int Cap, string? Message, bool Error);

    /// <summary>
    /// Trims the save of <paramref name="project"/> when its cap asks for it. Same safety as every other write: the
    /// emulator closed, a <see cref="VersionKind.BeforeSaveEdit"/> version and a backup first, verification after.
    /// Writes nothing (and says nothing) when nothing is over the cap.
    /// </summary>
    public static Outcome? Run(string projectPath, Project project, GameData rom, GameTitle title, AppSettings settings)
    {
        var tweaks = project.Tweaks;
        if (!tweaks.TrimsOverCap || tweaks.InstalledLevelCap is not { } cap)
            return null;
        if (settings.EffectiveEmulatorDirectory is not { } dir
            || EmulatorUserFolders.SaveFile(dir, title.TitleId()) is not { } savePath || !File.Exists(savePath))
            return null;
        try
        {
            if (EmulatorUserFolders.RunningEmulators(settings.EffectiveEmulatorName).Count > 0)
                return null; // the game holds the save: the pass runs when it closes
            var preview = SaveUpdater.TrimOverCap(savePath, rom, cap, backupRoot: null);
            if (preview.Changes.Count == 0)
                return null;

            var history = new ProjectHistory(projectPath);
            history.Record(project, VersionKind.BeforeSaveEdit, note: string.Format(Strings.LevelCap_HistoryNote, cap),
                romPath: project.Randomization.LastBuiltRom, savePath: savePath);
            history.Prune();
            string backups = Path.Combine(AppSettings.BackupRoot, settings.EffectiveEmulatorName);
            var done = SaveUpdater.TrimOverCap(savePath, rom, cap, backups);
            SaveUpdater.PruneBackups(backups, keep: 10);
            return new Outcome(done.Changes.Count, cap, string.Format(Strings.LevelCap_Trimmed, done.Changes.Count, cap), false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            return new Outcome(0, cap, string.Format(Strings.Save_WriteFailed, ex.Message), true);
        }
    }
}
