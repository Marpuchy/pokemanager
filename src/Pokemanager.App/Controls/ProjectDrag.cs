using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Pokemanager.App.ViewModels;

namespace Pokemanager.App.Controls;

/// <summary>
/// A project of the start screen dragged onto the bin below the list (2026-11-11, user: "la x se debe poner debajo de
/// los proyectos y que estos se puedan arrastrar ahí"). Dropping it there runs <see cref="RecentProjectItem.ForgetCommand"/>,
/// which asks before deleting, as the ✕ did. Same pattern as <see cref="SlotDrag"/>: the window holds the capture.
/// </summary>
public static class ProjectDrag
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ProjectDrag));

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    /// <summary>The drop target: a control with this class.</summary>
    public const string TrashClass = "trash";
    private const string DropClass = "drop";
    private const string DraggingClass = "dragging";
    private const double Threshold = 6;

    static ProjectDrag() => EnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
    {
        if (e.NewValue is true)
        {
            control.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerMovedEvent, PendingMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            control.AddHandler(InputElement.PointerReleasedEvent, PendingReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    });

    private static RecentProjectItem? source;
    private static Control? sourceControl;
    private static Point start;
    private static TopLevel? window;
    private static bool dragging;
    private static Control? highlighted;

    private static void Pressed(object? sender, PointerPressedEventArgs e)
    {
        // A press on the Open button stays a click.
        if (sender is not Control control || control.DataContext is not RecentProjectItem item
            || e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(control) is not { } top)
            return;
        End();
        source = item;
        sourceControl = control;
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
        dragging = true;
        window.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerCaptureLostEvent, CaptureLost, RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
        e.Pointer.Capture(window);
        ToolTip.SetServiceEnabled(window, false);
        window.Cursor = new Cursor(StandardCursorType.DragMove);
        sourceControl?.Classes.Set(DraggingClass, true);
        Hover(at);
    }

    private static void PendingReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging)
            source = null;
    }

    private static void Moved(object? sender, PointerEventArgs e)
    {
        if (!dragging || window is null)
            return;
        Hover(e.GetPosition(window));
        e.Handled = true;
    }

    private static void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging || window is null)
            return;
        var target = Under(e.GetPosition(window));
        var item = source;
        e.Handled = true;
        e.Pointer.Capture(null);
        End();
        if (target is not null && item is not null && item.ForgetCommand.CanExecute(null))
            item.ForgetCommand.Execute(null);
    }

    private static void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (dragging)
            End();
    }

    private static void Hover(Point at)
    {
        var target = Under(at);
        if (ReferenceEquals(target, highlighted))
            return;
        highlighted?.Classes.Set(DropClass, false);
        highlighted = target;
        highlighted?.Classes.Set(DropClass, true);
    }

    private static Control? Under(Point point)
    {
        if (window is null)
            return null;
        foreach (var visual in window.GetVisualsAt(point))
            foreach (var v in visual.GetSelfAndVisualAncestors())
                if (v is Control c && c.Classes.Contains(TrashClass))
                    return c;
        return null;
    }

    private static void End()
    {
        highlighted?.Classes.Set(DropClass, false);
        highlighted = null;
        sourceControl?.Classes.Set(DraggingClass, false);
        sourceControl = null;
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
