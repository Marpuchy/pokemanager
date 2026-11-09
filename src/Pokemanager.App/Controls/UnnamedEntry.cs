using System.Globalization;
using Avalonia.Data.Converters;

namespace Pokemanager.App.Controls;

/// <summary>
/// Hides the rows of a dropdown that name nothing (2026-11-09, user: "what are those (?), I do not want them"). The
/// game's item text keeps a placeholder for every unused id — "???" in English, "(?)" in Spanish; the Expansion has
/// 885-903 —, and the item pickers are bound by index, so the rows cannot be taken out of their lists: the theme hides
/// the row instead (<c>ComboBoxItem.IsVisible</c>). Only those exact placeholders: "———", the "no move" row of a move
/// list, is a real choice. The selected value still shows in the closed box, so a held placeholder is not lost.
/// </summary>
public sealed class UnnamedEntry : IValueConverter
{
    public static readonly UnnamedEntry Visible = new();

    public static bool IsPlaceholder(string? text) => text?.Trim() is "???" or "(?)";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !(value is string text && IsPlaceholder(text));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
