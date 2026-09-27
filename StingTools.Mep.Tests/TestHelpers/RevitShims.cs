// Test-only stand-ins for the few Revit / plugin types the linked MEP kernels
// name. RefrigerantPipeSolver and RefrigerantVendorLimits take an optional
// Document (only PathName is read); the vendor registry resolves its data
// file through StingToolsApp.FindDataFile and logs through StingLog.

using System;
using System.IO;

namespace Autodesk.Revit.DB
{
    public class Document
    {
        public string PathName { get; set; } = "";
    }
}

namespace StingTools.Core
{
    internal static class StingLog
    {
        public static void Info(string msg) { }
        public static void Warn(string msg) { }
        public static void Error(string msg, Exception ex = null) { }
    }

    internal static class StingToolsApp
    {
        /// <summary>Resolves a shipped data file from the repository's StingTools/Data.</summary>
        public static string FindDataFile(string fileName)
        {
            string p = Path.Combine(StingTools.Mep.Tests.RepoData.Dir, fileName);
            return File.Exists(p) ? p : null;
        }
    }
}

namespace StingTools.Mep.Tests
{
    internal static class RepoData
    {
        /// <summary>StingTools/Data, found by walking up from the test binary.</summary>
        public static string Dir
        {
            get
            {
                var d = new DirectoryInfo(AppContext.BaseDirectory);
                while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data")))
                    d = d.Parent;
                if (d == null) throw new DirectoryNotFoundException("StingTools/Data not found above the test binary.");
                return Path.Combine(d.FullName, "StingTools", "Data");
            }
        }

        public static string Read(string fileName) => File.ReadAllText(Path.Combine(Dir, fileName));
    }
}
