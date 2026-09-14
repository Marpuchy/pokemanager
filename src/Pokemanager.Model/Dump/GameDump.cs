using pk3DS.Core;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Dump;

/// <summary>An opened and validated game folder with the pk3DS model already loaded.</summary>
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

    /// <summary>What an imported folder was made from; null for a folder extracted by hand (the original X/Y setup).</summary>
    public DumpManifest? Manifest { get; }

    private GameDump(string romFsPath, string exeFsPath, GameTitle title, GameLanguage language, GameConfig config, DumpManifest? manifest)
    {
        RomFsPath = romFsPath;
        ExeFsPath = exeFsPath;
        Title = title;
        Language = language;
        Config = config;
        Manifest = manifest;
    }

    /// <exception cref="InvalidDumpException">The dump fails inspection; carries every problem.</exception>
    public static GameDump Open(string romFsPath, string exeFsPath, GameLanguage language)
    {
        romFsPath = Path.GetFullPath(romFsPath);
        exeFsPath = Path.GetFullPath(exeFsPath);

        GameTitle title;
        var manifest = DumpManifest.TryLoad(Path.GetDirectoryName(romFsPath)!);
        if (manifest is not null)
        {
            // Imported by Pokemanager: the game is known from the ROM; only check nothing was deleted.
            title = manifest.Game;
            var missing = GameImporter.RequiredFiles(title)
                .Where(f => !File.Exists(Path.Combine(romFsPath, f.Replace('/', Path.DirectorySeparatorChar))))
                .Select(f => string.Format(Strings.Dump_FileMissing, f))
                .ToList();
            if (!File.Exists(Path.Combine(exeFsPath, "code.bin")))
                missing.Add(Strings.Dump_CodeMissing);
            if (missing.Count > 0)
                throw new InvalidDumpException(missing);
        }
        else
        {
            var inspection = DumpInspector.Inspect(romFsPath, exeFsPath);
            if (!inspection.IsValid)
                throw new InvalidDumpException(inspection.Problems);
            title = inspection.Title!.Value;
        }

        // The version is already known: the pk3DS constructor that guesses it from the file count is not used.
        var config = new GameConfig(title.Pk3dsVersion());
        config.Initialize(romFsPath, exeFsPath, (int)language);

        return new GameDump(romFsPath, exeFsPath, title, language, config, manifest);
    }
}
