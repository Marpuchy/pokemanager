using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Multiplayer;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>A profile picture: the custom image, else the trainer sprite (downloaded in the background), else initials.</summary>
public sealed partial class AvatarViewModel : ObservableObject
{
    public AvatarViewModel(PlayerProfile profile)
    {
        Initials = new string(profile.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])).ToArray());
        Accent = new SolidColorBrush(Color.TryParse(profile.Color, out var c) ? c : Color.Parse("#1C3A70"));
        Image = TrainerSprites.FromBytes(profile.Image);
        if (Image is null && profile.Sprite is { } sprite)
            _ = LoadAsync(sprite);
    }

    private async Task LoadAsync(string sprite)
    {
        var bitmap = await TrainerSprites.GetAsync(sprite);
        Dispatcher.UIThread.Post(() => Image = bitmap);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial Bitmap? Image { get; private set; }

    public bool HasImage => Image is not null;
    public string Initials { get; }
    public IBrush Accent { get; }
}

/// <summary>A player in the list of the room.</summary>
public sealed class RoomPlayerRow(RoomPlayer player, string subtitle)
{
    public RoomPlayer Player { get; } = player;
    public string Id => Player.Id;
    public string Name { get; } = player.IsLocal ? string.Format(Strings.Room_You, player.Name) : player.Name;
    public string Subtitle { get; } = subtitle;
    public AvatarViewModel Avatar { get; } = new(player.Profile);
    public IBrush OnlineBrush { get; } = new SolidColorBrush(Color.Parse(player.Online ? "#2E8B57" : "#9E9E9E"));
    public string OnlineText { get; } = player.Online ? Strings.Room_Online : Strings.Room_Offline;
    public double Faded => Player.Online ? 1 : 0.55;
}

/// <summary>A Pokémon of another player: party card or box slot, colored by its types like the project preview.</summary>
public sealed partial class RoomMonCard(RoomViewModel owner, SharedPokemon? mon, Bitmap? icon, string name, string species,
    IReadOnlyList<TypeChip> types, string itemText) : ObservableObject
{
    public SharedPokemon? Mon { get; } = mon;
    public Bitmap? Icon { get; } = icon;
    public string Name { get; } = name;
    public string SpeciesName { get; } = species;
    public IReadOnlyList<TypeChip> TypeChips { get; } = types;
    public string ItemText { get; } = itemText;
    public bool IsEmpty => Mon is null;
    public string LevelText => Mon is null ? "" : string.Format(Strings.Preview_Level, Mon.Level);
    public string GenderText => Mon?.Gender switch { 0 => "♂", 1 => "♀", _ => "" };
    public string Tooltip => IsEmpty ? "" : $"{Name} · {SpeciesName} · {LevelText}";

    private int[] TypeIds => Mon?.Types is { Length: > 0 } t ? [t[0], t.Length > 1 ? t[1] : t[0]] : [0, 0];
    private static readonly IBrush EmptySlot = new SolidColorBrush(Color.Parse("#F2F4F6"));

    public IBrush TypeBackground => Mon is null ? EmptySlot : Mon.IsEgg ? TypeColors.Background(0) : TypeColors.Background(TypeIds[0], TypeIds[1]);
    public IBrush TypeForeground => TypeColors.Foreground(TypeIds);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => owner.SelectMon(this);
}

public sealed record RoomInfoLine(string Label, string Value);

/// <summary>Details of the Pokémon picked in another player's party or boxes.</summary>
public sealed class RoomMonDetail(RoomMonCard card, IReadOnlyList<RoomInfoLine> info, IReadOnlyList<MoveLine> moves, IReadOnlyList<RoomInfoLine> stats)
{
    public RoomMonCard Card { get; } = card;
    public IReadOnlyList<RoomInfoLine> Info { get; } = info;
    public IReadOnlyList<MoveLine> Moves { get; } = moves;
    public IReadOnlyList<RoomInfoLine> Stats { get; } = stats;
}

