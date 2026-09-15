using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Resources;
using Pokemanager.Model.Editing;

namespace Pokemanager.App.ViewModels;

/// <summary>Base of an editable field bound to (table, id, field) of the session.</summary>
public abstract class FieldViewModel(EditorSession session, string table, int id, string field, string label) : ObservableObject
{
    protected EditorSession Session { get; } = session;
    protected string Table { get; } = table;
    protected int Id { get; } = id;
    protected string Field { get; } = field;

    public string Label { get; } = label;
    public bool IsModified => Session.IsModified(Table, Id, Field);
    public string OriginalTip => string.Format(Strings.Field_OriginalTip, FormatOriginal(Session.GetOriginal(Table, Id, Field).GetValue<int>()));

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

public class IntFieldViewModel(EditorSession session, string table, int id, string field, string label, int min, int max)
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

/// <summary>A base stat with a bar, colored like Showdown's (red when low, green and teal when high).</summary>
public sealed class StatFieldViewModel(EditorSession session, string table, int id, string field, string label)
    : IntFieldViewModel(session, table, id, field, label, 0, 255)
{
    private const double FullBar = 180;

    private int Current => Session.GetInt(Table, Id, Field);

    public double BarWidth => Math.Max(2, Math.Min(Current, 200) / 200.0 * FullBar);

    public Avalonia.Media.IBrush BarBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Current switch
    {
        < 30 => "#F34444",
        < 60 => "#FF7F0F",
        < 90 => "#FFDD57",
        < 120 => "#A0E515",
        < 150 => "#23CD5E",
        _ => "#00C2B8",
    }));
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
