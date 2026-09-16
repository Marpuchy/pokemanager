using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using Pokemanager.App.Resources;
using Pokemanager.Model.Data;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Projects;
using Pokemanager.Model.Dump;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>A party or box slot button.</summary>
public partial class SlotViewModel(SaveEditorViewModel owner, SaveSlot slot) : ObservableObject, IMonCard
{
    public SaveSlot Slot { get; } = slot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    public partial string Detail { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyMark))]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    [ObservableProperty]
    public partial Avalonia.Media.Imaging.Bitmap? Icon { get; set; }

    [RelayCommand]
    private void Select() => owner.SelectSlot(Slot);

    public string Tooltip => IsEmpty ? Name : $"{Name} · {Detail}";

    /// <summary>Box slots show only the sprite; an empty one shows a dash.</summary>
    public bool ShowEmptyMark => IsEmpty && !Slot.IsParty;

    // Look of a filled slot, as the project preview: type colors, held item, level, gender and shiny.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeBackground), nameof(TypeForeground))]
    public partial int[] Types { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<TypeChip> TypeChips { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItem))]
    public partial Avalonia.Media.Imaging.Bitmap? ItemIcon { get; set; }

    [ObservableProperty]
    public partial string LevelText { get; set; } = "";

    [ObservableProperty]
    public partial string GenderText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsShiny { get; set; }

    [ObservableProperty]
    public partial bool IsEgg { get; set; }

    public bool HasItem => ItemIcon is not null;

    public Avalonia.Media.IBrush TypeBackground => IsEmpty || Types.Length == 0
        ? EmptyBrush
        : TypeColors.Background(Types[0], Types[^1]);

    private static readonly Avalonia.Media.IBrush EmptyBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F2F4F6"));

    public Avalonia.Media.IBrush TypeForeground => Types.Length == 0 ? Avalonia.Media.Brushes.Black : TypeColors.Foreground(Types);

    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(TypeBackground));
}

/// <summary>A problem line; clicking it selects the Pokémon it belongs to.</summary>
public sealed partial class ProblemViewModel(SaveEditorViewModel owner, SaveProblem problem) : ObservableObject
{
    public string Text { get; } = problem.Slot is { } s ? $"{SaveEditorViewModel.SlotLabel(s)}: {problem.Message}" : problem.Message;

    [RelayCommand]
    private void Go()
    {
        if (problem.Slot is { } slot)
            owner.SelectSlot(slot);
    }
}

/// <summary>Save file tab: a PKHeX-style editor for the emulator's save, written only on demand.</summary>
public partial class SaveEditorViewModel : ObservableObject, IBoxBrowser
{
    private readonly EditorViewModel editor;
    private readonly AppSettings settings;
    private SaveNames? names;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial SaveDocument? Document { get; private set; }

    public bool IsOpen => Document is not null;

    /// <summary>Why there is no document (no emulator, no save, unreadable…).</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = Strings.Save_NotLoaded;

    [ObservableProperty]
    public partial string HeaderText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    public ObservableCollection<SlotViewModel> PartySlots { get; } = [];
    public ObservableCollection<SlotViewModel> BoxSlots { get; } = [];
    public ObservableCollection<string> BoxNames { get; } = [];

    System.Collections.IEnumerable IBoxBrowser.BoxNames => BoxNames;
    System.Collections.IEnumerable IBoxBrowser.BoxSlots => BoxSlots;
    public ObservableCollection<ProblemViewModel> Problems { get; } = [];

    [ObservableProperty]
    public partial int SelectedBox { get; set; }

