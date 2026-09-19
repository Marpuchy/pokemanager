using PKHeX.Core;
using Pokemanager.Save.Resources;

namespace Pokemanager.Save;

/// <summary>Writes a save safely: backup of what was on disk, atomic replacement and a final byte comparison.</summary>
internal static class SaveWriter
{
    /// <summary>A copy of what is on disk right now, in a folder of its own named after the moment.</summary>
    /// <returns>Path of the copy.</returns>
    public static string BackUp(string savePath, string backupRoot)
    {
        string backupDir = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        for (int n = 1; Directory.Exists(backupDir); n++)
            backupDir = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + n);
        Directory.CreateDirectory(backupDir);
        string backup = Path.Combine(backupDir, Path.GetFileName(savePath));
        File.WriteAllBytes(backup, File.ReadAllBytes(savePath));
        return backup;
    }

    /// <returns>Path of the backup.</returns>
    public static string Write(string savePath, byte[] updated, string backupRoot)
    {
        var reread = SaveUpdater.Parse(updated) is { } parsed && SaveUpdater.IsSupported(parsed)
            ? parsed
            : throw new SaveUpdateException(Strings.Save_CannotReread);
        if (!reread.ChecksumsValid)
            throw new SaveUpdateException(Strings.Save_ModifiedBadChecksums);

        string backup = BackUp(savePath, backupRoot);

        string tmp = savePath + ".pokemanager.tmp";
        File.WriteAllBytes(tmp, updated);
        File.Move(tmp, savePath, overwrite: true);

        // Final check on what actually ended up on disk.
        if (!File.ReadAllBytes(savePath).AsSpan().SequenceEqual(updated))
            throw new SaveUpdateException(string.Format(Strings.Save_WrittenMismatch, backup));
        return backup;
    }
}
