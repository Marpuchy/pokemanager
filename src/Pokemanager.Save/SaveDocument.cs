using PKHeX.Core;
using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Save.Resources;
using GameData = Pokemanager.Model.Data.GameData;

namespace Pokemanager.Save;

/// <summary>A "safe for the game" problem: saving is blocked until it is fixed.</summary>
/// <param name="Slot">Pokémon it refers to; null for trainer or bag problems.</param>
public sealed record SaveProblem(SaveSlot? Slot, string Message);

/// <summary>Result of <see cref="SaveDocument.Write"/>.</summary>
public sealed record SaveWriteResult(string SavePath, string BackupPath);

/// <summary>
/// A save of a 3DS Pokémon game (X/Y, Omega Ruby/Alpha Sapphire, Sun/Moon, Ultra Sun/Ultra Moon) opened for editing, in
/// memory until <see cref="Write"/>. Everything that depends on the game data (abilities, growth rate, gender ratio,
/// forms, stats, PP) is taken from the ROM being played — the randomized one — not from PKHeX's tables of the original
/// game. Trainer data that only some games have is exposed with a <c>Has…</c> flag.
/// </summary>
public sealed class SaveDocument
{
    public const int MaxMoney = 9_999_999;
    public const int MaxBattlePoints = 9_999;
    public const int MaxEvTotal = 510;
    public const int MaxNameLength = 12;
    public const int MaxPlayedHours = 999;

    private readonly SaveFile sav;
    private readonly PlayerBag bag;

    public string SavePath { get; }
    public GameData Rom { get; private set; }

    /// <summary>Something changed since opening or the last write.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The bytes that were on disk when opened (or last written), to notice if the game saved meanwhile.</summary>
    private byte[] diskBytes;

    private SaveDocument(string savePath, SaveFile sav, PlayerBag bag, GameData rom, byte[] diskBytes)
    {
        SavePath = savePath;
        this.sav = sav;
        this.bag = bag;
        Rom = rom;
        this.diskBytes = diskBytes;
    }

    /// <exception cref="SaveUpdateException">Not a supported save or damaged checksums.</exception>
    public static SaveDocument Open(string savePath, GameData rom)
    {
        byte[] bytes = File.ReadAllBytes(savePath);
        var sav = SaveUpdater.Load(savePath);
        PlayerBag bag = sav switch
        {
            SAV6XY xy => xy.Inventory,
            SAV6AO ao => ao.Inventory,
            SAV7SM sm => sm.Inventory,
            SAV7USUM usum => usum.Inventory,
            _ => throw new SaveUpdateException(string.Format(Strings.Save_NotSupported, sav.GetType().Name)),
        };
        if (!sav.ChecksumsValid)
            throw new SaveUpdateException(Strings.Save_BadChecksums);
        return new SaveDocument(savePath, sav, bag, rom, bytes);
    }

    /// <summary>The file on disk is no longer what was opened (the game or another tool saved it).</summary>
    public bool ChangedOnDisk() => !File.Exists(SavePath) || !File.ReadAllBytes(SavePath).AsSpan().SequenceEqual(diskBytes);

    /// <summary>
    /// Replaces a save with a stored copy (for example one kept in the project history), after checking the copy is a
    /// valid save. The current file is backed up first.
    /// </summary>
    /// <returns>Path of the backup of the replaced save.</returns>
    public static string ReplaceFile(string savePath, string copyPath, string backupRoot)
    {
        byte[] copy = File.ReadAllBytes(copyPath);
        return SaveWriter.Write(savePath, copy, backupRoot);
    }

    /// <summary>The whole save as it would be written now (bag included), for undo.</summary>
    public byte[] CaptureState()
    {
        bag.CopyTo(sav);
        return sav.Write().ToArray();
    }

    /// <summary>
    /// The same save file in a state captured with <see cref="CaptureState"/>: a new document, dirty unless the state is what
    /// is on disk.
    /// </summary>
    public SaveDocument WithState(byte[] state)
    {
        var restored = SaveUpdater.Parse(state) ?? throw new SaveUpdateException(string.Format(Strings.Save_NotRecognized, SavePath));
        PlayerBag restoredBag = restored switch
        {
            SAV6XY xy => xy.Inventory,
            SAV6AO ao => ao.Inventory,
            SAV7SM sm => sm.Inventory,
            SAV7USUM usum => usum.Inventory,
            _ => throw new SaveUpdateException(string.Format(Strings.Save_NotSupported, restored.GetType().Name)),
        };
        return new SaveDocument(SavePath, restored, restoredBag, Rom, diskBytes) { IsDirty = !state.AsSpan().SequenceEqual(diskBytes) };
    }

    /// <summary>The ROM changed (another build): stats and options follow it from now on.</summary>
    public void UseRom(GameData rom) => Rom = rom;

    public void MarkDirty() => IsDirty = true;

    /// <summary>6 or 7.</summary>
    public int Generation => sav.Generation;

