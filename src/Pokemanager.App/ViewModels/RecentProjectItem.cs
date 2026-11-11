using System.IO;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.Model.Dump;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;

namespace Pokemanager.App.ViewModels;

/// <summary>A project in the start screen's list: selecting it shows its team, "Manage" opens the editor.</summary>
public partial class RecentProjectItem(WelcomeViewModel owner, RecentProject recent)
{
    public string Path => recent.Path;
    public string Name => System.IO.Path.GetFileNameWithoutExtension(recent.Path);
    public string Details => string.Format(Strings.Recent_Details, recent.Path, recent.LastOpened);

    private GameTitle? game;
    private bool gameRead;

    /// <summary>The project's game, read from its file the first time it is asked for (null when the file cannot say).</summary>
    public GameTitle? Game
    {
        get
        {
            if (!gameRead)
            {
                gameRead = true;
                game = ReadGame(recent.Path);
            }
            return game;
        }
    }

    public string GameName => Game?.DisplayName() ?? "";

    /// <summary>The strip on the row's left, in the game's colour.</summary>
    public IBrush GameBrush => new SolidColorBrush(GameColor);

    private Color GameColor => Game is { } g ? GameLook.Color(g) : AppTheme.Neutral;

    /// <summary>Text on <see cref="GameBrush"/>: white or a near-black ink, whichever contrasts more.</summary>
    public IBrush GameInk => new SolidColorBrush(AppTheme.Contrast(Colors.White, GameColor) >= AppTheme.Contrast(Color.Parse("#16130F"), GameColor)
        ? Colors.White : Color.Parse("#16130F"));

    private static GameTitle? ReadGame(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);
            // Projects from before other games existed have no "game": they are X/Y.
            return json.RootElement.TryGetProperty("game", out var value)
                ? Enum.TryParse<GameTitle>(value.GetString(), out var title) ? title : null
                : GameTitle.X;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    [RelayCommand]
    private void Manage() => owner.Manage(this);

    [RelayCommand]
    private Task Forget() => owner.DeleteAsync(this);
}
