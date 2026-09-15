using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Multiplayer;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// Playing together inside the game: the host runs the emulator's room server and everyone's emulator joins it through
/// the Pokemanager room. It opens by itself when another player has a game of the host's generation.
/// </summary>
public sealed partial class RoomViewModel
{
    private EmulatorRoomServer? emulatorServer;
    private bool emulatorAutoTried;

    public bool EmulatorRoomOpen => session?.EmulatorRoom is not null;
    public bool CanOpenEmulatorRoom => IsHost && !EmulatorRoomOpen;
    public bool CanCloseEmulatorRoom => IsHost && EmulatorRoomOpen;

    private int? LocalGeneration => session?.Players.FirstOrDefault(p => p.IsLocal)?.Snapshot?.Generation ?? shared?.Dump.Title.Generation();

    public string EmulatorRoomText
    {
        get
        {
            if (session?.EmulatorRoom is not { } room)
                return Strings.Emu_Closed;
            string game = TitleOf(room.Info.Game ?? "")?.DisplayName() ?? room.Info.Game ?? "?";
            string text = IsHost
                ? string.Format(Strings.Emu_OpenHost, game, room.Info.Generation)
                : string.Format(Strings.Emu_OpenGuest, session.Players.FirstOrDefault(p => p.IsHost)?.Name ?? "?", game, room.Info.Generation);
            if (LocalGeneration is { } mine && mine != room.Info.Generation)
                text += " " + string.Format(Strings.Emu_OtherGeneration, mine, room.Info.Generation);
            return text;
        }
    }

    public string EmulatorDirectConnectText => session?.EmulatorRoom is { } room
        ? string.Format(Strings.Emu_DirectConnect, room.Port, room.Info.Password)
        : "";

    [RelayCommand]
    private async Task OpenEmulatorRoom() => await OpenEmulatorRoomAsync(quiet: false);

    private async Task OpenEmulatorRoomAsync(bool quiet)
    {
        if (session is not { IsHost: true } s || shared is not { } project || s.EmulatorRoom is not null)
            return;
        var candidates = await Task.Run(() => EmulatorPrograms(project));
        if (EmulatorRoomServer.LocateServer(candidates) is not { } program)
        {
            if (!quiet)
                Say(Strings.Emu_NoServer, error: true);
            return;
        }
        try
        {
            var title = project.Dump.Title;
            emulatorServer?.Dispose();
            emulatorServer = await Task.Run(() => EmulatorRoomServer.Start(program, s.RoomName, title.DisplayName(), title.TitleId(),
                Path.Combine(AppSettings.DataRoot, "emulator-room.log")));
            var info = new EmulatorRoomInfo(s.RoomName, emulatorServer.Password, title.Generation(),
                session.Players.FirstOrDefault(p => p.IsLocal)?.Snapshot?.Game);
            s.OpenEmulatorRoom(info, emulatorServer.Port);
            Say(string.Format(Strings.Emu_Opened, info.Generation));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            Say(string.Format(Strings.Emu_Failed, ex.Message), error: true);
        }
    }

    [RelayCommand]
    private void CloseEmulatorRoom()
    {
        session?.CloseEmulatorRoom();
        emulatorServer?.Dispose();
        emulatorServer = null;
        emulatorAutoTried = true; // closed on purpose: do not open it again by itself
    }

    /// <summary>Starts the emulator with the shared project's ROM, joined to the emulator room.</summary>
    [RelayCommand]
    private async Task PlayInEmulatorRoom()
    {
        if (session?.EmulatorRoom is not { } room || shared is not { } project)
            return;
        var r = project.Project.Randomization;
        string? rom = r.LastBuiltRom is { } built && File.Exists(built)
            ? built
            : !r.Enabled && project.Project.Edits.Count == 0 ? project.Project.ResolveRomFile() : null;
        if (rom is null)
        {
            Say(Strings.Play_BuildFirst, error: true);
            return;
        }
        if (EmulatorUserFolders.RunningEmulators(Settings.EffectiveEmulatorName) is { Count: > 0 } running)
        {
            Say(string.Format(Strings.Emu_CloseEmulator, string.Join(", ", running)), error: true);
            return;
        }
        var candidates = await Task.Run(() => EmulatorPrograms(project));
        if (EmulatorRoomServer.LocateJoinProgram(candidates) is not { } program)
        {
            Say(Strings.Emu_NoJoinProgram, error: true);
            return;
        }
        try
        {
            EmulatorRoomServer.LaunchInRoom(program, rom, ProfileName, room.Info.Password, "127.0.0.1", room.Port);
            Say(string.Format(Strings.Emu_Started, Path.GetFileName(rom)));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Say(string.Format(Strings.Play_Failed, program, ex.Message), error: true);
        }
    }

    /// <summary>The emulator program in use and every other one found (the room programs live next to them).</summary>
    private List<string?> EmulatorPrograms(LoadedProject project)
    {
        var list = new List<string?> { Settings.EffectiveEmulatorExecutable(project.Project.ResolveRomFile(), project.Project.DumpDirectory) };
        var hints = EmulatorUserFolders.Detect().SelectMany(e => EmulatorExecutables.ConfigHints(e.Path));
        list.AddRange(EmulatorExecutables.Detect(hints).Select(e => e.Path));
        return list;
    }

    private void RefreshEmulatorRoom(RoomSession s)
    {
        // Host: open it by itself once another player online has a game of the same generation.
        if (s.IsHost && s.EmulatorRoom is null && !emulatorAutoTried && LocalGeneration is { } mine
            && s.Players.Any(p => !p.IsLocal && p.Online && p.Snapshot?.Generation == mine))
        {
            emulatorAutoTried = true;
            _ = OpenEmulatorRoomAsync(quiet: true);
        }
        foreach (string property in new[]
                 {
                     nameof(EmulatorRoomOpen), nameof(CanOpenEmulatorRoom), nameof(CanCloseEmulatorRoom), nameof(EmulatorRoomText),
                     nameof(EmulatorDirectConnectText),
                 })
            OnPropertyChanged(property);
    }

    private void StopEmulatorServer()
    {
        emulatorServer?.Dispose();
        emulatorServer = null;
        emulatorAutoTried = false;
    }
}
