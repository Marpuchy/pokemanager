namespace Pokemanager.Model.Dump;

public sealed class InvalidDumpException(IReadOnlyList<string> problems)
    : Exception("El volcado no es utilizable:" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
