using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Data;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// One evolution of a personal row, editable: how (method), into what (Pokémon and, in Generation 7, form), and what the
/// method needs — a level, an item, a move, a Pokémon in the team or a move type. Each need has its own control, shown
/// for the methods that use it, so no list ever changes under a combo box's selection.
/// </summary>
public sealed partial class EvolutionRowViewModel : ObservableObject
{
    private readonly EditorSession session;
    private readonly int id;
    private readonly int slot;
    private readonly Action<EvolutionRowViewModel> remove;
    private readonly Action changed;

    private readonly PokemonSprites? sprites;

    public EvolutionRowViewModel(EditorSession session, GameNames names, int id, int slot, bool generation7,
        Action<EvolutionRowViewModel> remove, Action changed, PokemonSprites? sprites = null)
    {
        this.sprites = sprites;
        this.session = session;
        this.id = id;
        this.slot = slot;
        this.remove = remove;
        this.changed = changed;
        Generation7 = generation7;
        Species = names.Species;
        Items = names.Items;
        Moves = names.Moves;
        Types = names.Types;
    }

    public int Slot => slot;
    public bool Generation7 { get; }
    public IReadOnlyList<string> Methods => EvolutionText.Methods;
    public IReadOnlyList<string> Species { get; }
    public IReadOnlyList<string> Items { get; }
    public IReadOnlyList<string> Moves { get; }
    public IReadOnlyList<string> Types { get; }

    private int Get(string field) => session.GetInt(GameTables.Evolutions, id, GameTables.EvolutionField(slot, field));

    private void Set(string field, int value)
    {
        if (Get(field) == value)
            return;
        session.SetInt(GameTables.Evolutions, id, GameTables.EvolutionField(slot, field), value);
        changed();
    }

    public int Method
    {
        get => Get("method");
        set
        {
            if ((uint)value >= EvolutionSlots.MethodCount || value == Method)
                return;
            int old = Method;
            Set("method", value);
            // What the old method needed means nothing to the new one: in Generation 6 a level and an item even share
            // the same number.
            if (EvolutionSlots.ArgumentOf(old) != EvolutionSlots.ArgumentOf(value)
                || !Generation7 && EvolutionSlots.UsesLevel(old) != EvolutionSlots.UsesLevel(value))
                Set("argument", 0);
            if (Generation7 && !EvolutionSlots.UsesLevel(value))
                Set("level", 0);
        }
    }

    public int Into
    {
        get => Get("species");
        set { if (value >= 0 && value < Species.Count) Set("species", value); }
    }

    /// <summary>Generation 7: the form it becomes, -1 to keep its own.</summary>
    public decimal? Form
    {
        get => Get("form");
        set { if (value is { } v) Set("form", (int)Math.Clamp(v, -1, 127)); }
    }

    /// <summary>The Pokémon it becomes, as the line shows it: picture and its type colours.</summary>
    public Avalonia.Media.Imaging.Bitmap? TargetIcon => sprites?.For(Into, TargetForm);

    /// <summary>The form it becomes, read the way the game does (Espurr's female branch is form 1 although it says -1).</summary>
    private int TargetForm
    {
        get
        {
            var data = session.Current;
            var index = PersonalIndex.Of(data);
            int count = Into > 0 && Into < data.Personal.Length && data.Personal[Into].FormeCount is > 0 and var n ? n : 1;
            var slot = data.Evolutions[id].Slot(this.slot)!;
            return EvolutionSlots.TargetForm(slot, index.Pokemon(id)?.Form ?? 0, count, Generation7);
        }
    }

    public Avalonia.Media.IBrush TargetBrush
    {
        get
        {
            var data = session.Current;
            int row = PersonalIndex.Of(data).Row(Into, TargetForm);
            if (row < 0)
                row = Into;
            return row > 0 && row < data.Personal.Length
                ? TypeColors.Background(data.Personal[row].Types[0], data.Personal[row].Types[1])
                : TypeColors.Background(0);
        }
    }

    public bool ShowLevel => EvolutionSlots.UsesLevel(Method);
    public bool ShowItem => EvolutionSlots.ArgumentOf(Method) == EvolutionArgument.Item;
    public bool ShowMove => EvolutionSlots.ArgumentOf(Method) == EvolutionArgument.Move;
    public bool ShowPartner => EvolutionSlots.ArgumentOf(Method) == EvolutionArgument.Species;
    public bool ShowType => EvolutionSlots.ArgumentOf(Method) == EvolutionArgument.Type;
    public bool ShowFriendshipNote => Method is 1 or 2 or 3;

    /// <summary>In Generation 6 the level of a level method is the argument itself.</summary>
    public decimal? Level
    {
        get => Generation7 ? Get("level") : Get("argument");
        set { if (value is { } v) Set(Generation7 ? "level" : "argument", (int)Math.Clamp(v, 0, 100)); }
    }

    public int Item
    {
        get => Get("argument");
        set { if (value >= 0 && value < Items.Count) Set("argument", value); }
    }

    public int Move
    {
        get => Get("argument");
        set { if (value >= 0 && value < Moves.Count) Set("argument", value); }
    }

    public int Partner
    {
        get => Get("argument");
        set { if (value >= 0 && value < Species.Count) Set("argument", value); }
    }

    public int MoveType
    {
        get => Get("argument");
        set { if (value >= 0 && value < Types.Count) Set("argument", value); }
    }

    public bool IsModified => GameTables.EvolutionTable.Fields.Where(f => f.StartsWith($"e{slot + 1}.", StringComparison.Ordinal))
        .Any(f => session.IsModified(GameTables.Evolutions, id, f));

    [RelayCommand]
    private void Remove() => remove(this);

    public void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>Empties the slot: the game skips a slot whose method is 0.</summary>
    internal void Clear()
    {
        foreach (string field in new[] { "method", "species", "argument", "level" })
            session.SetInt(GameTables.Evolutions, id, GameTables.EvolutionField(slot, field), 0);
        if (Generation7)
            session.SetInt(GameTables.Evolutions, id, GameTables.EvolutionField(slot, "form"), -1);
    }
}
