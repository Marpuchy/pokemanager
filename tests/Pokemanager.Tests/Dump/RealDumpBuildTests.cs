using pk3DS.Core.CTR;
using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Model.Build;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Dump;

/// <summary>Mod build against the real dump (skipped without <c>POKEMANAGER_DUMP</c>).</summary>
public class RealDumpBuildTests
{
    [Fact]
    public void BulbasaurHpEdit_OnlyChangesThatEntryAndConcatenatedTable()
    {
        string? dumpDir = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(dumpDir), "POKEMANAGER_DUMP is not set.");
        string modDir = Path.Combine(Path.GetTempPath(), "pokemanager-realmod-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = EditorSession.Open(new Project { DumpDirectory = dumpDir! });
            session.SetInt(GameTables.Personal, 1, "hp", 100);

            ModInstaller.Install(modDir, dumpDir!, null, ModBuilder.BuildEdits(session));

            byte[] originalGarc = File.ReadAllBytes(Path.Combine(dumpDir!, "romfs", "a", "2", "1", "8"));
            byte[] builtGarc = File.ReadAllBytes(Path.Combine(modDir, "romfs", "a", "2", "1", "8"));
            byte[][] built = new GARC.MemGARC(builtGarc).Files;

            Assert.Equal(originalGarc.Length, builtGarc.Length);
            Assert.Equal(100, new PersonalInfoXY(built[1]).HP);
            Assert.Equal(100, new PersonalInfoXY(built[^1][0x40..0x80]).HP);
            // Byte by byte, only entry 1 HP and its copy in the concatenated table differ.
            Assert.Equal(2, originalGarc.Zip(builtGarc).Count(p => p.First != p.Second));
        }
        finally
        {
            if (Directory.Exists(modDir))
                Directory.Delete(modDir, true);
        }
    }
}
