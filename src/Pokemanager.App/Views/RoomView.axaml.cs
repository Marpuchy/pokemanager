using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Pokemanager.App.ViewModels;

namespace Pokemanager.App.Views;

public partial class RoomView : UserControl
{
    public RoomView() => InitializeComponent();

    private async void CopyInvite(object? sender, RoutedEventArgs e) => await CopyAsync((DataContext as RoomViewModel)?.InviteText);

    private async void CopyAnswer(object? sender, RoutedEventArgs e) => await CopyAsync((DataContext as RoomViewModel)?.AnswerText);

    private async Task CopyAsync(string? text)
    {
        if (text is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
