using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Dump;
using Pokemanager.Multiplayer;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>A Pokémon of any player of the room.</summary>
public sealed partial class RoomMonViewModel(RoomViewModel owner, PokemonState mon, string playerName, bool isLocal)
{
    public PokemonState State { get; } = mon;
    public string PlayerName { get; } = playerName;
    public bool IsLocal { get; } = isLocal;
    public Bitmap? Icon { get; } = owner.Icon(mon);
    public string Name { get; } = owner.MonName(mon);
    public string Detail { get; } = string.Format(Strings.Room_MonDetail, owner.SpeciesName(mon.Species), mon.Level, owner.LocationName(mon));
    public string StatusText { get; } = RoomViewModel.StatusName(mon.Status);
    public IBrush StatusBrush { get; } = RoomViewModel.StatusColor(mon.Status);
    public double Faded { get; } = mon.Status == PokemonStatus.Dead ? 0.45 : 1;
    public string Tooltip => $"{PlayerName} · {Name} · {Detail} · {StatusText}";

    public bool CanMarkDead => IsLocal && State.Status != PokemonStatus.Dead;
    public bool CanUndoDeath => IsLocal && State.Status == PokemonStatus.Dead;
    public bool CanUnlink => IsLocal && State.LinkId is not null;

    [RelayCommand]
    private void MarkDead() => owner.MarkDead(this);

    [RelayCommand]
    private void UndoDeath() => owner.UndoDeath(this);

    [RelayCommand]
    private void Unlink() => owner.Unlink(this);
}

public sealed class RoomPlayerViewModel(string name, string subtitle, string lives, IReadOnlyList<RoomMonViewModel> party, bool isLocal)
{
    public string Name { get; } = name;
    public string Subtitle { get; } = subtitle;
    public string LivesText { get; } = lives;
    public bool HasLives => LivesText.Length > 0;
    public IReadOnlyList<RoomMonViewModel> Party { get; } = party;
    public bool HasParty => Party.Count > 0;
    public bool IsLocal { get; } = isLocal;
}

public sealed class RoomLinkViewModel(string title, IReadOnlyList<RoomMonViewModel> members, bool isDead)
{
    public string Title { get; } = title;
    public IReadOnlyList<RoomMonViewModel> Members { get; } = members;
    public bool IsDead { get; } = isDead;
    public double Faded => IsDead ? 0.6 : 1;
}

public sealed class RoomLineViewModel(string text, bool isWarning = false)
{
    public string Text { get; } = text;
    public bool IsWarning { get; } = isWarning;
}

/// <summary>A capture offered for a manual link: "player · nickname (location)".</summary>
public sealed class RoomChoice(LinkMember member, string text)
{
    public LinkMember Member { get; } = member;
    public string Text { get; } = text;
    public override string ToString() => Text;
}

/// <summary>
/// Multiplayer tab: the room of this project (one per project, <c>&lt;project&gt;.room.json</c>), its players, links,
/// notices and log, and the rules. Until the network layer exists, rooms are shared by exporting and importing files.
/// </summary>
public sealed partial class RoomViewModel : ObservableObject
{
    private readonly EditorViewModel editor;
    private readonly IDialogs dialogs;
    private RoomState? state;
    private bool refreshing;

