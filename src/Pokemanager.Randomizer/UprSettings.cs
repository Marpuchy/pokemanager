using System.Text.Json;

namespace Pokemanager.Randomizer;

/// <summary>Opciones de un preset de UPR ZX tal como las describe <c>describe-settings</c>.</summary>
public sealed record UprSettingsDescription(int Version, IReadOnlyList<UprOptionValue> Options, IReadOnlyList<UprTweakValue> Tweaks);

/// <param name="Type"><c>bool</c>, <c>int</c> o <c>enum</c>.</param>
/// <param name="Value">JSON: booleano, número o nombre de la constante.</param>
/// <param name="Choices">Constantes del enum, en el orden de UPR.</param>
public sealed record UprOptionValue(string Name, string Type, JsonElement Value, IReadOnlyList<string>? Choices);

/// <param name="Name">Nombre del campo en <c>MiscTweak</c> (p. ej. <c>FASTEST_TEXT</c>).</param>
/// <param name="Available">UPR lo admite para la ROM indicada (false si no se indicó ROM).</param>
public sealed record UprTweakValue(string Name, string Label, string Tooltip, bool Available, bool Value);
