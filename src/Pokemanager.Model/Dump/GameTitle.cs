namespace Pokemanager.Model.Dump;

/// <summary>Supported games.</summary>
public enum GameTitle
{
    X,
    Y,
}

public static class GameTitleExtensions
{
    /// <summary>Title ID of the game, the same in every region.</summary>
    public static ulong TitleId(this GameTitle title) => title switch
    {
        GameTitle.X => 0x0004000000055D00,
        GameTitle.Y => 0x0004000000055E00,
        _ => throw new ArgumentOutOfRangeException(nameof(title), title, null),
    };

    /// <summary>Title ID as 16 hex digits, as the emulator's mods folder uses it.</summary>
    public static string TitleIdHex(this GameTitle title) => title.TitleId().ToString("X16");
}
