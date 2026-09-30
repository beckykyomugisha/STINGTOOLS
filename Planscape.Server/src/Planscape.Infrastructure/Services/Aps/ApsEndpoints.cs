using Microsoft.Extensions.Configuration;

namespace Planscape.Infrastructure.Services.Aps;

/// <summary>
/// One place for the Autodesk Platform Services host. Every APS call in the
/// server (OAuth, Data Management hubs/projects, ACC Issues, webhooks) builds
/// its URL from <see cref="BaseUrl"/> so a proxy, a regional gateway or a test
/// stub is one setting (<c>Aps:BaseUrl</c>, env <c>Aps__BaseUrl</c>) rather
/// than a hunt for hard-coded hosts.
/// </summary>
public static class ApsEndpoints
{
    public const string DefaultBaseUrl = "https://developer.api.autodesk.com";

    public static string BaseUrl(IConfiguration? config)
    {
        var v = config?["Aps:BaseUrl"];
        return string.IsNullOrWhiteSpace(v) ? DefaultBaseUrl : v.TrimEnd('/');
    }

    public static string TokenUrl(IConfiguration? c)     => $"{BaseUrl(c)}/authentication/v2/token";
    public static string AuthorizeUrl(IConfiguration? c) => $"{BaseUrl(c)}/authentication/v2/authorize";
    public static string HubsUrl(IConfiguration? c)      => $"{BaseUrl(c)}/project/v1/hubs";

    /// <summary>
    /// ACC Issues v1 is addressed per project:
    /// <c>/construction/issues/v1/projects/{projectId}</c> (confirmed against the
    /// APS "Forma Issues API" base-URI note, 2026-09-30). The previous
    /// <c>/containers/{id}</c> form belongs to the retired BIM 360 Issues API.
    /// </summary>
    public static string IssuesProjectUrl(IConfiguration? c, string accProjectId)
        => $"{BaseUrl(c)}/construction/issues/v1/projects/{Uri.EscapeDataString(StripHubPrefix(accProjectId))}";

    /// <summary>
    /// Data Management returns project ids as <c>b.&lt;guid&gt;</c>; the ACC
    /// (construction/*) APIs take the bare guid. Accept either.
    /// </summary>
    public static string StripHubPrefix(string id)
        => id.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? id.Substring(2) : id;
}
