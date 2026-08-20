#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager;

/// <summary>
/// Named Planscape server targets, and the rules for choosing between them (#563).
///
/// <para><b>The problem this exists to solve is invisibility, not friction.</b> Switching
/// the BCC between production and a local docker stack meant hand-editing
/// <c>%APPDATA%\StingTools\planscape_server.json</c> or launching Revit from a shell
/// script. That is awkward, but the awkwardness is not what cost an evening — the BCC
/// never said <i>which</i> server it was talking to, so a panel showing production data
/// and a panel showing dev data were indistinguishable. A site-photo failure could not be
/// reproduced because the saved pointer was production, and stopping the local stack
/// changed nothing observable. The failure looked like the tests were wrong.</para>
///
/// <para><b>Resolution order is unchanged</b> and this type does not alter it:
/// <c>STING_PLANSCAPE_URL</c> → machine settings file → baked default. See
/// <see cref="PlanscapeServerClient.ResolveDefaultServerUrl"/>. What this adds is a list
/// of named targets to pick from, and an honest description of which one is actually in
/// effect and why.</para>
///
/// <para><b>Only a deliberate choice is ever persisted.</b> <see cref="SetActiveTarget"/>
/// is the single write path, and it is called from exactly one place: the user confirming
/// a switch in the picker. Connecting no longer writes anything — see the note on
/// <see cref="PlanscapeServerClient.SaveDefaultServerUrl"/>. That matters because the file
/// holds the owner's production pointer and is the fallback that makes the launcher script
/// safe; a stray write to it is how someone ends up pointed at a dev database full of
/// real-looking data without ever having chosen to be.</para>
///
/// <para><b>A switch needs a Revit restart.</b> <c>ResolveDefaultServerUrl</c> caches into
/// <c>_cachedDefaultUrl</c> for the process lifetime, and live objects
/// (<c>PlanscapeServerClient.Instance</c>, its SignalR hub, any open session) are already
/// bound to the old base. Callers MUST say so. A control that appears to switch and
/// silently does not is worse than no control at all.</para>
/// </summary>
public static class PlanscapeServerTargets
{
    /// <summary>A named server the user can switch to.</summary>
    public sealed class ServerTarget
    {
        public string Label { get; set; } = "";
        public string Url { get; set; } = "";

        /// <summary>Built-ins ship with the plugin and cannot be deleted.</summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>
        /// True for anything that is not the corporate production API. Drives the
        /// deliberately loud styling in the BCC — a dev session must never be mistakable
        /// for a real one at a glance.
        /// </summary>
        public bool IsNonProduction =>
            !string.Equals(
                PlanscapeServerClient.NormalizeServerUrl(Url),
                PlanscapeServerClient.NormalizeServerUrl(PlanscapeServerClient.BakedDefaultServerUrl),
                StringComparison.OrdinalIgnoreCase);

        public override string ToString() => $"{Label} — {Url}";
    }

    /// <summary>Where the URL currently in effect came from. Reported to the user
    /// verbatim, because "which server am I on" is only half the question — the other
    /// half is "and why, so I know what to change".</summary>
    public enum ActiveSource
    {
        /// <summary><c>STING_PLANSCAPE_URL</c> is set and wins over everything.</summary>
        EnvironmentVariable,
        /// <summary>A previously-chosen target in the machine settings file.</summary>
        SavedSetting,
        /// <summary>Neither of the above — the corporate default baked into the assembly.</summary>
        BakedDefault,
    }

    public sealed class ActiveTargetInfo
    {
        public string Url { get; set; } = "";
        public ActiveSource Source { get; set; }
        public string Label { get; set; } = "";
        public bool IsNonProduction { get; set; }

        /// <summary>True when <c>STING_PLANSCAPE_URL</c> is overriding the saved value.
        /// The picker uses this to say the saved value is NOT in effect rather than
        /// display a value that is not being used.</summary>
        public bool EnvOverrideActive => Source == ActiveSource.EnvironmentVariable;
    }

    /// <summary>Ships with the plugin. Production first so it is the default choice.</summary>
    private static IEnumerable<ServerTarget> BuiltIns()
    {
        yield return new ServerTarget
        {
            Label = "Production",
            Url = PlanscapeServerClient.BakedDefaultServerUrl,
            IsBuiltIn = true,
        };
        yield return new ServerTarget
        {
            // Offered so the custom domain can be selected the moment it is attached
            // (#705). It does NOT resolve today, and ProbeAsync refuses a target it
            // cannot reach — which is the point: choosing it before DNS exists fails
            // at the picker, where the reason is visible, rather than later as a
            // connect error with no explanation.
            Label = "Production (custom domain — pending DNS)",
            Url = PlanscapeServerClient.IntendedProductionServerUrl,
            IsBuiltIn = true,
        };
        yield return new ServerTarget
        {
            // Matches the docker-compose port mapping (5000:8080) in
            // Planscape.Server/docker/docker-compose.yml.
            Label = "Local (docker)",
            Url = "http://localhost:5000",
            IsBuiltIn = true,
        };
    }

