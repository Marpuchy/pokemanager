using pk3DS.Core.CTR;

namespace Pokemanager.Tests;

/// <summary>
/// Packing byte[][] into a VER_4 GARC (the X/Y format), serializing it and reading it back
/// from the raw bytes must return exactly the same files. No ROM needed.
/// </summary>
public class GarcRoundTripTests
{
    // Sizes chosen to cover: empty file, sizes that need padding to a multiple
    // of 4 (1, 2, 3, 5, 0x41), exact multiples, one PersonalInfoXY entry (0x40) and
    // a large file.
    private static readonly int[] MixedSizes = [0, 1, 2, 3, 4, 5, 0x40, 0x41, 1000, 70_001];

    [Fact]
    public void PackAndUnpack_Ver4_MixedSizes_ReturnsIdenticalBytes()
    {
        byte[][] files = SyntheticFiles(MixedSizes, seed: 1);

        AssertRoundTrip(files);
    }

    [Fact]
    public void PackAndUnpack_Ver4_PersonalLikeTable_ReturnsIdenticalBytes()
    {
        // Shape of the X/Y personal GARC: many 0x40-byte entries and a different last one
        // (the form index table).
        int[] sizes = [.. Enumerable.Repeat(0x40, 800), 0x32E];
        byte[][] files = SyntheticFiles(sizes, seed: 2);

        AssertRoundTrip(files);
    }

    private static void AssertRoundTrip(byte[][] files)
    {
        byte[] garcBytes = GARC.PackGARC(files, GARC.VER_4, 4).Data;

        // Header: "CRAG" magic on disk, version 0x0400 and consistent total size.
        Assert.Equal("CRAG"u8.ToArray(), garcBytes[..4]);
        Assert.Equal(GARC.VER_4, BitConverter.ToUInt16(garcBytes, 0x0A));
        Assert.Equal((uint)garcBytes.Length, BitConverter.ToUInt32(garcBytes, 0x14));

        // Re-read from the raw bytes, as if they came from a/2/1/8.
        var reread = new GARC.MemGARC(garcBytes);
        Assert.Equal(files.Length, reread.FileCount);
        for (int i = 0; i < files.Length; i++)
            Assert.True(files[i].AsSpan().SequenceEqual(reread.GetFile(i)), $"File {i} does not match.");

        // Repacking what was read yields a byte-identical GARC.
        byte[] repacked = GARC.PackGARC(reread.Files, GARC.VER_4, 4).Data;
        Assert.True(garcBytes.AsSpan().SequenceEqual(repacked), "The repacked GARC is not identical.");
    }

    private static byte[][] SyntheticFiles(int[] sizes, int seed)
    {
        var rng = new Random(seed);
        return sizes.Select(size =>
        {
            var data = new byte[size];
            rng.NextBytes(data);
            return data;
        }).ToArray();
    }
}
