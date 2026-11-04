using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pokemanager.App.ViewModels;

namespace Pokemanager.App.Controls;

/// <summary>
/// Drag and drop of the save editor's Pokémon between boxes and the party (2026-11-04, user). Switched on in the shared
/// <c>PartyCard</c>/<c>BoxCell</c> templates, it only acts when the slot is a <see cref="SlotViewModel"/>, so the project
/// preview and multiplayer rooms, which use the same templates read-only, are untouched.
/// </summary>
/// <remarks>
/// The pointer is captured by the window once a drag starts, not by the cell: changing box while dragging (holding the
/// Pokémon over ◀ or ▶) rebuilds the cells, and a cell that held the capture would end the drag as it went away. A
/// release over another slot moves the Pokémon there (swapping with what is in it); anywhere else nothing happens. The
/// release is marked handled, so the button the drag started on does not also take it as a click.
/// </remarks>
public static class SlotDrag
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(SlotDrag));

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    /// <summary>How far the pointer must travel before a press becomes a drag.</summary>
    private const double Threshold = 6;
    /// <summary>How long the Pokémon has to be held over ◀ or ▶ before the box changes, and again for the next one.</summary>
    private static readonly TimeSpan ArrowDelay = TimeSpan.FromMilliseconds(700);
    private const string DropClass = "drop";

    static SlotDrag() => EnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
    {
        if (e.NewValue is true)
        {
            control.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            control.AddHandler(InputElement.PointerMovedEvent, PendingMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            control.AddHandler(InputElement.PointerReleasedEvent, PendingReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    });

    // One drag at a time: the state of the one under way.
    private static SlotViewModel? source;
    private static Point start;
    private static TopLevel? window;
    private static bool dragging;
    private static Control? highlighted;
    private static Button? arrow;
    private static DateTime arrowSince;
    private static Point last;
    private static DispatcherTimer? timer;

    private static void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not SlotViewModel { IsEmpty: false } slot
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(control) is not { } top)
            return;
        End();
        source = slot;
        window = top;
        start = e.GetPosition(top);
    }

    private static void PendingMoved(object? sender, PointerEventArgs e)
    {
        if (source is null || dragging || window is null)
            return;
        var at = e.GetPosition(window);
        if (Math.Abs(at.X - start.X) < Threshold && Math.Abs(at.Y - start.Y) < Threshold)
            return;
        Begin(e);
    }

    private static void PendingReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging)
            source = null; // a click: the button selects the slot as always
    }

    private static void Begin(PointerEventArgs e)
    {
        if (window is null)
            return;
        dragging = true;
        window.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerCaptureLostEvent, CaptureLost, RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
        e.Pointer.Capture(window);
        // No tooltips while dragging: the one of the cell being left would open under the pointer.
        ToolTip.SetServiceEnabled(window, false);
        window.Cursor = new Cursor(StandardCursorType.DragMove);
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => Hover();
        timer.Start();
        last = e.GetPosition(window);
        Hover();
    }

    private static void Moved(object? sender, PointerEventArgs e)
    {
        if (!dragging || window is null)
            return;
        last = e.GetPosition(window);
        Hover();
        e.Handled = true;
    }

    private static void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging || window is null)
            return;
        last = e.GetPosition(window);
        var (target, _) = Under(last);
        var from = source;
        e.Handled = true;
        e.Pointer.Capture(null);
        End();
        if (from is not null && target?.DataContext is SlotViewModel to && to.Owner == from.Owner && to.Slot != from.Slot)
            from.Owner.MoveSlot(from.Slot, to.Slot);
    }

    private static void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (dragging)
            End();
    }

    /// <summary>Marks the slot under the pointer, and changes box when the Pokémon is held over ◀ or ▶.</summary>
    private static void Hover()
    {
        var (target, overArrow) = Under(last);
        if (!ReferenceEquals(target, highlighted))
        {
            highlighted?.Classes.Set(DropClass, false);
            highlighted = target;
            highlighted?.Classes.Set(DropClass, true);
        }
        if (!ReferenceEquals(overArrow, arrow))
        {
            arrow = overArrow;
            arrowSince = DateTime.UtcNow;
        }
        else if (arrow is not null && DateTime.UtcNow - arrowSince >= ArrowDelay)
        {
            if (arrow.Command is { } command && command.CanExecute(arrow.CommandParameter))
                command.Execute(arrow.CommandParameter);
            arrowSince = DateTime.UtcNow;
        }
    }

    /// <summary>The slot control under a point of the window, or the box arrow.</summary>
    private static (Control? Slot, Button? Arrow) Under(Point point)
    {
        if (window is null)
            return (null, null);
        foreach (var visual in window.GetVisualsAt(point))
            foreach (var v in visual.GetSelfAndVisualAncestors())
            {
                if (v is Control c && GetEnabled(c) && c.DataContext is SlotViewModel slot && source is not null && slot.Owner == source.Owner)
                    return (c, null);
                if (v is Button b && b.DataContext is IBoxBrowser browser
                    && (ReferenceEquals(b.Command, browser.PreviousBoxCommand) || ReferenceEquals(b.Command, browser.NextBoxCommand)))
                    return (null, b);
            }
        return (null, null);
    }

    private static void End()
    {
        timer?.Stop();
        timer = null;
        highlighted?.Classes.Set(DropClass, false);
        highlighted = null;
        arrow = null;
        if (window is not null && dragging)
        {
            window.RemoveHandler(InputElement.PointerMovedEvent, Moved);
            window.RemoveHandler(InputElement.PointerReleasedEvent, Released);
            window.RemoveHandler(InputElement.PointerCaptureLostEvent, CaptureLost);
            window.Cursor = null;
            window.ClearValue(ToolTip.ServiceEnabledProperty);
        }
        dragging = false;
        source = null;
        window = null;
    }
}