    [ObservableProperty]
    public partial SaveSlot? SelectedSlot { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPokemon))]
    public partial PokemonEditorViewModel? Pokemon { get; set; }

    public bool HasPokemon => Pokemon is not null;

    /// <summary>The selected slot is empty: offer to create a Pokémon there.</summary>
    [ObservableProperty]
    public partial bool CanCreate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSpeciesIcon))]
    public partial int NewSpecies { get; set; } = 1;

    public Avalonia.Media.Imaging.Bitmap? NewSpeciesIcon => Sprites.For(NewSpecies);

    public PokemonSprites Sprites => editor.Sprites;

    /// <summary>Game texts of the project (Pokédex entries, classifications).</summary>
    public GameNames GameNames => editor.Names;

    public GameTitle Game => editor.Dump.Title;

    [ObservableProperty]
    public partial decimal? NewLevel { get; set; } = 5;

    public IReadOnlyList<string> SpeciesChoices => names?.SpeciesChoices ?? [];

    [ObservableProperty]
    public partial SaveBagViewModel? Bag { get; set; }

    [ObservableProperty]
    public partial SaveTrainerViewModel? Trainer { get; set; }

    [ObservableProperty]
    public partial SaveDexViewModel? Dex { get; set; }

    public string ProblemsText => Problems.Count == 0 ? Strings.Save_NoProblems : string.Format(Strings.Save_ProblemsBlock, Problems.Count);
    public bool HasProblems => Problems.Count > 0;

    /// <summary>The project changed since the ROM was built: what is shown follows the project, not the ROM being played.</summary>
    public string? RomWarning => editor.ProjectDiffersFromBuiltRom ? Strings.Save_RomWarning : null;

    /// <summary>The editor rebuilt or something changed: the warning about the ROM may have appeared or gone.</summary>
    public void RefreshRomWarning()
    {
        OnPropertyChanged(nameof(RomWarning));
        OnPropertyChanged(nameof(HasRomWarning));
    }
    public bool HasRomWarning => RomWarning is not null;

    public SaveEditorViewModel(EditorViewModel editor, AppSettings settings)
    {
        this.editor = editor;
        this.settings = settings;
        Undo = new SnapshotHistory(() => Document?.CaptureState() ?? [], RestoreState);
    }

    /// <summary>Undo of the save edits: every change keeps the whole save before it (compressed).</summary>
    public SnapshotHistory Undo { get; }

    private bool restoring;

    private void RestoreState(byte[] state)
    {
        if (Document is null || state.Length == 0)
            return;
        restoring = true;
        try
        {
            var slot = SelectedSlot;
            int pocket = Bag is { SelectedPocket: { } selected } bag ? bag.Pockets.ToList().IndexOf(selected) : 0;
            Show(Document.WithState(state));
            if (Bag is not null && pocket >= 0 && pocket < Bag.Pockets.Count)
                Bag.SelectedPocket = Bag.Pockets[pocket];
            if (slot is { } s)
                SelectSlot(s);
        }
        finally
        {
            restoring = false;
        }
    }

    /// <summary>The game names boxes "Box 1", "Caja 1"… in the save's language; those are shown in the interface language.</summary>
    private static bool IsDefaultBoxName(string name, int box)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;
        var match = System.Text.RegularExpressions.Regex.Match(name.Trim(), @"^(Box|BOX|Caja|CAJA|Boîte|BOÎTE|Scatola|SCATOLA|ボックス|박스)\s*(\d+)$");
        return match.Success && int.Parse(match.Groups[2].Value) == box + 1;
    }

    public static string SlotLabel(SaveSlot slot) => slot.Box is { } box
        ? string.Format(Strings.Change_Box, box + 1, slot.Slot + 1)
        : string.Format(Strings.Change_Party, slot.Slot + 1);

    // ------------------------------------------------------------------ open / reload

    /// <summary>Opens the save the first time the tab is shown.</summary>
    public void EnsureOpen()
    {
        if (Document is null)
            Open();
    }

    /// <summary>(Re)loads the emulator save from disk, discarding unwritten changes.</summary>
    public void Open()
    {
        Document = null;
        Pokemon = null;
        Bag = null;
        Trainer = null;
        Dex = null;
        IsDirty = false;

        string? path = editor.Randomizer.SavePath;
        if (path is null) { Message = Strings.Rnd_NoEmulator; return; }
        if (!File.Exists(path)) { Message = string.Format(Strings.Rnd_NoSave, editor.Dump.Title.DisplayName(), settings.EffectiveEmulatorName); return; }

        try
        {
            Show(SaveDocument.Open(path, editor.Session.Current));
            SelectSlot(new SaveSlot(null, 0));
            Undo.Reset();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            Message = string.Format(Strings.Rnd_SaveUnreadable, settings.EffectiveEmulatorName, ex.Message);
        }
    }

    /// <summary>Shows a document: names, boxes, party, bag, trainer and Pokédex (opening, or a state put back by undo).</summary>
    private void Show(SaveDocument doc)
    {
        string path = doc.SavePath;
        {
            names = new SaveNames(editor.Names, GameTextLanguage.Current, doc.MaxSpecies);
            Document = doc;
            IsDirty = doc.IsDirty;
            HeaderText = string.Format(Strings.Save_Header, settings.EffectiveEmulatorName, doc.TrainerName, path, File.GetLastWriteTime(path));
            OnPropertyChanged(nameof(SpeciesChoices));

            // Clearing the names makes the combo box push -1 back into SelectedBox: remember the box, fill the names, then
            // select it again and tell the combo box, or it shows nothing while box 1 is displayed.
            int box = SelectedBox;
            BoxNames.Clear();
            for (int b = 0; b < doc.BoxCount; b++)
                BoxNames.Add(IsDefaultBoxName(doc.BoxName(b), b) ? string.Format(Strings.Save_BoxN, b + 1) : doc.BoxName(b));
            PartySlots.Clear();
            for (int i = 0; i < SaveDocument.PartySize; i++)
                PartySlots.Add(new SlotViewModel(this, new SaveSlot(null, i)));
            SelectedBox = box >= 0 && box < doc.BoxCount ? box : 0;
            OnPropertyChanged(nameof(SelectedBox));
            RebuildBoxSlots();

            Bag = new SaveBagViewModel(this, doc, names, editor.Names.ItemDescriptions);
            Trainer = new SaveTrainerViewModel(this, doc, names, editor.Dump.Title,
                MilestoneIcons.Load(new RomFsLayers(editor.Session.Project.RomFsPath), editor.Dump.Title), editor.ProfileAvatar);
            Dex = new SaveDexViewModel(this, doc, names);
            RefreshSlots();
            RefreshProblems();
            OnPropertyChanged(nameof(RomWarning));
            OnPropertyChanged(nameof(HasRomWarning));
        }
    }

    /// <summary>After a build or restore: the ROM data changed, so reopen unless there are unwritten edits.</summary>
    public void OnRomChanged()
    {
        if (Document is null)
            return;
        if (IsDirty)
        {
            Document.UseRom(editor.Session.Current);
            RefreshAll();
        }
        else
        {
            Open();
        }
    }

    partial void OnSelectedBoxChanged(int value) => RebuildBoxSlots();

    private void RebuildBoxSlots()
    {
        if (Document is null || SelectedBox < 0)
            return;
        BoxSlots.Clear();
        for (int i = 0; i < Document.BoxSlotCount; i++)
            BoxSlots.Add(new SlotViewModel(this, new SaveSlot(SelectedBox, i)));
        RefreshSlots();
        OnPropertyChanged(nameof(BoxWallpaper));
        // Keep the selection mark when coming back to the box of the selected Pokémon.
        foreach (var s in BoxSlots)
            s.IsSelected = s.Slot == SelectedSlot;
    }

    /// <summary>The box's wallpaper in the game, drawn behind its slots as PKHeX does.</summary>
    public Avalonia.Media.Imaging.Bitmap? BoxWallpaper => Document is null || SelectedBox < 0
        ? null
        : PkhexImages.Wallpaper(editor.Dump.Title, Document.BoxWallpaper(SelectedBox));

    [RelayCommand]
    private void PreviousBox()
    {
        if (Document is not null)
            SelectedBox = (SelectedBox + Document.BoxCount - 1) % Document.BoxCount;
    }

    [RelayCommand]
    private void NextBox()
    {
        if (Document is not null)
            SelectedBox = (SelectedBox + 1) % Document.BoxCount;
    }

    // ------------------------------------------------------------------ selection and edits

    public void SelectSlot(SaveSlot slot)
    {
        if (Document is null)
            return;
        if (slot.Box is { } box && box != SelectedBox)
            SelectedBox = box;

        // Party slots past the last Pokémon are "the next free one".
        if (slot.IsParty && slot.Slot > Document.PartyCount)
            slot = new SaveSlot(null, Document.PartyCount);

        SelectedSlot = slot;
        bool empty = slot.IsParty ? slot.Slot >= Document.PartyCount : Document.IsEmpty(slot);
        Pokemon = empty ? null : new PokemonEditorViewModel(this, Document, names!, slot);
        if (Pokemon is { } shown)
            Avalonia.Threading.Dispatcher.UIThread.Post(shown.RefreshSelections, Avalonia.Threading.DispatcherPriority.Background);
        CanCreate = empty && (!slot.IsParty || Document.PartyCount < SaveDocument.PartySize);
        foreach (var s in PartySlots.Concat(BoxSlots))
            s.IsSelected = s.Slot == slot;
    }

    /// <summary>Called by the Pokémon editor after every change.</summary>
    public void OnPokemonEdited(SaveSlot slot)
    {
        if (slot != SelectedSlot)
            SelectedSlot = slot;
        Touch();
    }

    public void SetStatus(string text) => editor.SetStatus(text);

    public void Touch() => Touch(Strings.Undo_SaveChange);

    /// <summary>Something in the save changed: refresh what depends on it and keep the step for undo.</summary>
    public void Touch(string label)
    {
        IsDirty = Document?.IsDirty ?? false;
        RefreshSlots();
        RefreshProblems();
        Trainer?.RefreshCard();
        if (!restoring && Document is not null)
            Undo.Record(SelectedSlot is { } slot && Pokemon is not null ? $"{SlotLabel(slot)} · {label}" : label);
    }

    private void RefreshAll()
    {
        if (SelectedSlot is { } slot)
            SelectSlot(slot);
        Touch();
    }

    private void RefreshSlots()
    {
        if (Document is null || names is null)
            return;
        var problemSlots = Document.Validate().Where(p => p.Slot is not null).Select(p => p.Slot!.Value).ToHashSet();
        foreach (var s in PartySlots.Concat(BoxSlots))
        {
            bool empty = s.Slot.IsParty ? s.Slot.Slot >= Document.PartyCount : Document.IsEmpty(s.Slot);
            s.IsEmpty = empty;
            if (empty)
            {
                s.Name = Strings.Save_Empty;
                s.Icon = null;
                s.Detail = "";
                s.Types = [];
                s.TypeChips = [];
                s.ItemIcon = null;
                s.LevelText = s.GenderText = "";
                s.IsShiny = s.IsEgg = false;
            }
            else
            {
                var pk = Document.Get(s.Slot);
                s.Name = pk.IsEgg ? Strings.Save_Egg : names.SpeciesName(pk.Species);
                s.Icon = pk.IsEgg ? null : Sprites.For(pk.Species, pk.Form, pk.Gender == 1, pk.IsShiny);
                s.Detail = string.Format(Strings.Save_SlotDetail, Document.Level(pk), pk.IsNicknamed ? pk.Nickname : "");
                int[] types = pk.IsEgg ? [0] : [.. (Document.Personal(pk.Species, pk.Form)?.Types ?? [0]).Distinct()];
                s.Types = types;
                s.TypeChips = pk.IsEgg ? [] : [.. types.Select(TypeChipOf)];
                s.ItemIcon = PkhexImages.Item(pk.HeldItem);
                s.LevelText = pk.IsEgg ? "" : string.Format(Strings.Preview_Level, Document.Level(pk));
                s.GenderText = pk.IsEgg ? "" : pk.Gender switch { 0 => "♂", 1 => "♀", _ => "" };
                s.IsShiny = pk.IsShiny && !pk.IsEgg;
                s.IsEgg = pk.IsEgg;
            }
            s.HasProblem = problemSlots.Contains(s.Slot);
        }
    }

    /// <summary>A type label in the colors of the type, named in the interface language.</summary>
    public TypeChip TypeChipOf(int type) =>
        new(names is not null && type >= 0 && type < names.Types.Count ? names.Types[type] : "?", TypeColors.Background(type), TypeColors.Foreground(type));

    private void RefreshProblems()
    {
        Problems.Clear();
        if (Document is not null)
            foreach (var p in Document.Validate())
                Problems.Add(new ProblemViewModel(this, p));
        OnPropertyChanged(nameof(ProblemsText));
        OnPropertyChanged(nameof(HasProblems));
    }

    [RelayCommand]
    private void CreatePokemon()
    {
        // Only into an empty slot: never replace a Pokémon that is already there.
        if (Document is null || SelectedSlot is not { } slot || !CanCreate || NewSpecies <= 0 || NewSpecies >= SpeciesChoices.Count)
            return;
        if (slot.IsParty ? slot.Slot < Document.PartyCount : !Document.IsEmpty(slot))
            return;
        var placed = Document.Set(slot, Document.Create((ushort)NewSpecies, (int)Math.Clamp(NewLevel ?? 5, 1, 100)));
        Touch();
        SelectSlot(placed);
    }

    [RelayCommand]
    private void DeletePokemon()
    {
        if (Document is null || SelectedSlot is not { } slot)
            return;
        if (!Document.Clear(slot))
        {
            editor.SetStatus(Strings.Save_LastPartyPokemon, error: true);
            return;
        }
        Touch();
        SelectSlot(slot);
    }

    // ------------------------------------------------------------------ write

    [RelayCommand]
    private async Task Reload()
    {
        if (IsDirty)
        {
            var question = new QuestionViewModel(Strings.Save_ReloadTitle, Strings.Save_ReloadMessage, Strings.Save_ReloadConfirm, Strings.Common_Cancel, []);
            if (!await editor.Dialogs.AskAsync(question))
                return;
        }
        Open();
        editor.SetStatus(Strings.Save_Reloaded);
    }

    [RelayCommand]
    private void Write()
    {
        if (Document is null)
            return;
        if (Problems.Count > 0)
        {
            editor.SetStatus(string.Format(Strings.Save_ProblemsBlock, Problems.Count), error: true);
            return;
        }
        if (EmulatorUserFolders.RunningEmulators(settings.EffectiveEmulatorName) is { Count: > 0 } running)
        {
            editor.SetStatus(string.Format(Strings.Status_CloseEmulator, string.Join(", ", running)), error: true);
            return;
        }
        if (Document.ChangedOnDisk())
        {
            editor.SetStatus(Strings.Save_ChangedOnDisk, error: true);
            return;
        }

        try
        {
            // The save as it was, in the project history, so it can be put back from there too.
            editor.History.History.Record(editor.Session.Project, VersionKind.BeforeSaveEdit, romPath: editor.Session.Project.Randomization.LastBuiltRom,
                savePath: Document.SavePath);
            editor.History.History.Prune();

            string backups = Path.Combine(AppSettings.BackupRoot, settings.EffectiveEmulatorName);
            var result = Document.Write(backups);
            SaveUpdater.PruneBackups(backups, keep: 10);
            IsDirty = false;
            HeaderText = string.Format(Strings.Save_Header, settings.EffectiveEmulatorName, Document.TrainerName, Document.SavePath, File.GetLastWriteTime(Document.SavePath));
            editor.History.Refresh();
            editor.Randomizer.RefreshSave();
            editor.SetStatus(string.Format(Strings.Save_Written, result.SavePath, result.BackupPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            editor.SetStatus(string.Format(Strings.Save_WriteFailed, ex.Message), error: true);
        }
    }
}

/// <summary>An item of a bag pocket: a row of the list and, when selected, the detail panel that edits it.</summary>
public sealed partial class BagItemViewModel(SaveBagViewModel owner, PocketViewModel pocket, int itemIndex, int count) : ObservableObject
{
    public PocketViewModel Pocket { get; } = pocket;

    /// <summary>Index into <see cref="PocketViewModel.ItemNames"/>.</summary>
    public int ItemIndex
    {
        get => itemIndex;
        set
        {
            if (value >= 0 && value != itemIndex)
            {
                itemIndex = value;
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Description));
                owner.Commit(Pocket);
            }
        }
    }

    public Avalonia.Media.Imaging.Bitmap? Icon => HasIcon ? PkhexImages.Item(ItemId, Pocket.IsTms) : null;

    /// <summary>PKHeX has no icon for the Gen 6/7 key items: those show the pocket's glyph instead of a question mark.</summary>
    public bool HasIcon => PkhexImages.HasItemIcon(ItemId, Pocket.IsTms);

    public string Name => Pocket.ItemNames[itemIndex];
    public string Description => owner.Describe(ItemId);

    public decimal? Count
    {
        get => count;
        set
        {
            if (value is not { } v)
                return;
            count = (int)Math.Clamp(v, 1, Pocket.MaxCount);
            OnPropertyChanged(nameof(CountText));
            owner.Commit(Pocket);
        }
    }

    public string CountText => $"×{count}";
    public int MaxCount => Pocket.MaxCount;
    public int ItemId => Pocket.ItemIds[itemIndex];
    public int Amount => count;

    [RelayCommand]
    private void Remove() => owner.RemoveItem(this);
}
public sealed class PocketViewModel(InventoryPouch pouch, SaveNames names)
{
    public InventoryPouch Pouch { get; } = pouch;