    /// <summary>
    /// The built-in targets plus any the user has added, de-duplicated by normalised URL
    /// with built-ins winning so a user entry cannot shadow Production under a different
    /// name.
    /// </summary>
    public static List<ServerTarget> LoadTargets()
    {
        var list = BuiltIns().ToList();
        var seen = new HashSet<string>(
            list.Select(t => PlanscapeServerClient.NormalizeServerUrl(t.Url)),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            string path = PlanscapeServerClient.MachineSettingsPath;
            if (File.Exists(path))
            {
                var o = JObject.Parse(File.ReadAllText(path));
                if (o["targets"] is JArray arr)
                {
                    foreach (var entry in arr.OfType<JObject>())
                    {
                        var url = entry["url"]?.Value<string>();
                        if (string.IsNullOrWhiteSpace(url)) continue;
                        string norm = PlanscapeServerClient.NormalizeServerUrl(url!);
                        if (!seen.Add(norm)) continue;
                        list.Add(new ServerTarget
                        {
                            Label = entry["label"]?.Value<string>() ?? norm,
                            Url = norm,
                            IsBuiltIn = false,
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // A malformed settings file must not cost the user the built-in targets —
            // those are the ones that get them back to production. Warn and carry on
            // with the built-ins rather than returning nothing.
            StingLog.Warn($"PlanscapeServerTargets.LoadTargets: {ex.Message}");
        }

        return list;
    }

    /// <summary>
    /// What is actually in effect right now, and why. Reads the environment and the file
    /// directly rather than through <c>ResolveDefaultServerUrl</c>'s cache, so the answer
    /// reflects the CURRENT state of both — which is what makes it useful for telling a
    /// user their pending switch has not taken effect yet.
    /// </summary>
    public static ActiveTargetInfo GetActiveTarget()
    {
        string? url = null;
        var source = ActiveSource.BakedDefault;

        try
        {
            var env = Environment.GetEnvironmentVariable(PlanscapeServerClient.ServerUrlEnvVar);
            if (!string.IsNullOrWhiteSpace(env))
            {
                url = PlanscapeServerClient.NormalizeServerUrl(env!);
                source = ActiveSource.EnvironmentVariable;
            }
        }
        catch (Exception ex) { StingLog.Warn($"GetActiveTarget(env): {ex.Message}"); }

        if (url == null)
        {
            try
            {
                string path = PlanscapeServerClient.MachineSettingsPath;
                if (File.Exists(path))
                {
                    var saved = JObject.Parse(File.ReadAllText(path))["serverUrl"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(saved))
                    {
                        url = PlanscapeServerClient.NormalizeServerUrl(saved!);
                        source = ActiveSource.SavedSetting;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetActiveTarget(file): {ex.Message}"); }
        }

        if (url == null) url = PlanscapeServerClient.BakedDefaultServerUrl;

        var match = LoadTargets().FirstOrDefault(t =>
            string.Equals(PlanscapeServerClient.NormalizeServerUrl(t.Url), url,
                          StringComparison.OrdinalIgnoreCase));

        return new ActiveTargetInfo
        {
            Url = url,
            Source = source,
            // An unnamed URL is shown as itself. Never invent a friendly label for a
            // server we do not recognise — the whole point is that the user can tell
            // exactly what they are pointed at.
            Label = match?.Label ?? url,
            IsNonProduction = !string.Equals(
                url,
                PlanscapeServerClient.NormalizeServerUrl(PlanscapeServerClient.BakedDefaultServerUrl),
                StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Persist a deliberately-chosen target. <b>The only write path.</b> Call this from a
    /// confirmed user action and from nowhere else — never from connect, never from a
    /// refresh, never as a side effect of anything.
    /// </summary>
    /// <returns>true when the value was written.</returns>
    public static bool SetActiveTarget(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        PlanscapeServerClient.SaveDefaultServerUrl(url);
        StingLog.Info($"Planscape: active server target set to {PlanscapeServerClient.NormalizeServerUrl(url)} " +
                      "by explicit user choice (restart Revit for it to take effect).");
        return true;
    }

    /// <summary>Add a user-defined target to the machine settings file. Built-ins are
    /// never written; a URL that already exists is a no-op rather than a duplicate.</summary>
    public static bool AddTarget(string label, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        string norm = PlanscapeServerClient.NormalizeServerUrl(url);
        if (LoadTargets().Any(t => string.Equals(
                PlanscapeServerClient.NormalizeServerUrl(t.Url), norm, StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            string path = PlanscapeServerClient.MachineSettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var o = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
            if (o["targets"] is not JArray arr) { arr = new JArray(); o["targets"] = arr; }
            arr.Add(new JObject
            {
                ["label"] = string.IsNullOrWhiteSpace(label) ? norm : label.Trim(),
                ["url"] = norm,
            });
            o["updatedUtc"] = DateTime.UtcNow.ToString("o");
            File.WriteAllText(path, o.ToString(Newtonsoft.Json.Formatting.Indented));
            StingLog.Info($"Planscape: server target added — {label} ({norm}).");
            return true;
        }
        catch (Exception ex)
        {
            StingLog.Warn($"PlanscapeServerTargets.AddTarget: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Is a candidate server actually there? Probes the anonymous liveness endpoint so a
    /// dead URL is refused at the point of CHOOSING rather than surfacing later as a
    /// confusing in-app error after a Revit restart.
    ///
    /// <para><c>/health/live</c>, not <c>/health</c> — the latter is the authenticated
    /// full diagnostic and answers 403 to an anonymous caller, which would read as "server
    /// is down" for a perfectly healthy production API.</para>
    ///
    /// <para>Returns the failure reason rather than a bare false: "could not reach it" and
    /// "reached it and it said no" are different problems and the user needs to know
    /// which.</para>
    /// </summary>
    public static async System.Threading.Tasks.Task<(bool Ok, string Detail)> ProbeAsync(string url)
    {
        string norm = PlanscapeServerClient.NormalizeServerUrl(url);
        try
        {
            using var http = new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromSeconds(6),
            };
            var resp = await http.GetAsync(norm.TrimEnd('/') + "/health/live").ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return (true, $"Reachable (HTTP {(int)resp.StatusCode}).");
            return (false, $"Reached {norm} but it answered HTTP {(int)resp.StatusCode} on /health/live.");
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            return (false, $"No response from {norm} within 6s.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach {norm} — {ex.Message}");
        }
    }
}
