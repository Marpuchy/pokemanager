using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Battle;
using Pokemanager.Model.Projects;
using Pokemanager.Multiplayer;

namespace Pokemanager.App.ViewModels;

/// <summary>A room of the list "Tus salas": open it again (host) or enter it again (guest).</summary>
public sealed partial class SavedRoomRow(RoomViewModel owner, SavedRoom room)
{
    public SavedRoom Room { get; } = room;
    public string Name => Room.Name.Length > 0 ? Room.Name : Strings.Room_DefaultName;

    public string Subtitle => string.Format(Strings.Room_SavedSubtitle,
        Room.IsHost ? Strings.Room_SavedHost : Strings.Room_SavedGuest,
        Room.LastUsed.ToString("g"),
        Room.ProjectPath is { } p ? Path.GetFileNameWithoutExtension(p) : "—");

    public string EnterText => Room.IsHost ? Strings.Room_SavedOpen : Strings.Room_SavedEnter;

    [RelayCommand]
    private Task Enter() => owner.EnterSavedAsync(Room);

    [RelayCommand]
    private Task Remove() => owner.RemoveSavedAsync(Room);
}

/// <summary>A prize of the battle roulette as the host edits it.</summary>
public sealed partial class RoomPrizeRow : ObservableObject
{
    private readonly Action<RoomPrizeRow> remove;

    public RoomPrizeRow(RoomPrize prize, IReadOnlyList<string> items, Action<RoomPrizeRow> remove)
    {
        this.remove = remove;
        Items = items;
        KindIndex = prize.Kind switch { "money" => 1, "text" => 2, "nothing" => 3, _ => 0 };
        ItemIndex = Math.Clamp(prize.Item, 0, Math.Max(0, items.Count - 1));
        Amount = prize.Amount;
        Text = prize.Text ?? "";
        Weight = prize.Weight;
    }

    /// <summary>The game's item names, by id (one list for every row, never replaced: the ItemsSource rule).</summary>
    public IReadOnlyList<string> Items { get; }

    public static IReadOnlyList<string> Kinds { get; } =
        [Strings.Locke_KindItem, Strings.Locke_KindMoney, Strings.Locke_KindText, Strings.Locke_KindNothing];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsItem), nameof(HasAmount), nameof(IsText))]
    public partial int KindIndex { get; set; }

    [ObservableProperty] public partial int ItemIndex { get; set; }
    [ObservableProperty] public partial decimal? Amount { get; set; }
    [ObservableProperty] public partial string Text { get; set; }
    [ObservableProperty] public partial decimal? Weight { get; set; }

    public bool IsItem => KindIndex == 0;
    public bool HasAmount => KindIndex is 0 or 1;
    public bool IsText => KindIndex == 2;

    public RoomPrize ToPrize() => new(
        KindIndex switch { 1 => "money", 2 => "text", 3 => "nothing", _ => "item" },
        Item: KindIndex == 0 ? ItemIndex : 0,
        Amount: KindIndex is 0 or 1 ? Math.Max(1, (int)(Amount ?? 1)) : 1,
        Text: KindIndex == 2 ? Text.Trim() : null,
        Weight: Math.Max(0, (int)(Weight ?? 1)));

    [RelayCommand]
    private void Remove() => remove(this);
}

/// <summary>
/// Saved rooms, the battle roulette and the rules PDF (2026-11-24, user: "keep the rooms you made over time, add a
/// battle roulette for whoever wins, and a PDF with the rules").
/// </summary>
public sealed partial class RoomViewModel
{
    private SavedRoom? currentSaved;

    // ------------------------------------------------------------------ saved rooms

    public ObservableCollection<SavedRoomRow> SavedRooms { get; } = [];
    public bool HasSavedRooms => SavedRooms.Count > 0;

    /// <summary>The list, newest first; the room remembered by an earlier version (only the last one) joins it once.</summary>
    private void LoadSavedRooms()
    {
        if (Memory.Saved.Count == 0 && (Memory.IsHost ? Memory.Secret is not null : Memory.Invite is not null))
        {
            Memory.Saved.Add(new SavedRoom
            {
                Name = Memory.RoomName ?? "", IsHost = Memory.IsHost, Secret = Memory.Secret, Port = Memory.Port,
                Invite = Memory.Invite, ProjectPath = Memory.ProjectPath,
            });
        }
        SavedRooms.Clear();
        foreach (var room in Memory.Saved.OrderByDescending(r => r.LastUsed))
            SavedRooms.Add(new SavedRoomRow(this, room));
        OnPropertyChanged(nameof(HasSavedRooms));
    }