    public string Name => Pouch.Type switch
    {
        InventoryType.Items => Strings.Bag_Items,
        InventoryType.KeyItems => Strings.Bag_KeyItems,
        InventoryType.TMHMs => Strings.Bag_TMs,
        InventoryType.Medicine => Strings.Bag_Medicine,
        InventoryType.Berries => Strings.Bag_Berries,
        _ => Pouch.Type.ToString(),
    };

    public bool IsTms => Pouch.Type == InventoryType.TMHMs;

    /// <summary>An item that stands for the pocket: Poké Ball, Potion, Oran Berry, a TM; key items have a key glyph.</summary>
    public Avalonia.Media.Imaging.Bitmap? Icon => Pouch.Type switch
    {
        InventoryType.Items => PkhexImages.Item(4),
        InventoryType.Medicine => PkhexImages.Item(17),
        InventoryType.Berries => PkhexImages.Item(155),
        InventoryType.TMHMs => PkhexImages.Item(1, isTm: true),
        _ => null,
    };

    public bool HasIcon => Icon is not null;

    /// <summary>Header color of the pocket, like the bag screens of the games.</summary>
    public Avalonia.Media.IBrush Accent => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Pouch.Type switch
    {
        InventoryType.Items => "#E8704F",
        InventoryType.Medicine => "#5DBB63",
        InventoryType.Berries => "#C0507A",
        InventoryType.TMHMs => "#4F86C6",
        InventoryType.KeyItems => "#C9A227",
        _ => "#7D8A96",
    }));

    public IReadOnlyList<ushort> ItemIds { get; } = SaveDocument.PouchItems(pouch);
    public IReadOnlyList<string> ItemNames { get; } = SaveDocument.PouchItems(pouch).Select(i => names.ItemName(i)).ToList();
    public int MaxCount => Pouch.MaxCount;
    public int Capacity => Pouch.Items.Length;
    public System.Collections.ObjectModel.ObservableCollection<BagItemViewModel> Items { get; } = [];
}

