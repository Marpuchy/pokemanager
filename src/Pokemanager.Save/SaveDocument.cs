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
/// An X/Y save opened for editing, in memory until <see cref="Write"/>. Everything that depends on the game data
/// (abilities, growth rate, gender ratio, forms, stats, PP) is taken from the ROM being played — the randomized one —
/// not from PKHeX's tables of the original game.
/// </summary>
public sealed class SaveDocument
{
    public const int MaxMoney = 9_999_999;
    public const int MaxBattlePoints = 9_999;
    public const int MaxEvTotal = 510;
    public const int MaxNameLength = 12;
    public const int MaxPlayedHours = 999;

    private readonly SAV6XY sav;
    private readonly PlayerBag6XY bag;

    public string SavePath { get; }
    public GameData Rom { get; private set; }

    /// <summary>Something changed since opening or the last write.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The bytes that were on disk when opened (or last written), to notice if the game saved meanwhile.</summary>
    private byte[] diskBytes;

    private SaveDocument(string savePath, SAV6XY sav, GameData rom, byte[] diskBytes)
    {
        SavePath = savePath;
        this.sav = sav;
        Rom = rom;
        bag = sav.Inventory;
        this.diskBytes = diskBytes;
    }

    /// <exception cref="SaveUpdateException">Not an X/Y save or damaged checksums.</exception>
    public static SaveDocument Open(string savePath, GameData rom)
    {
        byte[] bytes = File.ReadAllBytes(savePath);
        var sav = SaveUpdater.Load(savePath) as SAV6XY
                  ?? throw new SaveUpdateException(string.Format(Strings.Save_NotXY, savePath));
        if (!sav.ChecksumsValid)
            throw new SaveUpdateException(Strings.Save_BadChecksums);
        return new SaveDocument(savePath, sav, rom, bytes);
    }

    /// <summary>The file on disk is no longer what was opened (the game or another tool saved it).</summary>
    public bool ChangedOnDisk() => !File.Exists(SavePath) || !File.ReadAllBytes(SavePath).AsSpan().SequenceEqual(diskBytes);

    /// <summary>
    /// Replaces a save with a stored copy (for example one kept in the project history), after checking the copy is a
    /// valid X/Y save. The current file is backed up first.
    /// </summary>
    /// <returns>Path of the backup of the replaced save.</returns>
    public static string ReplaceFile(string savePath, string copyPath, string backupRoot)
    {
        byte[] copy = File.ReadAllBytes(copyPath);
        return SaveWriter.Write(savePath, copy, backupRoot);
    }

    /// <summary>The ROM changed (another build): stats and options follow it from now on.</summary>
    public void UseRom(GameData rom) => Rom = rom;

    public void MarkDirty() => IsDirty = true;

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
        get => sav.BP;
        set { value = Math.Clamp(value, 0, MaxBattlePoints); if (sav.BP != value) { sav.BP = value; MarkDirty(); } }
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

    public bool GetBadge(int index) => (sav.Badges & (1 << index)) != 0;

    public void SetBadge(int index, bool value)
    {
        int badges = value ? sav.Badges | (1 << index) : sav.Badges & ~(1 << index);
        if (badges != sav.Badges) { sav.Badges = badges; MarkDirty(); }
    }

    // ------------------------------------------------------------------ more trainer data (PKHeX's trainer editor)

    public const int MaxSayingLength = 16;
    public const int VivillonPatterns = 20;
    private static readonly DateTime Epoch = new(2000, 1, 1);

    /// <summary>The Mega Ring: Pokémon can Mega Evolve in battle.</summary>
    public bool MegaEvolutionUnlocked
    {
        get => sav.Status.IsMegaEvolutionUnlocked;
        set { if (sav.Status.IsMegaEvolutionUnlocked != value) { sav.Status.IsMegaEvolutionUnlocked = value; MarkDirty(); } }
    }

