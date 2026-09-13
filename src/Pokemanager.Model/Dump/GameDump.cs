using pk3DS.Core;

namespace Pokemanager.Model.Dump;

/// <summary>An opened and validated X/Y dump with the pk3DS model already loaded.</summary>
/// <remarks>
/// The dump is read-only. <see cref="Config"/> exposes pk3DS methods that write inside the romfs
/// (<c>GARCFile.Save</c>, <c>BackupFiles</c>): they must never be called.
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

    /// <exception cref="InvalidDumpException">The dump fails inspection; carries every problem.</exception>
    public static GameDump Open(string romFsPath, string exeFsPath, GameLanguage language)
    {
        romFsPath = Path.GetFullPath(romFsPath);
        exeFsPath = Path.GetFullPath(exeFsPath);

        var inspection = DumpInspector.Inspect(romFsPath, exeFsPath);
        if (!inspection.IsValid)
            throw new InvalidDumpException(inspection.Problems);

        // The version is already verified: the pk3DS constructor that guesses it from the file count is not used.
        var config = new GameConfig(GameVersion.XY);
        config.Initialize(romFsPath, exeFsPath, (int)language);

        return new GameDump(romFsPath, exeFsPath, inspection.Title!.Value, language, config);
    }
}