    /// <summary>A room was opened or entered: it goes to (or moves up in) the list.</summary>
    private SavedRoom RememberRoom(bool host, string name, string? secret, int port, string? invite, string projectPath)
    {
        var room = Memory.Saved.FirstOrDefault(r => r.Matches(host, secret, invite));
        if (room is null)
        {
            room = new SavedRoom { IsHost = host, Secret = secret, Invite = invite?.Trim() };
            Memory.Saved.Add(room);
        }
        if (host || room.Name.Length == 0)
            room.Name = name;
        room.Port = port;
        room.ProjectPath = projectPath;
        room.LastUsed = DateTime.Now;
        Settings.Save();
        LoadSavedRooms();
        return room;
    }

    public async Task EnterSavedAsync(SavedRoom room)
    {
        if (IsBusy || session is not null)
            return;
        var project = SelectedProject();
        if (room.ProjectPath is { } path && (project is null || !string.Equals(project.Path, path, StringComparison.OrdinalIgnoreCase)) && File.Exists(path))
        {
            try { project = await Task.Run(() => ProjectLoader.Load(path, main.Upr)); }
            catch (ProjectLoadException) { }
        }
        if (room.IsHost && room.Secret is { } secret)
        {
            RoomName = room.Name;
            await StartAsync(host: true, code: null, secret: Convert.FromBase64String(secret), port: room.Port, project);
        }
        else
        {
            await StartAsync(host: false, code: room.Invite, secret: null, port: 0, project);
        }
    }

