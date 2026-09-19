using System.Buffers.Binary;
using Pokemanager.Randomizer;

namespace Pokemanager.Tests.Randomizer;

/// <summary>
/// The header of a .cxi as UPR ZX leaves it — region sizes written one byte at a time — put right from the file itself.
/// </summary>
public sealed class NcchHeaderTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-ncch-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    private static uint Units(byte[] file, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset));

    /// <summary>
    /// Header, a 0x2000 logo, an ExeFS holding one 0x1234-byte file, then a RomFS aligned to 4 KB running to the end.
    /// The sizes are those of a bigger RomFS, but for their lowest byte — what UPR writes over the base ROM's header.
    /// </summary>
    private string Rom(int romfsUnits)
    {
        const int logo = 0x2000, exefsOffset = 0xA00 + logo, exefsLength = 0x200 + 0x1400;
        int romfsOffset = (exefsOffset + exefsLength + 0xFFF) / 0x1000 * 0x1000;
        byte[] file = new byte[romfsOffset + (romfsUnits * 0x200)];
        "NCCH"u8.CopyTo(file.AsSpan(0x100));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x198), 0xA00 / 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x19C), logo / 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x1A0), exefsOffset / 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x1A4), exefsLength / 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x1B0), (uint)(romfsOffset / 0x200));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x1B4), (uint)(romfsUnits + 0x100)); // stale: 128 KB too many
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x104), 0x12345);
        ".code"u8.CopyTo(file.AsSpan(exefsOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(exefsOffset + 8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(exefsOffset + 12), 0x1234);
        "IVFC"u8.CopyTo(file.AsSpan(romfsOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(romfsOffset + 4), 0x10000);
        string path = Path.Combine(dir, "rom.cxi");
        File.WriteAllBytes(path, file);
        return path;
    }

    [Fact]
    public void Repair_SetsTheSizesTheFileReallyHas()
    {
        string path = Rom(romfsUnits: 0x300);

        Assert.True(NcchHeader.Repair(path));

        byte[] file = File.ReadAllBytes(path);
        uint romfsOffset = Units(file, 0x1B0);
        Assert.Equal((uint)(file.Length / 0x200) - romfsOffset, Units(file, 0x1B4));
        Assert.Equal((uint)(file.Length / 0x200), Units(file, 0x104));
        Assert.Equal((0xA00u + 0x2000) / 0x200, Units(file, 0x1A0));
        Assert.False(NcchHeader.Repair(path)); // right now: nothing more to do
    }

    [Fact]
    public void Repair_RefusesAFileWithoutARomFsWhereTheLayoutPutsIt()
    {
        string path = Rom(romfsUnits: 0x300);
        byte[] file = File.ReadAllBytes(path);
        file.AsSpan((int)Units(file, 0x1B0) * 0x200, 4).Clear();
        File.WriteAllBytes(path, file);

        Assert.Throws<InvalidDataException>(() => NcchHeader.Repair(path));
    }
}
