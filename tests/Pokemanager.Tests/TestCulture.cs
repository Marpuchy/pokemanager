using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pokemanager.Tests;

/// <summary>Tests assert on English messages, whatever the culture of the machine running them.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseEnglish()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
