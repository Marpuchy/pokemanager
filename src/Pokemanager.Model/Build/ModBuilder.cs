using pk3DS.Core.CTR;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.Model.Build;

/// <summary>Computes the GARCs changed by the manual edits.</summary>
/// <remarks>
/// Only GARCs of tables with edits are produced. Inside each GARC only edited entries change; everything else keeps
/// the bytes of the base (dump or randomized dump).
/// </remarks>
public static class ModBuilder
{
    /// <returns>Relative path inside the title (<c>romfs/a/2/1/8</c>) → contents.</returns>
    public static IReadOnlyDictionary<string, byte[]> BuildEdits(EditorSession session)
    {
        var edits = session.Project.Edits;
        var layout = session.Current.Layout;
        var outputs = new Dictionary<string, byte[]>();

        if (EditedIds(edits, GameTables.Personal) is { Count: > 0 } personalIds)
            outputs["romfs/" + layout.Personal] = BuildPersonal(session, personalIds);
        if (EditedIds(edits, GameTables.Moves) is { Count: > 0 } moveIds)
            outputs["romfs/" + layout.Moves] = BuildMoves(session, moveIds);
        if (EditedIds(edits, GameTables.Learnsets) is { Count: > 0 } learnsetIds)
            outputs["romfs/" + layout.LevelUp] = BuildGarc(session, layout.LevelUp, learnsetIds, id => session.Current.Learnsets[id].Write());
        // Trainers: the record and the team live in two archives, both written when either changes.
        if (EditedIds(edits, GameTables.Trainers) is { Count: > 0 } trainerIds)
        {
            outputs["romfs/" + layout.TrainerData] =
                BuildGarc(session, layout.TrainerData, trainerIds, id => session.Current.Trainers[id].WriteData());
            outputs["romfs/" + layout.TrainerPokemon] =
                BuildGarc(session, layout.TrainerPokemon, trainerIds, id => session.Current.Trainers[id].WriteTeam());
        }
        if (EditedIds(edits, GameTables.MoveTexts) is { Count: > 0 } descriptionIds)
        {
            foreach (var (path, data) in BuildMoveDescriptions(session, descriptionIds))
                outputs["romfs/" + path] = data;
        }

        return outputs;
    }

    /// <summary>
    /// Edited move descriptions go to the text archive of every language the game has, so the game shows them in whatever
    /// language it runs; the other descriptions of each language are left as they are.
    /// </summary>
    private static IEnumerable<(string Path, byte[] Data)> BuildMoveDescriptions(EditorSession session, HashSet<int> ids)
    {
        var title = session.Current.Title;
        int file = GameText.MoveDescriptionFile(title);
        for (int language = 0; language < title.Layout().LanguageCount; language++)
        {
            string path = GameText.Archive(title, language);
            GARC.MemGARC garc;
            string[] lines;
            try
            {
                garc = ReadGarc(session, path);
                lines = GameText.Read(title, garc.Files[file]);
            }
            catch (Exception ex) when (ex is FileNotFoundException or IndexOutOfRangeException or InvalidDataException)
            {
                continue; // a language the dump does not have
            }
            foreach (int id in ids.Where(id => id < lines.Length))
                lines[id] = GameText.FromEditable(session.Current.MoveDescriptions[id]);
            byte[][] files = garc.Files;
            files[file] = GameText.Write(title, lines);
            yield return (path, GARC.PackGARC(files, garc.Version, garc.ContentPadding).Data);
        }
    }

    private static HashSet<int> EditedIds(EditSet edits, string table) =>
        edits.All.Where(e => e.Table == table).Select(e => e.Id).ToHashSet();

    /// <summary>Personal: edited individual entries plus the concatenated table in the last file.</summary>
    private static byte[] BuildPersonal(EditorSession session, HashSet<int> ids)
    {
        string path = session.Current.Layout.Personal;
        var garc = ReadGarc(session, path);
        byte[][] files = garc.Files;
        foreach (int id in ids)
            files[id] = session.Current.Personal[id].Write().ToArray();
        files[^1] = files[..^1].SelectMany(f => f).ToArray();
        return GARC.PackGARC(files, garc.Version, garc.ContentPadding).Data;
    }

    /// <summary>Moves: edited files, or the edited entries of the "WD" mini archive repacked into its single file.</summary>
    private static byte[] BuildMoves(EditorSession session, HashSet<int> ids)
    {
        var layout = session.Current.Layout;
        if (!layout.MovesPacked)
            return BuildGarc(session, layout.Moves, ids, id => session.Current.Moves[id].Write());

        var garc = ReadGarc(session, layout.Moves);
        byte[][] files = garc.Files;
        byte[][] moves = Mini.UnpackMini(files[0], "WD");
        foreach (int id in ids)
            moves[id] = session.Current.Moves[id].Write().ToArray();
        files[0] = Mini.PackMini(moves, "WD");
        return GARC.PackGARC(files, garc.Version, garc.ContentPadding).Data;
    }

    private static byte[] BuildGarc(EditorSession session, string path, HashSet<int> ids, Func<int, byte[]> write)
    {
        var garc = ReadGarc(session, path);
        byte[][] files = garc.Files;
        foreach (int id in ids)
            files[id] = write(id).ToArray();
        return GARC.PackGARC(files, garc.Version, garc.ContentPadding).Data;
    }

    private static GARC.MemGARC ReadGarc(EditorSession session, string path) =>
        new(File.ReadAllBytes(session.Layers.Resolve(path)));
}