/// <summary>
/// Multiplayer, on the start screen: create a room or join one with a code and see the other players' party and boxes
/// live. Lives as long as the app (opening a project does not close it). The save shared is the one of the project chosen
/// when the room started, sent again every time the game writes it.
/// </summary>
public sealed partial class RoomViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private RoomSession? session;
    private SaveWatcher? watcher;
    private LoadedProject? shared;
    private GameNames? gameNames;
    private SaveNames? names;
    private PokemonSprites sprites = PokemonSprites.Empty;
    private TrainerSnapshot? shownSnapshot;
    private bool refreshing, playerPicked;

    private AppSettings Settings => main.Settings;
    private RoomMemory Memory => Settings.Room;

    /// <summary>The project selected on the start screen (its save is the one a new room shares).</summary>
    public Func<LoadedProject?> SelectedProject { get; set; } = () => null;

    public RoomViewModel(MainWindowViewModel main)
    {
        this.main = main;
        RoomName = Memory.RoomName ?? Strings.Room_DefaultName;
        Avatar = new AvatarViewModel(Profile());
    }

    // ------------------------------------------------------------------ profile

    [ObservableProperty]
    public partial AvatarViewModel Avatar { get; private set; }

    public string ProfileName => Profile().Name;

    private PlayerProfile Profile()
    {
        var p = Settings.Profile;
        string name = p.Name.Trim().Length > 0 ? p.Name.Trim() : trainerName ?? Environment.UserName;
        byte[]? image = null;
        try { image = p.Image is { } b64 ? Convert.FromBase64String(b64) : null; }
        catch (FormatException) { }
        return new PlayerProfile(name, p.Sprite, image, p.Color);
    }

    private string? trainerName;

    /// <summary>The profile was edited in Settings: show it and tell the room.</summary>
    public void ProfileChanged()
    {
        Avatar = new AvatarViewModel(Profile());
        OnPropertyChanged(nameof(ProfileName));
        session?.UpdateProfile(Profile());
    }

    // ------------------------------------------------------------------ state shown

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSession), nameof(NoSession), nameof(IsHost), nameof(IsGuest), nameof(SummaryText))]
    public partial bool SessionOpen { get; private set; }

    public bool HasSession => SessionOpen;
    public bool NoSession => !SessionOpen;
    public bool IsHost => session?.IsHost == true;
    public bool IsGuest => session is { IsHost: false };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(JoinCommand), nameof(ResumeCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string RoomName { get; set; } = "";

    [ObservableProperty]
    public partial string JoinCode { get; set; } = "";

    [ObservableProperty]
    public partial string AnswerInput { get; set; } = "";

    /// <summary>Last message of an action (errors in red).</summary>
    [ObservableProperty]
    public partial string Message { get; private set; } = "";

    [ObservableProperty]
    public partial bool MessageIsError { get; private set; }

    private void Say(string text, bool error = false)
    {
        Message = text;
        MessageIsError = error;
    }

    public bool CanResume => Memory.IsHost ? Memory.Secret is not null : Memory.Invite is not null;
    public string ResumeText => Memory.IsHost
        ? string.Format(Strings.Room_ResumeHost, Memory.RoomName)
        : string.Format(Strings.Room_ResumeGuest, Memory.RoomName);

    public string SelectedProjectText => SelectedProject() is { } p
        ? string.Format(Strings.Room_ShareProject, Path.GetFileNameWithoutExtension(p.Path), p.Dump.Title.DisplayName())
        : Strings.Room_PickProject;

    /// <summary>One line for the start screen card.</summary>
    public string SummaryText => session is null
        ? Strings.Room_NotInRoom
        : string.Format(Strings.Room_Summary, session.RoomName.Length > 0 ? session.RoomName : "…", session.Players.Count(p => p.Online), StatusText);

    public string Title => session is null ? "" : string.Format(Strings.Room_Title, session.RoomName.Length > 0 ? session.RoomName : "…");
    public string? InviteText => session?.InviteText;
    public string? AnswerText => session?.AnswerText;
    public bool ShowAnswer => session is { IsHost: false, Status: RoomStatus.WaitingForAnswer };
    public string SharedProjectText => shared is null ? "" : string.Format(Strings.Room_SharingProject, Path.GetFileNameWithoutExtension(shared.Path));

    public string StatusText => session?.Status switch
    {
        null => "",
        RoomStatus.Online when session.IsHost => Strings.Room_StatusHosting,
        RoomStatus.Online => Strings.Room_StatusConnected,
        RoomStatus.Connecting => Strings.Room_StatusConnecting,
        RoomStatus.WaitingForAnswer => Strings.Room_StatusNeedsAnswer,
        RoomStatus.Reconnecting => Strings.Room_StatusReconnecting,
        _ => Strings.Room_StatusClosed,
    };

    public IBrush StatusBrush => new SolidColorBrush(Color.Parse(session?.Status switch
    {
        RoomStatus.Online => "#2E8B57",
        RoomStatus.Closed or null => "#9E9E9E",
        _ => "#E07B00",
    }));

    /// <summary>How other players can reach this app.</summary>
    public string ReachText => session switch
    {
        null => "",
        { Mapping: { } m } when RoomSession.IsPublic(m.External.Address) => string.Format(Strings.Room_ReachMapped, m.Method, m.External),
        { Mapping: { } m } => string.Format(Strings.Room_ReachDoubleNat, m.Method, m.External.Address),
        { PublicEndpoint: { } p } => string.Format(Strings.Room_ReachStun, p),
        _ => Strings.Room_ReachLocal,
    };

    [ObservableProperty]
    public partial string SharedText { get; private set; } = "";

    public bool ShareParty
    {
        get => session?.Rules.ShareParty ?? true;
        set { if (!refreshing && session is { IsHost: true } s) s.SetRules(s.Rules with { ShareParty = value }); }
    }

    public bool ShareBoxes
    {
        get => session?.Rules.ShareBoxes ?? true;
        set { if (!refreshing && session is { IsHost: true } s) s.SetRules(s.Rules with { ShareBoxes = value }); }
    }

    public ObservableCollection<RoomPlayerRow> Players { get; } = [];

    [ObservableProperty]
    public partial RoomPlayerRow? SelectedPlayer { get; set; }

    public ObservableCollection<RoomMonCard> Party { get; } = [];
    public ObservableCollection<RoomMonCard> BoxSlots { get; } = [];

    /// <summary>Kept as the same instance while the names do not change (it feeds a ComboBox with a two-way index).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> BoxNames { get; private set; } = [];

    [ObservableProperty]
    public partial int SelectedBox { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    public partial RoomMonDetail? Detail { get; private set; }

    public bool HasDetail => Detail is not null;

    [ObservableProperty]
    public partial string TrainerText { get; private set; } = "";

    public AvatarViewModel? ShownAvatar => SelectedPlayer?.Avatar;
    public string ShownName => SelectedPlayer?.Player.Name ?? "";
    public bool HasPlayerData => shownSnapshot is not null;
    public bool NoPlayerData => SelectedPlayer is not null && shownSnapshot is null;
    public bool HasParty => shownSnapshot?.Party is not null;
    public bool PartyHidden => shownSnapshot is not null && shownSnapshot.Party is null;
    public bool HasBoxes => shownSnapshot?.Boxes is not null;
    public bool BoxesHidden => shownSnapshot is not null && shownSnapshot.Boxes is null;

    // ------------------------------------------------------------------ create / join / leave

    private bool CanStart() => !IsBusy;

    private string? SavePath(LoadedProject project) => Settings.EffectiveEmulatorDirectory is { } dir
        ? EmulatorUserFolders.SaveFile(dir, project.Dump.Title.TitleId())
        : null;

    private SaveDocument? OpenSave()
    {
        if (shared is null || SavePath(shared) is not { } path || !File.Exists(path))
            return null;
        try
        {
            return SaveDocument.Open(path, shared.Session.Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
        {
            return null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task Create() => StartAsync(host: true, code: null, secret: null, port: 0, project: SelectedProject());

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task Join() => StartAsync(host: false, code: JoinCode, secret: null, port: 0, project: SelectedProject());

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Resume()
    {
        var project = SelectedProject();
        if (Memory.ProjectPath is { } path && (project is null || !string.Equals(project.Path, path, StringComparison.OrdinalIgnoreCase)) && File.Exists(path))
        {
            try { project = await Task.Run(() => ProjectLoader.Load(path, main.Upr)); }
            catch (ProjectLoadException) { }
        }
        if (Memory.IsHost && Memory.Secret is { } secret)
            await StartAsync(host: true, code: null, secret: Convert.FromBase64String(secret), port: Memory.Port, project);
        else
            await StartAsync(host: false, code: Memory.Invite, secret: null, port: 0, project);
    }

    private async Task StartAsync(bool host, string? code, byte[]? secret, int port, LoadedProject? project)
    {
        if (project is null)
        {
            Say(Strings.Room_PickProject, error: true);
            return;
        }
        if (!host && string.IsNullOrWhiteSpace(code))
        {
            Say(Strings.Room_NoCode, error: true);
            return;
        }
        IsBusy = true;
        Say(Strings.Room_Starting);
        try
        {
            shared = project;
            gameNames = new GameNames(project.Dump, project.Session.Original);
            names = new SaveNames(gameNames, GameTextLanguage.Current, gameNames.Species.Count - 1);
            sprites = await Task.Run(() => PokemonSprites.Load(project.Project, project.Session.Original));
            var save = await Task.Run(OpenSave);
            trainerName = save?.TrainerName;
            string roomName = RoomName.Trim().Length > 0 ? RoomName.Trim() : Strings.Room_DefaultName;
            // The host runs the battles of the room: with the simulator found here, or none.
            var battleTools = host ? await Task.Run(() => Pokemanager.Battle.ShowdownTools.Locate()) : null;

            async Task<RoomSession> Open(RoomOptions options) => host
                ? await RoomSession.HostAsync(roomName, Memory.PlayerId, Profile(), new RoomRules(), options, secret)
                : await RoomSession.JoinAsync(code!, Memory.PlayerId, Profile(), options);
            try
            {
                session = await Open(new RoomOptions { Port = port, Battles = battleTools });
            }
            catch (System.Net.Sockets.SocketException) when (port != 0)
            {
                session = await Open(new RoomOptions { Battles = battleTools }); // the remembered port is taken: a new one (the code changes)
            }
            session.Changed += OnSessionChanged;

            Memory.IsHost = host;
            Memory.RoomName = host ? roomName : Memory.RoomName;
            Memory.Secret = host ? Convert.ToBase64String(InviteCode.Parse(session.InviteText!).Key) : null;
            Memory.Port = session.Port;
            Memory.Invite = host ? null : code!.Trim();
            Memory.ProjectPath = project.Path;
            Settings.Save();

            if (save is not null)
                Share(save);
            else
                SharedText = Strings.Room_NoSave;
            WatchSave();
            SessionOpen = true;
            Avatar = new AvatarViewModel(Profile());
            OnPropertyChanged(nameof(ProfileName));
            Say(host ? Strings.Room_Created : Strings.Room_Joining);
            Refresh();
        }
        catch (FormatException ex)
        {
            Say(string.Format(Strings.Room_BadCode, ex.Message), error: true);
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            Say(string.Format(Strings.Room_NetworkFailed, ex.Message), error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void WatchSave()
    {
        watcher?.Dispose();
        watcher = null;
        if (shared is null || SavePath(shared) is not { } path)
            return;
        watcher = new SaveWatcher(path);
        watcher.Saved += () =>
        {
            if (OpenSave() is { } save)
                Dispatcher.UIThread.Post(() => Share(save));
        };
    }

    private void Share(SaveDocument save)
    {
        if (session is null)
            return;
        var snapshot = TrainerSnapshot.Read(save);
        session.Publish(snapshot);
        SharedText = string.Format(Strings.Room_Shared, DateTime.Now.ToString("T"), snapshot.Party?.Count ?? 0);
    }

    [RelayCommand]
    private async Task ShareNow()
    {
        if (await Task.Run(OpenSave) is { } save)
            Share(save);
        else
            Say(Strings.Room_NoSave, error: true);
    }

    [RelayCommand]
    private void AcceptAnswer()
    {
        if (session is null)
            return;
        try
        {
            session.AcceptAnswer(AnswerInput);
            AnswerInput = "";
            Say(Strings.Room_AnswerAccepted);
        }
        catch (FormatException ex)
        {
            Say(string.Format(Strings.Room_BadCode, ex.Message), error: true);
        }
    }

    [RelayCommand]
    private async Task Leave()
    {
        await CloseAsync();
        Say(Strings.Room_Left);
    }

    public async Task CloseAsync()
    {
        watcher?.Dispose();
        watcher = null;
        if (session is { } s)
        {
            session = null;
            s.Changed -= OnSessionChanged;
            await s.DisposeAsync();
        }
        SessionOpen = false;
        shownSnapshot = null;
        Players.Clear();
        Party.Clear();
        BoxSlots.Clear();
        Detail = null;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>On exit: closes the connection and removes the router port without touching the UI.</summary>
    public void CloseForExit()
    {
        watcher?.Dispose();
        if (session is { } s)
            Task.Run(async () => await s.DisposeAsync()).Wait(TimeSpan.FromSeconds(3));
    }

    // ------------------------------------------------------------------ refresh

    private void OnSessionChanged() => Dispatcher.UIThread.Post(Refresh);

    public void RefreshSelectedProject() => OnPropertyChanged(nameof(SelectedProjectText));

    public void Refresh()
    {
        if (session is not { } s)
            return;
        refreshing = true;
        try
        {
            string? selected = SelectedPlayer?.Id;
            Players.Clear();
            foreach (var player in s.Players)
                Players.Add(new RoomPlayerRow(player, PlayerSubtitle(player)));
            // Until the user picks someone, show the first other player as soon as there is one.
            SelectedPlayer = (playerPicked ? Players.FirstOrDefault(p => p.Id == selected) : null)
                             ?? Players.FirstOrDefault(p => !p.Player.IsLocal) ?? Players.FirstOrDefault();
            ShowPlayer(force: true);
            foreach (string property in new[]
                     {
                         nameof(Title), nameof(StatusText), nameof(StatusBrush), nameof(InviteText), nameof(AnswerText), nameof(ShowAnswer),
                         nameof(ReachText), nameof(ShareParty), nameof(ShareBoxes), nameof(IsHost), nameof(IsGuest), nameof(SummaryText),
                         nameof(SharedProjectText),
                     })
                OnPropertyChanged(property);
        }
        finally
        {
            refreshing = false;
        }
    }

    private static GameTitle? TitleOf(string game) => game switch
    {
        "X" => GameTitle.X, "Y" => GameTitle.Y, "OR" => GameTitle.OmegaRuby, "AS" => GameTitle.AlphaSapphire,
        "SN" => GameTitle.Sun, "MN" => GameTitle.Moon, "US" => GameTitle.UltraSun, "UM" => GameTitle.UltraMoon,
        _ => null,
    };

    private static string PlayerSubtitle(RoomPlayer player)
    {
        string role = player.IsHost ? Strings.Room_Host + " · " : "";
        if (player.Snapshot is not { } s)
            return role + Strings.Room_NoSaveYet;
        string game = TitleOf(s.Game)?.DisplayName() ?? s.Game;
        return role + string.Format(Strings.Room_PlayerSubtitle, game, s.MilestonesEarned, s.MilestoneCount, player.ReceivedAt?.ToString("T") ?? "—");
    }

    partial void OnSelectedPlayerChanged(RoomPlayerRow? value)
    {
        if (refreshing)
            return;
        playerPicked = value is not null;
        Detail = null;
        ShowPlayer(force: false);
    }

    partial void OnSelectedBoxChanged(int value)
    {
        if (!refreshing)
            FillBox();
    }

    private void ShowPlayer(bool force)
    {
        OnPropertyChanged(nameof(ShownAvatar));
        OnPropertyChanged(nameof(ShownName));
        var snapshot = SelectedPlayer?.Player.Snapshot;
        if (ReferenceEquals(snapshot, shownSnapshot) && !(force && snapshot is null))
        {
            NotifyPlayerData();
            return;
        }
        var previousDetail = Detail?.Card.Mon;
        shownSnapshot = snapshot;
        Party.Clear();
        if (snapshot is null)
        {
            TrainerText = "";
            BoxSlots.Clear();
            BoxNames = [];
            Detail = null;
            NotifyPlayerData();
            return;
        }

        string game = TitleOf(snapshot.Game)?.DisplayName() ?? snapshot.Game;
        TrainerText = string.Format(Strings.Room_Trainer, snapshot.Trainer, game, snapshot.MilestonesEarned, snapshot.MilestoneCount,
            snapshot.Money, snapshot.PlayedHours, snapshot.PlayedMinutes, snapshot.SavedAt);
        foreach (var mon in snapshot.Party ?? [])
            Party.Add(Card(mon));

        var boxNames = (snapshot.Boxes ?? []).Select((b, i) => BoxName(b.Name, i)).ToList();
        bool wasRefreshing = refreshing;
        refreshing = true;
        try
        {
            if (!BoxNames.SequenceEqual(boxNames))
                BoxNames = boxNames;
            if (SelectedBox >= boxNames.Count || SelectedBox < 0)
                SelectedBox = 0;
        }
        finally
        {
            refreshing = wasRefreshing;
        }
        FillBox();

        // Keep the same Pokémon open across live updates when it is still in the party.
        var again = previousDetail is null ? null : Party.FirstOrDefault(c => c.Mon is { } m && m.Species == previousDetail.Species && m.Nickname == previousDetail.Nickname);
        if (again is not null)
            SelectMon(again);
        else if (Detail is null && Party.FirstOrDefault() is { } first)
            SelectMon(first);
        NotifyPlayerData();
    }

    private void NotifyPlayerData()
    {
        OnPropertyChanged(nameof(HasPlayerData));
        OnPropertyChanged(nameof(NoPlayerData));
        OnPropertyChanged(nameof(HasParty));
        OnPropertyChanged(nameof(PartyHidden));
        OnPropertyChanged(nameof(HasBoxes));
        OnPropertyChanged(nameof(BoxesHidden));
    }

    private static string BoxName(string name, int index)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name.Trim(), @"^(Box|BOX|Caja|CAJA|Boîte|BOÎTE|Scatola|SCATOLA|ボックス|박스)\s*(\d+)$");
        return string.IsNullOrWhiteSpace(name) || (match.Success && int.Parse(match.Groups[2].Value) == index + 1)
            ? string.Format(Strings.Save_BoxN, index + 1)
            : name;
    }

    private void FillBox()
    {
        BoxSlots.Clear();
        if (shownSnapshot?.Boxes is not { } boxes || SelectedBox < 0 || SelectedBox >= boxes.Count)
            return;
        foreach (var mon in boxes[SelectedBox].Slots)
            BoxSlots.Add(Card(mon));
    }

    // ------------------------------------------------------------------ names

    private string Species(ushort id) => gameNames is not null && id < gameNames.Species.Count ? gameNames.Species[id] : $"#{id}";

    private static string Named(IReadOnlyList<string>? list, int id) =>
        list is not null && id >= 0 && id < list.Count && !string.IsNullOrWhiteSpace(list[id]) ? list[id] : $"#{id}";

    private RoomMonCard Card(SharedPokemon? mon)
    {
        if (mon is null)
            return new RoomMonCard(this, null, null, "", "", [], "");
        string species = Species(mon.Species);
        string name = mon.IsEgg ? Strings.Save_Egg : string.IsNullOrWhiteSpace(mon.Nickname) ? species : mon.Nickname;
        var icon = mon.IsEgg ? null : sprites.For(mon.Species, mon.Form, mon.Gender == 1, mon.IsShiny);
        var types = mon.Types.Distinct().Select(t => new TypeChip(Named(names?.Types, t), TypeColors.Background(t), TypeColors.Foreground(t))).ToList();
        string item = mon.HeldItem == 0 ? Strings.Preview_NoItem : Named(names?.Items, mon.HeldItem);
        return new RoomMonCard(this, mon, icon, mon.IsShiny ? name + " ★" : name, species, types, item);
    }

    public void SelectMon(RoomMonCard card)
    {
        if (card.Mon is not { } mon)
            return;
        foreach (var c in Party.Concat(BoxSlots))
            c.IsSelected = ReferenceEquals(c, card);
        var info = new List<RoomInfoLine>
        {
            new(Strings.Pkm_AbilityLabel, Named(names?.Abilities, mon.Ability)),
            new(Strings.Pkm_Nature, Named(names?.Natures, mon.Nature)),
            new(Strings.Pkm_HeldItem, card.ItemText),
        };
        if (mon.Hp >= 0 && mon.Stats.Length > 0)
            info.Add(new(Strings.Room_Hp, $"{mon.Hp} / {mon.Stats[0]}"));

        var moves = new List<MoveLine>();
        for (int i = 0; i < mon.Moves.Length; i++)
        {
            ushort move = mon.Moves[i];
            if (move == 0)
            {
                moves.Add(new MoveLine("—", "", "", Brushes.Transparent, Brushes.Gray));
                continue;
            }
            int type = mon.MoveTypes is { } types && i < types.Length ? types[i] : -1;
            string pp = mon.MovePp is { } p && (2 * i) + 1 < p.Length ? string.Format(Strings.Pkm_PP, p[2 * i], p[(2 * i) + 1]) : "";
            moves.Add(new MoveLine(Named(names?.Moves, move), pp, type >= 0 ? Named(names?.Types, type) : "", TypeColors.Background(type), TypeColors.Foreground(type)));
        }

        string[] labels = [Strings.Stat_HP, Strings.Stat_Atk, Strings.Stat_Def, Strings.Stat_Spe, Strings.Stat_SpA, Strings.Stat_SpD];
        int[] order = [0, 1, 2, 4, 5, 3]; // shown as the preview: HP, Atk, Def, SpA, SpD, Spe
        var stats = order.Where(i => i < mon.Stats.Length).Select(i => new RoomInfoLine(labels[i], mon.Stats[i].ToString())).ToList();
        Detail = new RoomMonDetail(card, info, moves, stats);
    }
}
