// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccCredentialStore.cs
//
// Where the ACC sign-in lives on this machine, and the three things that used to go
// wrong with it.
//
// 1. SECRETS IN PLAINTEXT. %APPDATA%\Planscape\acc_credentials.json held the client
//    secret, the access token and a refresh token that can mint new access tokens for
//    weeks, readable by anything running as the user and copied by every profile backup.
//    They are now protected with Windows DPAPI (CurrentUser scope, the same mechanism the
//    Planscape session file already uses). A protected value is written as "dpapi:<b64>";
//    a plain value is still READ, so an existing file keeps working and is protected on
//    its next save. A value that will not decrypt (the file was copied from another user
//    or machine) is treated as absent - the user signs in again - never as garbage.
//
// 2. TWO REVIT SESSIONS, ONE REFRESH TOKEN. APS rotates the refresh token on every
//    refresh; the old one stops working. Two Revit processes refreshing at the same time
//    both send the same refresh token and one of them is refused - and the per-process
//    semaphore could not see the other process. Refresh now happens under a lock FILE
//    (async-friendly, unlike a named Mutex, which must be released on the thread that took
//    it) and, inside the lock, the file is re-read: if another process already rotated the
//    token, this one ADOPTS the result instead of spending a dead refresh token.
//
// 3. A SAVE THAT FAILS AFTER A REFRESH. The rotated refresh token then exists only in
//    memory and dies with the session, and the next session is locked out with no
//    explanation. Saves are now atomic (temp file + replace) and a failed save is an
//    ERROR the caller reports, not a warning in a log nobody reads.
//
// Revit-free; the tests point CredentialsPathOverride at a temp directory so no test
// ever reads or writes the developer's real credentials.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    public static class AccCredentialStore
    {
        private const string Prefix = "dpapi:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StingTools.ACC.v1");

        /// <summary>The fields that are secrets. Everything else (ids, expiry) stays readable
        /// so a support call can still see which project a machine is pointed at.</summary>
        internal static readonly string[] SecretFields = { "ClientSecret", "AccessToken", "RefreshToken" };

        /// <summary>Test seam. Production never sets it.</summary>
        internal static string CredentialsPathOverride;

        public static string CredentialsPath => CredentialsPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Planscape", "acc_credentials.json");

        private static string LockPath => CredentialsPath + ".lock";

        // ── protection ─────────────────────────────────────────────────────────

        internal static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return plain ?? string.Empty;
            if (!OperatingSystem.IsWindows()) return plain;   // CI / Linux test runs only
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(bytes);
        }

        /// <summary>Plain values pass through (legacy files). A protected value that cannot be
        /// decrypted returns null - the caller treats the secret as absent.</summary>
        internal static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored ?? string.Empty;
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length)),
                    Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                return null;
            }
        }

        // ── load / save ────────────────────────────────────────────────────────

        /// <summary>Read the machine file. Missing file → empty credentials. A secret that will
        /// not decrypt is blanked and reported in <paramref name="warning"/>.</summary>
        public static AccCredentials Load(out string warning)
        {
            warning = string.Empty;
            string path = CredentialsPath;
            if (!File.Exists(path)) return new AccCredentials();
            var j = JObject.Parse(File.ReadAllText(path));
            foreach (var f in SecretFields)
            {
                if (j[f] == null || j[f].Type != JTokenType.String) continue;
                string v = Unprotect((string)j[f]);
                if (v == null)
                {
                    warning += $"{f} could not be decrypted (the file was protected by another Windows user or machine) — sign in again. ";
                    v = string.Empty;
                }
                j[f] = v;
            }
            return j.ToObject<AccCredentials>() ?? new AccCredentials();
        }

        /// <summary>Atomic write of an already-shaped JSON object, secrets protected.
        /// Returns false with <paramref name="error"/> set; never throws.</summary>
        public static bool Save(JObject shaped, out string error)
        {
            error = string.Empty;
            try
            {
                var j = (JObject)shaped.DeepClone();
                foreach (var f in SecretFields)
                    if (j[f] != null && j[f].Type == JTokenType.String)
                        j[f] = Protect((string)j[f]);

                string path = CredentialsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, j.ToString());
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ── cross-process refresh lock ─────────────────────────────────────────

        /// <summary>Hold an exclusive lock file for the duration of a token refresh. Returns
        /// null when it could not be taken within <paramref name="timeout"/>; the caller then
        /// refreshes anyway (a stuck lock must not lock everybody out), and says so.</summary>
        public static async Task<IDisposable> AcquireRefreshLockAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            var until = DateTime.UtcNow + timeout;
            try { Directory.CreateDirectory(Path.GetDirectoryName(LockPath)!); } catch (Exception) { /* reported by the open below */ }
            while (true)
            {
                try
                {
                    return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                        1, FileOptions.DeleteOnClose);
                }
                catch (IOException)
                {
                    if (DateTime.UtcNow >= until) return null;
                    await Task.Delay(150, ct).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }
    }

    /// <summary>How establishing a usable access token ended. Distinguishes "Autodesk said
    /// no" (sign in again) from "could not reach Autodesk" (try again later) - the two used
    /// to be the same bool, so a network drop was reported as a rejected refresh token.</summary>
    public sealed class AccAuthOutcome
    {
        public bool Ok { get; set; }
        /// <summary>AuthFailed or TransportFailed when not Ok.</summary>
        public AccFetchStatus Status { get; set; } = AccFetchStatus.Ok;
        public string Detail { get; set; } = string.Empty;
        /// <summary>Set when the token is usable for this session but could not be saved: the
        /// NEXT session will need a fresh sign-in, and the user must be told now.</summary>
        public string Warning { get; set; } = string.Empty;

        public static AccAuthOutcome Success(string warning = "") => new AccAuthOutcome { Ok = true, Warning = warning ?? string.Empty };
        public static AccAuthOutcome Rejected(string detail) => new AccAuthOutcome { Ok = false, Status = AccFetchStatus.AuthFailed, Detail = detail };
        public static AccAuthOutcome Unreachable(string detail) => new AccAuthOutcome { Ok = false, Status = AccFetchStatus.TransportFailed, Detail = detail };
    }

    /// <summary>How long a sign-in lasts, and when to warn. Autodesk refresh tokens expire
    /// after <see cref="RefreshTokenLifetime"/> if unused; every refresh issues a new one and
    /// restarts the clock. The KUT cycle runs fortnightly - one late cycle away from that
    /// limit - so the plugin refreshes in the background when a document opens
    /// (<see cref="AccTokenKeepAlive"/>) and the ACC card shows the remaining days.</summary>
    public static class AccSignInLifetime
    {
        public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(15);

        /// <summary>Refresh proactively once the token is this old.</summary>
        public static readonly TimeSpan KeepAliveAfter = TimeSpan.FromDays(3);

        /// <summary>Days left before the sign-in lapses, or null when the issue time is unknown
        /// (a token saved before this was recorded).</summary>
        public static double? DaysRemaining(AccCredentials c, DateTime utcNow)
        {
            if (c == null || c.RefreshTokenIssuedAt == default || string.IsNullOrEmpty(c.RefreshToken)) return null;
            return (c.RefreshTokenIssuedAt + RefreshTokenLifetime - utcNow).TotalDays;
        }

        public static string Describe(AccCredentials c, DateTime utcNow)
        {
            if (c == null || string.IsNullOrEmpty(c.RefreshToken)) return "Not signed in to Autodesk.";
            var d = DaysRemaining(c, utcNow);
            if (d == null) return "Signed in (sign-in age unknown — it is refreshed automatically when a model opens).";
            if (d <= 0) return "The Autodesk sign-in has probably EXPIRED — sign in again.";
            if (d < 3) return $"The Autodesk sign-in lapses in {d:F1} day(s) unless a model is opened with STING — sign in again to be safe.";
            return $"Signed in; renews automatically (lapses in {d:F0} days if unused).";
        }

        public static bool ShouldKeepAlive(AccCredentials c, DateTime utcNow)
        {
            if (c == null || string.IsNullOrEmpty(c.RefreshToken) || string.IsNullOrEmpty(c.ClientId)) return false;
            if (c.RefreshTokenIssuedAt == default) return true;
            return utcNow - c.RefreshTokenIssuedAt >= KeepAliveAfter;
        }
    }
}
