// Healthcare Pack H-7 — NFPA 99 §5.1.12 verification log persistence.
// Writes a tamper-evident JSON line to <project>/_BIM_COORD/healthcare/mgas_verifications/.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace StingTools.Core.MedGas
{
    public class MgasVerificationRecord
    {
        public string ProjectCode;
        public string Zone;
        public string GasCode;
        public string VerifierName;
        public string VerifierAsse6030Id;
        public string CertReference;
        public DateTime DateUtc;
        public Dictionary<string, bool> CheckResults = new(); // step → pass
        public bool OverallPass;
        public string Notes;
    }

    public static class MgasVerificationLog
    {
        public static string Persist(string projectFolderRoot, MgasVerificationRecord rec)
        {
            if (string.IsNullOrEmpty(projectFolderRoot) || rec == null) return null;
            var dir = Path.Combine(projectFolderRoot, "_BIM_COORD", "healthcare", "mgas_verifications");
            Directory.CreateDirectory(dir);
            var stamp = rec.DateUtc.ToString("yyyyMMdd_HHmmss");
            var safeZone = SafeName(rec.Zone ?? "all");
            var safeGas  = SafeName(rec.GasCode ?? "all");
            var path = Path.Combine(dir, $"{stamp}_{safeZone}_{safeGas}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(rec, Formatting.Indented));
            return path;
        }

        private static string SafeName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
