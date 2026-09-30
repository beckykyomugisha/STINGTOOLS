using System;
using System.IO;
using Microsoft.Win32;

namespace StingTools.Core.Licensing
{
    /// <summary>
    /// Keeps the trial stamp in two places — beside the licence in ProgramData and under
    /// HKCU — so clearing one does not restart the trial. Read the earliest start of the two;
    /// write both. Format of each copy: "startUnix|lastSeenUnix|seal".
    /// </summary>
    internal static class TrialStore
    {
        private const string RegKey = @"Software\Planscape\StingTools";
        private const string RegValue = "Trial";

        private static string FilePath => Path.Combine(LicenseGate.LicenseDir, "trial.dat");

        /// <summary>Starts the trial on first call, records this launch, and returns the trial state.</summary>
        public static LicenseResult EvaluateAndRecord(string machineCode, DateTimeOffset nowUtc)
        {
            TrialStamp fromFile = Parse(ReadFile());
            TrialStamp fromReg = Parse(ReadRegistry());

            // A copy that parses but does not verify was edited by hand. Stop there rather
            // than silently fall back to the other copy or a fresh trial.
            foreach (var s in new[] { fromFile, fromReg })
            {
                if (s != null && !TrialPolicy.IsSealed(s, machineCode))
                {
                    StingLog.Warn("STING trial record failed its integrity check.");
                    return TrialPolicy.Evaluate(s, machineCode, nowUtc);
                }
            }

            TrialStamp stamp = TrialPolicy.Merge(fromFile, fromReg, machineCode);
            if (stamp == null)
            {
                stamp = TrialPolicy.Start(machineCode, nowUtc);
                StingLog.Info("STING " + TrialPolicy.TrialDays + "-day trial started on this machine.");
            }

            var result = TrialPolicy.Evaluate(stamp, machineCode, nowUtc);
            Save(TrialPolicy.Touch(stamp, machineCode, nowUtc));
            return result;
        }

        private static void Save(TrialStamp s)
        {
            string text = s.StartUnix + "|" + s.LastSeenUnix + "|" + s.Seal;
            try
            {
                Directory.CreateDirectory(LicenseGate.LicenseDir);
                File.WriteAllText(FilePath, text);
            }
            catch (Exception ex) { StingLog.Warn("STING trial record (file) not saved: " + ex.Message); }
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegKey))
                    k?.SetValue(RegValue, text, RegistryValueKind.String);
            }
            catch (Exception ex) { StingLog.Warn("STING trial record (registry) not saved: " + ex.Message); }
        }

        private static string ReadFile()
        {
            try { return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : null; }
            catch (Exception ex) { StingLog.Warn("STING trial record (file) unreadable: " + ex.Message); return null; }
        }

        private static string ReadRegistry()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RegKey))
                    return k?.GetValue(RegValue) as string;
            }
            catch (Exception ex) { StingLog.Warn("STING trial record (registry) unreadable: " + ex.Message); return null; }
        }

        // Unparseable text (a truncated write, say) is treated as absent, not as tampering.
        private static TrialStamp Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var parts = text.Split('|');
            if (parts.Length != 3) return null;
            if (!long.TryParse(parts[0], out long start) || !long.TryParse(parts[1], out long last)) return null;
            return new TrialStamp { StartUnix = start, LastSeenUnix = last, Seal = parts[2] };
        }
    }
}