/// <summary>Bag: pockets and their items.</summary>
public partial class SaveBagViewModel : ObservableObject
{
    private readonly SaveEditorViewModel owner;
    private readonly SaveDocument doc;

    public IReadOnlyList<PocketViewModel> Pockets { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapacityText))]
    public partial PocketViewModel? SelectedPocket { get; set; }

    /// <summary>The item shown in the detail panel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    public partial BagItemViewModel? SelectedItem { get; set; }

    public bool HasSelectedItem => SelectedItem is not null;

    /// <summary>
    /// The first item of the new pocket. Set again once the list has taken the new pocket: replacing its items makes the
    /// ListBox push null into the selection.
    /// </summary>
    partial void OnSelectedPocketChanged(PocketViewModel? value)
    {
        SelectedItem = value?.Items.FirstOrDefault();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(SelectedPocket, value) && SelectedItem is null)
                SelectedItem = value?.Items.FirstOrDefault();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private readonly IReadOnlyList<string> descriptions;

    public string Describe(int item) => item > 0 && item < descriptions.Count && descriptions[item].Length > 0 ? descriptions[item] : Strings.Bag_NoDescription;

    public string CapacityText => SelectedPocket is { } p ? string.Format(Strings.Bag_Capacity, p.Items.Count, p.Capacity) : "";

    public SaveBagViewModel(SaveEditorViewModel owner, SaveDocument doc, SaveNames names, IReadOnlyList<string> descriptions)
    {
        this.owner = owner;
        this.doc = doc;
        this.descriptions = descriptions;
        Pockets = doc.Pouches.Select(p => new PocketViewModel(p, names)).ToList();
        foreach (var pocket in Pockets)
        {
            foreach (var item in pocket.Pouch.Items.Where(i => i.Index != 0))
            {
                int index = IndexOf(pocket, item.Index);
                if (index >= 0)
                    pocket.Items.Add(new BagItemViewModel(this, pocket, index, Math.Max(1, item.Count)));
            }
        }
        SelectedPocket = Pockets.FirstOrDefault();
    }

    private static int IndexOf(PocketViewModel pocket, int item)
    {
        for (int i = 0; i < pocket.ItemIds.Count; i++)
            if (pocket.ItemIds[i] == item)
                return i;
        return -1;
    }

    public void Commit(PocketViewModel pocket)
    {
        doc.SetPouch(pocket.Pouch, pocket.Items.Select(i => (i.ItemId, i.Amount)));
        OnPropertyChanged(nameof(CapacityText));
        owner.Touch();
    }

    public void RemoveItem(BagItemViewModel item)
    {
        int index = item.Pocket.Items.IndexOf(item);
        item.Pocket.Items.Remove(item);
        if (ReferenceEquals(SelectedItem, item))
            SelectedItem = item.Pocket.Items.Count == 0 ? null : item.Pocket.Items[Math.Min(index, item.Pocket.Items.Count - 1)];
        Commit(item.Pocket);
    }

    [RelayCommand]
    private void AddItem()
    {
        if (SelectedPocket is not { } pocket || pocket.Items.Count >= pocket.Capacity || pocket.ItemIds.Count == 0)
            return;
        var used = pocket.Items.Select(i => i.ItemId).ToHashSet();
        int index = Enumerable.Range(0, pocket.ItemIds.Count).FirstOrDefault(i => !used.Contains(pocket.ItemIds[i]));
        var added = new BagItemViewModel(this, pocket, index, 1);
        pocket.Items.Add(added);
        SelectedItem = added;
        Commit(pocket);
    }

    [RelayCommand]
    private void SortByName()
    {
        if (SelectedPocket is not { } pocket)
            return;
        var sorted = pocket.Items.OrderBy(i => pocket.ItemNames[i.ItemIndex], StringComparer.CurrentCultureIgnoreCase).ToList();
        pocket.Items.Clear();
        foreach (var item in sorted)
            pocket.Items.Add(item);
        Commit(pocket);
    }
}

