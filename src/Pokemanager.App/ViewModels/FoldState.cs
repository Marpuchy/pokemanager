using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// A panel that can be folded with a small chevron (2026-11-11, user: "desplegables y plegables… con una flechita sutil"):
/// the recent projects and the team fold to the left, the trials up. Remembered in <see cref="AppSettings.FoldedPanels"/>.
/// </summary>
public sealed partial class FoldState : ObservableObject
{
    private readonly AppSettings settings;
    private readonly string name;
    private readonly bool up;

    public FoldState(AppSettings settings, string name, bool up = false)
    {
        this.settings = settings;
        this.name = name;
        this.up = up;
        IsFolded = settings.IsFolded(name);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(Angle), nameof(Tip))]
    public partial bool IsFolded { get; set; }

    public bool IsOpen => !IsFolded;

    /// <summary>The chevron (drawn pointing right) turned to where the panel goes: back when open, out when folded.</summary>
    public double Angle => up ? (IsFolded ? 90 : 270) : (IsFolded ? 0 : 180);

    public string Tip => IsFolded ? Strings.Panel_Unfold : Strings.Panel_Fold;

    partial void OnIsFoldedChanged(bool value) => settings.SetFolded(name, value);

    [RelayCommand]
    private void Toggle() => IsFolded = !IsFolded;
}
