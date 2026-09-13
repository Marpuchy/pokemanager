using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Services;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.App.ViewModels;

public partial class MoveDetailViewModel : ObservableObject
{
    private const string M = GameTables.Moves;
    private readonly EditorSession session;

    public int Id { get; }
    public string Title { get; }
    public IReadOnlyList<FieldViewModel> Main { get; }
    public IReadOnlyList<IntFieldViewModel> Details { get; }

    public MoveDetailViewModel(EditorSession session, GameNames names, int id)
    {
        this.session = session;
        Id = id;
        Title = $"#{id:000} {names.Moves[id]}";

        IntFieldViewModel Int(string field, string label, int min = 0, int max = 255) => new(session, M, id, field, label, min, max);

        Main =
        [
            new ChoiceFieldViewModel(session, M, id, "type", "Tipo", names.Types),
            new ChoiceFieldViewModel(session, M, id, "category", "Categoría", GameNames.MoveCategories),
            Int("power", "Potencia"),
            Int("accuracy", "Precisión (101 = no falla)"),
            Int("pp", "PP"),
            Int("priority", "Prioridad", -7, 7),
        ];
        Details =
        [
            Int("critStage", "Nivel de crítico"),
            Int("flinch", "Retroceso (%)"),
            Int("hitMin", "Golpes mín.", 0, 15),
            Int("hitMax", "Golpes máx.", 0, 15),
            Int("recoil", "Retroceso/absorción (%)", -128, 127),
            Int("inflictPercent", "Prob. de efecto (%)"),
        ];
    }

    public bool IsModified => session.IsModified(M, Id);

    [RelayCommand]
    private void Revert()
    {
        session.Revert(M, Id);
        foreach (var f in Main.Concat(Details))
            f.Refresh();
        OnPropertyChanged(string.Empty);
    }
}
