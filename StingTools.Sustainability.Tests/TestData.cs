using System;
using System.IO;

namespace StingTools.Sustainability.Tests
{
    /// <summary>Reads the shipped corporate JSON data files copied to the test
    /// output Data/ folder (see the .csproj &lt;None Include&gt; entries).</summary>
    internal static class TestData
    {
        public static string Read(string fileName)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"Test data file not found (check .csproj CopyToOutputDirectory): {path}");
            return File.ReadAllText(path);
        }
    }
}
