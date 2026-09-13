using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pokemanager.App.Views;

public partial class QuestionWindow : Window
{
    public QuestionWindow()
    {
        InitializeComponent();
        Opened += (_, _) => InputBox.Focus();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
