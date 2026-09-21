using Pokemanager.App.Resources;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;
using Pokemanager.Save;

namespace Pokemanager.App.Services;

/// <summary>Badge roulette prizes: how they read and how they get into the game.</summary>
public static class LockeRewards
{
    /// <summary>Name of a milestone of the game (badge or trial), or its number when out of range.</summary>
    public static string MilestoneName(GameTitle game, int index) =>
        game.Milestones() is var m && index >= 0 && index < m.Count ? m[index].Name : $"#{index + 1}";

    public static string Describe(LockePrize prize, IReadOnlyList<string> itemNames) => prize.Kind switch
    {
        LockePrizeKind.Item => string.Format(Strings.Locke_PrizeItem,
            prize.ItemId > 0 && prize.ItemId < itemNames.Count ? itemNames[prize.ItemId] : $"#{prize.ItemId}", prize.Amount),
        LockePrizeKind.Money => string.Format(Strings.Locke_PrizeMoney, prize.Amount),
        LockePrizeKind.Life => Strings.Locke_PrizeLife,
        LockePrizeKind.Text => string.IsNullOrWhiteSpace(prize.Text) ? Strings.Locke_PrizeTextEmpty : prize.Text,
        _ => Strings.Locke_PrizeNothing,
    };

    /// <summary>The prize's picture: the item's icon, a Nugget for money; lives and bad luck have <see cref="Glyph"/> instead.</summary>
    public static Avalonia.Media.Imaging.Bitmap? Icon(LockePrize prize) => prize.Kind switch
    {
        LockePrizeKind.Item => PkhexImages.Item(prize.ItemId),
        LockePrizeKind.Money => PkhexImages.Item(92),
        _ => null,
    };

    /// <summary>A heart for a life, a cross for nothing; null when the prize has an icon.</summary>
    public static string? Glyph(LockePrize prize) => prize.Kind switch
    {
        LockePrizeKind.Life => "♥",
        LockePrizeKind.Nothing => "✕",
        LockePrizeKind.Text => "✎",
        _ => null,
    };

    /// <summary>
    /// The player put the prize in the game by hand (a free-text prize, or an item they added themselves): it is marked as
    /// given without touching the save.
    /// </summary>
    public static void MarkAddedByHand(LoadedProject loaded, int badge)
    {
        loaded.Project.Locke.MarkClaimed(badge);
        loaded.Project.Save(loaded.Path);
    }

    /// <summary>
    /// Forgets this milestone's spin, so its roulette can be rolled again — the same as "Allow again" in the Locke tab.
    /// What a claimed prize already put in the save stays there: nothing is taken back.
    /// </summary>
    public static void Forget(LoadedProject loaded, int badge)
    {
        loaded.Project.Locke.ForgetSpin(badge);
        loaded.Project.Save(loaded.Path);
    }

    /// <summary>Records what the roulette gave (lives and bad luck apply at once) and saves the project.</summary>
    public static void RecordWin(LoadedProject loaded, int badge, LockePrize prize)
    {
        loaded.Project.Locke.RecordWin(badge, prize, DateTime.Now);
        loaded.Project.Save(loaded.Path);
    }

    /// <summary>
    /// Puts an item or money prize into the emulator save — with the same safety as the save editor: emulator closed, a
    /// history version and a backup before writing, verification after — and marks the badge as claimed. Returns the
    /// message to show; expected problems do not throw.
    /// </summary>
    public static (bool Ok, string Message) Claim(LoadedProject loaded, AppSettings settings, int badge, LockePrize prize, IReadOnlyList<string> itemNames)
    {
        var project = loaded.Project;
        string label = Describe(prize, itemNames);
        try
        {
            if (prize.Kind is LockePrizeKind.Item or LockePrizeKind.Money)
            {
                if (EmulatorUserFolders.RunningEmulators(settings.EffectiveEmulatorName) is { Count: > 0 } running)
                    return (false, string.Format(Strings.Status_CloseEmulator, string.Join(", ", running)));
                if (settings.EffectiveEmulatorDirectory is not { } dir
                    || EmulatorUserFolders.SaveFile(dir, loaded.Dump.Title.TitleId()) is not { } savePath || !File.Exists(savePath))
                    return (false, Strings.Rnd_NoEmulator);

                var doc = SaveDocument.Open(savePath, loaded.Session.Current);
                if (prize.Kind == LockePrizeKind.Item)
                {
                    if (doc.GiveItem(prize.ItemId, prize.Amount) == 0)
                        return (false, string.Format(Strings.Locke_BagFull, label));
                }
                else
                {
                    doc.Money = (uint)Math.Min((long)doc.Money + prize.Amount, SaveDocument.MaxMoney);
                }

                var history = new ProjectHistory(loaded.Path);
                history.Record(project, VersionKind.BeforeSaveEdit, note: string.Format(Strings.Locke_HistoryNote, label),
                    romPath: project.Randomization.LastBuiltRom, savePath: savePath);
                history.Prune();
                string backups = Path.Combine(AppSettings.BackupRoot, settings.EffectiveEmulatorName);
                doc.Write(backups);
                SaveUpdater.PruneBackups(backups, keep: 10);
            }

            project.Locke.MarkClaimed(badge);
            project.Save(loaded.Path);
            return (true, prize.Kind switch
            {
                LockePrizeKind.Item or LockePrizeKind.Money => string.Format(Strings.Locke_ClaimedSave, label),
                LockePrizeKind.Life => Strings.Locke_ClaimedLife,
                _ => Strings.Locke_ClaimedNothing,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            return (false, string.Format(Strings.Save_WriteFailed, ex.Message));
        }
    }
}
