using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Pokemanager.App.Services;

/// <summary>
/// A picture for a trainer class, from the same Pokémon Showdown collection the profiles use. The games carry their
/// trainers as 3D models, so there is no 2D art to read out of the ROM: the class name is matched against the sprite
/// names instead, preferring the variant of the game's own generation (<c>acetrainer-gen6xy</c>, <c>dancer-gen7</c>…).
/// </summary>
/// <remarks>
/// Measured on the real X dump: 52 of its 178 class names match by the name alone, and the aliases below take the
/// common ones further. What is left over is mostly names that are a person and not a class (Leader, Elite Four,
/// Pokémon Trainer, Successor) and would need a table of who each one is in every game. A ROM whose class names were
/// randomized matches nothing, and a player without internet gets nothing the first time: either way the rows keep the
/// colour strip they already had. Pictures are downloaded once and cached on disk by <see cref="TrainerSprites"/>.
/// </remarks>
public static class TrainerClassSprites
{
    /// <summary>Classes whose name does not lead to their picture, by the key the name reduces to.</summary>
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["teamflare"] = "flaregrunt",
        ["teamflareboss"] = "lysandre",
        ["teamflareadmin"] = "flaregrunt",
        ["teamaqua"] = "aquagrunt",
        ["teamaquagrunt"] = "aquagrunt",
        ["teamaquaboss"] = "archie",
        ["teammagma"] = "magmagrunt",
        ["teammagmagrunt"] = "magmagrunt",
        ["teammagmaboss"] = "maxie",
        ["teamskull"] = "skullgrunt",
        ["teamskullgrunt"] = "skullgrunt",
        ["teamskullboss"] = "guzma",
        ["teamskulladmin"] = "plumeria",
        ["teamrocket"] = "rocketgrunt",
        ["aetherfoundation"] = "aetherfoundation",
        ["aetherbranchchief"] = "faba",
        ["pokemonbreeder"] = "pokemonbreeder",
        ["breeder"] = "pokemonbreeder",
        ["pokemonranger"] = "pokemonranger",
        ["madame"] = "madame",
        ["monsieur"] = "gentleman",
        ["garcon"] = "waiter",
        ["punkgirl"] = "punkgirlf",
        ["officer"] = "policeman",
        ["schoolkid"] = "schoolkid",
        ["twins"] = "twins",
        ["youngathlete"] = "youngster",
        ["athlete"] = "youngster",
    };

    private static readonly Dictionary<string, Bitmap?> Loaded = [];
    private static readonly HashSet<string> Asking = [];

    /// <summary>
    /// The class picture when it is already at hand; otherwise null, and <paramref name="whenLoaded"/> is called on the
    /// UI thread once it is (the caller raises its own change notification then). A class asked for twice is fetched
    /// once.
    /// </summary>
    public static Bitmap? Get(string? className, int generation, Action? whenLoaded = null)
    {
        // The list of names may not have been read yet (first run, or a data folder of its own): come back when it is.
        if (TrainerSprites.Cached.Count == 0)
        {
            WaitForIndex(whenLoaded);
            return null;
        }
        if (Name(className, generation) is not { } name)
            return null;
        lock (Loaded)
        {
            if (Loaded.TryGetValue(name, out var bitmap))
                return bitmap;
            if (!Asking.Add(name))
                return null;
        }
        _ = LoadAsync(name, whenLoaded);
        return null;
    }

    private static Task? indexTask;
    private static readonly List<Action> Waiting = [];

    /// <summary>Everything that asked before the list of names was there is told once, together, when it arrives.</summary>
    private static void WaitForIndex(Action? whenLoaded)
    {
        lock (Waiting)
        {
            if (whenLoaded is not null)
                Waiting.Add(whenLoaded);
            indexTask ??= Task.Run(async () =>
            {
                await TrainerSprites.IndexAsync();
                Action[] pending;
                lock (Waiting)
                {
                    pending = [.. Waiting];
                    Waiting.Clear();
                }
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var one in pending)
                        one();
                });
            });
        }
    }

    private static async Task LoadAsync(string name, Action? whenLoaded)
    {
        var bitmap = await TrainerSprites.GetAsync(name);
        lock (Loaded)
        {
            Loaded[name] = bitmap;
            Asking.Remove(name);
        }
        if (bitmap is not null && whenLoaded is not null)
            Dispatcher.UIThread.Post(whenLoaded);
    }

    /// <summary>The sprite name for a class, or null when nothing sensible matches.</summary>
    public static string? Name(string? className, int generation)
    {
        if (string.IsNullOrWhiteSpace(className))
            return null;
        string key = Key(className);
        if (key.Length < 3)
            return null;
        if (Aliases.TryGetValue(key, out string? alias))
            key = alias;

        var all = TrainerSprites.Cached;
        if (all.Count == 0)
            return null;

        // The generation's own look first, then the plain name, then any variant of it.
        string[] preferred = generation == 7 ? ["-gen7", ""] : ["-gen6xy", "-gen6", ""];
        foreach (string suffix in preferred)
        {
            if (all.Contains(key + suffix))
                return key + suffix;
        }
        return all.FirstOrDefault(n => n.StartsWith(key + "-", StringComparison.Ordinal));
    }

    /// <summary>Letters and digits only, accents folded: "Ace Trainer" and "Garçon" become "acetrainer" and "garcon".</summary>
    private static string Key(string name)
    {
        var text = name.Normalize(System.Text.NormalizationForm.FormD);
        return new string([.. text
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant)
            .Where(char.IsLetterOrDigit)]);
    }
}
