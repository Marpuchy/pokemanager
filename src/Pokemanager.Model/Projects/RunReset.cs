using Pokemanager.Model.Data;
using Pokemanager.Model.Editing;

namespace Pokemanager.Model.Projects;

/// <summary>
/// What a "start the game over" also puts back, beyond the run's progress. Every one of them is a rule of the ROM, so
/// every one of them needs the ROM built again: they are offered unticked, and nothing here happens unless it is asked
/// for. The run's own progress is not in this list because it is never a question (see <see cref="RunReset"/>).
/// </summary>
/// <param name="TableEdits">Every hand edit of the six game tables (personal, move, learnset, movetext, trainer, item).</param>
/// <param name="Randomization">
/// The randomization: whether it is on, its seed and its preset. What describes the file on disk — the ROM that was
/// built and the seed it was built with — is kept, because a reset builds nothing.
/// </param>
/// <param name="Shops">What the shops sell beyond what the game does (<see cref="ShopSettings"/>).</param>
/// <param name="GameRules">
/// The game options of <see cref="GameSettings"/> that are the player's choice: the shiny patch, the three level
/// percentages, the difficulty profile's label and the cap levels the player wrote over the plan's. Note that what a
/// difficulty profile actually did lives in the trainer edits, so clearing its label without
/// <paramref name="TableEdits"/> leaves the trainers as the profile made them.
/// </param>
public sealed record RunResetOptions(bool TableEdits = false, bool Randomization = false, bool Shops = false,
    bool GameRules = false);

/// <summary>What a reset put back, so the application can say how much it was instead of leaving the player guessing.</summary>
/// <param name="LivesLost">Lost lives that came back.</param>
/// <param name="Spins">Roulette spins forgotten, so those milestones can be spun again.</param>
/// <param name="LevelCapStep">The cap went back to the first step of the plan.</param>
/// <param name="TableEdits">Hand edits of the game tables undone.</param>
/// <param name="Randomization">The randomization was forgotten.</param>
/// <param name="Shops">The shops went back to the game's.</param>
/// <param name="GameRules">Game options put back (one per value that was not already the game's).</param>
public sealed record RunResetReport(int LivesLost, int Spins, bool LevelCapStep, int TableEdits, bool Randomization,
    bool Shops, int GameRules)
{
    /// <summary>How many things were put back, counting a life bar, a spin, an edit and an option each as one.</summary>
    public int Count => (LivesLost > 0 ? 1 : 0) + Spins + (LevelCapStep ? 1 : 0) + TableEdits
                        + (Randomization ? 1 : 0) + (Shops ? 1 : 0) + GameRules;

    /// <summary>Whether the built ROM no longer matches the project, so the "build the ROM" notice has to appear.</summary>
    public bool AffectsRom => LevelCapStep || TableEdits > 0 || Randomization || Shops || GameRules > 0;
}

/// <summary>
/// Starting the adventure over, on the project's side. Moving the save away is what makes the game begin again; this is
/// what the application has to forget so the new adventure is actually new, and it separates three kinds of thing:
/// <list type="bullet">
/// <item><description>
/// <b>Progress of the run</b> — the lost lives, the spent roulette spins and which step of the level cap plan the
/// player is on. A new adventure has none of it, so it always goes back and nothing is asked.
/// </description></item>
/// <item><description>
/// <b>Rules of the run</b> — the maximum lives, the prizes, the roulette interval, whether the cap follows the
/// milestones and whether levels past it are brought back. These are how the player wants to play, not how far they
/// got: never touched.
/// </description></item>
/// <item><description>
/// <b>Rules of the ROM</b> — the edits, the randomization, the shops and the game options. They are in the built file,
/// so putting them back means building again: each one is a question (<see cref="RunResetOptions"/>), unticked.
/// </description></item>
/// </list>
/// </summary>
public static class RunReset
{
    /// <summary>
    /// Puts back the run's progress, and whatever of the ROM's rules <paramref name="options"/> asks for. The edits go
    /// through the session, so the views follow them as they do for any other change; everything else is written into
    /// the project's settings objects in place, because the views hold those instances.
    /// </summary>
    public static RunResetReport Apply(EditorSession session, RunResetOptions options)
    {
        var project = session.Project;

        // The ROM's rules go first: the plan's levels are computed from the trainer levels with the level percentage
        // applied and with the player's own cap levels over them, so the step the cap goes back to can only be read
        // once those are whatever they are going to be.
        int tableEdits = 0;
        if (options.TableEdits)
        {
            int before = project.Edits.Count;
            session.RevertAll();
            tableEdits = before - project.Edits.Count;
        }
        bool randomization = options.Randomization && ResetRandomization(project.Randomization);
        bool shops = options.Shops && ResetShops(project.Shops);
        int gameRules = options.GameRules ? ResetGameRules(project.Tweaks) : 0;

        // The run's progress: always, and without asking.
        int livesLost = project.Locke.LivesLost;
        project.Locke.LivesLost = 0;
        int spins = project.Locke.Spins.Count;
        project.Locke.Spins.Clear();
        bool levelCapStep = ResetLevelCapStep(session);

        return new RunResetReport(livesLost, spins, levelCapStep, tableEdits, randomization, shops, gameRules);
    }

