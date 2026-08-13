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
            return VerifyEither(text, DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Verify against the full-factor <see cref="MachineFingerprint.Current"/> code and,
        /// if that fails, the MachineGuid-only <see cref="MachineFingerprint.Stable"/> code.
        /// The fallback means a transient WMI failure on a secondary factor (CPU / board /
        /// BIOS serial) — which flips Current and silently invalidates a valid license —
        /// no longer locks the user out, provided the license was issued against Stable.
        /// </summary>
        private static LicenseResult VerifyEither(string text, DateTimeOffset now)
        {
            var r = LicenseVerifier.Verify(text, LicensePublicKey.Pem, MachineFingerprint.Current, now);
            if (r.IsValid) return r;
            if (!string.Equals(MachineFingerprint.Stable, MachineFingerprint.Current, StringComparison.Ordinal))
            {
                var rs = LicenseVerifier.Verify(text, LicensePublicKey.Pem, MachineFingerprint.Stable, now);
                if (rs.IsValid) return rs;
            }
            return r; // Current-code result carries the most relevant message
        }

        /// <summary>Validate + persist a license string. Returns null on success, else error message.</summary>
        public static string Apply(string licenseText)
        {
            licenseText = (licenseText ?? "").Trim();
            var r = VerifyEither(licenseText, DateTimeOffset.UtcNow);
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
