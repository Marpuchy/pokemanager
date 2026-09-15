using System.Text.Json;
using Pokemanager.App.Resources;
using Pokemanager.Multiplayer;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// One battle of the room on Pokémon Showdown's battle screen (<c>BattleScreen/battle.html</c> in a WebView): sends the page
/// the new protocol lines and requests, and passes the player's decisions to the room.
/// </summary>
public sealed class BattleViewModel
{
    private readonly RoomViewModel room;
    private readonly IReadOnlyDictionary<int, int> romMoveTypes;
    private RoomBattle battle;
    private bool pageReady;
    private int sentLines;
    private string? sentRequest;
    private bool sentFinish;

    /// <summary>Sound of the battle screen, for this session of the app.</summary>
    private static bool muted;

    public BattleViewModel(RoomViewModel room, RoomBattle battle, string title, IReadOnlyDictionary<int, int> romMoveTypes)
    {
        this.room = room;
        this.battle = battle;
        this.romMoveTypes = romMoveTypes;
        Title = title;
    }

    public string BattleId => battle.Id;
    public string Title { get; }

    public static Uri PageUri => new(Path.Combine(AppContext.BaseDirectory, "BattleScreen", "battle.html"));

    /// <summary>JavaScript for the page, in order. Raised on the UI thread.</summary>
    public event Action<string>? ScriptRequested;

    /// <summary>The window should come to the front.</summary>
    public event Action? ActivateRequested;

    /// <summary>The window was closed.</summary>
    public event Action? Closed;

    public void Activate() => ActivateRequested?.Invoke();

    public void OnClosed() => Closed?.Invoke();

    /// <summary>The room changed: send what the page does not have yet.</summary>
    public void Update(RoomBattle update)
    {
        battle = update;
        if (pageReady)
            Push();
    }

    /// <summary>A message from the page: {"type": "ready" | "choose" | "forfeit" | "mute", …}.</summary>
    public void OnPageMessage(string body)
    {
        JsonElement message;
        try
        {
            // WebView2 hands over the string posted by the page as a JSON string literal.
            using var doc = JsonDocument.Parse(body);
            message = doc.RootElement.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(doc.RootElement.GetString()!).RootElement.Clone()
                : doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }
        string? type = message.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "ready":
                pageReady = true;
                sentLines = 0;
                sentRequest = null;
                sentFinish = false;
                Run("PM.init(" + JsonSerializer.Serialize(new
                {
                    side = battle.MySide ?? "p1",
                    moveTypes = romMoveTypes.ToDictionary(p => p.Key.ToString(), p => p.Value),
                    muted,
                    strings = PageStrings(),
                }) + ")");
                Push();
                break;
            case "choose" when message.TryGetProperty("choice", out var choice) && choice.GetString() is { Length: > 0 and < 60 } text:
                room.ChooseInBattle(battle.Id, text);
                break;
            case "forfeit":
                room.ForfeitBattle(battle.Id);
                break;
            case "mute":
                muted = message.TryGetProperty("muted", out var m) && m.ValueKind == JsonValueKind.True;
                break;
        }
    }

    private void Push()
    {
        if (battle.Log.Count < sentLines)
        {
            // The battle started over (a new log): reload the page, it asks for everything again.
            pageReady = false;
            Run("location.reload()");
            return;
        }
        if (battle.Log.Count > sentLines)
        {
            Run("PM.add(" + JsonSerializer.Serialize(battle.Log.Skip(sentLines)) + ")");
            sentLines = battle.Log.Count;
        }
        if (battle.Phase == BattlePhase.Running && battle.Request != sentRequest)
        {
            sentRequest = battle.Request;
            Run("PM.request(" + JsonSerializer.Serialize(battle.Request) + ")");
        }
        if (!battle.IsActive && !sentFinish)
        {
            sentFinish = true;
            Run("PM.finish(" + JsonSerializer.Serialize(room.BattleResultText(battle)) + ")");
        }
    }

    private void Run(string script) => ScriptRequested?.Invoke(script);

    private static Dictionary<string, string> PageStrings() => new()
    {
        ["whatDo"] = Strings.Battle_WhatDo,
        ["attack"] = Strings.Battle_Attack,
        ["switchTitle"] = Strings.Battle_Switch,
        ["mega"] = Strings.Battle_Mega,
        ["zPower"] = Strings.Battle_ZPower,
        ["ultraBurst"] = Strings.Battle_UltraBurst,
        ["waiting"] = Strings.Battle_Waiting,
        ["teamPreview"] = Strings.Battle_TeamPreviewPick,
        ["forceSwitch"] = Strings.Battle_ForceSwitch,
        ["trapped"] = Strings.Battle_Trapped,
        ["forfeit"] = Strings.Battle_Forfeit,
        ["forfeitConfirm"] = Strings.Battle_ForfeitConfirm,
        ["soundOn"] = Strings.Battle_SoundOn,
        ["soundOff"] = Strings.Battle_SoundOff,
        ["loading"] = Strings.Battle_Loading,
        ["offline"] = Strings.Battle_Offline,
    };
}
