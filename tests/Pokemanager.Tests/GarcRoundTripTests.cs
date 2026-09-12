using pk3DS.Core.CTR;

namespace Pokemanager.Tests;

/// <summary>
/// Empaquetar byte[][] en un GARC VER_4 (el formato de X/Y), serializarlo y volver a leerlo
/// desde los bytes crudos debe devolver exactamente los mismos archivos. Sin ROM.
/// </summary>
public class GarcRoundTripTests
{
    // Tamaños elegidos para cubrir: archivo vacío, tamaños que requieren relleno a múltiplo
    // de 4 (1, 2, 3, 5, 0x41), múltiplos exactos, una entrada de PersonalInfoXY (0x40) y
    // un archivo grande.
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
        // Forma del GARC de personal de X/Y: muchas entradas de 0x40 bytes y una final
        // distinta (la tabla de índices de formas).
        int[] sizes = [.. Enumerable.Repeat(0x40, 800), 0x32E];
        byte[][] files = SyntheticFiles(sizes, seed: 2);

        AssertRoundTrip(files);
    }

    private static void AssertRoundTrip(byte[][] files)
    {
        byte[] garcBytes = GARC.PackGARC(files, GARC.VER_4, 4).Data;

        // Cabecera: magic "CRAG" en disco, versión 0x0400 y tamaño total coherente.
        Assert.Equal("CRAG"u8.ToArray(), garcBytes[..4]);
        Assert.Equal(GARC.VER_4, BitConverter.ToUInt16(garcBytes, 0x0A));
        Assert.Equal((uint)garcBytes.Length, BitConverter.ToUInt32(garcBytes, 0x14));

        // Releer desde los bytes crudos, como si vinieran de a/2/1/8.
        var reread = new GARC.MemGARC(garcBytes);
        Assert.Equal(files.Length, reread.FileCount);
        for (int i = 0; i < files.Length; i++)
            Assert.True(files[i].AsSpan().SequenceEqual(reread.GetFile(i)), $"El archivo {i} no coincide.");

        // Reempaquetar lo leído produce un GARC idéntico byte a byte.
        byte[] repacked = GARC.PackGARC(reread.Files, GARC.VER_4, 4).Data;
        Assert.True(garcBytes.AsSpan().SequenceEqual(repacked), "El reempaquetado no es idéntico.");
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
