using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Pokemanager.App.Services;

public interface IDialogs
{
    Task<string?> PickFolderAsync(string title, string? startDirectory = null);

    /// <param name="patterns">Filtros como <c>*.rnqs</c>. Null: proyectos (<c>*.json</c>).</param>
    Task<string?> PickOpenFileAsync(string title, IReadOnlyList<string>? patterns = null);

    Task<string?> PickSaveFileAsync(string title, string suggestedName);
}

public sealed class Dialogs(TopLevel owner) : IDialogs
{
    private static readonly FilePickerFileType ProjectType = new("Proyecto de Pokemanager") { Patterns = ["*.json"] };

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

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        var result = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = [ProjectType],
        });
        return result?.TryGetLocalPath();
    }
}
