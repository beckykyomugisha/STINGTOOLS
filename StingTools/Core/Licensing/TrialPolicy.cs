using System;
using System.Security.Cryptography;
using System.Text;

namespace StingTools.Core.Licensing
{
    /// <summary>When this machine's trial started and the latest time it was seen running.</summary>
    public sealed class TrialStamp
    {
        public long StartUnix;
        public long LastSeenUnix;
        public string Seal;
    }

    /// <summary>
    /// Pure rules for the built-in trial: every machine runs STING for <see cref="TrialDays"/>
    /// days from first launch without a licence file, then needs one.
    ///
    /// This is a convenience, not a lock. The stamp lives on the user's own machine, so
    /// deleting it restarts the trial; the seal only catches a hand-edited date, and the
    /// last-seen time stops a clock wound backwards from adding days. Anything that must not
    /// be bypassed belongs in a signed licence, whose expiry cannot be changed locally.
    /// </summary>
    public static class TrialPolicy
    {
        public const int TrialDays = 90;

        public static TrialStamp Start(string machineCode, DateTimeOffset nowUtc)
        {
            long now = nowUtc.ToUnixTimeSeconds();
            return Sealed(machineCode, now, now);
        }

        /// <summary>Moves last-seen forward (never back) and re-seals.</summary>
        public static TrialStamp Touch(TrialStamp s, string machineCode, DateTimeOffset nowUtc) =>
            Sealed(machineCode, s.StartUnix, Math.Max(s.LastSeenUnix, nowUtc.ToUnixTimeSeconds()));

        public static bool IsSealed(TrialStamp s, string machineCode) =>
            s != null && !string.IsNullOrEmpty(s.Seal) &&
            string.Equals(s.Seal, ComputeSeal(machineCode, s.StartUnix, s.LastSeenUnix), StringComparison.Ordinal);

        /// <summary>
        /// Combines two stored copies of the stamp: earliest start, latest last-seen.
        /// Either may be null. The result is re-sealed.
        /// </summary>
        public static TrialStamp Merge(TrialStamp a, TrialStamp b, string machineCode)
        {
            if (a == null) return b == null ? null : Sealed(machineCode, b.StartUnix, b.LastSeenUnix);
            if (b == null) return Sealed(machineCode, a.StartUnix, a.LastSeenUnix);
            return Sealed(machineCode, Math.Min(a.StartUnix, b.StartUnix), Math.Max(a.LastSeenUnix, b.LastSeenUnix));
        }

        public static LicenseResult Evaluate(TrialStamp s, string machineCode, DateTimeOffset nowUtc)
        {
            if (s == null)
                return new LicenseResult { State = LicenseState.TrialExpired,
                    Message = "No trial record on this machine." };

            if (!IsSealed(s, machineCode))
                return new LicenseResult { State = LicenseState.TrialExpired,
                    Message = "The trial record on this machine was modified. Activate STING with a licence to continue." };

            var start = DateTimeOffset.FromUnixTimeSeconds(s.StartUnix);
            var end = start.AddDays(TrialDays);
            // A clock set backwards must not buy extra days: count from the latest time seen.
            var effectiveNow = DateTimeOffset.FromUnixTimeSeconds(Math.Max(s.LastSeenUnix, nowUtc.ToUnixTimeSeconds()));

            if (effectiveNow >= end)
                return new LicenseResult { State = LicenseState.TrialExpired, Expiry = end,
                    Message = "Your " + TrialDays + "-day trial ended on " + end.UtcDateTime.ToString("yyyy-MM-dd") +
                              ". Activate STING with a licence to continue." };

            int daysLeft = (int)Math.Ceiling((end - effectiveNow).TotalDays);
            return new LicenseResult { State = LicenseState.Trial, Expiry = end, DaysLeft = daysLeft,
                Message = "Trial: " + daysLeft + " day" + (daysLeft == 1 ? "" : "s") + " left (ends " +
                          end.UtcDateTime.ToString("yyyy-MM-dd") + "). Paste a licence below to keep using STING after that." };
        }

        private static TrialStamp Sealed(string machineCode, long start, long lastSeen) =>
            new TrialStamp { StartUnix = start, LastSeenUnix = lastSeen, Seal = ComputeSeal(machineCode, start, lastSeen) };

        // Keyed by the machine code, so a stamp copied from another PC does not verify here.
        private static string ComputeSeal(string machineCode, long start, long lastSeen)
        {
            byte[] key;
            using (var sha = SHA256.Create())
                key = sha.ComputeHash(Encoding.UTF8.GetBytes("STING-TRIAL-v1|" + (machineCode ?? "").ToUpperInvariant()));
            using (var h = new HMACSHA256(key))
            {
                byte[] mac = h.ComputeHash(Encoding.UTF8.GetBytes(start + "|" + lastSeen));
                return Convert.ToBase64String(mac);
            }
        }
    }
}