    /// <summary>The game version the save belongs to (PKHeX).</summary>
    public GameVersion Version => sav.Version;

    // Typed access to the game-specific blocks.
    private SAV6XY? XY => sav as SAV6XY;
    private SAV6AO? AO => sav as SAV6AO;
    private SAV7? Gen7 => sav as SAV7;

    // ------------------------------------------------------------------ trainer

    public string TrainerName
    {
        get => sav.OT;
        set { if (sav.OT != value) { sav.OT = value; MarkDirty(); } }
    }

    /// <summary>0 = male, 1 = female.</summary>
    public int TrainerGender
    {
        get => sav.Gender;
        set { if (sav.Gender != value) { sav.Gender = (byte)value; MarkDirty(); } }
    }

    public ushort TrainerId => sav.TID16;
    public ushort SecretId => sav.SID16;

    /// <summary>Language of the save (PKHeX <see cref="LanguageID"/>), used for default nicknames.</summary>
    public int Language => sav.Language;

    public uint Money
    {
        get => sav.Money;
        set { value = Math.Min(value, (uint)MaxMoney); if (sav.Money != value) { sav.Money = value; MarkDirty(); } }
    }

    public int BattlePoints
    {
        get => XY?.BP ?? AO?.BP ?? (int)(Gen7?.Misc.BP ?? 0);
        set
        {
            value = Math.Clamp(value, 0, MaxBattlePoints);
            if (BattlePoints == value)
                return;
            if (XY is { } xy) xy.BP = value;
            else if (AO is { } ao) ao.BP = value;
            else if (Gen7 is { } g7) g7.Misc.BP = (uint)value;
            MarkDirty();
        }
    }

    public int PlayedHours
    {
        get => sav.PlayedHours;
        set { value = Math.Clamp(value, 0, MaxPlayedHours); if (sav.PlayedHours != value) { sav.PlayedHours = value; MarkDirty(); } }
    }

    public int PlayedMinutes
    {
        get => sav.PlayedMinutes;
        set { value = Math.Clamp(value, 0, 59); if (sav.PlayedMinutes != value) { sav.PlayedMinutes = value; MarkDirty(); } }
    }

    public int PlayedSeconds
    {
        get => sav.PlayedSeconds;
        set { value = Math.Clamp(value, 0, 59); if (sav.PlayedSeconds != value) { sav.PlayedSeconds = value; MarkDirty(); } }
    }

    // ------------------------------------------------------------------ progression: badges (Gen 6) or trials (Gen 7)

    /// <summary>Milestones the save tracks: 8 gym badges in Gen 6; in Gen 7 the four grand trials and the island challenge.</summary>
    public int MilestoneCount => Generation == 6 ? 8 : 5;

    /// <summary>Gen 6: badge bits 0–7. Gen 7: trainer stamps 1–5 (Melemele … Poni trials, island challenge completed).</summary>
    public bool GetMilestone(int index) => Generation == 6
        ? ((XY?.Badges ?? AO!.Badges) & (1 << index)) != 0
        : (Gen7!.Misc.Stamps & (1u << (index + 1))) != 0;

    public void SetMilestone(int index, bool value)
    {
        if (GetMilestone(index) == value)
            return;
        if (Generation == 6)
        {
            int badges = XY?.Badges ?? AO!.Badges;
            badges = value ? badges | (1 << index) : badges & ~(1 << index);
            if (XY is { } xy) xy.Badges = badges; else AO!.Badges = badges;
        }
        else
        {
            uint bit = 1u << (index + 1);
            Gen7!.Misc.Stamps = value ? Gen7.Misc.Stamps | bit : Gen7.Misc.Stamps & ~bit;
        }
        MarkDirty();
    }

    /// <summary>A gym badge is a milestone (kept for the badge editor).</summary>
    public bool GetBadge(int index) => GetMilestone(index);

    public void SetBadge(int index, bool value) => SetMilestone(index, value);

    // ------------------------------------------------------------------ more trainer data (PKHeX's trainer editor)

    public const int MaxSayingLength = 16;
    public const int VivillonPatterns = 20;
    private static readonly DateTime Epoch = new(2000, 1, 1);

    /// <summary>The Mega Ring (Gen 6) or Key Stone (Gen 7): Pokémon can Mega Evolve in battle.</summary>
    public bool MegaEvolutionUnlocked
    {
        get => XY?.Status.IsMegaEvolutionUnlocked ?? AO?.Status.IsMegaEvolutionUnlocked ?? Gen7!.MyStatus.MegaUnlocked;
        set
        {
            if (MegaEvolutionUnlocked == value)
                return;
            if (XY is { } xy) xy.Status.IsMegaEvolutionUnlocked = value;
            else if (AO is { } ao) ao.Status.IsMegaEvolutionUnlocked = value;
            else Gen7!.MyStatus.MegaUnlocked = value;
            MarkDirty();
        }
    }

    public bool HasZMoves => Gen7 is not null;

