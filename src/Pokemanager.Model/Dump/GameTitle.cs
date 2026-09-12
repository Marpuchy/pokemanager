namespace Pokemanager.Model.Dump;

/// <summary>Juegos soportados.</summary>
public enum GameTitle
{
    X,
    Y,
}

public static class GameTitleExtensions
{
    /// <summary>Title ID del juego, igual en todas las regiones.</summary>
    public static ulong TitleId(this GameTitle title) => title switch
    {
        GameTitle.X => 0x0004000000055D00,
        GameTitle.Y => 0x0004000000055E00,
        _ => throw new ArgumentOutOfRangeException(nameof(title), title, null),
    };

    /// <summary>Title ID en 16 dígitos hexadecimales, como lo usa la carpeta de mods del emulador.</summary>
    public static string TitleIdHex(this GameTitle title) => title.TitleId().ToString("X16");
}
