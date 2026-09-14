using pk3DS.Core.CTR;
using Pokemanager.Model.Data;
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

        return outputs;
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
