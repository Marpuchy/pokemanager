using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Pokemanager.App.Resources;

namespace Pokemanager.App.Controls;

/// <summary>
/// Any tab can be taken out to a window of its own (2026-11-11, user: "si yo cojo team/pokedex o lo que sea lo saco a
/// otra ventana, que muestre solamente eso"; chose dragging it out and a right-click menu). The tab keeps its place
/// with a note; closing the window puts the content back. Switched on for every <see cref="TabControl"/> by a style in
/// <c>ClassicTheme.axaml</c>; tabs built from <c>ItemsSource</c> (no <see cref="TabItem"/> with its own content) are left
/// alone.
/// </summary>
/// <remarks>
/// The content is moved, not copied, so it keeps its state and bindings. The window gets the styles of the views the tab
/// was in (a view's <c>UserControl.Styles</c> only reach its own children) and the data context the content had.
/// </remarks>
public static class TabTearOff
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TabControl, bool>("Enabled", typeof(TabTearOff));

    public static bool GetEnabled(TabControl control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(TabControl control, bool value) => control.SetValue(EnabledProperty, value);

    /// <summary>How far a tab must be dragged, away from its strip, before it is taken out.</summary>
    private const double Distance = 60;

    static TabTearOff() => EnabledProperty.Changed.AddClassHandler<TabControl>((tabs, e) =>
    {
        if (e.NewValue is true)
        {
            tabs.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            tabs.AddHandler(InputElement.PointerReleasedEvent, RightReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        }
    });

    private static TabControl? sourceTabs;
    private static TabItem? source;
    private static TopLevel? window;
    private static Point start;
    private static bool dragging;

    /// <summary>The tab whose header is under the event, when it belongs to <paramref name="tabs"/> and can be taken out.</summary>
    private static TabItem? HeaderOf(TabControl tabs, RoutedEventArgs e) =>
        e.Source is Visual v && v.FindAncestorOfType<TabItem>(includeSelf: true) is { } item
        && ReferenceEquals(item.FindAncestorOfType<TabControl>(), tabs) && item.Content is Control
            ? item
            : null;

    private static void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TabControl tabs || HeaderOf(tabs, e) is not { } item
            || !e.GetCurrentPoint(tabs).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(tabs) is not { } top)
            return;
        End();
        sourceTabs = tabs;
        source = item;
        window = top;
        start = e.GetPosition(top);
        top.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void Moved(object? sender, PointerEventArgs e)
    {
        if (window is null || source is null)
            return;
        if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
        {
            End();
            return;
        }
        bool away = Away(e.GetPosition(window));
        if (away && !dragging)
        {
            dragging = true;
            e.Pointer.Capture(window);
            window.Cursor = new Cursor(StandardCursorType.DragMove);
        }
        else if (!away && dragging)
        {
            dragging = false;
            window.Cursor = null;
        }
    }

    private static void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (window is null)
            return;
        var (tabs, item, take) = (sourceTabs, source, dragging);
        if (take)
        {
            e.Handled = true;
            e.Pointer.Capture(null);
        }
        End();
        if (take && tabs is not null && item is not null)
            Detach(tabs, item);
    }

    /// <summary>Far enough from where the drag started, and off the strip of tabs (or out of the window).</summary>
    private static bool Away(Point at)
    {
        if (window is null || sourceTabs is null)
            return false;
        if (at.X < 0 || at.Y < 0 || at.X > window.Bounds.Width || at.Y > window.Bounds.Height)
            return true;
        if (Point.Distance(at, start) < Distance)
            return false;
        var strip = sourceTabs.GetVisualDescendants().OfType<ItemsPresenter>().FirstOrDefault();
        if (strip?.TransformToVisual(window) is not { } toWindow)
            return true;
        var rect = new Rect(strip.Bounds.Size).TransformToAABB(toWindow).Inflate(24);
        return !rect.Contains(at);
    }

    private static void End()
    {
        if (window is not null)
        {
            window.RemoveHandler(InputElement.PointerMovedEvent, Moved);
            window.RemoveHandler(InputElement.PointerReleasedEvent, Released);
            window.Cursor = null;
        }
        dragging = false;
        source = null;
        sourceTabs = null;
        window = null;
    }

    private static void RightReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || sender is not TabControl tabs || HeaderOf(tabs, e) is not { } item)
            return;
        var open = new MenuItem { Header = Strings.Tab_Detach };
        open.Click += (_, _) => Detach(tabs, item);
        var menu = new ContextMenu { Items = { open } };
        ToolTip.SetTip(open, Strings.Tab_DetachTip);
        menu.Open(item);
        e.Handled = true;
    }

    /// <summary>A copy of a view's style for another window: a style can only belong to one place.</summary>
    private static IStyle? Copy(IStyle style)
    {
        switch (style)
        {
            case Style s:
                var copy = new Style { Selector = s.Selector };
                foreach (var setter in s.Setters)
                    copy.Setters.Add(setter);
                foreach (var child in s.Children)
                    if (Copy(child) is { } c)
                        copy.Children.Add(c);
                return copy;
            case Styles many:
                var all = new Styles();
                foreach (var child in many)
                    if (Copy(child) is { } c)
                        all.Add(c);
                return all;
            default:
                return null;
        }
    }

    /// <summary>Moves the tab's content to a window of its own until that window is closed.</summary>
    public static void Detach(TabControl tabs, TabItem item)
    {
        if (item.Content is not Control content || TopLevel.GetTopLevel(tabs) is not Window owner)
            return;
        object? context = content.DataContext;
        string title = item.Header switch
        {
            string text => text,
            TextBlock block => block.Text ?? "",
            _ => item.Header?.ToString() ?? "",
        };

        var host = new Window
        {
            Title = $"{title} — Pokemanager",
            // As big as it was on screen; a tab that was not showing gets three quarters of the main window.
            Width = Math.Max(640, content.Bounds.Width > 100 ? content.Bounds.Width + 32 : owner.ClientSize.Width * 0.75),
            Height = Math.Max(480, content.Bounds.Height > 100 ? content.Bounds.Height + 48 : owner.ClientSize.Height * 0.75),
            DataContext = context,
            Icon = owner.Icon,
        };
        // The styles of every view the tab sits in, outermost first, so the content looks as it did.
        foreach (var style in tabs.GetSelfAndLogicalAncestors().OfType<StyledElement>().Reverse()
                     .Where(e => e is not Window && e.Styles.Count > 0)
                     .SelectMany(e => e.Styles))
            if (Copy(style) is { } copy)
                host.Styles.Add(copy);

        var back = new Button { Content = Strings.Tab_Back, HorizontalAlignment = HorizontalAlignment.Left };
        var placeholder = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Strings.Tab_Detached, Classes = { "hint" } },
                back,
            },
        };
        back.Click += (_, _) => host.Close();

        item.Content = placeholder;
        host.Content = new Border { Padding = new Thickness(8), Child = content };
        host.Closed += (_, _) =>
        {
            if (host.Content is Border { Child: { } c } border)
            {
                border.Child = null;
                item.Content = c;
            }
            tabs.SelectedItem = item;
        };
        host.Show(owner);
    }
}
