using NUnit.Framework;
using StingTools.Revit.SmokeTests.Fixtures;

namespace StingTools.Revit.SmokeTests
{
    /// <summary>
    /// Closes the shared smoke model once every fixture has run. It lives in the ROOT test
    /// namespace on purpose: an NUnit SetUpFixture only wraps tests in its own namespace
    /// and below, so one declared in ".Fixtures" would wrap nothing and never close the model.
    /// </summary>
    [SetUpFixture]
    public class SmokeRunTeardown
    {
        [OneTimeTearDown]
        public void CloseModel() => SmokeModelCache.Close();
    }
}
