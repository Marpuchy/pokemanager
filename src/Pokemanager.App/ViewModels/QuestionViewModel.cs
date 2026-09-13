using CommunityToolkit.Mvvm.ComponentModel;

namespace Pokemanager.App.ViewModels;

/// <summary>An optional choice shown as a check box in a question dialog.</summary>
public partial class DialogCheck(string text, bool isChecked = false, bool isEnabled = true) : ObservableObject
{
    public string Text { get; } = text;

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;

    public bool IsEnabled { get; } = isEnabled;
}

/// <summary>A confirmation or a short text prompt.</summary>
public partial class QuestionViewModel(string title, string message, string confirmText, string cancelText,
    IReadOnlyList<DialogCheck> checks, string? inputPlaceholder = null) : ObservableObject
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public string ConfirmText { get; } = confirmText;
    public string CancelText { get; } = cancelText;
    public IReadOnlyList<DialogCheck> Checks { get; } = checks;

    /// <summary>Not null: the dialog asks for a line of text.</summary>
    public string? InputPlaceholder { get; } = inputPlaceholder;
    public bool HasInput => InputPlaceholder is not null;

    [ObservableProperty]
    public partial string Input { get; set; } = "";
}