    /// <summary>The Z-Ring (Gen 7): Z-Moves can be used.</summary>
    public bool ZMovesUnlocked
    {
        get => Gen7?.MyStatus.ZMoveUnlocked ?? false;
        set { if (Gen7 is { } g7 && g7.MyStatus.ZMoveUnlocked != value) { g7.MyStatus.ZMoveUnlocked = value; MarkDirty(); } }
    }

    /// <summary>Vivillon pattern of the player's region (0–19).</summary>
    public int Vivillon
    {
        get => XY?.Vivillon ?? AO?.Vivillon ?? Gen7!.Misc.Vivillon;
        set
        {
            value = Math.Clamp(value, 0, VivillonPatterns - 1);
            if (Vivillon == value)
                return;
            if (XY is { } xy) xy.Vivillon = value;
            else if (AO is { } ao) ao.Vivillon = value;
            else Gen7!.Misc.Vivillon = value;
            MarkDirty();
        }
    }

    public int BoxesUnlocked
    {
        get => sav.BoxesUnlocked;
        set { value = Math.Clamp(value, 1, sav.BoxCount); if (sav.BoxesUnlocked != value) { sav.BoxesUnlocked = value; MarkDirty(); } }
    }

    /// <summary>PR Video phrases (Generation 6).</summary>
    public bool HasSayings => Generation == 6;

    private MyStatus6? Status6 => XY?.Status ?? AO?.Status;

    /// <summary>PR Video phrases 1–5.</summary>
    public string GetSaying(int index) => Status6 is not { } status ? "" : index switch
    {
        0 => status.Saying1, 1 => status.Saying2, 2 => status.Saying3, 3 => status.Saying4, _ => status.Saying5,
    };

    public void SetSaying(int index, string value)
    {
        if (Status6 is not { } status || value.Length > MaxSayingLength || GetSaying(index) == value)
            return;
        switch (index)
        {
            case 0: status.Saying1 = value; break;
            case 1: status.Saying2 = value; break;
            case 2: status.Saying3 = value; break;
            case 3: status.Saying4 = value; break;
            default: status.Saying5 = value; break;
        }
        MarkDirty();
    }

    /// <summary>When the adventure started.</summary>
    public DateTime GameStarted
    {
        get => Epoch.AddSeconds(sav.SecondsToStart);
        set { uint s = ToSeconds(value); if (sav.SecondsToStart != s) { sav.SecondsToStart = s; MarkDirty(); } }
    }

    /// <summary>First entry into the Hall of Fame; null while the league has not been beaten.</summary>
    public DateTime? HallOfFame
    {
        get => sav.SecondsToFame == 0 ? null : Epoch.AddSeconds(sav.SecondsToFame);
        set { uint s = value is { } d ? ToSeconds(d) : 0; if (sav.SecondsToFame != s) { sav.SecondsToFame = s; MarkDirty(); } }
    }

    public DateTime? LastSaved => (XY?.Played ?? AO?.Played ?? Gen7?.Played)?.LastSavedDate;

    private static uint ToSeconds(DateTime date) => (uint)Math.Clamp((date - Epoch).TotalSeconds, 0, uint.MaxValue);

    /// <summary>Game records (steps, battles…): (id, PKHeX name) for the ones PKHeX knows in this generation.</summary>
    public IReadOnlyList<(int Id, string Name)> RecordNames =>
        (Generation == 6 ? RecordLists.RecordList_6 : RecordLists.RecordList_7).OrderBy(r => r.Key).Select(r => (r.Key, r.Value)).ToList();

    private RecordBlock6? RecordBlock => sav switch
    {
        SAV6XY xy => xy.Records,
        SAV6AO ao => ao.Records,
        SAV7 s7 => s7.Records,
        _ => null,
    };

    public int GetRecord(int id) => RecordBlock?.GetRecord(id) ?? 0;
    public int GetRecordMax(int id) => RecordBlock?.GetRecordMax(id) ?? 0;

    public void SetRecord(int id, int value)
    {
        if (RecordBlock is not { } records)
            return;
        value = Math.Clamp(value, 0, records.GetRecordMax(id));
        if (records.GetRecord(id) != value) { records.SetRecord(id, value); MarkDirty(); }
    }

    /// <summary>Battle Maison, O-Powers, Super Training and Poké Puffs (Generation 6).</summary>
    public bool HasGen6Extras => Generation == 6;

    /// <summary>Friend Safari and fashion items (X/Y only).</summary>
    public bool HasXYExtras => XY is not null;

    /// <summary>Battle Maison styles in PKHeX order.</summary>
    public static IReadOnlyList<BattleStyle6> MaisonStyles { get; } =
        [BattleStyle6.Single, BattleStyle6.Double, BattleStyle6.Triple, BattleStyle6.Rotation, BattleStyle6.Multi];

    private MaisonBlock? Maison => XY?.Maison ?? AO?.Maison;

