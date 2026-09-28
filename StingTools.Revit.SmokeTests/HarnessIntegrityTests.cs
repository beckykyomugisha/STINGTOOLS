// StingTools.Revit.SmokeTests — is the build under test the build that ran?
//
// Revit may already have a StingTools.dll loaded: the deployed add-in, from whatever
// folder the .addin manifest points at (it has moved five times in two days before).
// If the CLR hands the tests THAT assembly instead of the one built beside this test
// DLL, every result below describes a different build — green or red, it would be
// evidence about the wrong code. This test compares module version ids and fails the
// run loudly when they differ, rather than letting the rest pass or fail for the
// wrong reason.

using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Autodesk.Revit.ApplicationServices;
using NUnit.Framework;
using StingTools.Core.Drawing.Dimensioning;

namespace StingTools.Revit.SmokeTests
{
    [TestFixture]
    public class HarnessIntegrityTests
    {
        private Application _app;

        [OneTimeSetUp]
        public void Setup(Application application) => _app = application;

        [Test]
        public void RevitLoaded_TheStingToolsBuildBesideThisAssembly()
        {
            var loaded = typeof(ElementDimensioner).Assembly;
            var here = Path.GetDirectoryName(typeof(HarnessIntegrityTests).Assembly.Location);
            var expectedPath = Path.Combine(here ?? "", "StingTools.dll");

            TestContext.Progress.WriteLine($"[smoke] Revit {_app?.VersionNumber} ({_app?.VersionBuild})");
            TestContext.Progress.WriteLine($"[smoke] StingTools loaded from: '{loaded.Location}' mvid {loaded.ManifestModule.ModuleVersionId}");
            TestContext.Progress.WriteLine($"[smoke] StingTools expected at: '{expectedPath}'");

            Assert.That(File.Exists(expectedPath), Is.True,
                $"No StingTools.dll beside the test assembly ({expectedPath}); cannot tell which build is under test.");

            Guid expectedMvid;
            using (var fs = File.OpenRead(expectedPath))
            using (var pe = new PEReader(fs))
            {
                var md = pe.GetMetadataReader();
                expectedMvid = md.GetGuid(md.GetModuleDefinition().Mvid);
            }

            Assert.That(loaded.ManifestModule.ModuleVersionId, Is.EqualTo(expectedMvid),
                $"Revit resolved a DIFFERENT StingTools.dll ('{loaded.Location}') from the one built beside these tests " +
                $"('{expectedPath}'). Every other result in this run describes that build, not this one. " +
                "Disable the deployed add-in (move its StingTools.addin out of %APPDATA%\\Autodesk\\Revit\\Addins\\<ver>) " +
                "or deploy this build, then re-run.");
        }
    }
}
