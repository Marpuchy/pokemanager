namespace Pokemanager.Model.Dump;

/// <summary>
/// Language of the game texts. The value is the index pk3DS uses in X/Y: it is added to <c>a/0/7/2</c> (gametext) and
/// <c>a/0/8/0</c> (storytext) to pick that language's GARC.
/// </summary>
public enum GameLanguage
{
    JapaneseKana = 0,
    JapaneseKanji = 1,
    English = 2,
    French = 3,
    Italian = 4,
    German = 5,
    Spanish = 6,
    Korean = 7,
}
