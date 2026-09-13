using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Pokemanager.App.Resources;
using Pokemanager.App.ViewModels;
using Pokemanager.App.Views;

namespace Pokemanager.App.Services;

public interface IDialogs
{
    Task<string?> PickFolderAsync(string title, string? startDirectory = null);

    /// <param name="patterns">Filters such as <c>*.rnqs</c>. Null: projects (<c>*.json</c>).</param>
    Task<string?> PickOpenFileAsync(string title, IReadOnlyList<string>? patterns = null);

    Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension = "json");

    Task ShowSettingsAsync(SettingsViewModel viewModel);

    /// <summary>Asks for confirmation; <paramref name="question"/> keeps the state of its check boxes and input.</summary>
    Task<bool> AskAsync(QuestionViewModel question);

    /// <summary>Opens the randomization log (spoilers) in its own window.</summary>
    void ShowLog(RandomizerViewModel randomizer);

    /// <summary>Shows the badge roulette until it is closed.</summary>
    Task ShowRouletteAsync(RouletteViewModel roulette);
}

public sealed class Dialogs(Window owner) : IDialogs
{
    private static FilePickerFileType ProjectType => new(Strings.Dialog_ProjectType) { Patterns = ["*.json"] };

    public async Task<string?> PickFolderAsync(string title, string? startDirectory = null)
    {
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (startDirectory is not null && Directory.Exists(startDirectory))
            options.SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(startDirectory);

        var result = await owner.StorageProvider.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickOpenFileAsync(string title, IReadOnlyList<string>? patterns = null)
    {
        var type = patterns is null ? ProjectType : new FilePickerFileType(string.Join(", ", patterns)) { Patterns = patterns };
        var result = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [type],
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension = "json")
    {
        var type = extension == "json" ? ProjectType : new FilePickerFileType("*." + extension) { Patterns = ["*." + extension] };
        var result = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [type],
        });
        return result?.TryGetLocalPath();
    }

    public Task ShowSettingsAsync(SettingsViewModel viewModel) =>
        new SettingsWindow { DataContext = viewModel }.ShowDialog(owner);

    public Task<bool> AskAsync(QuestionViewModel question) =>
        new QuestionWindow { DataContext = question }.ShowDialog<bool>(owner);

    public void ShowLog(RandomizerViewModel randomizer) =>
        new LogWindow { DataContext = randomizer }.Show(owner);

    public Task ShowRouletteAsync(RouletteViewModel roulette) =>
        new RouletteWindow { DataContext = roulette }.ShowDialog(owner);
}
