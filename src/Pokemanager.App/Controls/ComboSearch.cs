using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Pokemanager.App.Resources;

namespace Pokemanager.App.Controls;

/// <summary>
/// A search field at the top of every long dropdown (2026-11-13, user: "where a dropdown has many options, like the
/// roulette's items or choosing moves, put a field to search at the top"). Switched on with <see cref="TypeAhead"/>,
/// so it is in every <c>ComboBox</c> of the application.
/// </summary>
/// <remarks>
/// The list itself is never filtered: the dropdowns are bound by index to fixed lists (the ItemsSource rule of
/// CLAUDE.md), so changing what they hold would move selections. While something is typed the open list is covered by
/// a list of what matches — any part of the row, without accents or case, rows whose words start with it first — and
/// picking one there selects that row of the real list. Up/Down move through the matches, Enter picks, Escape closes.
/// </remarks>
public static class ComboSearch
{
    /// <summary>From how many rows a dropdown gets the field.</summary>
    public const int MinItems = 16;

    private const int MaxHits = 300;

    private sealed class State
    {
        public required TextBox Search { get; init; }
        public required ListBox Results { get; init; }
        public required Control List { get; init; }
    }

    private sealed record Hit(int Index, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly ConditionalWeakTable<ComboBox, State> States = new();

    internal static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is not ComboBox box || e.NameScope.Find<Border>("PopupBorder") is not { Child: { } list } border)
            return;

        var search = new TextBox
        {
            Watermark = Strings.Combo_Search,
            Margin = new Thickness(4, 4, 4, 2),
            IsVisible = false,
        };
        DockPanel.SetDock(search, Dock.Top);
        var results = new ListBox
        {
            IsVisible = false,
            MaxHeight = Math.Max(120, box.MaxDropDownHeight - 40),
            VerticalAlignment = VerticalAlignment.Top,
        };
        border.Child = null;
        var body = new Panel();
        body.Children.Add(list);
        body.Children.Add(results);
        var root = new DockPanel();
        root.Children.Add(search);
        root.Children.Add(body);
        border.Child = root;

        var state = new State { Search = search, Results = results, List = list };
        States.AddOrUpdate(box, state);
        search.TextChanged += (_, _) => Filter(box, state);
        search.AddHandler(InputElement.KeyDownEvent, (_, k) => OnSearchKey(box, state, k), RoutingStrategies.Bubble, handledEventsToo: false);
        results.Tapped += (_, _) => Pick(box, state);
    }

    internal static void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox box || !States.TryGetValue(box, out var state))
            return;
        bool shown = box.ItemCount >= MinItems;
        state.Search.IsVisible = shown;
        state.Search.Text = "";
        Filter(box, state);
        if (shown)
            Dispatcher.UIThread.Post(() => state.Search.Focus(), DispatcherPriority.Input);
    }

    private static void Filter(ComboBox box, State state)
    {
        string query = TypeAhead.Simplify(state.Search.Text?.Trim() ?? "");
        if (query.Length == 0)
        {
            state.Results.IsVisible = false;
            state.List.IsVisible = true;
            state.Results.ItemsSource = null;
            return;
        }
        var first = new List<Hit>();
        var rest = new List<Hit>();
        for (int i = 0; i < box.ItemCount && first.Count + rest.Count < MaxHits; i++)
        {
            string label = TypeAhead.Text(box, i);
            if (UnnamedEntry.IsPlaceholder(label))
                continue;
            string text = TypeAhead.Simplify(label);
            if (!text.Contains(query, StringComparison.Ordinal))
                continue;
            bool word = text.StartsWith(query, StringComparison.Ordinal)
                        || text.Split(TypeAhead.Separators, StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(query, StringComparison.Ordinal));
            (word ? first : rest).Add(new Hit(i, label));
        }
        var hits = first.Concat(rest).ToList();
        state.Results.ItemsSource = hits;
        state.Results.SelectedIndex = hits.Count > 0 ? 0 : -1;
        state.Results.IsVisible = true;
        state.List.IsVisible = false;
    }

    private static void OnSearchKey(ComboBox box, State state, KeyEventArgs e)
    {
        var results = state.Results;
        switch (e.Key)
        {
            case Key.Down when results.IsVisible && results.ItemCount > 0:
                results.SelectedIndex = Math.Min(results.ItemCount - 1, results.SelectedIndex + 1);
                results.ScrollIntoView(results.SelectedIndex);
                e.Handled = true;
                break;
            case Key.Up when results.IsVisible && results.ItemCount > 0:
                results.SelectedIndex = Math.Max(0, results.SelectedIndex - 1);
                results.ScrollIntoView(results.SelectedIndex);
                e.Handled = true;
                break;
            case Key.Enter:
                if (results.IsVisible)
                    Pick(box, state);
                e.Handled = true;
                break;
            case Key.Escape:
                box.IsDropDownOpen = false;
                e.Handled = true;
                break;
        }
    }

    private static void Pick(ComboBox box, State state)
    {
        if (state.Results.SelectedItem is not Hit hit)
            return;
        box.SelectedIndex = hit.Index;
        box.IsDropDownOpen = false;
        box.Focus();
    }
}
