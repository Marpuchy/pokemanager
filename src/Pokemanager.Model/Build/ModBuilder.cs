using System.Text.Json;
using pk3DS.Core.CTR;
using Pokemanager.Model.Data;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.Model.Build;

public sealed record BuildResult(string ModDirectory, IReadOnlyList<string> Written, IReadOnlyList<string> Removed);

/// <summary>
/// Genera la carpeta de mod (overlay de romfs) a partir del volcado y las ediciones.
/// </summary>
/// <remarks>
/// Solo se escriben los GARC de tablas con ediciones. Dentro de cada GARC solo cambian las entradas
/// editadas; el resto conserva los bytes originales. Un manifiesto registra lo escrito para borrar en
/// la siguiente construcción lo que ya no toque, sin tocar archivos ajenos a la aplicación.
/// </remarks>
public static class ModBuilder
{
    public const string ManifestName = "pokemanager-build.json";

    public static BuildResult Build(EditorSession session, string modDirectory)
    {
        modDirectory = Path.GetFullPath(modDirectory);
        GuardNotInsideDump(session, modDirectory);

        var edits = session.Project.Edits;
        var outputs = new Dictionary<string, byte[]>();

        if (EditedIds(edits, GameTables.Personal) is { Count: > 0 } personalIds)
            outputs[GameData.PersonalGarc] = BuildPersonal(session, personalIds);
        if (EditedIds(edits, GameTables.Moves) is { Count: > 0 } moveIds)
            outputs[GameData.MoveGarc] = BuildGarc(session, GameData.MoveGarc, moveIds, id => session.Current.Moves[id].Write());
        if (EditedIds(edits, GameTables.Learnsets) is { Count: > 0 } learnsetIds)
            outputs[GameData.LevelUpGarc] = BuildGarc(session, GameData.LevelUpGarc, learnsetIds, id => session.Current.Learnsets[id].Write());

        var written = new List<string>();
        foreach (var (garc, bytes) in outputs)
        {
            string relative = "romfs/" + garc;
            string path = Path.Combine(modDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            written.Add(relative);
        }

        var removed = new List<string>();
        foreach (string stale in ReadManifest(modDirectory).Except(written))
        {
            string path = Path.Combine(modDirectory, stale.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                File.Delete(path);
                removed.Add(stale);
            }
        }

        WriteManifest(modDirectory, written);
        return new BuildResult(modDirectory, written, removed);
    }

    private static HashSet<int> EditedIds(EditSet edits, string table) =>
        edits.All.Where(e => e.Table == table).Select(e => e.Id).ToHashSet();

    /// <summary>Personal: entradas individuales editadas y la tabla concatenada del último archivo.</summary>
    private static byte[] BuildPersonal(EditorSession session, HashSet<int> ids)
    {
        byte[][] files = GameData.ReadFiles(session.Project.RomFsPath, GameData.PersonalGarc);
        foreach (int id in ids)
            files[id] = session.Current.Personal[id].Write().ToArray();
        files[^1] = files[..^1].SelectMany(f => f).ToArray();
        return GARC.PackGARC(files, GARC.VER_4, 4).Data;
    }

    private static byte[] BuildGarc(EditorSession session, string garc, HashSet<int> ids, Func<int, byte[]> write)
    {
        byte[][] files = GameData.ReadFiles(session.Project.RomFsPath, garc);
        foreach (int id in ids)
            files[id] = write(id).ToArray();
        return GARC.PackGARC(files, GARC.VER_4, 4).Data;
    }

    private static void GuardNotInsideDump(EditorSession session, string modDirectory)
    {
        string dump = Path.GetFullPath(session.Project.DumpDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = modDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (target.StartsWith(dump, StringComparison.OrdinalIgnoreCase) || dump.StartsWith(target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"La carpeta del mod ({modDirectory}) no puede estar dentro del volcado ni contenerlo: el volcado es de solo lectura.");
    }

    private static IReadOnlyList<string> ReadManifest(string modDirectory)
    {
        string path = Path.Combine(modDirectory, ManifestName);
        if (!File.Exists(path))
            return [];
        return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))?.Files ?? [];
    }

    private static void WriteManifest(string modDirectory, List<string> files)
    {
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(Path.Combine(modDirectory, ManifestName),
            JsonSerializer.Serialize(new Manifest(files), new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record Manifest(List<string> Files);
}