    public int GetMaison(BattleStyle6 style, bool current, bool super) => Maison?.GetMaisonStat(style, current, super) ?? 0;

    public void SetMaison(BattleStyle6 style, bool current, bool super, int value)
    {
        ushort v = (ushort)Math.Clamp(value, 0, ushort.MaxValue);
        if (Maison is { } maison && maison.GetMaisonStat(style, current, super) != v) { maison.SetMaisonStat(style, current, super, v); MarkDirty(); }
    }

    private OPower6? OPower => XY?.OPower ?? AO?.OPower;

    public int OPowerPoints
    {
        get => OPower?.Points ?? 0;
        set { byte v = (byte)Math.Clamp(value, 0, 255); if (OPower is { } o && o.Points != v) { o.Points = v; MarkDirty(); } }
    }

    public void UnlockAllOPowers() { if (OPower is { } o) { o.UnlockAll(); MarkDirty(); } }
    public void UnlockAllFriendSafari() { if (XY is { } xy) { xy.UnlockAllFriendSafariSlots(); MarkDirty(); } }
    public void UnlockAllFashion() { if (XY is { } xy) { xy.Fashion.UnlockAllAccessories(); MarkDirty(); } }

    public void UnlockAllSuperTraining()
    {
        if (XY is { } xy) xy.SuperTrain.UnlockAllStages(dist: true);
        else if (AO is { } ao) ao.SuperTrain.UnlockAllStages(dist: true);
        else return;
        MarkDirty();
    }

    private Puff6? Puff => XY?.Puff ?? AO?.Puff;

    public void FillPokePuffs() { if (Puff is { } p) { p.MaxCheat(special: false); MarkDirty(); } }
    public int PokePuffCount => Puff?.PuffCount ?? 0;

    /// <summary>Where the player is (Generation 6 only here).</summary>
    public bool HasPosition => Generation == 6;

    private Situation6? Situation => XY?.Situation ?? AO?.Situation;

    /// <summary>Where the player is: map number and coordinates. A wrong value can leave the player stuck.</summary>
    public (int Map, float X, float Y, float Z, int Rotation) Position => Situation is { } s ? (s.M, s.X, s.Y, s.Z, s.R) : (0, 0, 0, 0, 0);

    public void SetPosition(int map, float x, float y, float z, int rotation)
    {
        if (Situation is not { } situation || Position == (map, x, y, z, rotation))
            return;
        (situation.M, situation.X, situation.Y, situation.Z, situation.R) = (map, x, y, z, rotation);
        MarkDirty();
    }

    // ------------------------------------------------------------------ Pokémon

    public int BoxCount => sav.BoxCount;
    public int BoxSlotCount => sav.BoxSlotCount;
    public const int PartySize = 6;
    public int PartyCount => sav.PartyCount;

    public string BoxName(int box) => sav is IBoxDetailName names ? names.GetBoxName(box) : $"Box {box + 1}";

    /// <summary>Wallpaper chosen for the box in the game (0-based), 0 when the save has none.</summary>
    public int BoxWallpaper(int box) => sav is IBoxDetailWallpaper wallpapers ? wallpapers.GetBoxWallpaper(box) : 0;

    /// <summary>A copy of the Pokémon in the slot (species 0 when empty). Changes apply with <see cref="Set"/>.</summary>
    public PKM Get(SaveSlot slot) => (slot.Box is { } box
        ? sav.GetBoxSlotAtIndex(box, slot.Slot)
        : slot.Slot < sav.PartyCount ? sav.GetPartySlotAtIndex(slot.Slot) : sav.BlankPKM).Clone();

    public bool IsEmpty(SaveSlot slot) => Get(slot).Species == 0;

    /// <summary>
    /// Stores the Pokémon. Party slots are filled in order (a Pokémon placed after the last one goes right after it);
    /// party stats are recalculated with the ROM.
    /// </summary>
    public SaveSlot Set(SaveSlot slot, PKM pk)
    {
        pk = pk.Clone();
        Normalize(pk, slot.IsParty);
        if (slot.Box is { } box)
        {
            sav.SetBoxSlotAtIndex(pk, box, slot.Slot, EntityImportSettings.None);
        }
        else
        {
            int index = Math.Min(slot.Slot, sav.PartyCount);
            sav.SetPartySlotAtIndex(pk, index, EntityImportSettings.None);
            slot = new SaveSlot(null, index);
        }
        MarkDirty();
        return slot;
    }

    /// <summary>Empties the slot. The last Pokémon of the party cannot be removed.</summary>
    public bool Clear(SaveSlot slot)
    {
        if (slot.Box is { } box)
        {
            sav.SetBoxSlotAtIndex(sav.BlankPKM, box, slot.Slot, EntityImportSettings.None);
        }
        else
        {
            if (slot.Slot >= sav.PartyCount || sav.PartyCount <= 1)
                return false;
            sav.DeletePartySlot(slot.Slot);
        }
        MarkDirty();
        return true;
    }