    public string RoomPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoom), nameof(NoRoom), nameof(IsHost), nameof(CanEditRules), nameof(RoomTitle), nameof(RoomSubtitle))]
    public partial LinkRoom? Room { get; private set; }

    public bool HasRoom => Room is not null;
    public bool NoRoom => Room is null;
    public bool IsHost => Room is { } r && r.HostPlayerId == r.LocalPlayerId;
    public bool CanEditRules => IsHost;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(JoinCommand), nameof(SynchronizeCommand))]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<RoomPlayerViewModel> Players { get; } = [];
    public ObservableCollection<RoomLinkViewModel> Links { get; } = [];
    public ObservableCollection<RoomLineViewModel> Notices { get; } = [];
    public ObservableCollection<RoomLineViewModel> Log { get; } = [];
    public ObservableCollection<RoomChoice> MyUnlinked { get; } = [];
    public ObservableCollection<RoomChoice> OthersUnlinked { get; } = [];

    [ObservableProperty]
    public partial RoomChoice? SelectedMine { get; set; }

    [ObservableProperty]
    public partial RoomChoice? SelectedOther { get; set; }

    public bool HasNotices => Notices.Count > 0;
    public bool HasLinks => Links.Count > 0;
    public bool IsManualMatching => Room?.Rules.Matching == LinkMatching.Manual;

    public string RoomTitle => Room is { } r ? string.Format(Strings.Room_Title, r.Name) : "";

    public string RoomSubtitle => Room is { } r && state is { } s
        ? string.Format(Strings.Room_Subtitle, s.Players.Count, IsHost ? Strings.Room_YouHost : Strings.Room_YouGuest,
            r.LastSnapshot?.Taken.ToString("g") ?? Strings.Room_NeverSynced)
        : "";

    public RoomViewModel(EditorViewModel editor, IDialogs dialogs, string projectPath)
    {
        this.editor = editor;
        this.dialogs = dialogs;
        RoomPath = Path.ChangeExtension(projectPath, ".room.json");
        if (File.Exists(RoomPath))
        {
            try
            {
                Room = LinkRoom.Load(RoomPath);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                editor.SetStatus(string.Format(Strings.Room_Unreadable, RoomPath, ex.Message), error: true);
            }
        }
        Refresh();
    }

    // ------------------------------------------------------------------ names and pictures

    public Bitmap? Icon(PokemonState mon) => editor.Sprites.For(mon.Species, mon.Form, shiny: mon.IsShiny);

    public string SpeciesName(ushort species) =>
        species < editor.Names.Species.Count ? editor.Names.Species[species] : $"#{species}";

    public string MonName(PokemonState mon) => string.IsNullOrWhiteSpace(mon.Nickname) ? SpeciesName(mon.Species) : mon.Nickname;

    /// <summary>
    /// The location in the interface language, whatever the language of the player who reported it: PKHeX has the
    /// location names of every game, so the player's game and the location id are enough.
    /// </summary>
    public string LocationName(PokemonState mon)
    {
        if (state?.Players.GetValueOrDefault(mon.PlayerId) is not { Generation: > 0 } player)
            return mon.LocationName;
        string language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (language is not ("ja" or "en" or "fr" or "it" or "de" or "es" or "ko" or "zh"))
            language = "en";
        var version = Enum.TryParse<PKHeX.Core.GameVersion>(player.Game, out var v) ? v : PKHeX.Core.GameVersion.Any;
        string name = PKHeX.Core.GameInfo.GetStrings(language)
            .GetLocationName(false, (ushort)mon.Location, (byte)player.Generation, (byte)player.Generation, version) ?? "";
        return name.Length > 0 ? name : mon.LocationName;
    }

    private string PlayerName(string id) => state?.Players.GetValueOrDefault(id)?.Name ?? "?";

    private string MonText(LinkMember member) =>
        state?.Find(member) is { } mon ? MonName(mon) : "?";

    public static string StatusName(PokemonStatus status) => status switch
    {
        PokemonStatus.Doomed => Strings.Room_StatusDoomed,
        PokemonStatus.Dead => Strings.Room_StatusDead,
        _ => Strings.Room_StatusAlive,
    };

    public static IBrush StatusColor(PokemonStatus status) => status switch
    {
        PokemonStatus.Doomed => new SolidColorBrush(Color.Parse("#E07B00")),
        PokemonStatus.Dead => new SolidColorBrush(Color.Parse("#D13438")),
        _ => new SolidColorBrush(Color.Parse("#2E8B57")),
    };

    /// <summary>The PKHeX version codes a snapshot stores, as the app's games.</summary>
    private static GameTitle? TitleOf(string game) => game switch
    {
        "X" => GameTitle.X, "Y" => GameTitle.Y, "OR" => GameTitle.OmegaRuby, "AS" => GameTitle.AlphaSapphire,
        "SN" => GameTitle.Sun, "MN" => GameTitle.Moon, "US" => GameTitle.UltraSun, "UM" => GameTitle.UltraMoon,
        _ => null,
    };

    private static string MilestoneName(PlayerState player, int index) =>
        TitleOf(player.Game) is { } title ? LockeRewards.MilestoneName(title, index) : $"#{index + 1}";

    // ------------------------------------------------------------------ refresh

    public void Refresh()
    {
        refreshing = true;
        try
        {
            state = Room?.State();
            Players.Clear();
            Links.Clear();
            Notices.Clear();
            Log.Clear();
            MyUnlinked.Clear();
            OthersUnlinked.Clear();
            if (Room is { } room && state is { } s)
            {
                FillPlayers(room, s);
                FillLinks(room, s);
                FillNotices(s);
                FillLog(room);
                FillChoices(room, s);
            }
            OnPropertyChanged(string.Empty);
        }
        finally
        {
            refreshing = false;
        }
    }

    private RoomMonViewModel Mon(LinkRoom room, PokemonState mon) =>
        new(this, mon, PlayerName(mon.PlayerId), mon.PlayerId == room.LocalPlayerId);

    private void FillPlayers(LinkRoom room, RoomState s)
    {
        foreach (var player in s.Players.Values.OrderByDescending(p => p.Id == room.LocalPlayerId).ThenBy(p => p.Name))
        {
            var mons = s.PokemonOf(player.Id).ToList();
            var party = player.Party.Select(k => s.Find(new LinkMember(player.Id, k))).OfType<PokemonState>()
                .Select(m => Mon(room, m)).ToList();
            string game = TitleOf(player.Game)?.DisplayName() ?? player.Game;
            string subtitle = string.Format(Strings.Room_PlayerSubtitle, game, player.Milestones.Count, player.MilestoneCount,
                mons.Count, mons.Count(m => m.Status == PokemonStatus.Dead),
                player.LastSync?.ToString("g") ?? Strings.Room_NeverSynced);
            string lives = s.PoolOf(player.Id) is { } pool
                ? new string('♥', pool.Left) + new string('♡', Math.Max(0, pool.Lost)) + "  " + string.Format(Strings.Locke_Lives, pool.Left, pool.Max)
                : "";
            string name = player.Id == room.LocalPlayerId ? string.Format(Strings.Room_You, player.Name) : player.Name;
            if (player.Id == room.HostPlayerId)
                name += " · " + Strings.Room_Host;
            Players.Add(new RoomPlayerViewModel(name, subtitle, lives, party, player.Id == room.LocalPlayerId));
        }
    }

    private void FillLinks(LinkRoom room, RoomState s)
    {
        // What needs attention first: links with a death, then links that already join several players, newest first.
        var groups = s.Links.Values
            .Select(g => (Group: g, Mons: g.Members.Select(s.Find).OfType<PokemonState>().ToList()))
            .Where(g => g.Mons.Count > 0)
            .OrderByDescending(g => g.Mons.Any(m => m.Status != PokemonStatus.Alive))
            .ThenByDescending(g => g.Mons.Count > 1)
            .ThenByDescending(g => g.Mons.Max(m => m.Order));
        foreach (var (group, mons) in groups)
        {
            var members = mons.Select(m => Mon(room, m)).ToList();
            var first = members[0].State;
            string title = group.Id.Contains("/order/")
                ? string.Format(Strings.Room_LinkOrder, first.Order + 1)
                : group.Id.StartsWith("manual/", StringComparison.Ordinal) ? Strings.Room_LinkManual : LocationName(first);
            Links.Add(new RoomLinkViewModel(title, members, members.All(m => m.State.Status == PokemonStatus.Dead)));
        }
    }

    private void FillNotices(RoomState s)
    {
        // Actions to take first, then broken rules.
        foreach (var n in s.Notices.OrderBy(n => n.Kind switch
                 {
                     NoticeKind.MustRetire => 0, NoticeKind.NoLivesLeft => 1, NoticeKind.LinkedDied => 2,
                     NoticeKind.OverLevelCap => 3, NoticeKind.DuplicatePrimaryType => 4, _ => 5,
                 }))
        {
            string player = PlayerName(n.PlayerId), mon = MonText(new LinkMember(n.PlayerId, n.Key));
            string other = n.Other is { } o ? MonText(o) : "", otherPlayer = n.Other is { } op ? PlayerName(op.PlayerId) : "";
            string location = s.Find(new LinkMember(n.PlayerId, n.Key)) is { } at ? LocationName(at) : "";
            string text = n.Kind switch
            {
                NoticeKind.MustRetire => string.Format(Strings.Room_NoticeMustRetire, player, mon, other, otherPlayer),
                NoticeKind.LinkedDied => string.Format(Strings.Room_NoticeLinkedDied, player, mon, other, otherPlayer),
                NoticeKind.SecondEncounter => string.Format(Strings.Room_NoticeSecondEncounter, player, mon, location),
                NoticeKind.LocationClaimed => string.Format(Strings.Room_NoticeClaimed, player, mon, location, otherPlayer),
                NoticeKind.DuplicatePrimaryType => string.Format(Strings.Room_NoticeDuplicateType, player, mon,
                    n.Value >= 0 && n.Value < editor.Names.Types.Count ? editor.Names.Types[n.Value] : $"#{n.Value}"),
                NoticeKind.OverLevelCap => string.Format(Strings.Room_NoticeLevelCap, player, mon, n.Value),
                NoticeKind.NoLivesLeft => string.Format(Strings.Room_NoticeNoLives, player),
                _ => n.Kind.ToString(),
            };
            Notices.Add(new RoomLineViewModel(text, isWarning: true));
        }
    }

    private void FillLog(LinkRoom room)
    {
        foreach (var e in room.Events.AsEnumerable().Reverse().Where(e => e is not (SaveSynced or PokemonUpdated)).Take(200))
        {
            string player = PlayerName(e.PlayerId);
            string when = e.When == default ? "" : e.When.ToString("g") + "  ";
            string? text = e switch
            {
                PlayerJoined j => string.Format(Strings.Room_LogJoined, player, TitleOf(j.Game)?.DisplayName() ?? j.Game),
                PokemonCaptured c => string.Format(Strings.Room_LogCaptured, player,
                    string.IsNullOrWhiteSpace(c.Nickname) ? SpeciesName(c.Species) : c.Nickname,
                    state?.Find(new LinkMember(e.PlayerId, c.Key)) is { } caught ? LocationName(caught) : c.LocationName),
                PokemonDied d => string.Format(Strings.Room_LogDied, player, MonText(new LinkMember(e.PlayerId, d.Key)), CauseName(d.Cause)),
                DeathUndone u => string.Format(Strings.Room_LogUndone, player, MonText(new LinkMember(e.PlayerId, u.Key))),
                MilestoneEarned m => string.Format(Strings.Room_LogMilestone, player,
                    state?.Players.GetValueOrDefault(e.PlayerId) is { } p ? MilestoneName(p, m.Index) : $"#{m.Index + 1}"),
                LivesChanged l => string.Format(Strings.Room_LogLives, player, l.Delta.ToString("+0;-0"), l.Reason),
                ManualLink l => string.Format(Strings.Room_LogLinked, player, MonText(new LinkMember(e.PlayerId, l.Key)),
                    string.Join(", ", l.With.Select(MonText))),
                ManualUnlink u => string.Format(Strings.Room_LogUnlinked, player, MonText(new LinkMember(e.PlayerId, u.Key))),
                RouletteSpun r => string.Format(Strings.Room_LogRoulette, player, LockeRewards.Describe(r.Prize, editor.Names.Items)),
                _ => null,
            };
            if (text is not null)
                Log.Add(new RoomLineViewModel(when + text));
        }
    }

    private void FillChoices(LinkRoom room, RoomState s)
    {
        foreach (var mon in s.Pokemon.Where(m => m.Counts && m.Status != PokemonStatus.Dead).OrderBy(m => m.Order))
        {
            var choice = new RoomChoice(mon.Member, $"{PlayerName(mon.PlayerId)} · {MonName(mon)} ({LocationName(mon)})");
            (mon.PlayerId == room.LocalPlayerId ? MyUnlinked : OthersUnlinked).Add(choice);
        }
    }

    private static string CauseName(DeathCause cause) => cause switch
    {
        DeathCause.FaintedInParty => Strings.Room_CauseFainted,
        DeathCause.GraveyardBox => Strings.Room_CauseGraveyard,
        DeathCause.Released => Strings.Room_CauseReleased,
        _ => Strings.Room_CauseManual,
    };

    // ------------------------------------------------------------------ create, join, share

    private bool CanRun() => !IsBusy;

    /// <summary>Reads the emulator save of this project (read-only) as the rules see it.</summary>
    private async Task<SaveSnapshot?> ReadSaveAsync()
    {
        string? path = editor.Randomizer.SavePath;
        if (path is null || !File.Exists(path))
        {
            editor.SetStatus(Strings.Room_NoSave, error: true);
            return null;
        }
        try
        {
            var rom = editor.Session.Current;
            return await Task.Run(() => SaveSnapshot.Read(SaveDocument.Open(path, rom), DateTime.Now));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            editor.SetStatus(string.Format(Strings.Room_SaveFailed, ex.Message), error: true);
            return null;
        }
    }

    private async Task<(string Trainer, SaveSnapshot Save)?> ReadSaveWithTrainerAsync()
    {
        string? path = editor.Randomizer.SavePath;
        if (await ReadSaveAsync() is not { } snapshot)
            return null;
        string trainer = await Task.Run(() => SaveDocument.Open(path!, editor.Session.Current).TrainerName);
        return (trainer, snapshot);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Create()
    {
        var question = new QuestionViewModel(Strings.Room_CreateTitle, Strings.Room_CreateMessage, Strings.Room_CreateConfirm,
            Strings.Common_Cancel, [], Strings.Room_NamePlaceholder);
        question.Input = Path.GetFileNameWithoutExtension(editor.ProjectPath);
        if (!await dialogs.AskAsync(question) || string.IsNullOrWhiteSpace(question.Input))
            return;

        IsBusy = true;
        try
        {
            if (await ReadSaveWithTrainerAsync() is not { } save)
                return;
            var room = LinkRoom.Create(question.Input.Trim(), save.Trainer, save.Save, LinkRules.Preset(LinkPreset.SoulLink));
            room.Synchronize(save.Save);
            Room = room;
            Persist();
            editor.SetStatus(string.Format(Strings.Room_Created, room.Name));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Join()
    {
        if (await dialogs.PickOpenFileAsync(Strings.Room_JoinTitle, ["*.room.json", "*.json"]) is not { } path)
            return;
        LinkRoom shared;
        try
        {
            shared = LinkRoom.Load(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            editor.SetStatus(string.Format(Strings.Room_ImportFailed, ex.Message), error: true);
            return;
        }

        IsBusy = true;
        try
        {
            if (await ReadSaveWithTrainerAsync() is not { } save)
                return;
            var room = new LinkRoom
            {
                RoomId = shared.RoomId, Name = shared.Name, HostPlayerId = shared.HostPlayerId, Rules = shared.Rules,
                LocalPlayerId = Guid.NewGuid().ToString("N"),
            };
            room.Append(shared.Events);
            room.Join(save.Trainer, save.Save);
            room.Synchronize(save.Save);
            Room = room;
            Persist();
            editor.SetStatus(string.Format(Strings.Room_Joined, room.Name));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Synchronize()
    {
        if (Room is not { } room)
            return;
        IsBusy = true;
        try
        {
            if (await ReadSaveAsync() is not { } snapshot)
                return;
            var events = room.Synchronize(snapshot);
            Persist();
            int facts = events.Count(e => e is not SaveSynced);
            editor.SetStatus(facts == 0 ? Strings.Room_SyncedNothing : string.Format(Strings.Room_Synced, facts));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>A copy of the room for the other players: rules, events and who exported it, without the local snapshot.</summary>
    [RelayCommand]
    private async Task Export()
    {
        if (Room is not { } room)
            return;
        string suggested = $"{room.Name} - {PlayerName(room.LocalPlayerId)}.room.json";
        if (await dialogs.PickSaveFileAsync(Strings.Room_ExportTitle, suggested, "json") is not { } path)
            return;
        try
        {
            var copy = new LinkRoom
            {
                RoomId = room.RoomId, Name = room.Name, HostPlayerId = room.HostPlayerId, LocalPlayerId = room.LocalPlayerId,
                Rules = room.Rules, Events = room.Events,
            };
            await Task.Run(() => copy.Save(path));
            editor.SetStatus(string.Format(Strings.Room_Exported, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.Room_ImportFailed, ex.Message), error: true);
        }
    }

    /// <summary>Adds what another player exported. The host's file also brings the current rules.</summary>
    [RelayCommand]
    private async Task Import()
    {
        if (Room is not { } room)
            return;
        if (await dialogs.PickOpenFileAsync(Strings.Room_ImportTitle, ["*.room.json", "*.json"]) is not { } path)
            return;
        try
        {
            var other = LinkRoom.Load(path);
            if (other.RoomId != room.RoomId)
            {
                editor.SetStatus(string.Format(Strings.Room_OtherRoom, other.Name), error: true);
                return;
            }
            int added = room.Append(other.Events);
            bool rules = other.LocalPlayerId == room.HostPlayerId && room.LocalPlayerId != room.HostPlayerId;
            if (rules)
            {
                room.Rules = other.Rules;
                room.Name = other.Name;
            }
            Persist();
            editor.SetStatus(string.Format(Strings.Room_Imported, added, rules ? Strings.Room_ImportedRules : ""));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            editor.SetStatus(string.Format(Strings.Room_ImportFailed, ex.Message), error: true);
        }
    }

    [RelayCommand]
    private async Task Leave()
    {
        if (Room is null)
            return;
        var question = new QuestionViewModel(Strings.Room_LeaveTitle, Strings.Room_LeaveMessage, Strings.Room_LeaveConfirm, Strings.Common_Cancel, []);
        if (!await dialogs.AskAsync(question))
            return;
        try
        {
            File.Delete(RoomPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.Room_ImportFailed, ex.Message), error: true);
            return;
        }
        Room = null;
        Refresh();
    }

    // ------------------------------------------------------------------ manual actions

    private void Add(RoomEvent e)
    {
        if (Room is not { } room)
            return;
        room.Append([e]);
        Persist();
    }

    public void MarkDead(RoomMonViewModel mon) =>
        Add(new PokemonDied(mon.State.Key, DeathCause.Manual) { PlayerId = mon.State.PlayerId, When = DateTime.Now });

    public void UndoDeath(RoomMonViewModel mon) =>
        Add(new DeathUndone(mon.State.Key) { PlayerId = mon.State.PlayerId, When = DateTime.Now });

    public void Unlink(RoomMonViewModel mon) =>
        Add(new ManualUnlink(mon.State.Key) { PlayerId = mon.State.PlayerId, When = DateTime.Now });

    [RelayCommand]
    private void LinkSelected()
    {
        if (Room is not { } room || SelectedMine is not { } mine || SelectedOther is not { } other)
            return;
        Add(new ManualLink(mine.Member.Key, [other.Member]) { PlayerId = room.LocalPlayerId, When = DateTime.Now });
    }

    [RelayCommand]
    private void LoseLife() => ChangeLives(-1);

    [RelayCommand]
    private void GainLife() => ChangeLives(1);

    private void ChangeLives(int delta)
    {
        if (Room is { } room)
            Add(new LivesChanged(delta, Strings.Room_LivesByHand) { PlayerId = room.LocalPlayerId, When = DateTime.Now });
    }

    private void Persist()
    {
        if (Room is not { } room)
            return;
        try
        {
            room.Save(RoomPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.Room_ImportFailed, ex.Message), error: true);
        }
        Refresh();
    }

    // ------------------------------------------------------------------ rules (host only)

    public static IReadOnlyList<string> PresetNames { get; } =
        [Strings.Room_PresetCustom, Strings.Room_PresetSoulLink, Strings.Room_PresetSoulLinkGames, Strings.Room_PresetShared, Strings.Room_PresetRace];

    public static IReadOnlyList<string> MatchingNames { get; } =
        [Strings.Room_MatchNone, Strings.Room_MatchLocation, Strings.Room_MatchOrder, Strings.Room_MatchManual];

    public static IReadOnlyList<string> DeathNames { get; } = [Strings.Room_DeathKill, Strings.Room_DeathNotify, Strings.Room_DeathNone];

    public static IReadOnlyList<string> LivesNames { get; } = [Strings.Room_LivesOff, Strings.Room_LivesIndividual, Strings.Room_LivesShared];

    public static IReadOnlyList<string> RouletteNames { get; } =
        [Strings.Room_RoulettePerPlayer, Strings.Room_RouletteEveryone, Strings.Room_RouletteTeam];

    public static IReadOnlyList<string> LevelCapNames { get; } = [Strings.Room_CapOff, Strings.Room_CapOwn, Strings.Room_CapSlowest];

    private LinkRules Rules => Room?.Rules ?? new LinkRules();

    private void ChangeRules(Action<LinkRules> change)
    {
        if (refreshing || Room is not { } room || !IsHost)
            return;
        change(room.Rules);
        Persist();
    }

    /// <summary>Always shows "custom": choosing a preset replaces the rules (keeping the teams) and goes back to it.</summary>
    public int PresetIndex
    {
        get => 0;
        set
        {
            if (value <= 0)
                return;
            ChangeRules(r =>
            {
                var preset = LinkRules.Preset((LinkPreset)value);
                preset.Teams = r.Teams;
                Room!.Rules = preset;
            });
        }
    }

    public int MatchingIndex { get => (int)Rules.Matching; set => ChangeRules(r => r.Matching = (LinkMatching)Math.Max(0, value)); }
    public bool MergeSubAreas { get => Rules.MergeSubAreas; set => ChangeRules(r => r.MergeSubAreas = value); }
    public bool RaceForLocations { get => Rules.RaceForLocations; set => ChangeRules(r => r.RaceForLocations = value); }
    public bool FirstEncounterOnly { get => Rules.FirstEncounterOnly; set => ChangeRules(r => r.FirstEncounterOnly = value); }
    public bool DupesClause { get => Rules.DupesClause; set => ChangeRules(r => r.DupesClause = value); }
    public bool ShinyClause { get => Rules.ShinyClause; set => ChangeRules(r => r.ShinyClause = value); }
    public bool EggsCount { get => Rules.Eggs == SpecialCaptures.Count; set => ChangeRules(r => r.Eggs = value ? SpecialCaptures.Count : SpecialCaptures.Ignore); }
    public int DeathIndex { get => (int)Rules.OnDeath; set => ChangeRules(r => r.OnDeath = (DeathSpread)Math.Max(0, value)); }
    public bool DetectFainted { get => Rules.DeathDetection.HasFlag(DeathDetection.FaintedInParty); set => ChangeRules(r => r.DeathDetection = Flag(r.DeathDetection, DeathDetection.FaintedInParty, value)); }
    public bool DetectGraveyard { get => Rules.DeathDetection.HasFlag(DeathDetection.GraveyardBox); set => ChangeRules(r => r.DeathDetection = Flag(r.DeathDetection, DeathDetection.GraveyardBox, value)); }
    public bool DetectReleased { get => Rules.DeathDetection.HasFlag(DeathDetection.Released); set => ChangeRules(r => r.DeathDetection = Flag(r.DeathDetection, DeathDetection.Released, value)); }

    /// <summary>1-based box, 0 = the last box of each save.</summary>
    public decimal? GraveyardBox
    {
        get => Rules.GraveyardBox + 1;
        set { if (value is { } v) ChangeRules(r => r.GraveyardBox = (int)Math.Clamp(v, 0, 32) - 1); }
    }

    public bool UniquePrimaryTypes { get => Rules.UniquePrimaryTypes; set => ChangeRules(r => r.UniquePrimaryTypes = value); }
    public int LivesIndex { get => (int)Rules.Lives; set => ChangeRules(r => r.Lives = (LivesMode)Math.Max(0, value)); }
    public decimal? MaxLives { get => Rules.MaxLives; set { if (value is { } v) ChangeRules(r => r.MaxLives = (int)Math.Clamp(v, 0, 99)); } }
    public bool DeathCostsLife { get => Rules.DeathCostsLife; set => ChangeRules(r => r.DeathCostsLife = value); }
    public int RouletteIndex { get => (int)Rules.Roulette; set => ChangeRules(r => r.Roulette = (RouletteMode)Math.Max(0, value)); }
    public int LevelCapIndex { get => (int)Rules.LevelCap; set => ChangeRules(r => r.LevelCap = (LevelCapMode)Math.Max(0, value)); }

    public string LevelCaps
    {
        get => string.Join(", ", Rules.LevelCaps);
        set => ChangeRules(r => r.LevelCaps = value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => int.TryParse(t, out int n) ? Math.Clamp(n, 1, 100) : 0).Where(n => n > 0).ToList());
    }

    public bool HasLivesPool => Room is { } r && state?.PoolOf(r.LocalPlayerId) is not null;

    private static DeathDetection Flag(DeathDetection flags, DeathDetection flag, bool on) => on ? flags | flag : flags & ~flag;
}
