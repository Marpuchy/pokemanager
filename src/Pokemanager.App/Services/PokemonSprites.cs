using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Pokemanager.Model.Data;
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
        for (int s = 1; s < Math.Min(PokemonIcons.SpeciesCount, data.Personal.Length); s++)
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
        var icons = PokemonIcons.Load(new RomFsLayers(project.RomFsPath), Path.Combine(project.ExeFsPath, "code.bin"));
        return icons is null ? Empty : new PokemonSprites(icons, data);
    }

    public Bitmap? For(int species, int form = 0, bool female = false, bool shiny = false) =>
        icons is null ? null : Bitmap(icons.IndexFor(species, form, female, shiny));

    /// <summary>Icon of a personal table entry: a species, or an alternate form stored after the species.</summary>
    public Bitmap? ForPersonalEntry(int index) =>
        formEntries.TryGetValue(index, out var f) ? For(f.Species, f.Form) : index < PokemonIcons.SpeciesCount ? For(index) : null;

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
        {
            var writeable = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            using (var buffer = writeable.Lock())
            {
                for (int y = 0; y < image.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(image.Rgba, y * image.Width * 4, buffer.Address + (y * buffer.RowBytes), image.Width * 4);
            }
            bitmap = writeable;
        }

        lock (bitmaps)
            bitmaps[index] = bitmap;
        return bitmap;
    }
}
