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
    /// <summary>
    /// What the field is, from the resources (<c>Tip_Field_&lt;table&gt;_&lt;field&gt;</c>, the team slot of a trainer dropped
    /// so the six share one text), or null when nothing is written for it.
    /// </summary>
    public string? Description =>
        Strings.ResourceManager.GetString($"Tip_Field_{Table}_{Field[(Field.IndexOf('.') + 1)..]}", Strings.Culture);

    /// <summary>On hover: what the field is, and what the game had there before any edit.</summary>
    public string OriginalTip
    {
        get
        {
            string original = string.Format(Strings.Field_OriginalTip, FormatOriginal(Session.GetOriginal(Table, Id, Field).GetValue<int>()));
            return Description is { } what ? what + Environment.NewLine + original : original;
        }
    }

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

/// <summary>
/// The AI level of a trainer: bits 0-2 of the AI byte as a single list, because the game only ever uses five of the
/// eight combinations and three check boxes made "expert" look like three separate switches. The other bits are kept.
/// </summary>
public sealed class AiLevelFieldViewModel(EditorSession session, string table, int id, string field, string label)
    : FieldViewModel(session, table, id, field, label)
{
    /// <summary>The combinations the game gives itself, measured on the real dump (see <c>docs/game-ai.md</c>).</summary>
    public static readonly int[] Values = [0x00, 0x01, 0x03, 0x05, 0x07];

    private static IReadOnlyList<string>? labels;

    /// <summary>
    /// Built once and kept: this list is bound straight to an <c>ItemsSource</c> next to a two-way <c>SelectedIndex</c>,
    /// and a fresh instance on every read makes the ComboBox drop its selection and write the old value back.
    /// </summary>
    public static IReadOnlyList<string> Labels =>
        labels ??= [Strings.Ai_LevelNone, Strings.Ai_LevelBasic, Strings.Ai_LevelStrong, Strings.Ai_LevelExpert, Strings.Ai_LevelFull];

    public IReadOnlyList<string> Options => Labels;

    /// <summary>-1 when the byte holds a combination the game never uses (only reachable by editing the value by hand).</summary>
    public int SelectedIndex
    {
        get => Array.IndexOf(Values, Session.GetInt(Table, Id, Field) & 0x07);
        set
        {
            if ((uint)value >= Values.Length)
                return;
            Write((Session.GetInt(Table, Id, Field) & ~0x07) | Values[value]);
        }
    }

    protected override string FormatOriginal(int value) =>
        Array.IndexOf(Values, value & 0x07) is var i and >= 0 ? Labels[i] : $"0x{value:X2}";
}

/// <summary>One bit of an int field, shown as a check box: the AI flags of a trainer.</summary>
/// <param name="bit">0-based bit number.</param>
public sealed class BitFieldViewModel(EditorSession session, string table, int id, string field, string label, int bit, string tip)
    : FieldViewModel(session, table, id, field, label)
{
    public string Tip { get; } = tip;

    /// <summary>"bit 2 · 0x04", so the value in the file is always visible.</summary>
    public string BitText => string.Format(Strings.Ai_BitText, bit, 1 << bit);

    public bool IsSet
    {
        get => (Session.GetInt(Table, Id, Field) & (1 << bit)) != 0;
        set
        {
            int current = Session.GetInt(Table, Id, Field);
            Write(value ? current | (1 << bit) : current & ~(1 << bit));
        }
    }

    protected override string FormatOriginal(int value) => ((value & (1 << bit)) != 0).ToString();
}

/// <summary>A field shown as a check box (the trainer's healer flag).</summary>
public sealed class BoolFieldViewModel(EditorSession session, string table, int id, string field, string label)
    : FieldViewModel(session, table, id, field, label)
{
    public bool IsSet
    {
        get => Session.GetInt(Table, Id, Field) != 0;
        set => Write(value ? 1 : 0);
    }

    protected override string FormatOriginal(int value) => (value != 0).ToString();
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
