using PKHeX.Core;
using Pokemanager.Save.Resources;

namespace Pokemanager.Save;

/// <summary>Writes a save safely: backup of what was on disk, atomic replacement and a final byte comparison.</summary>
internal static class SaveWriter
{
    /// <returns>Path of the backup.</returns>
    public static string Write(string savePath, byte[] updated, string backupRoot)
    {
        var reread = (SaveUtil.GetSaveFile(updated) ?? (updated.Length == SaveUpdater.SizeXY ? new SAV6XY(updated) : null)) as SAV6XY
                     ?? throw new SaveUpdateException(Strings.Save_CannotReread);
        if (!reread.ChecksumsValid)
            throw new SaveUpdateException(Strings.Save_ModifiedBadChecksums);

        byte[] original = File.ReadAllBytes(savePath);
        string backupDir = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        for (int n = 1; Directory.Exists(backupDir); n++)
            backupDir = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + n);
        Directory.CreateDirectory(backupDir);
        string backup = Path.Combine(backupDir, Path.GetFileName(savePath));
        File.WriteAllBytes(backup, original);

        string tmp = savePath + ".pokemanager.tmp";
        File.WriteAllBytes(tmp, updated);
        File.Move(tmp, savePath, overwrite: true);

        // Final check on what actually ended up on disk.
        if (!File.ReadAllBytes(savePath).AsSpan().SequenceEqual(updated))
            throw new SaveUpdateException(string.Format(Strings.Save_WrittenMismatch, backup));
        return backup;
    }
}
