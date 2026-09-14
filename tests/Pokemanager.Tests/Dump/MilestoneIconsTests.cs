using System.Text;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.Tests.Dump;

public class MilestoneIconsTests
{
    /// <summary>A tall RGBA8 BCLIM (8×12, stored as 8×16) decodes: textures are not always square.</summary>
    [Fact]
    public void DecodeBclim_NonSquareTexture()
    {
        var data = new byte[8 * 16 * 4];
        // Pixel index 64 is the first pixel of the second 8×8 tile, which is (0, 8). RGBA8 is stored as A, B, G, R.
        data[64 * 4] = 255;
        data[(64 * 4) + 3] = 200;
        var footer = new byte[0x28];
        "CLIM"u8.CopyTo(footer);
        BitConverter.GetBytes((ushort)8).CopyTo(footer, 0x1C);
        BitConverter.GetBytes((ushort)12).CopyTo(footer, 0x1E);
        footer[0x20] = 0x09; // RGBA8

        var image = PokemonIcons.DecodeBclim([.. data, .. footer])!;

        Assert.Equal((8, 12), (image.Width, image.Height));
        Assert.Equal([200, 0, 0, 255], image.Rgba[(8 * 8 * 4)..((8 * 8 * 4) + 4)]);
    }

    [Fact]
    public void ReadDarc_FilesByName()
    {
        byte[] darc = BuildDarc(("badge_01.bclim", [1, 2, 3]), ("base.bclim", [9]));

        var files = MilestoneIcons.ReadDarc([0xAA, 0xBB, .. darc])!; // the archive may sit after a small header

        Assert.Equal([1, 2, 3], files["badge_01.bclim"]);
        Assert.Equal([9], files["base.bclim"]);
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void RealDump_EightBadges()
    {
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(dump), "POKEMANAGER_DUMP is not set.");

        var badges = MilestoneIcons.Load(new RomFsLayers(Path.Combine(dump!, "romfs")), GameTitle.X)!;

        // Measured on Pokémon X.
        Assert.Equal([(55, 64), (55, 77), (70, 45), (50, 55), (64, 64), (90, 34), (60, 60), (55, 60)],
            badges.Select(b => (b!.Width, b.Height)));
    }

    /// <summary>darc with a root directory entry and the given files; offsets relative to the archive start.</summary>
    private static byte[] BuildDarc(params (string Name, byte[] Data)[] files)
    {
        const int header = 0x1C;
        int count = files.Length + 1;
        var names = new List<byte>();
        names.AddRange([0, 0]); // root name ""
        var nameOffsets = new List<int>();
        foreach (var (name, _) in files)
        {
            nameOffsets.Add(names.Count);
            names.AddRange(Encoding.Unicode.GetBytes(name + "\0"));
        }
        int tableSize = (12 * count) + names.Count;
        int dataOffset = header + tableSize;
        var table = new List<byte>();
        table.AddRange(BitConverter.GetBytes(0x01000000)); // root: directory
        table.AddRange(BitConverter.GetBytes(0));
        table.AddRange(BitConverter.GetBytes(count));
        var body = new List<byte>();
        for (int i = 0; i < files.Length; i++)
        {
            table.AddRange(BitConverter.GetBytes(nameOffsets[i]));
            table.AddRange(BitConverter.GetBytes(dataOffset + body.Count));
            table.AddRange(BitConverter.GetBytes(files[i].Data.Length));
            body.AddRange(files[i].Data);
        }
        var head = new byte[header];
        "darc"u8.CopyTo(head);
        BitConverter.GetBytes(header).CopyTo(head, 0x10);
        BitConverter.GetBytes(tableSize).CopyTo(head, 0x14);
        BitConverter.GetBytes(dataOffset).CopyTo(head, 0x18);
        return [.. head, .. table, .. names, .. body];
    }
}
