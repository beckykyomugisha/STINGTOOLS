// No test may read or write the developer's real ACC sign-in.
//
// AccIssueSync now re-reads the machine credentials file during a refresh (to adopt a
// token another Revit session rotated) and saves after every refresh. Without this, a
// test that provokes a 401 would read %APPDATA%\Planscape\acc_credentials.json, might
// adopt a real token, and could overwrite the real file with test values. The module
// initializer runs before any test, so no test can forget it.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using StingTools.V6;

namespace StingTools.Acc.Tests.TestHelpers
{
    internal static class CredentialIsolation
    {
        internal static readonly string Dir =
            Path.Combine(Path.GetTempPath(), "sting-acc-tests-" + Guid.NewGuid().ToString("N"));

        [ModuleInitializer]
        internal static void Init()
        {
            Directory.CreateDirectory(Dir);
            AccCredentialStore.CredentialsPathOverride = Path.Combine(Dir, "acc_credentials.json");
        }

        /// <summary>Start a test with no saved sign-in, so a token an earlier test saved
        /// cannot be adopted by the next one.</summary>
        internal static void Reset()
        {
            try { File.Delete(AccCredentialStore.CredentialsPath); } catch (IOException) { }
        }
    }
}