    /// <summary>
    /// The cap goes back to the first step of the plan — the one a save with no badge and no crystal is on — and not to
    /// null, which would take the cap off the next ROM altogether. A cap set by hand is a rule the player chose and not
    /// progress, so without <see cref="GameSettings.LevelCapByMilestones"/> it is left alone.
    /// </summary>
    /// <remarks>
    /// <see cref="GameSettings.InstalledLevelCap"/> is deliberately untouched: it says what the ROM that is on disk
    /// has, the same way the installed seed does, and a reset builds no ROM. <c>SaveUpdater</c> trims experience
    /// against it, so blanking it would make the application believe the played ROM had no cap.
    /// </remarks>
    private static bool ResetLevelCapStep(EditorSession session)
    {
        var tweaks = session.Project.Tweaks;
        if (!tweaks.LevelCapByMilestones || LevelCaps.Plan(session) is not { Count: > 0 } plan)
            return false;
        if (tweaks.LevelCap == plan[0].Level)
            return false;
        tweaks.LevelCap = plan[0].Level;
        return true;
    }

    /// <summary>
    /// Forgets the randomization: off, no seed, no preset. <see cref="RandomizationSettings.LastBuiltRom"/> and
    /// <see cref="RandomizationSettings.InstalledSeed"/> describe the file that exists on disk, so they are kept, as
    /// they are through an undo; so are the build preferences (the output's name, replacing the previous ROM, adapting
    /// the save), which are not part of a run.
    /// </summary>
    private static bool ResetRandomization(RandomizationSettings r)
    {
        if (!r.Enabled && r.Seed == 0 && r.Preset is null && r.PresetName is null && !r.PresetModified
            && !r.AllowAllItems && !r.MegaStonesInPool)
            return false;
        r.Enabled = false;
        r.Seed = 0;
        r.PresetName = null;
        r.Preset = null;
        r.PresetModified = false;
        // Both only widen the pool the item randomization draws from, so they go with it.
        r.AllowAllItems = false;
        r.MegaStonesInPool = false;
        return true;
    }

    private static bool ResetShops(ShopSettings shops)
    {
        if (shops.IsEmpty)
            return false;
        shops.FreeRareCandies = false;
        shops.MegaStonesOnSale = false;
        return true;
    }

    /// <summary>
    /// The game options that are the player's choice. <see cref="GameSettings.LevelCapByMilestones"/>,
    /// <see cref="GameSettings.TrimOverCap"/> and <see cref="GameSettings.InstalledLevelCap"/> are not among them: the
    /// first two are how the cap works and the third is a fact of the build.
    /// </summary>
    private static int ResetGameRules(GameSettings tweaks)
    {
        int changed = 0;
        if (tweaks.AlwaysShiny)
        {
            tweaks.AlwaysShiny = false;
            changed++;
        }
        if (tweaks.TrainerLevelPercent != 0)
        {
            tweaks.TrainerLevelPercent = 0;
            changed++;
        }
        if (tweaks.WildLevelPercent != 0)
        {
            tweaks.WildLevelPercent = 0;
            changed++;
        }
        if (tweaks.StaticLevelPercent != 0)
        {
            tweaks.StaticLevelPercent = 0;
            changed++;
        }
        if (tweaks.Difficulty is not null)
        {
            tweaks.Difficulty = null;
            changed++;
        }
        if (tweaks.LevelCapOverrides.Count > 0)
        {
            changed += tweaks.LevelCapOverrides.Count;
            tweaks.LevelCapOverrides.Clear();
        }
        return changed;
    }
}