    /// <summary>A new Pokémon of the trainer: level, ability 1, the latest level-up moves and a random nature and IVs.</summary>
    public PKM Create(ushort species, int level)
    {
        var pk = sav.BlankPKM;
        EntityTemplates.TemplateFields(pk, sav);
        pk.Species = species;
        pk.Form = 0;
        pk.EncryptionConstant = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        pk.PID = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        if (pk.IsShiny)
            CommonEdits.SetUnshiny(pk);
        pk.Nature = (Nature)Random.Shared.Next(25);
        pk.StatAlignment = pk.Nature;
        pk.IV_HP = Random.Shared.Next(32); pk.IV_ATK = Random.Shared.Next(32); pk.IV_DEF = Random.Shared.Next(32);
        pk.IV_SPA = Random.Shared.Next(32); pk.IV_SPD = Random.Shared.Next(32); pk.IV_SPE = Random.Shared.Next(32);
        pk.Ball = (byte)Ball.Poke;
        pk.AbilityNumber = 1;
        pk.Language = sav.Language;
        pk.IsNicknamed = false;
        pk.Nickname = SpeciesName.GetSpeciesNameGeneration(species, sav.Language, (byte)sav.Generation);

        var personal = Personal(species, 0);
        if (personal is not null)
        {
            pk.Gender = RandomGender(personal.Gender);
            pk.OriginalTrainerFriendship = (byte)personal.BaseFriendship;
        }
        SetLevel(pk, level);
        pk.MetLevel = (byte)Math.Clamp(level, 1, 100);
        var now = DateTime.Now;
        (pk.MetYear, pk.MetMonth, pk.MetDay) = ((byte)(now.Year - 2000), (byte)now.Month, (byte)now.Day);

        var moves = LevelUpMoves(species, 0, level);
        pk.SetMoves(moves);
        RestorePP(pk);
        return pk;
    }

    // ------------------------------------------------------------------ ROM-based rules

    /// <summary>Personal entry of a species and form in the ROM.</summary>
    public PersonalInfoXY? Personal(ushort species, byte form)
    {
        if (species == 0 || species >= Rom.Personal.Length)
            return null;
        int index = Rom.Personal[species].FormeIndex(species, form);
        return index < Rom.Personal.Length ? Rom.Personal[index] : null;
    }

    public int MaxSpecies => Math.Min(sav.MaxSpeciesID, Rom.Personal.Length - 1);
    public int MaxMove => Rom.Moves.Length - 1;
    public int MaxItem => sav.MaxItemID;
    public int MaxAbility => sav.MaxAbilityID;
    public int MaxBall => sav.MaxBallID;

    public int FormCount(ushort species) => Personal(species, 0)?.FormeCount is > 0 and var n ? n : 1;

    /// <summary>Ability IDs for ability numbers 1, 2 and hidden.</summary>
    public int[] AbilityOptions(ushort species, byte form) => Personal(species, form)?.Abilities.ToArray() ?? [0, 0, 0];

    /// <summary>Level from EXP with the ROM's growth rate (what the game shows).</summary>
    public int Level(PKM pk) => Personal(pk.Species, pk.Form) is { } p ? ExperienceLevels.LevelOf(pk, (byte)p.EXPGrowth) : pk.CurrentLevel;

    public void SetLevel(PKM pk, int level)
    {
        level = Math.Clamp(level, 1, 100);
        if (Personal(pk.Species, pk.Form) is { } p)
            pk.EXP = Experience.GetEXP((byte)level, (byte)p.EXPGrowth);
        else
            pk.CurrentLevel = (byte)level;
        pk.Stat_Level = (byte)level;
    }

    /// <summary>Genders the species allows: 0 male, 1 female, 2 genderless.</summary>
    public int[] GenderOptions(ushort species, byte form) => Personal(species, form)?.Gender switch
    {
        255 => [2],
        254 => [1],
        0 => [0],
        _ => [0, 1],
    };

    /// <summary>Maximum PP of a move with PP Ups, from the ROM's move data.</summary>
    public int MaxPP(ushort move, int ppUps) => move == 0 || move >= Rom.Moves.Length ? 0 : Rom.Moves[move].PP * (5 + Math.Clamp(ppUps, 0, 3)) / 5;

    public void RestorePP(PKM pk)
    {
        pk.Move1_PP = MaxPP(pk.Move1, pk.Move1_PPUps);
        pk.Move2_PP = MaxPP(pk.Move2, pk.Move2_PPUps);
        pk.Move3_PP = MaxPP(pk.Move3, pk.Move3_PPUps);
        pk.Move4_PP = MaxPP(pk.Move4, pk.Move4_PPUps);
    }