    public async Task RemoveSavedAsync(SavedRoom room)
    {
        if (!await main.Dialogs.AskAsync(new QuestionViewModel(Strings.Room_SavedRemoveTitle,
                string.Format(Strings.Room_SavedRemoveMessage, room.Name), Strings.Room_SavedRemove, Strings.Common_Cancel, [])))
            return;
        Memory.Saved.Remove(room);
        if (room.RulesPdf is { } pdf)
        {
            try { File.Delete(pdf); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Settings.Save();
        LoadSavedRooms();
    }

    /// <summary>After a room opened: remember it, and as the host send its prizes and rules PDF again.</summary>
    private void OnRoomOpened(bool host, string roomName, string? code, LoadedProject project)
    {
        currentSaved = RememberRoom(host, roomName, host ? Memory.Secret : null, session!.Port, host ? null : code, project.Path);
        PrizeItemNames = gameNames?.Items ?? [];
        PrizeRows.Clear();
        if (!host)
            return;
        foreach (var prize in currentSaved.Prizes)
            PrizeRows.Add(new RoomPrizeRow(prize, PrizeItemNames, RemovePrizeRow));
        if (currentSaved.Prizes.Count > 0)
            session.SetPrizes(currentSaved.Prizes);
        if (currentSaved.RulesPdf is { } pdf && File.Exists(pdf))
        {
            try
            {
                session.SetRulesDocument(new RoomRulesDocument(currentSaved.RulesPdfName ?? Path.GetFileName(pdf), File.ReadAllBytes(pdf)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    // ------------------------------------------------------------------ battle roulette

    public ObservableCollection<RoomPrizeRow> PrizeRows { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> PrizeItemNames { get; private set; } = [];

    /// <summary>What the roulette offers, for everyone (the host edits it above).</summary>
    [ObservableProperty]
    public partial string PrizesText { get; private set; } = "";

    public ObservableCollection<string> PrizesWonLines { get; } = [];
    public bool HasPrizesWon => PrizesWonLines.Count > 0;

    private void RemovePrizeRow(RoomPrizeRow row) => PrizeRows.Remove(row);

    [RelayCommand]
    private void AddPrize() => PrizeRows.Add(new RoomPrizeRow(new RoomPrize("item", Item: 50, Amount: 1), PrizeItemNames, RemovePrizeRow));

    [RelayCommand]
    private void ApplyPrizes()
    {
        if (session is not { IsHost: true } s || currentSaved is null)
            return;
        var prizes = PrizeRows.Select(r => r.ToPrize()).Where(p => p.Weight > 0 && (p.Kind != "text" || p.Text is { Length: > 0 })).ToList();
        s.SetPrizes(prizes);
        currentSaved.Prizes = prizes;
        Settings.Save();
        Say(string.Format(Strings.Room_PrizesSaved, prizes.Count));
    }

    internal static LockePrize ToLocke(RoomPrize p) => new(
        p.Kind switch { "item" => LockePrizeKind.Item, "money" => LockePrizeKind.Money, "text" => LockePrizeKind.Text, _ => LockePrizeKind.Nothing },
        p.Item, p.Amount, Math.Max(0, p.Weight), p.Text);

    private string Describe(RoomPrize prize) => LockeRewards.Describe(ToLocke(prize), gameNames?.Items ?? []);

    /// <summary>The winner of that battle may spin: it is over, they won it, and the roulette has prizes and was not spun.</summary>
    public bool CanSpinFor(RoomBattle battle) => session is { } s && battle.Phase == BattlePhase.Finished
                                                 && battle.Winner == LocalPlayerId && s.Prizes.Count > 0 && !s.WasSpun(battle.Id);

    /// <summary>
    /// The battle roulette: the prize is rolled when the wheel starts, told to the room when it stops (one spin per battle
    /// won), and an item or money goes into the shared save with the save editor's safety.
    /// </summary>
    public async Task SpinPrizeAsync(string battleId)
    {
        if (session is not { } s || shared is not { } project || s.Battles.FirstOrDefault(b => b.Id == battleId) is not { } battle
            || !CanSpinFor(battle))
            return;
        var prizes = s.Prizes.ToList();
        var locke = new LockeSettings { Prizes = [.. prizes.Select(ToLocke)] };
        string opponent = FindPlayer(battle.MySide == "p2" ? battle.ChallengerId : battle.OpponentId)?.Name ?? "?";
        var items = gameNames?.Items ?? [];
        var roulette = new RouletteViewModel(string.Format(Strings.Room_RouletteTitle, opponent), locke, 0, items, null,
            onWin: won => s.ReportPrize(battleId, prizes[locke.Prizes.IndexOf(won)]),
            claim: won => LockeRewards.GiveToSave(project, Settings, won, items,
                string.Format(Strings.Room_PrizeHistoryNote, LockeRewards.Describe(won, items), opponent)),
            markAdded: () => { },
            forget: () => { })
        {
            AllowSpinAgain = false,
            Hint = Strings.Room_RouletteHint,
        };
        await main.Dialogs.ShowRouletteAsync(roulette);
        Refresh();
    }

    // ------------------------------------------------------------------ rules PDF

    public bool HasRulesPdf => session?.RulesDocument is not null;

    public string RulesPdfText => session?.RulesDocument is { } doc
        ? string.Format(Strings.Room_RulesPdfName, doc.FileName, doc.Pdf.Length / 1024)
        : Strings.Room_RulesPdfNone;

    [RelayCommand]
    private async Task ChooseRulesPdf()
    {
        if (session is not { IsHost: true } s || currentSaved is null)
            return;
        if (await main.Dialogs.PickOpenFileAsync(Strings.Room_RulesPdfPick, ["*.pdf"]) is not { } file)
            return;
        try
        {
            byte[] pdf = await File.ReadAllBytesAsync(file);
            if (pdf.Length > RoomSession.MaxRulesPdfBytes)
            {
                Say(string.Format(Strings.Room_RulesPdfTooLarge, RoomSession.MaxRulesPdfBytes / (1024 * 1024)), error: true);
                return;
            }
            string dir = Path.Combine(AppSettings.DataRoot, "rooms", currentSaved.Id);
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, "rules.pdf");
            await File.WriteAllBytesAsync(copy, pdf);
            currentSaved.RulesPdf = copy;
            currentSaved.RulesPdfName = Path.GetFileName(file);
            Settings.Save();
            s.SetRulesDocument(new RoomRulesDocument(currentSaved.RulesPdfName, pdf));
            Say(string.Format(Strings.Room_RulesPdfSent, currentSaved.RulesPdfName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say(ex.Message, error: true);
        }
    }

    [RelayCommand]
    private void RemoveRulesPdf()
    {
        if (session is not { IsHost: true } s || currentSaved is null)
            return;
        s.SetRulesDocument(null);
        currentSaved.RulesPdf = null;
        currentSaved.RulesPdfName = null;
        Settings.Save();
    }

    /// <summary>Opens the room's rules PDF with the computer's PDF reader (a copy in the data folder).</summary>
    [RelayCommand]
    private void OpenRulesPdf()
    {
        if (session?.RulesDocument is not { } doc)
            return;
        try
        {
            string name = string.Concat(doc.FileName.Split(Path.GetInvalidFileNameChars()));
            if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                name += ".pdf";
            string dir = Path.Combine(AppSettings.DataRoot, "rooms", "received");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, doc.Pdf);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Say(ex.Message, error: true);
        }
    }

    // ------------------------------------------------------------------ refresh

    private void RefreshExtras(RoomSession s)
    {
        PrizesText = s.Prizes.Count == 0
            ? Strings.Room_PrizesNone
            : string.Join(" · ", s.Prizes.Select(p => $"{Describe(p)} ({p.Weight})"));
        var lines = s.PrizesWon.Select(w => string.Format(Strings.Room_PrizeWonLine, w.When.ToString("t"),
            FindPlayer(w.PlayerId)?.Name ?? "?", Describe(w.Prize))).ToList();
        if (!PrizesWonLines.SequenceEqual(lines))
        {
            PrizesWonLines.Clear();
            foreach (string line in lines)
                PrizesWonLines.Add(line);
        }
        OnPropertyChanged(nameof(HasPrizesWon));
        OnPropertyChanged(nameof(HasRulesPdf));
        OnPropertyChanged(nameof(RulesPdfText));
    }
}