    /// <summary>Vivillon pattern of the player's region (0–19).</summary>
    public int Vivillon
    {
        get => sav.Vivillon;
        set { value = Math.Clamp(value, 0, VivillonPatterns - 1); if (sav.Vivillon != value) { sav.Vivillon = value; MarkDirty(); } }
    }

    public int BoxesUnlocked
    {
        get => sav.BoxesUnlocked;
        set { value = Math.Clamp(value, 1, sav.BoxCount); if (sav.BoxesUnlocked != value) { sav.BoxesUnlocked = value; MarkDirty(); } }
    }

    /// <summary>PR Video phrases 1–5.</summary>
    public string GetSaying(int index) => index switch
    {
        0 => sav.Status.Saying1, 1 => sav.Status.Saying2, 2 => sav.Status.Saying3, 3 => sav.Status.Saying4, _ => sav.Status.Saying5,
    };

    public void SetSaying(int index, string value)
    {
        if (value.Length > MaxSayingLength || GetSaying(index) == value)
            return;
        switch (index)
        {
            case 0: sav.Status.Saying1 = value; break;
            case 1: sav.Status.Saying2 = value; break;
            case 2: sav.Status.Saying3 = value; break;
            case 3: sav.Status.Saying4 = value; break;
            default: sav.Status.Saying5 = value; break;
        }
        MarkDirty();
    }

    /// <summary>When the adventure started.</summary>
    public DateTime GameStarted
    {
        get => Epoch.AddSeconds(sav.GameTime.SecondsToStart);
        set { uint s = ToSeconds(value); if (sav.GameTime.SecondsToStart != s) { sav.GameTime.SecondsToStart = s; MarkDirty(); } }
    }

    /// <summary>First entry into the Hall of Fame; null while the league has not been beaten.</summary>
    public DateTime? HallOfFame
    {
        get => sav.GameTime.SecondsToFame == 0 ? null : Epoch.AddSeconds(sav.GameTime.SecondsToFame);
        set { uint s = value is { } d ? ToSeconds(d) : 0; if (sav.GameTime.SecondsToFame != s) { sav.GameTime.SecondsToFame = s; MarkDirty(); } }
    }

    public DateTime? LastSaved => sav.Played.LastSavedDate;

    private static uint ToSeconds(DateTime date) => (uint)Math.Clamp((date - Epoch).TotalSeconds, 0, uint.MaxValue);

    /// <summary>Game records (steps, battles…): (id, PKHeX name) for the ones PKHeX knows.</summary>
    public static IReadOnlyList<(int Id, string Name)> RecordNames { get; } =
        RecordLists.RecordList_6.OrderBy(r => r.Key).Select(r => (r.Key, r.Value)).ToList();

    public int GetRecord(int id) => sav.GetRecord(id);
    public int GetRecordMax(int id) => sav.GetRecordMax(id);

    public void SetRecord(int id, int value)
    {
        value = Math.Clamp(value, 0, sav.GetRecordMax(id));
        if (sav.GetRecord(id) != value) { sav.SetRecord(id, value); MarkDirty(); }
    }

    /// <summary>Battle Maison styles in PKHeX order.</summary>
    public static IReadOnlyList<BattleStyle6> MaisonStyles { get; } =
        [BattleStyle6.Single, BattleStyle6.Double, BattleStyle6.Triple, BattleStyle6.Rotation, BattleStyle6.Multi];

    public int GetMaison(BattleStyle6 style, bool current, bool super) => sav.Maison.GetMaisonStat(style, current, super);

    public void SetMaison(BattleStyle6 style, bool current, bool super, int value)
    {
        ushort v = (ushort)Math.Clamp(value, 0, ushort.MaxValue);
        if (sav.Maison.GetMaisonStat(style, current, super) != v) { sav.Maison.SetMaisonStat(style, current, super, v); MarkDirty(); }
    }

    public int OPowerPoints
    {
        get => sav.OPower.Points;
        set { byte v = (byte)Math.Clamp(value, 0, 255); if (sav.OPower.Points != v) { sav.OPower.Points = v; MarkDirty(); } }
    }

