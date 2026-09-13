using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Dump;

public sealed class InvalidDumpException(IReadOnlyList<string> problems)
    : Exception(Strings.Dump_Invalid + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
