// Test-only StingToolsApp. TitleBlockSpecRegistry.Load looks its data file up
// through StingToolsApp.FindDataFile; the real class is an IExternalApplication
// and needs Revit. Tests read the shipped JSON directly and never call Load(),
// so this only has to exist and say "not found".
namespace StingTools.Core
{
    internal static class StingToolsApp
    {
        public static string FindDataFile(string fileName) => null;
    }
}
