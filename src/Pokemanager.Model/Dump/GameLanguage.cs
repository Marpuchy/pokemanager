namespace Pokemanager.Model.Dump;

/// <summary>
/// Idioma de los textos del juego. El valor es el índice que usa pk3DS en X/Y: se suma a
/// <c>a/0/7/2</c> (gametext) y <c>a/0/8/0</c> (storytext) para elegir el GARC de ese idioma.
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
