using System;

namespace StingTools.Core.Licensing
{
    public enum LicenseState { Valid, NoLicense, BadSignature, WrongMachine, Expired, Malformed }

    public sealed class LicenseResult
    {
        public LicenseState State;
        public string Licensee;
        public DateTimeOffset? Expiry;
        public string Message;
        public bool IsValid => State == LicenseState.Valid;
    }

    public static class LicenseVerifier
    {
        public static LicenseResult Verify(string licenseText, string publicKeyPem,
                                           string machineCode, DateTimeOffset nowUtc)
        {
            if (string.IsNullOrWhiteSpace(licenseText))
                return new LicenseResult { State = LicenseState.NoLicense, Message = "Not activated." };

            string json = LicenseCrypto.VerifyAndExtract(licenseText, publicKeyPem);
            if (json == null)
                return new LicenseResult { State = LicenseState.BadSignature, Message = "License signature invalid or corrupted." };

            LicensePayload p;
            try { p = LicensePayload.FromJson(json); } catch { p = null; }
            if (p == null || string.IsNullOrEmpty(p.MachineCode))
                return new LicenseResult { State = LicenseState.Malformed, Message = "License content unreadable." };

            if (!string.Equals(p.MachineCode, machineCode, StringComparison.OrdinalIgnoreCase))
                return new LicenseResult { State = LicenseState.WrongMachine, Licensee = p.Licensee,
                    Message = "This license is for a different machine." };

            var expiry = DateTimeOffset.FromUnixTimeSeconds(p.ExpiryUnix);
            if (nowUtc >= expiry)
                return new LicenseResult { State = LicenseState.Expired, Expiry = expiry, Licensee = p.Licensee,
                    Message = "License expired on " + expiry.UtcDateTime.ToString("yyyy-MM-dd") + "." };

            return new LicenseResult { State = LicenseState.Valid, Expiry = expiry, Licensee = p.Licensee,
                Message = "Active until " + expiry.UtcDateTime.ToString("yyyy-MM-dd") + "." };
        }
    }
}
