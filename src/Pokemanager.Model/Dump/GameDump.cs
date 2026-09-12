using pk3DS.Core;

namespace Pokemanager.Model.Dump;

/// <summary>
/// Volcado de X/Y abierto y validado, con el modelo de pk3DS ya cargado.
/// </summary>
/// <remarks>
/// El volcado es de solo lectura. <see cref="Config"/> expone métodos de pk3DS que escriben dentro del
/// romfs (<c>GARCFile.Save</c>, <c>BackupFiles</c>): no deben llamarse nunca. Todo lo generado va a la
/// carpeta de mods del emulador.
/// </remarks>
public sealed class GameDump
{
    public string RomFsPath { get; }
    public string ExeFsPath { get; }
    public GameTitle Title { get; }
    public GameLanguage Language { get; }
    public GameConfig Config { get; }

    private GameDump(string romFsPath, string exeFsPath, GameTitle title, GameLanguage language, GameConfig config)
    {
        RomFsPath = romFsPath;
        ExeFsPath = exeFsPath;
        Title = title;
        Language = language;
        Config = config;
    }

    /// <exception cref="InvalidDumpException">El volcado no pasa la inspección; incluye todos los problemas.</exception>
    public static GameDump Open(string romFsPath, string exeFsPath, GameLanguage language)
    {
        romFsPath = Path.GetFullPath(romFsPath);
        exeFsPath = Path.GetFullPath(exeFsPath);

        var inspection = DumpInspector.Inspect(romFsPath, exeFsPath);
        if (!inspection.IsValid)
            throw new InvalidDumpException(inspection.Problems);

        // La versión ya está verificada: no se usa el constructor de pk3DS que la deduce del conteo.
        var config = new GameConfig(GameVersion.XY);
        config.Initialize(romFsPath, exeFsPath, (int)language);

        return new GameDump(romFsPath, exeFsPath, inspection.Title!.Value, language, config);
    }
}
