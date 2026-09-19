using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Pokemanager.App.Controls;

/// <summary>
/// Typing inside a dropdown jumps to what you type, as the lists of a desktop application do. The dropdowns here are
/// hundreds of items long (every item, every move, every species), and Avalonia's ComboBox does not search by itself.
/// </summary>
/// <remarks>
/// Switched on for every <c>ComboBox</c> by the theme, so it works in the whole application without touching each view.
/// What is typed is kept for a second: "gr" finds Growl and then Growth, and after the pause the next letter starts a
/// new search. Letters are compared without accents or case, so "pokemon" finds "Pokémon". Typing what is already
/// selected again steps to the next item that matches, which is how repeated letters are expected to behave.
/// </remarks>
public static class TypeAhead
{
    private static readonly TimeSpan Forget = TimeSpan.FromSeconds(1);

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, bool>("IsEnabled", typeof(TypeAhead));

    public static void SetIsEnabled(ComboBox box, bool value) => box.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(ComboBox box) => box.GetValue(IsEnabledProperty);

    private static readonly Dictionary<ComboBox, (string Text, DateTime When)> Typed = [];

    static TypeAhead()
    {
        IsEnabledProperty.Changed.AddClassHandler<ComboBox>((box, e) =>
        {
            box.TextInput -= OnTextInput;
            box.KeyDown -= OnKeyDown;
            box.DetachedFromVisualTree -= OnDetached;
            if (e.NewValue is true)
            {
                box.TextInput += OnTextInput;
                box.KeyDown += OnKeyDown;
                box.DetachedFromVisualTree += OnDetached;
            }
        });
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ComboBox box)
            Typed.Remove(box);
    }

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is ComboBox box && Jump(box, e.Text ?? ""))
            e.Handled = true;
    }

    /// <summary>
    /// The keys, for the platforms and layouts where a dropdown never sees the text of what was typed. A letter that
    /// both events bring in is only used once: <see cref="Jump"/> ignores a repeat within a few milliseconds.
    /// </summary>
    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ComboBox box || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
            return;
        string typed = e.KeySymbol ?? Symbol(e.Key);
        if (typed.Length == 0 || char.IsControl(typed[0]))
            return;
        if (Jump(box, typed))
            e.Handled = true;
    }

    /// <summary>The letter or digit of a key, when the platform did not say which character it produced.</summary>
    private static string Symbol(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('a' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        _ => "",
    };

    /// <summary>
    /// Moves the selection to what was typed, keeping what came before it for a second. Public because this is the part
    /// worth checking without a keyboard.
    /// </summary>
    /// <returns>Whether something was found.</returns>
    public static bool Jump(ComboBox box, string text)
    {
        if (box.ItemCount == 0)
            return false;
        string typed = text.Trim();
        if (typed.Length == 0)
            return false;

        var now = DateTime.UtcNow;
        bool repeat = Typed.TryGetValue(box, out var last) && now - last.When < TimeSpan.FromMilliseconds(40) && last.Text.EndsWith(typed, StringComparison.Ordinal);
        if (repeat)
            return false;
        string search = last.Text is { Length: > 0 } && now - last.When < Forget ? last.Text + typed : typed;
        Typed[box] = (search, now);

        // A single letter typed again means "the next one that starts with it", not "the same one".
        bool step = search.Length == 1 && last.Text == search && now - last.When < Forget;
        int from = step || box.SelectedIndex < 0 ? box.SelectedIndex + 1 : box.SelectedIndex;
        if (Find(box, Simplify(search), from) is not { } index)
            return false;
        box.SelectedIndex = index;
        // While the list is open the selection has to be brought into view; the control does not do it on its own.
        Dispatcher.UIThread.Post(() => box.ScrollIntoView(box.SelectedItem!), DispatcherPriority.Background);
        return true;
    }

    /// <summary>
    /// The first item at or after <paramref name="from"/> that starts with the text, wrapping around; when none does,
    /// the first whose *any* word starts with it — the lists here are written "#025 Pikachu", so what the player types
    /// is hardly ever the first thing in the row.
    /// </summary>
    private static int? Find(ComboBox box, string search, int from)
    {
        if (search.Length == 0)
            return null;
        int count = box.ItemCount;
        int? word = null;
        for (int step = 0; step < count; step++)
        {
            int index = ((from < 0 ? 0 : from) + step) % count;
            string text = Simplify(Text(box, index));
            if (text.StartsWith(search, StringComparison.Ordinal))
                return index;
            if (word is null && text.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                    .Any(w => w.StartsWith(search, StringComparison.Ordinal)))
            {
                word = index;
            }
        }
        return word;
    }

    private static readonly char[] Separators = [' ', '·', '-', '(', ')', '[', ']', ',', '.', '/'];

    private static string Text(ComboBox box, int index) =>
        box.ItemsSource?.Cast<object?>().ElementAtOrDefault(index)?.ToString()
        ?? box.Items.ElementAtOrDefault(index)?.ToString()
        ?? "";

    /// <summary>Lower case and without accents, so "pokemon" finds "Pokémon" and "e" finds "Éxito".</summary>
    private static string Simplify(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }
}