    public void UnlockAllOPowers() { sav.OPower.UnlockAll(); MarkDirty(); }
    public void UnlockAllFriendSafari() { sav.UnlockAllFriendSafariSlots(); MarkDirty(); }
    public void UnlockAllFashion() { sav.Fashion.UnlockAllAccessories(); MarkDirty(); }
    public void UnlockAllSuperTraining() { sav.SuperTrain.UnlockAllStages(dist: true); MarkDirty(); }
    public void FillPokePuffs() { sav.Puff.MaxCheat(special: false); MarkDirty(); }
    public int PokePuffCount => sav.Puff.PuffCount;

    /// <summary>Where the player is: map number and coordinates. A wrong value can leave the player stuck.</summary>
    public (int Map, float X, float Y, float Z, int Rotation) Position => (sav.Situation.M, sav.Situation.X, sav.Situation.Y, sav.Situation.Z, sav.Situation.R);

    public void SetPosition(int map, float x, float y, float z, int rotation)
    {
        if (Position == (map, x, y, z, rotation))
            return;
        (sav.Situation.M, sav.Situation.X, sav.Situation.Y, sav.Situation.Z, sav.Situation.R) = (map, x, y, z, rotation);
        MarkDirty();
    }

    // ------------------------------------------------------------------ Pokémon

    public int BoxCount => sav.BoxCount;
    public int BoxSlotCount => sav.BoxSlotCount;
    public const int PartySize = 6;
    public int PartyCount => sav.PartyCount;

    public string BoxName(int box) => sav.GetBoxName(box);

    /// <summary>A copy of the Pokémon in the slot (species 0 when empty). Changes apply with <see cref="Set"/>.</summary>
    public PK6 Get(SaveSlot slot) => (PK6)(slot.Box is { } box
        ? sav.GetBoxSlotAtIndex(box, slot.Slot)
        : slot.Slot < sav.PartyCount ? sav.GetPartySlotAtIndex(slot.Slot) : sav.BlankPKM).Clone();

    public bool IsEmpty(SaveSlot slot) => Get(slot).Species == 0;

    /// <summary>
    /// Stores the Pokémon. Party slots are filled in order (a Pokémon placed after the last one goes right after it);
    /// party stats are recalculated with the ROM.
    /// </summary>
    public SaveSlot Set(SaveSlot slot, PK6 pk)
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
    public PK6 Create(ushort species, int level)
    {
        var pk = new PK6();
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
        pk.Nickname = SpeciesName.GetSpeciesNameGeneration(species, sav.Language, 6);

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
    public int Level(PKM pk) => Personal(pk.Species, pk.Form) is { } p ? Experience.GetLevel(pk.EXP, (byte)p.EXPGrowth) : pk.CurrentLevel;

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
    public void Normalize(PK6 pk, bool isParty)
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
            pk.Nickname = SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, 6);

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
                else if (item.Count <= 0 || item.Count > pouch.MaxCount)
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

    // ------------------------------------------------------------------ Pokédex

    public bool GetSeen(ushort species) => sav.GetSeen(species);
    public bool GetCaught(ushort species) => sav.GetCaught(species);

    public void SetSeen(ushort species, bool value)
    {
        if (GetSeen(species) == value)
            return;
        if (value)
            sav.Zukan.SetSeen(species, true);
        else
        {
            sav.Zukan.SetCaught(species, false);
            sav.Zukan.ClearSeen(species);
        }
        MarkDirty();
    }

    public void SetCaught(ushort species, bool value)
    {
        if (GetCaught(species) == value)
            return;
        if (value)
        {
            sav.Zukan.SetSeen(species, true);
            sav.Zukan.SetCaught(species, true);
            sav.Zukan.SetLanguageFlag(species, (LanguageID)sav.Language, true);
        }
        else
        {
            sav.Zukan.SetCaught(species, false);
        }
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
}
