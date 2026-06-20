#nullable enable
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager;

/// <summary>
/// Machine-level default-server resolution for the Planscape client.
///
/// Until now the only fallback when no per-document
/// <c>planscape_connection.json</c> was present was a hard-coded
/// <c>http://localhost:5000</c> const baked into the assembly. That meant
/// every install pointed at a developer's docker stack until the user
/// re-typed the cloud URL on every model.
///
/// This partial resolves the default server URL in priority order:
///   1. Environment variable <c>STING_PLANSCAPE_URL</c> (per-machine / per-user
///      override, ideal for managed / Citrix deployments).
///   2. Machine settings file
///      <c>%APPDATA%\StingTools\planscape_server.json</c> (key <c>serverUrl</c>),
///      written by <see cref="SaveDefaultServerUrl"/> the first time a user
///      connects so the cloud URL sticks across documents and Revit restarts.
///   3. The baked corporate default <see cref="BakedDefaultServerUrl"/>.
///
/// It also derives the human-facing web-app URL from the API base so the
/// "Open Planscape" buttons land on the coordinator SPA
/// (<c>app.planscape.build</c>) rather than the API host
/// (<c>api.planscape.build</c>), while keeping the same-origin
/// <c>&lt;base&gt;/app/</c> convention for localhost / self-hosted stacks.
/// </summary>
public sealed partial class PlanscapeServerClient
{
    /// <summary>
    /// Corporate default API base. Used only when neither the
    /// <c>STING_PLANSCAPE_URL</c> env var nor the machine settings file
    /// supplies a URL. Points at the production Planscape API.
    /// </summary>
    public const string BakedDefaultServerUrl = "https://api.planscape.build";

    /// <summary>Env var name a deployment can set to override the default
    /// server URL without editing any file.</summary>
    public const string ServerUrlEnvVar = "STING_PLANSCAPE_URL";

    private static string? _cachedDefaultUrl;
    private static readonly object _defaultUrlLock = new();

    /// <summary>Path to the machine-level server settings file
    /// (<c>%APPDATA%\StingTools\planscape_server.json</c>).</summary>
    public static string MachineSettingsPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StingTools");
            return Path.Combine(dir, "planscape_server.json");
        }
    }

    /// <summary>
    /// Resolve the default Planscape API base URL: env var → machine settings
    /// file → baked corporate default. Cached after first resolution; call
    /// <see cref="SaveDefaultServerUrl"/> (which refreshes the cache) to change it.
    /// </summary>
    public static string ResolveDefaultServerUrl()
    {
        if (_cachedDefaultUrl != null) return _cachedDefaultUrl;
        lock (_defaultUrlLock)
        {
            if (_cachedDefaultUrl != null) return _cachedDefaultUrl;

            // 1. Environment override.
            try
            {
                var env = Environment.GetEnvironmentVariable(ServerUrlEnvVar);
                if (!string.IsNullOrWhiteSpace(env))
                    return _cachedDefaultUrl = NormalizeServerUrl(env);
            }
            catch (Exception ex) { StingLog.Warn($"ResolveDefaultServerUrl(env): {ex.Message}"); }

            // 2. Machine settings file.
            try
            {
                string path = MachineSettingsPath;
                if (File.Exists(path))
                {
                    var o = JObject.Parse(File.ReadAllText(path));
                    var url = o["serverUrl"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(url))
                        return _cachedDefaultUrl = NormalizeServerUrl(url);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveDefaultServerUrl(file): {ex.Message}"); }

            // 3. Baked corporate default.
            return _cachedDefaultUrl = BakedDefaultServerUrl;
        }
    }

    /// <summary>
    /// Persist the default server URL to the machine settings file so it sticks
    /// across documents and Revit restarts. Called after a successful connect so
    /// the user only types the cloud URL once. No-ops on a blank/whitespace URL.
    /// </summary>
    public static void SaveDefaultServerUrl(string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) return;
        try
        {
            string normalized = NormalizeServerUrl(serverUrl);
            string path = MachineSettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var o = File.Exists(path)
                ? JObject.Parse(File.ReadAllText(path))
                : new JObject();
            o["serverUrl"] = normalized;
            o["updatedUtc"] = DateTime.UtcNow.ToString("o");
            File.WriteAllText(path, o.ToString(Newtonsoft.Json.Formatting.Indented));
            lock (_defaultUrlLock) { _cachedDefaultUrl = normalized; }
            StingLog.Info($"Planscape: default server URL saved to machine settings ({normalized}).");
        }
        catch (Exception ex) { StingLog.Warn($"SaveDefaultServerUrl: {ex.Message}"); }
    }

    /// <summary>Drop the cached default so the next resolve re-reads env + file.</summary>
    public static void InvalidateDefaultServerUrlCache()
    {
        lock (_defaultUrlLock) { _cachedDefaultUrl = null; }
    }

    /// <summary>
    /// Format the coordinator-SPA URL for a given API base. For a cloud host
    /// whose subdomain is <c>api.&lt;domain&gt;</c> the SPA lives at the sibling
    /// <c>app.&lt;domain&gt;</c> root (e.g. <c>https://api.planscape.build</c> →
    /// <c>https://app.planscape.build/</c>). For localhost / self-hosted stacks
    /// the SPA is served same-origin under <c>&lt;base&gt;/app/</c>. An optional
    /// hash (e.g. <c>#models?project={id}</c>) is appended either way.
    /// </summary>
    internal static string FormatWebAppUrl(string? baseUrl, string? hash)
    {
        string resolved = NormalizeServerUrl(baseUrl);
        string url;
        if (Uri.TryCreate(resolved, UriKind.Absolute, out var u))
        {
            string host = u.Host;
            bool isLocal = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                           || host == "127.0.0.1"
                           || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
            if (!isLocal && host.StartsWith("api.", StringComparison.OrdinalIgnoreCase))
            {
                var b = new UriBuilder(u) { Host = "app." + host.Substring(4), Path = "/" };
                if ((u.Scheme == Uri.UriSchemeHttps && u.Port == 443) ||
                    (u.Scheme == Uri.UriSchemeHttp  && u.Port == 80))
                    b.Port = -1;
                url = b.Uri.GetLeftPart(UriPartial.Authority) + "/";
            }
            else
            {
                url = resolved.TrimEnd('/') + "/app/";
            }
        }
        else
        {
            url = resolved.TrimEnd('/') + "/app/";
        }

        if (!string.IsNullOrWhiteSpace(hash))
            url += hash!.StartsWith("#") ? hash : "#" + hash;
        return url;
    }
}
