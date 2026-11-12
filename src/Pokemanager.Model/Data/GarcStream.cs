namespace Pokemanager.Model.Data;

/// <summary>
/// A GARC read file by file from a stream, for archives too big to load (the Generation 7 encounter archive is 483 MB
/// and the encounters tab needs 74 of its 3696 files). Only the first entry of each file is read.
/// </summary>
public sealed class GarcStream : IDisposable
{
    private readonly Stream stream;
    private readonly (long Start, int Length)[] files;
    private readonly long data;

    public GarcStream(Stream stream)
    {
        this.stream = stream;
        byte[] header = Read(0, 0x1C);
        if (header[0] != (byte)'C' || header[1] != (byte)'R' || header[2] != (byte)'A' || header[3] != (byte)'G')
            throw new InvalidDataException("Not a GARC.");
        int headerSize = BitConverter.ToInt32(header, 4);
        data = BitConverter.ToUInt32(header, 0x10);
        byte[] fato = Read(headerSize, 0xC);
        int count = BitConverter.ToUInt16(fato, 8);
        byte[] offsets = Read(headerSize + 0xC, 4 * count);
        long fatb = headerSize + 0xC + 4L * count;
        files = new (long, int)[count];
        for (int i = 0; i < count; i++)
        {
            long entry = fatb + 0xC + BitConverter.ToUInt32(offsets, 4 * i);
            uint bits = BitConverter.ToUInt32(Read(entry, 4), 0);
            if (bits == 0)
                continue;
            byte[] first = Read(entry + 4, 12);
            files[i] = (BitConverter.ToUInt32(first, 0), (int)BitConverter.ToUInt32(first, 8));
        }
    }

    public int Count => files.Length;

    public byte[] File(int index) => index < 0 || index >= files.Length || files[index].Length == 0
        ? []
        : Read(data + files[index].Start, files[index].Length);

    private byte[] Read(long at, int length)
    {
        var buffer = new byte[length];
        stream.Seek(at, SeekOrigin.Begin);
        stream.ReadExactly(buffer);
        return buffer;
    }

    public void Dispose() => stream.Dispose();
}
