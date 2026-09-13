using pk3DS.Core.CTR;
using Pokemanager.Model.Data;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.Model.Build;

/// <summary>Calcula los GARC que cambian por las ediciones manuales.</summary>
/// <remarks>
/// Solo se generan los GARC de tablas con ediciones. Dentro de cada GARC solo cambian las entradas
/// editadas; el resto conserva los bytes de la base (volcado o volcado randomizado).
/// </remarks>
public static class ModBuilder
{
    /// <returns>Ruta relativa dentro del mod (<c>romfs/a/2/1/8</c>) → contenido.</returns>
    public static IReadOnlyDictionary<string, byte[]> BuildEdits(EditorSession session)
    {
        var edits = session.Project.Edits;
        var outputs = new Dictionary<string, byte[]>();

        if (EditedIds(edits, GameTables.Personal) is { Count: > 0 } personalIds)
            outputs["romfs/" + GameData.PersonalGarc] = BuildPersonal(session, personalIds);
        if (EditedIds(edits, GameTables.Moves) is { Count: > 0 } moveIds)
            outputs["romfs/" + GameData.MoveGarc] = BuildGarc(session, GameData.MoveGarc, moveIds, id => session.Current.Moves[id].Write());
        if (EditedIds(edits, GameTables.Learnsets) is { Count: > 0 } learnsetIds)
            outputs["romfs/" + GameData.LevelUpGarc] = BuildGarc(session, GameData.LevelUpGarc, learnsetIds, id => session.Current.Learnsets[id].Write());

        return outputs;
    }

    private static HashSet<int> EditedIds(EditSet edits, string table) =>
        edits.All.Where(e => e.Table == table).Select(e => e.Id).ToHashSet();

    /// <summary>Personal: entradas individuales editadas y la tabla concatenada del último archivo.</summary>
    private static byte[] BuildPersonal(EditorSession session, HashSet<int> ids)
    {
        byte[][] files = GameData.ReadFiles(session.Layers, GameData.PersonalGarc);
        foreach (int id in ids)
            files[id] = session.Current.Personal[id].Write().ToArray();
        files[^1] = files[..^1].SelectMany(f => f).ToArray();
        return GARC.PackGARC(files, GARC.VER_4, 4).Data;
    }

    private static byte[] BuildGarc(EditorSession session, string garc, HashSet<int> ids, Func<int, byte[]> write)
    {
        byte[][] files = GameData.ReadFiles(session.Layers, garc);
        foreach (int id in ids)
            files[id] = write(id).ToArray();
        return GARC.PackGARC(files, GARC.VER_4, 4).Data;
    }
}
