using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
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
            new ChoiceFieldViewModel(session, M, id, "type", Strings.Move_Type, names.Types),
            new ChoiceFieldViewModel(session, M, id, "category", Strings.Move_Category, GameNames.MoveCategories),
            Int("power", Strings.Move_Power),
            Int("accuracy", Strings.Move_Accuracy),
            Int("pp", Strings.Move_PP),
            Int("priority", Strings.Move_Priority, -7, 7),
        ];
        Details =
        [
            Int("critStage", Strings.Move_CritStage),
            Int("flinch", Strings.Move_Flinch),
            Int("hitMin", Strings.Move_HitMin, 0, 15),
            Int("hitMax", Strings.Move_HitMax, 0, 15),
            Int("recoil", Strings.Move_Recoil, -128, 127),
            Int("inflictPercent", Strings.Move_EffectChance),
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
