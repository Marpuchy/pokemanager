using pk3DS.Core.CTR;
using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Dump;

/// <summary>Construcción del mod contra el volcado real (se omite sin <c>POKEMANAGER_DUMP</c>).</summary>
public class RealDumpBuildTests
{
    [Fact]
    public void BulbasaurHpEdit_OnlyChangesThatEntryAndConcatenatedTable()
    {
        string? dumpDir = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(dumpDir), "POKEMANAGER_DUMP no está definida.");
        string modDir = Path.Combine(Path.GetTempPath(), "pokemanager-realmod-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = EditorSession.Open(new Project { DumpDirectory = dumpDir! });
            session.SetInt(GameTables.Personal, 1, "hp", 100);

            ModBuilder.Build(session, modDir);

            string originalPath = Path.Combine(dumpDir!, "romfs", "a", "2", "1", "8");
            byte[] originalGarc = File.ReadAllBytes(originalPath);
            byte[] builtGarc = File.ReadAllBytes(Path.Combine(modDir, "romfs", "a", "2", "1", "8"));
            byte[][] original = new GARC.MemGARC(originalGarc).Files;
            byte[][] built = new GARC.MemGARC(builtGarc).Files;

            Assert.Equal(originalGarc.Length, builtGarc.Length);
            Assert.Equal(100, new PersonalInfoXY(built[1]).HP);
            Assert.Equal(100, new PersonalInfoXY(built[^1][0x40..0x80]).HP);
            // Byte a byte, solo difieren el PS de la entrada 1 y su copia en la tabla concatenada.
            int differences = originalGarc.Zip(builtGarc).Count(p => p.First != p.Second);
            Assert.Equal(2, differences);
        }
        finally
        {
            if (Directory.Exists(modDir))
                Directory.Delete(modDir, true);
        }
    }
}
