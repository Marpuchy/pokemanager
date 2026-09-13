using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.Model.Editing;

namespace Pokemanager.App.ViewModels;

/// <summary>Base de un campo editable enlazado a (tabla, id, campo) de la sesión.</summary>
public abstract class FieldViewModel(EditorSession session, string table, int id, string field, string label) : ObservableObject
{
    protected EditorSession Session { get; } = session;
    protected string Table { get; } = table;
    protected int Id { get; } = id;
    protected string Field { get; } = field;

    public string Label { get; } = label;
    public bool IsModified => Session.IsModified(Table, Id, Field);
    public string OriginalTip => $"Original: {FormatOriginal(Session.GetOriginal(Table, Id, Field).GetValue<int>())}";

    protected virtual string FormatOriginal(int value) => value.ToString();

    protected void Write(int value)
    {
        if (Session.GetInt(Table, Id, Field) == value)
            return;
        Session.SetInt(Table, Id, Field, value);
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
    }
}

public sealed class IntFieldViewModel(EditorSession session, string table, int id, string field, string label, int min, int max)
    : FieldViewModel(session, table, id, field, label)
{
    public int Min { get; } = min;
    public int Max { get; } = max;

    public decimal? Value
    {
        get => Session.GetInt(Table, Id, Field);
        set
        {
            if (value is null)
                return;
            Write((int)Math.Clamp(value.Value, Min, Max));
        }
    }
}

public sealed class ChoiceFieldViewModel(EditorSession session, string table, int id, string field, string label, IReadOnlyList<string> options)
    : FieldViewModel(session, table, id, field, label)
{
    public IReadOnlyList<string> Options { get; } = options;

    public int SelectedIndex
    {
        get => Session.GetInt(Table, Id, Field);
        set
        {
            if (value < 0 || value >= Options.Count)
                return;
            Write(value);
        }
    }

    protected override string FormatOriginal(int value) =>
        value >= 0 && value < Options.Count ? Options[value] : value.ToString();
}
