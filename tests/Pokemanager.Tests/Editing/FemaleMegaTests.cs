using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Model.Data;
using Xunit;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// The expansion mod's Meowstic: form 1 is the female, form 2 the male mega (the only one its mega table lists) and form 3
/// the female mega, which the game picks by gender. The Pokédex showed form 3 as an ordinary form.
/// </summary>
public class FemaleMegaTests
{
    private static PersonalInfoXY Row(int gender, int forms = 0, int statsIndex = 0)
    {
        var bytes = new byte[PersonalInfoXY.SIZE];
        bytes[0x12] = (byte)gender;
        bytes[0x20] = (byte)forms;
        BitConverter.GetBytes((ushort)statsIndex).CopyTo(bytes, 0x1C);
        return new PersonalInfoXY(bytes);
    }

    private static PersonalInfoXY[] Table()
    {
        // 0: empty, 1: Meowstic (4 forms, forms from row 2), 2: female, 3: male mega, 4: female mega
        return [Row(0), Row(127, 4, 2), Row(127), Row(0), Row(254)];
    }

    [Fact]
    public void A_female_only_form_after_a_male_only_mega_is_the_female_mega()
    {
        var forms = new Dictionary<(int, int), int> { [(1, 2)] = 518 };
        var duplicates = GameData.AddFemaleMegas(Table(), forms);
        Assert.Equal(518, forms[(1, 3)]);
        Assert.Equal(2, duplicates[(1, 3)]);   // a duplicate of form 2, which the application hides
        Assert.False(forms.ContainsKey((1, 1)));
    }

    [Fact]
    public void A_mega_that_is_not_male_only_adds_nothing()
    {
        var table = Table();
        table[3] = Row(127);
        var forms = new Dictionary<(int, int), int> { [(1, 2)] = 518 };
        GameData.AddFemaleMegas(table, forms);
        Assert.Single(forms);
    }
}
