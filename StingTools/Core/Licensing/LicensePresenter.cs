// Tells planscape.build which licence this machine is running.
//
// REPORTING ONLY — and that constraint is load-bearing, not a nicety. A STING
// licence is verified entirely offline against a public key compiled into this
// assembly. It must keep working when planscape.build is unreachable, refuses
// the presentation, is mid-deploy, or has never heard of this licence at all.
// So every failure here is swallowed, nothing is ever awaited on the Revit
// thread, no dialog is ever shown, and the response is never consulted for
// permission. If this whole class threw on every call, Revit would behave
// identically.
//
// What it buys: today we issue a licence and never hear from it again. We
// cannot answer "how many of the machines we licensed are actually running",
// "on which Revit", "on which plugin build", or "is the .lic file out there
// still the one we issued". Presentation answers all four, and lands on the
// same row the seat cap is counted from (see functions/api/license/present.ts).
//
// Turning it off, for air-gapped or secure-estate machines:
//   setx STING_LICENSE_PRESENT 0
// StingOfflineConfig is also honoured, but note it is still at its defaults
// during OnStartup — the per-project sting_config.json has not been read yet,
// because no document is open. The environment variable is the reliable
// machine-level switch.

using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Licensing
{
    public static class LicensePresenter
    {
        private const string DefaultEndpoint = "https://planscape.build/api/license/present";

        // A licensed machine that is used daily should cost us one request a
        // day, not one per Revit launch — some users restart Revit a dozen
        // times an afternoon.
        private static readonly TimeSpan MinInterval = TimeSpan.FromHours(24);

        private static readonly HttpClient Http = new HttpClient
        {
            // Startup is the worst possible moment to be slow. This runs off
            // the Revit thread, but a socket left hanging still costs a thread
            // and a wake-up on shutdown.
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static string StateDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Planscape");

        private static string StatePath => Path.Combine(StateDir, "license_presentation.json");

        /// <summary>
        /// Presents this machine's licence in the background. Returns immediately.
        /// Never throws, never blocks, never gates anything.
        /// </summary>
        public static void PresentInBackground(string revitVersion)
        {
            try
            {
                // Deliberately not awaited, and deliberately not surfaced. The
                // continuation swallows faults so an unobserved task exception
                // can never reach Revit's unhandled handler.
                Task.Run(() => PresentAsync(revitVersion))
                    .ContinueWith(
                        t => StingLog.Warn("License presentation faulted: " + t.Exception?.GetBaseException().Message),
                        TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                StingLog.Warn("License presentation could not start: " + ex.Message);
            }
        }

        private static async Task PresentAsync(string revitVersion)
        {
            try
            {
                if (!IsEnabled()) return;

                string licenseText = ReadLicenseText();
                if (string.IsNullOrWhiteSpace(licenseText)) return;

                string hash = Sha256(licenseText);
                if (!ShouldPresent(hash)) return;

                var body = new
                {
                    license = licenseText,
                    pluginVersion = PluginVersion(),
                    revitVersion = revitVersion ?? ""
                };

                using (var content = new StringContent(
                    JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"))
                using (var response = await Http.PostAsync(Endpoint(), content).ConfigureAwait(false))
                {
                    string payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        // 404 is the interesting one: a licence we are running
                        // that planscape.build has no record of. Worth a log
                        // line, worth nothing else — the licence stays valid.
                        StingLog.Info("License presentation declined (" + (int)response.StatusCode +
                                      "): " + Summarise(payload));
                        // Still stamp, so a server that is down or a licence we
                        // hand-issued does not mean a request every single launch.
                        StampPresented(hash);
                        return;
                    }

                    StampPresented(hash);
                    StingLog.Info("License presented: " + Summarise(payload));
                }
            }
            catch (Exception ex)
            {
                // Offline, DNS failure, proxy, TLS interception, timeout — all
                // completely normal and none of them are the user's problem.
                StingLog.Info("License presentation skipped: " + ex.Message);
            }
        }

        private static bool IsEnabled()
        {
            try
            {
                string flag = Environment.GetEnvironmentVariable("STING_LICENSE_PRESENT");
                if (!string.IsNullOrWhiteSpace(flag) &&
                    (flag.Trim() == "0" ||
                     flag.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }
            catch { /* environment unreadable => fall through to the default */ }

            if (StingOfflineConfig.IsOffline)
            {
                StingLog.Info("License presentation skipped: project is offline-only.");
                return false;
            }
            return true;
        }

        private static string Endpoint()
        {
            try
            {
                string url = Environment.GetEnvironmentVariable("STING_LICENSE_PRESENT_URL");
                if (!string.IsNullOrWhiteSpace(url)) return url.Trim();
            }
            catch { /* fall through */ }
            return DefaultEndpoint;
        }

        private static string ReadLicenseText()
        {
            try
            {
                return File.Exists(LicenseGate.LicensePath)
                    ? File.ReadAllText(LicenseGate.LicensePath).Trim()
                    : null;
            }
            catch (Exception ex)
            {
                StingLog.Info("License presentation: licence unreadable — " + ex.Message);
                return null;
            }
        }

        // Throttled on the licence CONTENT, not just the clock: a fresh
        // activation or a renewal should be visible immediately rather than up
        // to a day later, which is exactly when someone is watching for it.
        private static bool ShouldPresent(string licenseHash)
        {
            try
            {
                if (!File.Exists(StatePath)) return true;

                var state = JObject.Parse(File.ReadAllText(StatePath));

                if (state.Value<bool?>("optOut") == true) return false;
                if (!string.Equals(state.Value<string>("licenseHash"), licenseHash, StringComparison.Ordinal))
                    return true;

                var last = state.Value<DateTime?>("lastPresentedUtc");
                if (last == null) return true;

                // A clock that has moved backwards (VM restore, manual change)
                // would otherwise suppress presentation for up to a day.
                var age = DateTime.UtcNow - last.Value.ToUniversalTime();
                return age >= MinInterval || age < TimeSpan.Zero;
            }
            catch
            {
                // Corrupt or unreadable state should not silence reporting.
                return true;
            }
        }

        private static void StampPresented(string licenseHash)
        {
            try
            {
                Directory.CreateDirectory(StateDir);
                var state = new JObject
                {
                    ["lastPresentedUtc"] = DateTime.UtcNow,
                    ["licenseHash"] = licenseHash,
                    ["optOut"] = false
                };
                File.WriteAllText(StatePath, state.ToString());
            }
            catch (Exception ex)
            {
                // Worst case we present again next launch. Harmless.
                StingLog.Info("License presentation state not saved: " + ex.Message);
            }
        }

        private static string PluginVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
                {
                    // Strip any "+<commit sha>" build metadata.
                    int plus = info.InformationalVersion.IndexOf('+');
                    return plus > 0
                        ? info.InformationalVersion.Substring(0, plus)
                        : info.InformationalVersion;
                }
                return asm.GetName().Version?.ToString() ?? "";
            }
            catch { return ""; }
        }

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // The response is information for the log, never a decision. Keep it to
        // one short line so a daily heartbeat cannot bloat StingTools.log.
        private static string Summarise(string payload)
        {
            try
            {
                var o = JObject.Parse(payload ?? "");
                string error = o.Value<string>("error");
                if (!string.IsNullOrEmpty(error)) return error;

                return string.Format(
                    "licensee={0} expires={1} inUse={2}/{3}{4}{5}",
                    o.Value<string>("licensee"),
                    o.Value<string>("expiresAt"),
                    o.Value<int?>("licencesInUse"),
                    o.Value<int?>("licencesIncluded")?.ToString() ?? "unlimited",
                    o.Value<bool?>("revoked") == true ? " REVOKED" : "",
                    o.Value<bool?>("matchesRecord") == false ? " DIVERGED" : "");
            }
            catch
            {
                return "(unparseable response)";
            }
        }
    }
}
