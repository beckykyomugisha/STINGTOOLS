using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StingTools.Acc.Tests
{
    /// <summary>
    /// KUT deep review INT-17. Every path that uploads to ACC must run the post-upload step that
    /// turns the ACCPublish bundle's PREPARED transmittal into SENT. The BIM Coordination Center
    /// card called AccModelUpload.UploadAsync directly and skipped it, so a bundle uploaded from
    /// the card reached ACC while its transmittal stayed PREPARED. Source guard: the upload
    /// needs a live ACC session and Revit, so it cannot run here.
    /// </summary>
    public class AccUploadMarksSentGuardTests
    {
        [Fact]
        public void EveryUploadCallerMarksTheTransmittalSent()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            var sep = Path.DirectorySeparatorChar;
            var callers = Directory.EnumerateFiles(Path.Combine(dir.FullName, "StingTools"), "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(sep + "obj" + sep) && !p.Contains(sep + "bin" + sep))
                .Where(p => !p.EndsWith("AccModelUpload.cs", StringComparison.OrdinalIgnoreCase))
                .Select(p => (p, s: File.ReadAllText(p)))
                .Where(t => Regex.IsMatch(t.s, @"AccModelUpload\.UploadAsync\("))
                .ToList();
            Assert.True(callers.Count >= 2, "expected the command and the BCC card to upload; found " + callers.Count);
            var missing = callers.Where(t => !t.s.Contains("MarkBundleTransmittalSent(")).Select(t => Path.GetFileName(t.p)).ToList();
            Assert.True(missing.Count == 0, "uploads to ACC without marking the transmittal SENT: " + string.Join(", ", missing));
        }
    }
}
