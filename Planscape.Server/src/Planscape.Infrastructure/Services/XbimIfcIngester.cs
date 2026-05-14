using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// T4-27 — Default IFC ingester. Uses Xbim.Essentials (IfcStore +
/// IModel) which transparently handles IFC2x3 and IFC4 schema
/// dispatch. Property-only first pass: walks every IIfcElement,
/// resolves linked IfcPropertySets via IfcRelDefinesByProperties,
/// and emits a flattened (key → string) bag.
///
/// Memory: IfcStore.Open uses an Esent-backed file on disk for
/// large models, so a 1 GB IFC doesn't blow the heap. Esent is
/// Windows-only by default; on Linux we fall back to the in-memory
/// store. Both are read-only here.
///
/// Threading: not thread-safe per instance. Hangfire creates one
/// scope per job invocation; that scope's DI graph gets a fresh
/// ingester so concurrent ingest is safe across jobs.
/// </summary>
public class XbimIfcIngester : IIfcIngester
{
    private readonly ILogger<XbimIfcIngester> _logger;

    public XbimIfcIngester(ILogger<XbimIfcIngester> logger)
    {
        _logger = logger;
    }

    public async Task<IfcIngestResult> IngestAsync(string ifcPath, CancellationToken ct)
    {
        if (!File.Exists(ifcPath))
            throw new FileNotFoundException("IFC file not found", ifcPath);

        var sw = Stopwatch.StartNew();
        var elements = new List<IfcElementProperties>();
        var countsByType = new Dictionary<string, int>(StringComparer.Ordinal);
        var warnings = new List<string>();

        // Open with default settings — xbim picks Esent on Windows,
        // in-memory on Linux. For property-only ingest we don't need
        // geometry, so we skip the (slow) geometric model load.
        // IfcStore implements IDisposable but not IAsyncDisposable, so use
        // a plain using statement (no await) even though IngestAsync is async.
        using var model = IfcStore.Open(ifcPath, null, null, null, Xbim.IO.XbimDBAccess.Read);
        ct.ThrowIfCancellationRequested();

        var schemaVersion = model.SchemaVersion switch
        {
            XbimSchemaVersion.Ifc2X3 => "IFC2X3",
            XbimSchemaVersion.Ifc4   => "IFC4",
            XbimSchemaVersion.Ifc4x3 => "IFC4X3",
            _                        => model.SchemaVersion.ToString(),
        };

        // Pre-build a cached map of (element id → property sets) so we
        // don't repeatedly traverse the inverse relationship on every
        // element. xbim's inverse traversal is correct but O(N) per
        // probe; the prebuild collapses to O(N) total.
        var propsByElement = BuildPropertyIndex(model);

        foreach (var element in model.Instances.OfType<IIfcElement>())
        {
            ct.ThrowIfCancellationRequested();

            var ifcType = element.GetType().Name;            // e.g. "IfcWallStandardCase"
            countsByType[ifcType] = countsByType.GetValueOrDefault(ifcType) + 1;

            var bag = new Dictionary<string, string>(StringComparer.Ordinal);

            // Direct attributes — always available.
            // Use ToString() / null-coalescing rather than 'is string' or 'as string'
            // patterns: xbim value types (IfcIdentifier, IfcLabel) are structs, so
            // the 'as' operator would always return null and '?.' is disallowed on them.
            var gid = element.GlobalId.ToString();
            if (!string.IsNullOrEmpty(gid)) bag["IfcGlobalId"] = gid;
            var elName = element.Name?.ToString();
            if (!string.IsNullOrEmpty(elName)) bag["IfcName"] = elName;
            var elDesc = element.Description?.ToString();
            if (!string.IsNullOrEmpty(elDesc)) bag["IfcDescription"] = elDesc;
            var elTag = element.Tag?.ToString();
            if (!string.IsNullOrEmpty(elTag)) bag["IfcTag"] = elTag;

            // Property sets via the prebuilt index.
            if (propsByElement.TryGetValue(element.EntityLabel, out var pSets))
            {
                foreach (var pset in pSets)
                {
                    var psetName = pset.Name?.ToString() ?? "Pset";
                    foreach (var prop in pset.HasProperties.OfType<IIfcPropertySingleValue>())
                    {
                        var pname = prop.Name.ToString();
                        if (string.IsNullOrEmpty(pname)) continue;
                        var pvalue = prop.NominalValue?.Value?.ToString();
                        if (pvalue == null) continue;
                        bag[$"{psetName}.{pname}"] = pvalue;
                    }
                }
            }

            string? predefined = TryReadPredefinedType(element);

            elements.Add(new IfcElementProperties(
                GlobalId: element.GlobalId.ToString() ?? "",
                IfcType: ifcType,
                Name: element.Name?.ToString(),
                PredefinedType: predefined,
                Properties: bag));
        }

        sw.Stop();
        _logger.LogInformation(
            "XbimIfcIngester: {Path} → {Schema} {Count} elements in {Ms}ms",
            ifcPath, schemaVersion, elements.Count, sw.ElapsedMilliseconds);

        return new IfcIngestResult(
            SchemaVersion: schemaVersion,
            ElementCount: elements.Count,
            CountsByType: countsByType,
            Elements: elements,
            Duration: sw.Elapsed,
            Warnings: warnings.Count > 0 ? string.Join("; ", warnings) : null);
    }

    /// <summary>
    /// One pass over IfcRelDefinesByProperties to build a
    /// (elementEntityLabel → list&lt;IfcPropertySet&gt;) lookup. Avoids
    /// repeated inverse-relationship traversal in the main loop.
    /// </summary>
    private static Dictionary<int, List<IIfcPropertySet>> BuildPropertyIndex(IModel model)
    {
        var index = new Dictionary<int, List<IIfcPropertySet>>();
        foreach (var rel in model.Instances.OfType<IIfcRelDefinesByProperties>())
        {
            var def = rel.RelatingPropertyDefinition as IIfcPropertySet;
            if (def == null) continue;
            foreach (var obj in rel.RelatedObjects)
            {
                if (!index.TryGetValue(obj.EntityLabel, out var list))
                    index[obj.EntityLabel] = list = new List<IIfcPropertySet>();
                list.Add(def);
            }
        }
        return index;
    }

    /// <summary>
    /// PredefinedType lives at different positions on different IFC
    /// types (e.g. IfcWall.PredefinedType vs IfcDoor.PredefinedType
    /// vs IfcCovering.PredefinedType). Reflect once + cache.
    /// </summary>
    private static string? TryReadPredefinedType(IIfcElement element)
    {
        var prop = element.GetType().GetProperty("PredefinedType");
        if (prop == null) return null;
        var v = prop.GetValue(element);
        return v?.ToString();
    }
}
