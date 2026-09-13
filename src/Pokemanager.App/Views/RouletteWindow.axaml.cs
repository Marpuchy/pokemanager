using Avalonia.Controls;
using Avalonia.Interactivity;
using Pokemanager.App.Controls;
using Pokemanager.App.ViewModels;

namespace Pokemanager.App.Views;

public partial class RouletteWindow : Window
{
    private const double Duration = 4.2;

    public RouletteWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is RouletteViewModel vm)
                vm.SpinRequested += (_, winner) => Animate(vm, winner);
        };
        // The prize was already decided when the wheel started: closing mid-spin must not give a free second try.
        Closing += (_, _) =>
        {
            if (DataContext is RouletteViewModel { State: RouletteState.Spinning } vm)
                vm.SpinFinished();
        };
    }

    /// <summary>
    /// Turns the wheel several times and eases out so the winning segment stops under the pointer, at a random point
    /// inside it (never on its edge).
    /// </summary>
    private void Animate(RouletteViewModel vm, int winner)
    {
        var (start, end) = WheelControl.Spans(vm.Segments)[winner];
        double inside = start + ((end - start) * (0.2 + (Random.Shared.NextDouble() * 0.6)));
        double from = Wheel.Angle % 360;
        double target = (360 * 6) + (360 - inside);
        var begun = DateTime.UtcNow;

        void Frame(TimeSpan _)
        {
            if (vm.State != RouletteState.Spinning)
                return; // finished early (window closed)
            double t = Math.Min(1, (DateTime.UtcNow - begun).TotalSeconds / Duration);
            double eased = 1 - Math.Pow(1 - t, 4);
            Wheel.Angle = from + ((target - from) * eased);
            if (t >= 1)
                vm.SpinFinished();
            else
                RequestAnimationFrame(Frame);
        }
        RequestAnimationFrame(Frame);
    }

    /// <summary>For tests and previews: place the wheel on the winner without animating.</summary>
    public void JumpTo(RouletteViewModel vm, int winner)
    {
        var (start, end) = WheelControl.Spans(vm.Segments)[winner];
        Wheel.Angle = 360 - ((start + end) / 2);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
