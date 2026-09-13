using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>A stat row: base (from the ROM), IV and EV (editable) and the resulting stat.</summary>
public sealed class StatRowViewModel(PokemonEditorViewModel owner, int index, string label) : ObservableObject
{
    /// <summary>0 HP, 1 Atk, 2 Def, 3 SpA, 4 SpD, 5 Spe.</summary>
    public int Index { get; } = index;
    public string Label { get; } = label;

    public int Base => owner.BaseStat(Index);
    public int Stat => owner.StatValue(Index);

    public decimal? Iv
    {
        get => owner.GetIv(Index);
        set { if (value is { } v) owner.SetIv(Index, (int)Math.Clamp(v, 0, 31)); }
    }

    public decimal? Ev
    {
        get => owner.GetEv(Index);
        set { if (value is { } v) owner.SetEv(Index, (int)Math.Clamp(v, 0, 252)); }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>A move slot: move, PP Ups and current/maximum PP.</summary>
public sealed class MoveSlotViewModel(PokemonEditorViewModel owner, int index) : ObservableObject
{
    public int Index { get; } = index;
    public string Label => string.Format(Strings.Pkm_MoveN, Index + 1);
    public IReadOnlyList<string> MoveNames => owner.Names.Moves;

    public int Move
    {
        get => owner.Pokemon.GetMove(Index);
        set
        {
            if (value < 0 || value >= MoveNames.Count || value == owner.Pokemon.GetMove(Index))
                return;
            owner.Edit(pk =>
            {
                pk.SetMove(Index, (ushort)value);
                SetPP(pk, owner.Document.MaxPP((ushort)value, PPUps(pk)));
            });
        }
    }

    public decimal? PPUpsValue
    {
        get => PPUps(owner.Pokemon);
        set
        {
            if (value is not { } v)
                return;
            int ups = (int)Math.Clamp(v, 0, 3);
            owner.Edit(pk =>
            {
                SetPPUps(pk, ups);
                SetPP(pk, owner.Document.MaxPP(pk.GetMove(Index), ups));
            });
        }
    }

    public string PPText => owner.Pokemon.GetMove(Index) == 0
        ? ""
        : string.Format(Strings.Pkm_PP, PP(owner.Pokemon), owner.Document.MaxPP(owner.Pokemon.GetMove(Index), PPUps(owner.Pokemon)));

    private int PPUps(PKM pk) => Index switch { 0 => pk.Move1_PPUps, 1 => pk.Move2_PPUps, 2 => pk.Move3_PPUps, _ => pk.Move4_PPUps };
    private int PP(PKM pk) => Index switch { 0 => pk.Move1_PP, 1 => pk.Move2_PP, 2 => pk.Move3_PP, _ => pk.Move4_PP };

    private void SetPPUps(PKM pk, int v)
    {
        switch (Index) { case 0: pk.Move1_PPUps = v; break; case 1: pk.Move2_PPUps = v; break; case 2: pk.Move3_PPUps = v; break; default: pk.Move4_PPUps = v; break; }
    }

    private void SetPP(PKM pk, int v)
    {
        switch (Index) { case 0: pk.Move1_PP = v; break; case 1: pk.Move2_PP = v; break; case 2: pk.Move3_PP = v; break; default: pk.Move4_PP = v; break; }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// Editor of the Pokémon in a save slot. Every change is applied to the save document at once (still in memory until
/// "Write to save"), and the document makes it consistent with the ROM: ability, gender, PP and party stats.
/// </summary>
public partial class PokemonEditorViewModel : ObservableObject
{
    private readonly SaveEditorViewModel owner;

    public SaveDocument Document { get; }
    public SaveNames Names { get; }
    public SaveSlot Slot { get; private set; }
    public PK6 Pokemon { get; private set; }

    public IReadOnlyList<StatRowViewModel> StatRows { get; }
    public IReadOnlyList<MoveSlotViewModel> MoveSlots { get; }

    [ObservableProperty]
    public partial string LegalityText { get; set; } = "";

    public PokemonEditorViewModel(SaveEditorViewModel owner, SaveDocument document, SaveNames names, SaveSlot slot)
    {
        this.owner = owner;
        Document = document;
        Names = names;
        Slot = slot;
        Pokemon = document.Get(slot);
        StatRows =
        [
            new(this, 0, Strings.Stat_HP), new(this, 1, Strings.Stat_Atk), new(this, 2, Strings.Stat_Def),
            new(this, 3, Strings.Stat_SpA), new(this, 4, Strings.Stat_SpD), new(this, 5, Strings.Stat_Spe),
        ];
        MoveSlots = [new(this, 0), new(this, 1), new(this, 2), new(this, 3)];
    }

    /// <summary>
    /// True while the view is being refreshed after an edit. Controls push values back while they rebind (a combo box
    /// whose item list changed resets its selection, for example); those are not user edits and are ignored.
    /// </summary>
    private bool refreshing;

    /// <summary>Applies a change to a copy, stores it in the document and refreshes everything shown.</summary>
    public void Edit(Action<PK6> change, string? except = null)
    {
        if (refreshing)
            return;
        var pk = Pokemon.Clone();
        change(pk);
        Slot = Document.Set(Slot, pk);
        Pokemon = Document.Get(Slot);

        refreshing = true;
        try
        {
            LegalityText = "";
            foreach (string name in Notified)
                if (name != except)
                    OnPropertyChanged(name);
            foreach (var row in StatRows) row.Refresh();
            foreach (var move in MoveSlots) move.Refresh();
        }
        finally
        {
            refreshing = false;
        }
        owner.OnPokemonEdited(Slot);
    }

    /// <summary>Keeps the same list instance while its contents do not change, so combo boxes keep their selection.</summary>
    private static IReadOnlyList<string> Stable(ref IReadOnlyList<string>? cache, IReadOnlyList<string> fresh)
    {
        if (cache is null || !cache.SequenceEqual(fresh))
            cache = fresh;
        return cache;
    }

    private IReadOnlyList<string>? formChoices, abilityChoices, genderChoices;

    private static readonly string[] Notified =
    [
        nameof(Title), nameof(Icon), nameof(SpeciesIndex), nameof(FormChoices), nameof(FormIndex), nameof(HasForms), nameof(Nickname), nameof(IsNicknamed),
        nameof(Level), nameof(NatureIndex), nameof(AbilityChoices), nameof(AbilityIndex), nameof(HeldItemIndex),
        nameof(GenderChoices), nameof(GenderIndex), nameof(IsShiny), nameof(Friendship), nameof(BallIndex), nameof(OriginalTrainer),
        nameof(EvTotalText), nameof(EvTotalTooHigh), nameof(Problems), nameof(HasProblems), nameof(IsEgg), nameof(ExpText),
    ];

    public string Title => $"{SaveEditorViewModel.SlotLabel(Slot)} · {Names.SpeciesName(Pokemon.Species)}";
    public Avalonia.Media.Imaging.Bitmap? Icon => Pokemon.IsEgg ? null : owner.Sprites.For(Pokemon.Species, Pokemon.Form, Pokemon.Gender == 1, Pokemon.IsShiny);
    public bool IsEgg => Pokemon.IsEgg;

    public IReadOnlyList<string> SpeciesNames => Names.SpeciesChoices;

    public int SpeciesIndex
    {
        get => Pokemon.Species;
        set
        {
            if (value <= 0 || value >= SpeciesNames.Count || value == Pokemon.Species)
                return;
            int level = Document.Level(Pokemon);
            Edit(pk =>
            {
                pk.Species = (ushort)value;
                pk.Form = 0;
                Document.SetLevel(pk, level); // same level with the new species' growth rate
            });
        }
    }

    public bool HasForms => Document.FormCount(Pokemon.Species) > 1;

    public IReadOnlyList<string> FormChoices => Stable(ref formChoices, Enumerable.Range(0, Document.FormCount(Pokemon.Species))
        .Select(f => string.Format(Strings.Pkm_Form, f)).ToList());

    public int FormIndex
    {
        get => Pokemon.Form;
        set
        {
            if (value < 0 || value >= Document.FormCount(Pokemon.Species) || value == Pokemon.Form)
                return;
            int level = Document.Level(Pokemon);
            Edit(pk => { pk.Form = (byte)value; Document.SetLevel(pk, level); });
        }
    }

    public string Nickname
    {
        get => Pokemon.Nickname;
        set
        {
            if (value == Pokemon.Nickname || value.Length == 0 || value.Length > SaveDocument.MaxNameLength)
                return;
            Edit(pk => { pk.IsNicknamed = true; pk.Nickname = value; }, except: nameof(Nickname));
        }
    }

    public bool IsNicknamed
    {
        get => Pokemon.IsNicknamed;
        set { if (value != Pokemon.IsNicknamed) Edit(pk => pk.IsNicknamed = value); }
    }

    public decimal? Level
    {
        get => Document.Level(Pokemon);
        set { if (value is { } v && (int)v != Document.Level(Pokemon)) Edit(pk => Document.SetLevel(pk, (int)Math.Clamp(v, 1, 100))); }
    }

    public string ExpText => string.Format(Strings.Pkm_Exp, Pokemon.EXP);

    public IReadOnlyList<string> NatureNames => Names.Natures;

    public int NatureIndex
    {
        get => (int)Pokemon.Nature;
        set
        {
            if (value < 0 || value >= 25 || value == (int)Pokemon.Nature)
                return;
            Edit(pk => { pk.Nature = (Nature)value; pk.StatAlignment = (Nature)value; });
        }
    }

    public IReadOnlyList<string> AbilityChoices
    {
        get
        {
            int[] ids = Document.AbilityOptions(Pokemon.Species, Pokemon.Form);
            return Stable(ref abilityChoices,
            [
                string.Format(Strings.Pkm_Ability1, Names.AbilityName(ids[0])),
                string.Format(Strings.Pkm_Ability2, Names.AbilityName(ids[1])),
                string.Format(Strings.Pkm_AbilityHidden, Names.AbilityName(ids[2])),
            ]);
        }
    }

    public int AbilityIndex
    {
        get => SaveUpdaterIndex(Pokemon.AbilityNumber);
        set
        {
            int number = value switch { 0 => 1, 1 => 2, 2 => 4, _ => 0 };
            if (number == 0 || number == Pokemon.AbilityNumber)
                return;
            Edit(pk => pk.AbilityNumber = number);
        }
    }

    private static int SaveUpdaterIndex(int abilityNumber) => abilityNumber switch { 2 => 1, 4 => 2, _ => 0 };

    public IReadOnlyList<string> ItemNames => Names.Items;

    public int HeldItemIndex
    {
        get => Pokemon.HeldItem;
        set
        {
            if (value < 0 || value > Document.MaxItem || value >= ItemNames.Count || value == Pokemon.HeldItem)
                return;
            Edit(pk => pk.HeldItem = value);
        }
    }

    private int[] Genders => Document.GenderOptions(Pokemon.Species, Pokemon.Form);

    public IReadOnlyList<string> GenderChoices => Stable(ref genderChoices, Genders.Select(g => g switch
    {
        0 => Strings.Pkm_Male,
        1 => Strings.Pkm_Female,
        _ => Strings.Pkm_Genderless,
    }).ToList());

    public int GenderIndex
    {
        get => Array.IndexOf(Genders, (int)Pokemon.Gender);
        set
        {
            var genders = Genders;
            if (value < 0 || value >= genders.Length || genders[value] == Pokemon.Gender)
                return;
            Edit(pk => pk.Gender = (byte)genders[value]);
        }
    }

    public bool IsShiny
    {
        get => Pokemon.IsShiny;
        set { if (value != Pokemon.IsShiny) Edit(pk => CommonEdits.SetIsShiny(pk, value)); }
    }

    public decimal? Friendship
    {
        get => Pokemon.CurrentFriendship;
        set { if (value is { } v && (byte)v != Pokemon.CurrentFriendship) Edit(pk => pk.CurrentFriendship = (byte)Math.Clamp(v, 0, 255)); }
    }

    public IReadOnlyList<string> BallNames => Names.Balls;

    public int BallIndex
    {
        get => Pokemon.Ball;
        set
        {
            if (value <= 0 || value > Document.MaxBall || value >= BallNames.Count || value == Pokemon.Ball)
                return;
            Edit(pk => pk.Ball = (byte)value);
        }
    }

    public string OriginalTrainer
    {
        get => Pokemon.OriginalTrainerName;
        set
        {
            if (value == Pokemon.OriginalTrainerName || value.Length == 0 || value.Length > SaveDocument.MaxNameLength)
                return;
            Edit(pk => pk.OriginalTrainerName = value, except: nameof(OriginalTrainer));
        }
    }

    // ------------------------------------------------------------------ stats

    /// <summary>Display order HP, Atk, Def, SpA, SpD, Spe → the document's order HP, Atk, Def, Spe, SpA, SpD.</summary>
    private static int DocumentIndex(int index) => index switch { 3 => 4, 4 => 5, 5 => 3, _ => index };

    public int BaseStat(int index)
    {
        var p = Document.Personal(Pokemon.Species, Pokemon.Form);
        return p is null ? 0 : index switch { 0 => p.HP, 1 => p.ATK, 2 => p.DEF, 3 => p.SPA, 4 => p.SPD, _ => p.SPE };
    }

    public int StatValue(int index) => Document.Stats(Pokemon)[DocumentIndex(index)];

    public int GetIv(int index) => index switch
    {
        0 => Pokemon.IV_HP, 1 => Pokemon.IV_ATK, 2 => Pokemon.IV_DEF, 3 => Pokemon.IV_SPA, 4 => Pokemon.IV_SPD, _ => Pokemon.IV_SPE,
    };

    public int GetEv(int index) => index switch
    {
        0 => Pokemon.EV_HP, 1 => Pokemon.EV_ATK, 2 => Pokemon.EV_DEF, 3 => Pokemon.EV_SPA, 4 => Pokemon.EV_SPD, _ => Pokemon.EV_SPE,
    };

    public void SetIv(int index, int value)
    {
        if (GetIv(index) == value)
            return;
        Edit(pk =>
        {
            switch (index) { case 0: pk.IV_HP = value; break; case 1: pk.IV_ATK = value; break; case 2: pk.IV_DEF = value; break; case 3: pk.IV_SPA = value; break; case 4: pk.IV_SPD = value; break; default: pk.IV_SPE = value; break; }
        });
    }

    public void SetEv(int index, int value)
    {
        if (GetEv(index) == value)
            return;
        Edit(pk =>
        {
            switch (index) { case 0: pk.EV_HP = value; break; case 1: pk.EV_ATK = value; break; case 2: pk.EV_DEF = value; break; case 3: pk.EV_SPA = value; break; case 4: pk.EV_SPD = value; break; default: pk.EV_SPE = value; break; }
        });
    }

    private int EvTotal => Enumerable.Range(0, 6).Sum(GetEv);
    public string EvTotalText => string.Format(Strings.Pkm_EvTotal, EvTotal, SaveDocument.MaxEvTotal);
    public bool EvTotalTooHigh => EvTotal > SaveDocument.MaxEvTotal;

    public IReadOnlyList<string> Problems => Document.Check(Pokemon);
    public bool HasProblems => Problems.Count > 0;

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private void RestorePP() => Edit(Document.RestorePP);

    [RelayCommand]
    private void MaxIvs() => Edit(pk => { pk.IV_HP = pk.IV_ATK = pk.IV_DEF = pk.IV_SPA = pk.IV_SPD = pk.IV_SPE = 31; });

    [RelayCommand]
    private void ClearEvs() => Edit(pk => { pk.EV_HP = pk.EV_ATK = pk.EV_DEF = pk.EV_SPA = pk.EV_SPD = pk.EV_SPE = 0; });

    [RelayCommand]
    private void LevelUpMoves() => Edit(pk =>
    {
        pk.SetMoves(Document.LevelUpMoves(pk.Species, pk.Form, Document.Level(pk)));
        pk.Move1_PPUps = pk.Move2_PPUps = pk.Move3_PPUps = pk.Move4_PPUps = 0;
        Document.RestorePP(pk);
    });

    [RelayCommand]
    private void CheckLegality()
    {
        var (valid, report) = SaveDocument.Legality(Pokemon);
        LegalityText = (valid ? Strings.Pkm_LegalityValid : Strings.Pkm_LegalityInvalid) + Environment.NewLine + report;
    }
}
