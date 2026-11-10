using Pokemanager.Model.Data;
using Pokemanager.Model.Editing;

namespace Pokemanager.Model.Build;

/// <summary>
/// The trainers as the build writes them: the session's (randomization and edits), then what <see cref="ModBuilder"/>
/// does on top — the trainer level percentage and <see cref="BattleSafety"/> (megas, Z-crystals, battle-only forms).
/// Issue #2: the advanced editor showed the teams before those, not as they come out in the ROM.
/// </summary>
public static class TrainersAsBuilt
{
    /// <returns>One per trainer of the session; an unreadable record comes back unreadable.</returns>
    public static IReadOnlyList<Trainer> Build(EditorSession session)
    {
        var data = session.Current;
        var title = data.Title;
        var trainers = data.Trainers;
        byte[][] records = [.. trainers.Select(t => t.WriteData().ToArray())];
        byte[][] teams = [.. trainers.Select(t => t.WriteTeam().ToArray())];
        ModBuilder.ScaleTrainerLevels(session, title, records, teams);
        BattleSafety.FixTrainers(data, title, records, teams, session.Project.Tweaks.TrainerItems);
        return [.. records.Select((r, i) => Trainer.Read(title, r, teams[i]))];
    }
}
