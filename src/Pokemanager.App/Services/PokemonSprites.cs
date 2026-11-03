using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.Services;

/// <summary>
/// Pokémon icons as Avalonia bitmaps, decoded from the user's dump on first use and cached. Everything returns null
/// when the dump has no icons, so the UI simply shows no sprite.
/// </summary>
public sealed class PokemonSprites
{
    private readonly PokemonIcons? icons;
    private readonly Dictionary<int, (int Species, int Form)> formEntries = [];
    private readonly Dictionary<int, Bitmap?> bitmaps = [];

    public static PokemonSprites Empty { get; } = new(null, null);

    private PokemonSprites(PokemonIcons? icons, GameData? data)
    {
        this.icons = icons;
        if (data is null)
            return;
        // Personal entries above the species count are alternate forms: FormStatsIndex says where each species' forms start.
        // The count comes from the ROM's table (GameData.SpeciesCount), not from the icon table, which is a different
        // thing and need not grow with it: a species with no icon simply gets no sprite.
        for (int s = 1; s < Math.Min(data.SpeciesCount, data.Personal.Length); s++)
        {
            var p = data.Personal[s];
            if (p.FormStatsIndex <= 0)
                continue;
            for (int form = 1; form < p.FormeCount; form++)
                formEntries.TryAdd(p.FormStatsIndex + form - 1, (s, form));
        }
    }

    public static PokemonSprites Load(Project project, GameData data)
    {
        // Each game keeps its icons in a different archive; an imported game folder only has the one it uses. Read
        // through the project's own stack of layers, so a mod that replaces the icon archive or the executable (where
        // the species-to-icon table lives) is the one that answers.
        var layers = project.ReadLayers();
        var layout = project.Game.Layout();
        string code = project.CodePath();
        var icons = layout.Icons
            .Where(layers.Has)
            .Select(garc => PokemonIcons.Load(layers, code, garc, data.SpeciesCount, data.VanillaSpeciesCount,
                Path.Combine(project.ExeFsPath, "code.bin")))
            .FirstOrDefault(i => i is not null);
        return icons is null ? Empty : new PokemonSprites(icons, data);
    }

    public Bitmap? For(int species, int form = 0, bool female = false, bool shiny = false) =>
        icons is null ? null : Bitmap(icons.IndexFor(species, form, female, shiny));

    /// <summary>Icon of a personal table entry: a species, or an alternate form stored after the species.</summary>
    public Bitmap? ForPersonalEntry(int index) =>
        formEntries.TryGetValue(index, out var f) ? For(f.Species, f.Form) : index < (icons?.SpeciesCount ?? 0) ? For(index) : null;

    /// <summary>
    /// Canvas of Gen 6 box icons. Gen 7 draws the same sprites on a 64×32 canvas (verified on Ultra Moon: same sprite box,
    /// 12 px further right and 1 px lower), which looked smaller when fitted into the same space; it is brought to this.
    /// </summary>
    private const int IconWidth = 40, IconHeight = 30;

    /// <summary>
    /// A Pokémon box icon on the Gen 6 canvas with the sprite centred in it (user, 2026-11-04: "centra las imágenes
    /// siempre"). The games draw each icon wherever on its canvas — Gen 7 12 px to the right, and the mods' Pokémon
    /// each in its own place — so the drawn pixels are found and put in the middle. A sprite larger than the canvas
    /// keeps its centre and loses its edges.
    /// </summary>
    private static Bitmap PokemonBitmap(IconImage image) => ToBitmap(Centred(image, IconWidth, IconHeight));

    internal static IconImage Centred(IconImage image, int width, int height)
    {
        int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            if (image.Rgba[((y * image.Width) + x) * 4 + 3] == 0)
                continue;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        if (maxX < 0)
            return new IconImage(width, height, new byte[width * height * 4]);
        // Where the sprite's box starts on the new canvas; negative when it is wider than the canvas.
        int offsetX = (width - (maxX - minX + 1)) / 2 - minX;
        int offsetY = (height - (maxY - minY + 1)) / 2 - minY;
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sy = y - offsetY;
            if (sy < 0 || sy >= image.Height)
                continue;
            for (int x = 0; x < width; x++)
            {
                int sx = x - offsetX;
                if (sx >= 0 && sx < image.Width)
                    Array.Copy(image.Rgba, ((sy * image.Width) + sx) * 4, rgba, ((y * width) + x) * 4, 4);
            }
        }
        return new IconImage(width, height, rgba);
    }

    /// <summary>Any decoded image as it is (badges, stamps…).</summary>
    public static Bitmap ToBitmap(IconImage image)
    {
        var writeable = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var buffer = writeable.Lock())
        {
            for (int y = 0; y < image.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(image.Rgba, y * image.Width * 4, buffer.Address + (y * buffer.RowBytes), image.Width * 4);
        }
        return writeable;
    }

    private Bitmap? Bitmap(int index)
    {
        if (icons is null || index <= 0)
            return null;
        lock (bitmaps)
        {
            if (bitmaps.TryGetValue(index, out var cached))
                return cached;
        }

        Bitmap? bitmap = null;
        if (icons.Get(index) is { } image)
            bitmap = PokemonBitmap(image);

        lock (bitmaps)
            bitmaps[index] = bitmap;
        return bitmap;
    }
}