    /// <summary>The last four different moves learned by level up at or below <paramref name="level"/>, in the ROM.</summary>
    public ushort[] LevelUpMoves(ushort species, byte form, int level)
    {
        var personal = Personal(species, form);
        int index = personal is null ? species : Rom.Personal[species].FormeIndex(species, form);
        if (index >= Rom.Learnsets.Length)
            return [0, 0, 0, 0];
        var learnset = Rom.Learnsets[index];
        var moves = new List<ushort>();
        for (int i = 0; i < learnset.Moves.Length; i++)
        {
            if (learnset.Levels[i] > level)
                break;
            ushort move = (ushort)learnset.Moves[i];
            moves.Remove(move);
            moves.Add(move);
        }
        var last = moves.TakeLast(4).ToList();
        while (last.Count < 4)
            last.Add(0);
        return [.. last];
    }

    /// <summary>Stats the game will show: party stats with the ROM's base stats.</summary>
    public int[] Stats(PKM pk) => Personal(pk.Species, pk.Form) is { } p ? SaveUpdater.CalculateStats(pk, p) : [0, 0, 0, 0, 0, 0];

    /// <summary>
    /// Makes the Pokémon consistent with the ROM after an edit: ability from its ability number, gender allowed by the
    /// species, current PP within the maximum, and party stats and level.
    /// </summary>
    public void Normalize(PKM pk, bool isParty)
    {
        if (pk.Species == 0)
            return;
        byte formCount = (byte)FormCount(pk.Species);
        if (pk.Form >= formCount)
            pk.Form = 0;

        var abilities = AbilityOptions(pk.Species, pk.Form);
        int number = pk.AbilityNumber is 1 or 2 or 4 ? pk.AbilityNumber : 1;
        pk.AbilityNumber = number;
        if (abilities[SaveUpdater.AbilityIndex(number)] is var ability and not 0)
            pk.Ability = ability;

        var genders = GenderOptions(pk.Species, pk.Form);
        if (!genders.Contains(pk.Gender))
            pk.Gender = (byte)genders[0];

        pk.Move1_PP = Math.Min(pk.Move1_PP, MaxPP(pk.Move1, pk.Move1_PPUps));
        pk.Move2_PP = Math.Min(pk.Move2_PP, MaxPP(pk.Move2, pk.Move2_PPUps));
        pk.Move3_PP = Math.Min(pk.Move3_PP, MaxPP(pk.Move3, pk.Move3_PPUps));
        pk.Move4_PP = Math.Min(pk.Move4_PP, MaxPP(pk.Move4, pk.Move4_PPUps));

        if (!pk.IsNicknamed)
            pk.Nickname = SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, (byte)sav.Generation);