public sealed class DexEntryViewModel(SaveDexViewModel owner, SaveDocument doc, ushort species, string name, PokemonSprites sprites) : ObservableObject
{
    public ushort Species { get; } = species;
    public string Number => $"#{Species:000}";
    public Avalonia.Media.Imaging.Bitmap? Icon => sprites.For(Species);
    public string Name { get; } = name;

    public Avalonia.Media.IBrush TypeBrush => doc.Personal(Species, 0)?.Types is { Length: > 0 } t
        ? TypeColors.Background(t[0], t[^1])
        : Avalonia.Media.Brushes.Transparent;

    public bool Seen
    {
        get => doc.GetSeen(Species);
        set { doc.SetSeen(Species, value); owner.Changed(this); }
    }

    public bool Caught
    {
        get => doc.GetCaught(Species);
        set { doc.SetCaught(Species, value); owner.Changed(this); }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>Pokédex: seen and caught per species.</summary>
public partial class SaveDexViewModel : ObservableObject
{
    private readonly SaveEditorViewModel owner;
    private readonly SaveDocument doc;
    private readonly List<DexEntryViewModel> all;

    private readonly SaveNames names;

    public ObservableCollection<DexEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    public string CountText => string.Format(Strings.Dex_Count, all.Count(e => e.Seen), all.Count(e => e.Caught), all.Count);

    [ObservableProperty]
    public partial DexEntryViewModel? SelectedEntry { get; set; }

    /// <summary>The selected species as the game's Pokédex shows it, with the data of the ROM played.</summary>
    [ObservableProperty]
    public partial DexDetailViewModel? Detail { get; private set; }

    public SaveDexViewModel(SaveEditorViewModel owner, SaveDocument doc, SaveNames names)
    {
        this.owner = owner;
        this.doc = doc;
        this.names = names;
        all = Enumerable.Range(1, doc.MaxSpecies).Select(s => new DexEntryViewModel(this, doc, (ushort)s, names.SpeciesName((ushort)s), owner.Sprites)).ToList();
        ApplyFilter();
        SelectedEntry = Entries.FirstOrDefault();
    }

    partial void OnFilterChanged(string value)
    {
        var keep = SelectedEntry;
        ApplyFilter();
        SelectedEntry = keep is not null && Entries.Contains(keep) ? keep : Entries.FirstOrDefault();
    }

    partial void OnSelectedEntryChanged(DexEntryViewModel? value) =>
        Detail = value is null ? null : new DexDetailViewModel(owner, doc, names, value);

    private void ApplyFilter()
    {
        string filter = Filter.Trim();
        Entries.Clear();
        foreach (var e in all.Where(e => filter.Length == 0
                                         || (int.TryParse(filter.TrimStart('#'), out int n) ? e.Species == n : e.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))))
            Entries.Add(e);
    }

    public void Changed(DexEntryViewModel entry)
    {
        entry.Refresh();
        OnPropertyChanged(nameof(CountText));
        Detail?.Refresh();
        owner.Touch(Strings.Save_TabDex);
    }

    [RelayCommand]
    private void AllSeen()
    {
        foreach (var e in all)
            doc.SetSeen(e.Species, true);
        foreach (var e in all) e.Refresh();
        OnPropertyChanged(nameof(CountText));
        owner.Touch();
    }

    [RelayCommand]
    private void AllCaught()
    {
        foreach (var e in all)
            doc.SetCaught(e.Species, true);
        foreach (var e in all) e.Refresh();
        OnPropertyChanged(nameof(CountText));
        owner.Touch();
    }
}
