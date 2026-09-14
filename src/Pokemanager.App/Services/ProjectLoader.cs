using Pokemanager.App.Resources;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.Services;

/// <summary>A project read from disk with its dump and editing session, ready to preview or to manage.</summary>
public sealed record LoadedProject(string Path, Project Project, GameDump Dump, EditorSession Session);

public static class ProjectLoader
{
    /// <summary>Reads the project, opens its dump and the session (on top of its cached randomization, if any).</summary>
    /// <exception cref="ProjectLoadException">The message is ready to show.</exception>
    public static LoadedProject Load(string path, UprService upr)
    {
        try
        {
            var project = Project.Load(path);
            var dump = GameDump.Open(project.RomFsPath, project.ExeFsPath, GameTextLanguage.Current);
            project.Game = dump.Title; // projects from before other games were supported did not store it
            string? randomRomFs = upr.TryGetCached(project, project.Randomization.Preset) is { } cached
                ? System.IO.Path.Combine(cached.TitleDirectory, "romfs")
                : null;
            return new LoadedProject(path, project, dump, EditorSession.Open(project, randomRomFs));
        }
        catch (InvalidDumpException ex)
        {
            throw new ProjectLoadException(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            throw new ProjectLoadException(string.Format(Strings.Main_OpenFailed, path, ex.Message));
        }
    }
}

public sealed class ProjectLoadException(string message) : Exception(message);
