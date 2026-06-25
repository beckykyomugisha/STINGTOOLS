using System;
using System.IO;

namespace StingTools.Core.Licensing
{
    public static class LicenseGate
    {
        private static LicenseResult _cached;

        public static string MachineCode => MachineFingerprint.Current;
        public static string LicenseDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Planscape", "StingTools");
        public static string LicensePath => Path.Combine(LicenseDir, "StingTools.lic");

        public static LicenseResult Status => _cached ??= Evaluate();
        public static bool IsLicensed => Status.IsValid;
        public static void Invalidate() => _cached = null;

        private static LicenseResult Evaluate()
        {
            string text = null;
            try { if (File.Exists(LicensePath)) text = File.ReadAllText(LicensePath).Trim(); }
            catch { /* unreadable => NoLicense */ }
            return LicenseVerifier.Verify(text, LicensePublicKey.Pem, MachineCode, DateTimeOffset.UtcNow);
        }

        /// <summary>Validate + persist a license string. Returns null on success, else error message.</summary>
        public static string Apply(string licenseText)
        {
            licenseText = (licenseText ?? "").Trim();
            var r = LicenseVerifier.Verify(licenseText, LicensePublicKey.Pem, MachineCode, DateTimeOffset.UtcNow);
            if (!r.IsValid) return r.Message;
            try
            {
                Directory.CreateDirectory(LicenseDir);
                File.WriteAllText(LicensePath, licenseText);
                Invalidate();
                return null;
            }
            catch (Exception ex) { return "Could not save license: " + ex.Message; }
        }
    }
}
