using System.Text.Json;

namespace Pokemanager.Randomizer;

/// <summary>Options of a UPR ZX preset as described by <c>describe-settings</c>.</summary>
public sealed record UprSettingsDescription(int Version, IReadOnlyList<UprOptionValue> Options, IReadOnlyList<UprTweakValue> Tweaks);

/// <param name="Type"><c>bool</c>, <c>int</c> or <c>enum</c>.</param>
/// <param name="Value">JSON: boolean, number or the constant name.</param>
/// <param name="Choices">Enum constants, in UPR's order.</param>
public sealed record UprOptionValue(string Name, string Type, JsonElement Value, IReadOnlyList<string>? Choices);

/// <param name="Name">Field name in <c>MiscTweak</c> (e.g. <c>FASTEST_TEXT</c>).</param>
/// <param name="Available">UPR supports it for the given ROM (false when no ROM was given).</param>
public sealed record UprTweakValue(string Name, string Label, string Tooltip, bool Available, bool Value);
