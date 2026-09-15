using System.Text.Json.Nodes;
using Avalonia.Media;
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
    private readonly GameNames names;

    public int Id { get; }
    public string Title { get; }
    public string Name { get; }
    public IReadOnlyList<FieldViewModel> Main { get; }
    public IReadOnlyList<IntFieldViewModel> Details { get; }

    public MoveDetailViewModel(EditorSession session, GameNames names, int id)
    {
        this.session = session;
        this.names = names;
        Id = id;
        Name = names.Moves[id];
        Title = $"#{id:000} {Name}";

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
        // The colored header follows the values as they are edited.
        foreach (var field in Main)
            field.PropertyChanged += (_, _) => NotifyHeader();
    }

    // ------------------------------------------------------------------ header

    private int TypeId => session.GetInt(M, Id, "type");
    private int CategoryId => session.GetInt(M, Id, "category");

    public IBrush TypeBackground => TypeColors.Background(TypeId);
    public IBrush TypeForeground => TypeColors.Foreground(TypeId);
    public TypeChip TypeChip => new(TypeId < names.Types.Count ? names.Types[TypeId] : "?", TypeColors.Background(TypeId), TypeColors.Foreground(TypeId));

    public string CategoryText => CategoryId < GameNames.MoveCategories.Count ? GameNames.MoveCategories[CategoryId] : "?";

    public Avalonia.Media.Imaging.Bitmap? TypeIcon => PkhexImages.Type(TypeId);
    public IImage CategoryIcon => PkhexImages.Category(CategoryId);

    /// <summary>Showdown's colors: physical red-orange, special blue-grey, status grey.</summary>
    public IBrush CategoryBrush => new SolidColorBrush(Color.Parse(CategoryId switch { 1 => "#C92112", 2 => "#4F5870", _ => "#8C888C" }));

    public string PowerText => session.GetInt(M, Id, "power") is var p and > 1 ? p.ToString() : "—";
    public string AccuracyText => session.GetInt(M, Id, "accuracy") is var a and > 0 and <= 100 ? a + "%" : "—";
    public string PpText => session.GetInt(M, Id, "pp").ToString();
    public string PriorityText => session.GetInt(M, Id, "priority") is var p and not 0 ? p.ToString("+0;-0") : "0";

    private void NotifyHeader()
    {
        foreach (string property in new[]
                 {
                     nameof(TypeBackground), nameof(TypeForeground), nameof(TypeChip), nameof(CategoryText), nameof(CategoryBrush), nameof(TypeIcon), nameof(CategoryIcon),
                     nameof(PowerText), nameof(AccuracyText), nameof(PpText), nameof(PriorityText), nameof(IsModified),
                 })
            OnPropertyChanged(property);
    }

    // ------------------------------------------------------------------ description

    /// <summary>The description: the game's English text, or the edited one (written to every language of the game).</summary>
    public string Description
    {
        get => session.Get(GameTables.MoveTexts, Id, GameTables.Description).GetValue<string>();
        set
        {
            if (value == Description)
                return;
            session.Set(GameTables.MoveTexts, Id, GameTables.Description, JsonValue.Create(value ?? ""));
            OnPropertyChanged(nameof(IsDescriptionModified));
            OnPropertyChanged(nameof(IsModified));
        }
    }

    public string OriginalDescription => session.GetOriginal(GameTables.MoveTexts, Id, GameTables.Description).GetValue<string>();
    public bool IsDescriptionModified => session.IsModified(GameTables.MoveTexts, Id, GameTables.Description);

    [RelayCommand]
    private void RevertDescription()
    {
        session.Revert(GameTables.MoveTexts, Id);
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsDescriptionModified));
        OnPropertyChanged(nameof(IsModified));
    }

    public bool IsModified => session.IsModified(M, Id) || session.IsModified(GameTables.MoveTexts, Id);

    [RelayCommand]
    private void Revert()
    {
        session.Revert(M, Id);
        session.Revert(GameTables.MoveTexts, Id);
        foreach (var f in Main.Concat(Details))
            f.Refresh();
        OnPropertyChanged(string.Empty);
    }
}
