// planscape:// URL protocol — the shared contract.
//
// WHY THIS FILE EXISTS AS A SEPARATE, REVIT-FREE UNIT
// ---------------------------------------------------
// StingTools generates `planscape://dashboard/{name}/{ts}` links (see
// PlanscapeServerClient.BuildDashboardShareLink) and also
// `planscape://issue/{id}` and `planscape://deliverable/{code}` from the BCC.
// Until now nothing on Windows knew what `planscape://` meant, so those links
// were formatted strings and clicking one did nothing.
//
// Windows protocol handlers must point at a standalone .exe. StingTools is a
// DLL loaded inside Revit.exe, so it cannot be the registered target. The
// registered target is the tiny StingLink.exe helper in
// StingTools.LinkHandler/, which shares THIS file via <Compile Include> — the
// same trick tools/StampDrawingTypeChecksums uses. That is why nothing here may
// touch the Revit API, WPF, or anything else the helper cannot load.
//
// Flow (Revit already running — the case this ships working):
//
//   user clicks planscape://issue/abc
//     → Windows launches StingLink.exe "planscape://issue/abc"
//     → helper writes one .link file into the inbox and foregrounds Revit
//     → PlanscapeLinkWatcher (an IIdlingJob inside the plugin) picks it up on
//       the next Idling tick and opens/focuses the BCC on the right tab.
//
// A file inbox rather than a named pipe: the plugin has no listener thread and
// should not grow one — everything in Revit must end up on the API thread via
// Idling anyway, so a pipe would only add a thread whose job is to write to a
// queue the Idling job already reads. The inbox also survives the "Revit is not
// running yet" case for free.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace StingTools.Core
{
    /// <summary>One parsed <c>planscape://</c> link.</summary>
    public sealed class PlanscapeLink
    {
        /// <summary>The first path segment, lower-cased: dashboard | issue | deliverable | …</summary>
        public string Kind { get; set; } = "";

        /// <summary>The second segment, URL-decoded: a project name, an issue id, a deliverable code.</summary>
        public string Target { get; set; } = "";

        /// <summary>The optional third segment. Only <c>dashboard</c> carries one (a yyyyMMdd-HHmm stamp).</summary>
        public string Stamp { get; set; } = "";

        /// <summary>The URI exactly as received, for logging.</summary>
        public string Raw { get; set; } = "";

        public override string ToString() =>
            string.IsNullOrEmpty(Stamp) ? $"{Kind}/{Target}" : $"{Kind}/{Target}/{Stamp}";
    }

    public static class PlanscapeProtocol
    {
        public const string Scheme = "planscape";
        private const string Prefix = Scheme + "://";

        /// <summary>File name of the registered helper executable.</summary>
        public const string HelperExeName = "StingLink.exe";

        /// <summary>
        /// Links older than this are dropped unread. A link is a "take me there
        /// now" gesture; acting on one a user clicked last Tuesday, because Revit
        /// happened to be shut at the time, would be a jump-scare.
        /// </summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Where the helper drops links and the plugin collects them.
        /// LocalApplicationData, so it is per-user and needs no elevation, and
        /// so a roaming profile does not carry a stale link between machines.
        /// </summary>
        public static string InboxDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "STING", "link-inbox");

        // ────────────────────────────────────────────────────────────────
        //  Formatting + parsing
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Percent-encode one path segment. Project names contain spaces
        /// ("Kampala Uganda Temple") and the original link builder did not escape
        /// them, which produced a URI Windows silently truncates at the space.
        /// </summary>
        public static string EscapeSegment(string value) =>
            Uri.EscapeDataString(value ?? string.Empty);

        /// <summary>
        /// Parse a link. Returns null for anything that is not a
        /// <c>planscape://</c> URI with at least a kind and a target.
        ///
        /// Hand-parsed rather than via <see cref="Uri"/> on purpose: links minted
        /// before this pass are unescaped, so <c>new Uri(...)</c> throws on the
        /// very links already sitting in people's clipboards and chat history.
        /// Being lenient here is what makes those still work.
        /// </summary>
        public static PlanscapeLink Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim().Trim('"');
            if (!s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var rest = s.Substring(Prefix.Length).TrimEnd('/');
            if (rest.Length == 0) return null;

            var parts = rest.Split('/');
            var link = new PlanscapeLink
            {
                Raw = s,
                Kind = Unescape(parts[0]).ToLowerInvariant(),
                Target = parts.Length > 1 ? Unescape(parts[1]) : string.Empty,
                Stamp = parts.Length > 2 ? Unescape(parts[2]) : string.Empty,
            };
            return string.IsNullOrEmpty(link.Kind) ? null : link;
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            try { return Uri.UnescapeDataString(s); }
            catch (UriFormatException) { return s; } // a stray % — better raw than lost
        }

        // ────────────────────────────────────────────────────────────────
        //  Inbox
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Write a link into the inbox. Called by the helper .exe. Returns the
        /// file path, or null if it could not be written.
        /// </summary>
        public static string Drop(string rawUri)
        {
            if (string.IsNullOrWhiteSpace(rawUri)) return null;
            try
            {
                Directory.CreateDirectory(InboxDir);
                // Time-ordered name + a GUID tail: two links clicked in the same
                // millisecond must not overwrite one another.
                var name = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyyMMdd-HHmmss-fff}-{1}.link",
                    DateTime.UtcNow, Guid.NewGuid().ToString("N").Substring(0, 8));
                var path = Path.Combine(InboxDir, name);
                File.WriteAllText(path, rawUri.Trim(), new UTF8Encoding(false));
                return path;
            }
            catch (Exception)
            {
                // The helper has no logger and no console. Swallowing here is
                // deliberate; the caller reports failure to the user instead.
                return null;
            }
        }

        /// <summary>
        /// Collect and DELETE every pending link. Called by the plugin.
        ///
        /// Deleting as we read is what makes this safe with two Revit instances
        /// open: whichever one reads the file first owns the link, and the other
        /// never sees it. Two Revits both jumping to the same issue would be
        /// worse than one of them missing it.
        /// </summary>
        public static List<PlanscapeLink> TakePending()
        {
            var found = new List<PlanscapeLink>();
            string dir = InboxDir;
            if (!Directory.Exists(dir)) return found;

            string[] files;
            try { files = Directory.GetFiles(dir, "*.link"); }
            catch (Exception) { return found; }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase); // oldest first
            var cutoff = DateTime.UtcNow - MaxAge;

            foreach (var file in files)
            {
                string raw = null;
                bool stale = false;
                try
                {
                    stale = File.GetLastWriteTimeUtc(file) < cutoff;
                    if (!stale) raw = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    // The helper may still be writing it. Leave it; the next tick
                    // gets it. Do NOT delete a file we failed to read.
                    continue;
                }
                catch (UnauthorizedAccessException) { continue; }

                try { File.Delete(file); } catch (Exception) { /* best effort */ }

                if (stale || raw == null) continue;
                var link = Parse(raw);
                if (link != null) found.Add(link);
            }
            return found;
        }

        // ────────────────────────────────────────────────────────────────
        //  Windows registration — HKEY_CURRENT_USER ONLY
        // ────────────────────────────────────────────────────────────────
        //
        // Deliberately never HKLM. HKCU\Software\Classes needs no elevation, is
        // scoped to the signed-in user, and takes precedence over HKLM for the
        // same key. Writing machine-wide state from a plugin's OnStartup — which
        // runs unattended every time Revit launches — is not something to do
        // without a human saying yes.

        private static string KeyPath => @"Software\Classes\" + Scheme;

        /// <summary>The command line currently registered for the scheme, or null.</summary>
        public static string RegisteredCommand()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command"))
                    return key?.GetValue(null) as string;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Build the exact command string for a helper path.</summary>
        public static string CommandFor(string helperExePath) => "\"" + helperExePath + "\" \"%1\"";

        /// <summary>
        /// Register (or repair) the scheme for the current user so it points at
        /// <paramref name="helperExePath"/>. Idempotent — returns false with a
        /// reason when it did nothing, including the common "already correct".
        ///
        /// Re-registering on every startup is intentional: the plugin folder
        /// moves (it currently lives in CompiledPlugin, and has moved before), and
        /// a protocol pointing at a deleted exe fails silently and confusingly.
        /// </summary>
        public static bool EnsureRegistered(string helperExePath, out string detail)
        {
            detail = "";
            if (string.IsNullOrWhiteSpace(helperExePath))
            {
                detail = "no helper path";
                return false;
            }
            if (!File.Exists(helperExePath))
            {
                // Expected when the plugin is deployed without the helper —
                // registering a path that does not exist is worse than not
                // registering, because Windows then reports a broken handler
                // instead of "no handler".
                detail = $"{HelperExeName} not found beside the plugin ({helperExePath})";
                return false;
            }

            string want = CommandFor(helperExePath);
            if (string.Equals(RegisteredCommand(), want, StringComparison.OrdinalIgnoreCase))
            {
                detail = "already registered";
                return false;
            }

            try
            {
                using (var root = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (root == null) { detail = "could not create HKCU key"; return false; }
                    root.SetValue(null, "URL:Planscape Protocol");
                    // The presence of "URL Protocol" — empty value and all — is
                    // what marks a key as a protocol handler. Without it Windows
                    // ignores the whole branch.
                    root.SetValue("URL Protocol", "");
                    using (var icon = root.CreateSubKey("DefaultIcon"))
                        icon?.SetValue(null, helperExePath + ",0");
                    using (var cmd = root.CreateSubKey(@"shell\open\command"))
                        cmd?.SetValue(null, want);
                }
                detail = want;
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        /// <summary>Remove the registration. Not called automatically — uninstall is a human decision.</summary>
        public static bool Unregister(out string detail)
        {
            detail = "";
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
                return true;
            }
            catch (Exception ex) { detail = ex.Message; return false; }
        }
    }
}
