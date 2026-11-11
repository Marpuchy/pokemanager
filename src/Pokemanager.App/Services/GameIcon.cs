using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Pokemanager.App.Services;

/// <summary>
/// The game's own icon, the one the 3DS menu shows, read from the <c>icon.bin</c> (SMDH) the import copied into the game
/// folder: the large 48×48 picture at 0x24C0, RGB565 in 8×8 tiles in Morton order. For the editor's top bar
/// (2026-11-11, user: "icono del juego en la barra").
/// </summary>
public static class GameIcon
{
    private const int LargeIconOffset = 0x24C0;
    private const int Size = 48;

    /// <summary>The icon of the game folder <paramref name="dumpDirectory"/>, or null when it has none.</summary>
    public static Bitmap? Load(string dumpDirectory)
    {
        try
        {
            string path = Path.Combine(dumpDirectory, "exefs", "icon.bin");
            if (!File.Exists(path))
                return null;
            byte[] smdh = File.ReadAllBytes(path);
            if (smdh.Length < LargeIconOffset + Size * Size * 2 || smdh[0] != 'S' || smdh[1] != 'M' || smdh[2] != 'D' || smdh[3] != 'H')
                return null;
            return Decode(smdh);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Bitmap Decode(byte[] smdh)
    {
        var pixels = new byte[Size * Size * 4];
        int at = LargeIconOffset;
        for (int tileY = 0; tileY < Size; tileY += 8)
            for (int tileX = 0; tileX < Size; tileX += 8)
                for (int i = 0; i < 64; i++)
                {
                    int value = smdh[at] | (smdh[at + 1] << 8);
                    at += 2;
                    int x = 0, y = 0;
                    for (int bit = 0; bit < 3; bit++)
                    {
                        x |= ((i >> (2 * bit)) & 1) << bit;
                        y |= ((i >> (2 * bit + 1)) & 1) << bit;
                    }
                    int p = ((tileY + y) * Size + tileX + x) * 4;
                    pixels[p] = (byte)((value & 31) * 255 / 31);            // B
                    pixels[p + 1] = (byte)(((value >> 5) & 63) * 255 / 63); // G
                    pixels[p + 2] = (byte)(((value >> 11) & 31) * 255 / 31);// R
                    pixels[p + 3] = 255;
                }
        var bitmap = new WriteableBitmap(new PixelSize(Size, Size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
            for (int row = 0; row < Size; row++)
                System.Runtime.InteropServices.Marshal.Copy(pixels, row * Size * 4, buffer.Address + row * buffer.RowBytes, Size * 4);
        return bitmap;
    }
}