        if (isParty)
        {
            int[] stats = Stats(pk);
            bool fullHp = pk.Stat_HPCurrent >= pk.Stat_HPMax;
            pk.Stat_Level = (byte)Level(pk);
            (pk.Stat_HPMax, pk.Stat_ATK, pk.Stat_DEF, pk.Stat_SPE, pk.Stat_SPA, pk.Stat_SPD) = (stats[0], stats[1], stats[2], stats[3], stats[4], stats[5]);
            pk.Stat_HPCurrent = fullHp || pk.Stat_HPCurrent > stats[0] ? stats[0] : pk.Stat_HPCurrent;
        }
        pk.RefreshChecksum();
    }

    private static byte RandomGender(int ratio) => ratio switch
    {
        255 => 2,
        254 => 1,
        0 => 0,
        _ => (byte)(Random.Shared.Next(253) + 1 < ratio ? 1 : 0),
    };

    // ------------------------------------------------------------------ validation

    /// <summary>"Safe for the game" problems of one Pokémon.</summary>
    public IReadOnlyList<string> Check(PKM pk)
    {
        var problems = new List<string>();
        if (pk.Species == 0)
            return problems;
        if (pk.Species > MaxSpecies || Personal(pk.Species, 0) is null)
        {
            problems.Add(string.Format(Strings.Check_Species, pk.Species));
            return problems;
        }
        if (pk.Form >= FormCount(pk.Species))
            problems.Add(string.Format(Strings.Check_Form, pk.Form));
        int level = Level(pk);
        if (level is < 1 or > 100)
            problems.Add(string.Format(Strings.Check_Level, level));

        ushort[] moves = [pk.Move1, pk.Move2, pk.Move3, pk.Move4];
        if (moves.All(m => m == 0))
            problems.Add(Strings.Check_NoMoves);
        foreach (ushort move in moves.Where(m => m > MaxMove))
            problems.Add(string.Format(Strings.Check_Move, move));
        if (moves.Where(m => m != 0).GroupBy(m => m).Any(g => g.Count() > 1))
            problems.Add(Strings.Check_DuplicateMoves);
        int[] ppUps = [pk.Move1_PPUps, pk.Move2_PPUps, pk.Move3_PPUps, pk.Move4_PPUps];
        if (ppUps.Any(u => u is < 0 or > 3))
            problems.Add(Strings.Check_PPUps);

        if (pk.AbilityNumber is not (1 or 2 or 4) || pk.Ability <= 0 || pk.Ability > MaxAbility)
            problems.Add(string.Format(Strings.Check_Ability, pk.Ability));
        if (pk.HeldItem < 0 || pk.HeldItem > MaxItem)
            problems.Add(string.Format(Strings.Check_Item, pk.HeldItem));
        if ((int)pk.Nature > 24)
            problems.Add(Strings.Check_Nature);
        if (pk.Ball is 0 || pk.Ball > MaxBall)
            problems.Add(string.Format(Strings.Check_Ball, pk.Ball));
        if (!GenderOptions(pk.Species, pk.Form).Contains(pk.Gender))
            problems.Add(Strings.Check_Gender);

        int[] ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE];
        int[] evs = [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE];
        if (ivs.Any(v => v is < 0 or > 31))
            problems.Add(Strings.Check_IVs);
        if (evs.Any(v => v is < 0 or > 252))
            problems.Add(Strings.Check_EVs);
        if (evs.Sum() > MaxEvTotal)
            problems.Add(string.Format(Strings.Check_EvTotal, evs.Sum(), MaxEvTotal));

        if (string.IsNullOrEmpty(pk.Nickname) || pk.Nickname.Length > MaxNameLength)
            problems.Add(Strings.Check_Nickname);
        if (string.IsNullOrEmpty(pk.OriginalTrainerName) || pk.OriginalTrainerName.Length > MaxNameLength)
            problems.Add(Strings.Check_OriginalTrainer);
        return problems;
    }

    /// <summary>Every "safe for the game" problem of the save. Writing is refused while there are any.</summary>
    public IReadOnlyList<SaveProblem> Validate()
    {
        var problems = new List<SaveProblem>();
        if (string.IsNullOrEmpty(TrainerName) || TrainerName.Length > MaxNameLength)
            problems.Add(new SaveProblem(null, Strings.Check_TrainerName));

        if (sav.PartyCount == 0 || Enumerable.Range(0, sav.PartyCount).All(i => sav.GetPartySlotAtIndex(i).IsEgg))
            problems.Add(new SaveProblem(null, Strings.Check_PartyEmpty));
        for (int i = 0; i < sav.PartyCount; i++)
            foreach (string p in Check(sav.GetPartySlotAtIndex(i)))
                problems.Add(new SaveProblem(new SaveSlot(null, i), p));
        for (int box = 0; box < sav.BoxCount; box++)
            for (int slot = 0; slot < sav.BoxSlotCount; slot++)
                foreach (string p in Check(sav.GetBoxSlotAtIndex(box, slot)))
                    problems.Add(new SaveProblem(new SaveSlot(box, slot), p));

        foreach (var pouch in bag.Pouches)
        {
            var legal = pouch.GetAllItems().ToArray().Select(i => (int)i).ToHashSet();
            foreach (var item in pouch.Items.Where(i => i.Index != 0))
            {
                if (!legal.Contains(item.Index))
                    problems.Add(new SaveProblem(null, string.Format(Strings.Check_BagItem, item.Index, pouch.Type)));
                // Gen 7 keeps used-up items in the bag with count 0 (the game writes them); Gen 6 removes them.
                else if (item.Count < (Generation == 6 ? 1 : 0) || item.Count > pouch.MaxCount)
                    problems.Add(new SaveProblem(null, string.Format(Strings.Check_BagCount, item.Index, item.Count, pouch.MaxCount)));
            }
        }
        return problems;
    }

    /// <summary>
    /// PKHeX legality report, only informative: PKHeX compares with the original game, so anything the randomizer
    /// changes (abilities, learnsets, encounters) shows up as illegal.
    /// </summary>
    public static (bool Valid, string Report) Legality(PKM pk)
    {
        if (pk.Species == 0)
            return (true, "");
        try
        {
            var la = new LegalityAnalysis(pk);
            return (la.Valid, la.Report());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ------------------------------------------------------------------ bag

    public IReadOnlyList<InventoryPouch> Pouches => bag.Pouches;

    /// <summary>Whether the bag holds the item (any pouch, count above zero).</summary>
    public bool HasItem(int item) =>
        item > 0 && bag.Pouches.Any(p => p.Items.Any(i => i.Index == item && i.Count > 0));

    /// <summary>Item IDs a pouch accepts.</summary>
    public static IReadOnlyList<ushort> PouchItems(InventoryPouch pouch) => pouch.GetAllItems().ToArray();

    /// <summary>Replaces the contents of a pouch with (item, count) pairs, in order.</summary>
    public void SetPouch(InventoryPouch pouch, IEnumerable<(int Item, int Count)> items)
    {
        var list = items.Where(i => i.Item != 0 && i.Count > 0).Take(pouch.Items.Length).ToList();
        for (int i = 0; i < pouch.Items.Length; i++)
        {
            if (i < list.Count)
            {
                pouch.Items[i].Index = list[i].Item;
                pouch.Items[i].Count = Math.Min(list[i].Count, pouch.MaxCount);
            }
            else
            {
                pouch.Items[i].Clear();
            }
        }
        MarkDirty();
    }

    /// <summary>
    /// Adds <paramref name="count"/> of an item to the pocket that holds it, on top of what is already there and without
    /// passing the pocket's maximum. Returns how many were actually added (0 if no pocket takes it or it is full).
    /// </summary>
    public int GiveItem(int item, int count)
    {
        if (item <= 0 || count <= 0)
            return 0;
        var pouch = bag.Pouches.FirstOrDefault(p => p.CanContain((ushort)item));
        if (pouch is null)
            return 0;

        int added;
        var existing = pouch.Items.FirstOrDefault(i => i.Index == item);
        if (existing is not null)
        {
            added = Math.Min(count, pouch.MaxCount - existing.Count);
            existing.Count += Math.Max(0, added);
        }
        else
        {
            var free = pouch.Items.FirstOrDefault(i => i.Index == 0);
            if (free is null)
                return 0;
            added = Math.Min(count, pouch.MaxCount);
            free.Index = item;
            free.Count = added;
        }
        if (added > 0)
            MarkDirty();
        return Math.Max(0, added);
    }

    // ------------------------------------------------------------------ Pokédex

    public bool GetSeen(ushort species) => sav.GetSeen(species);
    public bool GetCaught(ushort species) => sav.GetCaught(species);

    public void SetSeen(ushort species, bool value)
    {
        if (GetSeen(species) == value)
            return;
        if (!value)
            sav.SetCaught(species, false);
        sav.SetSeen(species, value);
        MarkDirty();
    }

    public void SetCaught(ushort species, bool value)
    {
        if (GetCaught(species) == value)
            return;
        if (value)
            sav.SetSeen(species, true);
        sav.SetCaught(species, value);
        MarkDirty();
    }

    // ------------------------------------------------------------------ write

    /// <summary>
    /// Writes the save: refuses while <see cref="Validate"/> reports problems, keeps a backup of what was on disk, writes
    /// atomically and checks the result.
    /// </summary>
    public SaveWriteResult Write(string backupRoot)
    {
        bag.CopyTo(sav);
        if (Validate() is { Count: > 0 } problems)
            throw new SaveUpdateException(string.Format(Strings.Check_Blocked, problems.Count, problems[0].Message));
        if (ChangedOnDisk())
            throw new SaveUpdateException(Strings.Check_ChangedOnDisk);

        byte[] updated = sav.Write().ToArray();
        string backup = SaveWriter.Write(SavePath, updated, backupRoot);
        diskBytes = updated;
        IsDirty = false;
        return new SaveWriteResult(SavePath, backup);
    }

    /// <summary>
    /// Starts the game over, the way the console does it: the whole save data goes, not just the file.
    /// </summary>
    /// <remarks>
    /// **Measured in the user's Citra folder**: a game's save is a folder (<c>data/00000001/</c>, holding <c>main</c>) and a
    /// 16-byte <c>data/00000001.metadata</c> next to it — the archive's format. With both gone the game finds its save
    /// data unformatted and starts a clean adventure; with only <c>main</c> gone (what this did before) the archive is
    /// still there, the file is not, and the game reports the save as **corrupted**. So both are moved to the backups,
    /// together, and can be put back the same way. The path is checked to be that shape first, so nothing else is ever
    /// removed.
    /// </remarks>
    /// <returns>The folder the old save data was moved to.</returns>
    public string Reset(string backupRoot)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(SavePath))
                        ?? throw new SaveUpdateException(string.Format(Strings.Save_ResetNotSaveData, SavePath));
        string metadata = folder + ".metadata";
        string name = Path.GetFileName(folder);
        bool shaped = name.Length == 8 && name.All(Uri.IsHexDigit)
                      && string.Equals(Path.GetFileName(Path.GetDirectoryName(folder)), "data", StringComparison.OrdinalIgnoreCase);
        if (!shaped)
            throw new SaveUpdateException(string.Format(Strings.Save_ResetNotSaveData, SavePath));

        string target = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-start-over");
        for (int n = 1; Directory.Exists(target); n++)
            target = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-start-over-" + n);
        Directory.CreateDirectory(target);

        // Copy first, check the copy, and only then take the originals away.
        string copiedFolder = Path.Combine(target, name);
        CopyFolder(folder, copiedFolder);
        if (File.Exists(metadata))
            File.Copy(metadata, Path.Combine(target, name + ".metadata"));
        if (!File.ReadAllBytes(Path.Combine(copiedFolder, Path.GetFileName(SavePath))).AsSpan().SequenceEqual(File.ReadAllBytes(SavePath)))
            throw new SaveUpdateException(string.Format(Strings.Save_ResetCopyFailed, target));

        Directory.Delete(folder, recursive: true);
        if (File.Exists(metadata))
            File.Delete(metadata);
        IsDirty = false;
        return target;
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (string sub in Directory.GetDirectories(from))
            CopyFolder(sub, Path.Combine(to, Path.GetFileName(sub)));
    }
}
