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
///
/// ArchiCAD quantity set support (T4-27c): reads IIfcElementQuantity
/// entities via IfcRelDefinesByProperties in addition to
/// IIfcPropertySet. Quantities are emitted into the property bag
/// under the key "{qsetName}.{quantityName}" so mapping rules with
/// quantity_type:true can resolve them with the same lookup path as
/// regular properties.
///
/// Source tagging: if the model contains any AC_Pset_RenovationInfo
/// or AC_Pset_ElementID property set (ArchiCAD-specific), the
/// IngestResult.Source is set to "archicad"; otherwise "ifc".
/// </summary>
public class XbimIfcIngester : IIfcIngester
{
    private readonly ILogger<XbimIfcIngester> _logger;

    public XbimIfcIngester(ILogger<XbimIfcIngester> logger)
    {
        _logger = logger;
    }

    public Task<IfcIngestResult> IngestAsync(string ifcPath, CancellationToken ct)
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
        using var model = IfcStore.Open(ifcPath, null, null, null, Xbim.IO.XbimDBAccess.Read);
        ct.ThrowIfCancellationRequested();

        var schemaVersion = model.SchemaVersion switch
        {
            XbimSchemaVersion.Ifc2X3 => "IFC2X3",
            XbimSchemaVersion.Ifc4   => "IFC4",
            XbimSchemaVersion.Ifc4x3 => "IFC4X3",
            _                        => model.SchemaVersion.ToString(),
        };

        // Pre-build cached maps so we don't repeatedly traverse inverse
        // relationships on every element. Both build in O(N) total.
        var propsByElement  = BuildPropertyIndex(model);
        var qtysByElement   = BuildQuantityIndex(model);
        var spatialAncestors = BuildSpatialAncestorIndex(model);
        var openingAreaByHost = BuildOpeningAreaIndex(model, qtysByElement);

        // Detect ArchiCAD-specific and Tekla-specific pset names to set Source field.
        bool hasAcPsets    = false;
        bool hasTeklaPsets = false;

        foreach (var element in model.Instances.OfType<IIfcElement>())
        {
            ct.ThrowIfCancellationRequested();

            var ifcType = element.GetType().Name;            // e.g. "IfcWallStandardCase"
            countsByType[ifcType] = countsByType.GetValueOrDefault(ifcType) + 1;

            var bag = new Dictionary<string, string>(StringComparer.Ordinal);

            // Direct attributes — always available.
            if (element.GlobalId.Value is string g) bag["IfcGlobalId"] = g;
            if (element.Name?.Value is string n)    bag["IfcName"]    = n;
            if (element.Description?.Value is string d) bag["IfcDescription"] = d;
            if (element.Tag?.Value is string t)     bag["IfcTag"] = t;

            // Spatial hierarchy (building / storey / space)
            if (spatialAncestors.TryGetValue(element.EntityLabel, out var ancestors))
            {
                if (ancestors.Building  != null) bag["IfcHierarchy.Building"]  = ancestors.Building;
                if (ancestors.Storey    != null) bag["IfcHierarchy.Storey"]    = ancestors.Storey;
                if (ancestors.Space     != null) bag["IfcHierarchy.Space"]     = ancestors.Space;
            }

            // Property sets via the prebuilt index.
            if (propsByElement.TryGetValue(element.EntityLabel, out var pSets))
            {
                foreach (var pset in pSets)
                {
                    var psetName = pset.Name?.Value as string ?? "Pset";

                    // Track ArchiCAD-specific psets for source detection.
                    if (!hasAcPsets &&
                        (psetName == "AC_Pset_RenovationInfo" || psetName == "AC_Pset_ElementID"))
                    {
                        hasAcPsets = true;
                    }

                    // Track Tekla-specific psets for source detection.
                    // Tekla uses exactly "Tekla " or "Tekla_" prefixed pset names.
                    if (!hasTeklaPsets &&
                        (psetName.StartsWith("Tekla ", StringComparison.Ordinal) ||
                         psetName.StartsWith("Tekla_", StringComparison.Ordinal)))
                    {
                        hasTeklaPsets = true;
                    }

                    foreach (var prop in pset.HasProperties.OfType<IIfcPropertySingleValue>())
                    {
                        var pname = prop.Name.Value?.ToString();
                        if (string.IsNullOrEmpty(pname)) continue;
                        var pvalue = prop.NominalValue?.Value?.ToString();
                        if (pvalue == null) continue;
                        bag[$"{psetName}.{pname}"] = pvalue;
                    }
                }

                // Tekla normalised keys: promote Tekla-specific bag entries to
                // canonical STING names so mapping rules work without knowing the
                // exact pset / property name combination.
                if (hasTeklaPsets)
                {
                    // Assembly mark: "Tekla Assembly.AssemblyMark" or
                    //                "Tekla Assembly.ASSEMBLY_MARK" or
                    //                "Tekla Common.NAME"
                    string? assemblyMark =
                        bag.GetValueOrDefault("Tekla Assembly.AssemblyMark")
                        ?? bag.GetValueOrDefault("Tekla Assembly.ASSEMBLY_MARK")
                        ?? bag.GetValueOrDefault("Tekla Common.NAME");
                    if (assemblyMark != null)
                        bag["TeklaAssemblyMark"] = assemblyMark;

                    // Cast unit mark: "Tekla Cast Unit.CAST_UNIT_MARK" or
                    //                 "Tekla Cast Unit.CastUnitMark"
                    string? castUnitMark =
                        bag.GetValueOrDefault("Tekla Cast Unit.CAST_UNIT_MARK")
                        ?? bag.GetValueOrDefault("Tekla Cast Unit.CastUnitMark");
                    if (castUnitMark != null)
                        bag["TeklarCastUnitMark"] = castUnitMark;

                    // Part number: "Tekla Steel Part.PART_POS" or
                    //              "Tekla Steel Part.PART_NUMBER"
                    string? teklaPart =
                        bag.GetValueOrDefault("Tekla Steel Part.PART_POS")
                        ?? bag.GetValueOrDefault("Tekla Steel Part.PART_NUMBER");
                    if (teklaPart != null)
                        bag["TeklaPart"] = teklaPart;

                    // Material: "Tekla Common.MATERIAL"
                    string? teklaMaterial = bag.GetValueOrDefault("Tekla Common.MATERIAL");
                    if (teklaMaterial != null)
                        bag["TeklarMaterial"] = teklaMaterial;

                    // Profile: "Tekla Profile.Profile" or "Tekla Common.Profile"
                    string? teklaProfile =
                        bag.GetValueOrDefault("Tekla Profile.Profile")
                        ?? bag.GetValueOrDefault("Tekla Common.Profile");
                    if (teklaProfile != null)
                        bag["TeklaProfile"] = teklaProfile;
                }
            }

            // Quantity sets via the prebuilt quantity index.
            // Quantities are stored as "{qsetName}.{quantityName}" so that
            // mapping rules with quantity_type:true resolve with the same
            // lookup path as regular pset properties.
            if (qtysByElement.TryGetValue(element.EntityLabel, out var qSets))
            {
                foreach (var qset in qSets)
                {
                    var qsetName = qset.Name?.Value as string ?? "BaseQuantities";
                    foreach (var qty in qset.Quantities.OfType<IIfcPhysicalSimpleQuantity>())
                    {
                        var qname = qty.Name.Value?.ToString();
                        if (string.IsNullOrEmpty(qname)) continue;

                        // Extract the numeric value from whichever quantity sub-type
                        // is present (area, volume, length, weight, count, time).
                        string? qvalue = qty switch
                        {
                            IIfcQuantityArea   qa => qa.AreaValue.ToString(),
                            IIfcQuantityVolume qv => qv.VolumeValue.ToString(),
                            IIfcQuantityLength ql => ql.LengthValue.ToString(),
                            IIfcQuantityWeight qw => qw.WeightValue.ToString(),
                            IIfcQuantityCount  qc => qc.CountValue.ToString(),
                            IIfcQuantityTime   qt => qt.TimeValue.ToString(),
                            _                     => null,
                        };
                        if (qvalue == null) continue;
                        bag[$"{qsetName}.{qname}"] = qvalue;
                    }
                }
            }

            // Derived quantities: net area = gross - openings; cost total = rate × qty
            ComputeDerivedQuantities(element.EntityLabel, bag, openingAreaByHost);

            string? predefined = TryReadPredefinedType(element);

            elements.Add(new IfcElementProperties(
                GlobalId: element.GlobalId.ToString() ?? "",
                IfcType: ifcType,
                Name: element.Name?.ToString(),
                PredefinedType: predefined,
                Properties: bag));
        }

        // Warn if no quantity sets were found at all — ArchiCAD users
        // need to enable "Export Quantity Sets (Qto)" in the IFC
        // translator to populate cost extraction.
        bool hasQuantities = qtysByElement.Count > 0;
        if (!hasQuantities)
        {
            warnings.Add("No quantity sets found. Re-export from ArchiCAD with 'Export Quantity Sets (Qto)' enabled for cost extraction.");
        }

        sw.Stop();
        string source = hasAcPsets ? "archicad" : hasTeklaPsets ? "tekla" : "ifc";
        _logger.LogInformation(
            "XbimIfcIngester: {Path} → {Schema} {Count} elements in {Ms}ms (quantitySets={HasQty}, source={Source})",
            ifcPath, schemaVersion, elements.Count, sw.ElapsedMilliseconds,
            hasQuantities, source);

        return Task.FromResult(new IfcIngestResult(
            SchemaVersion: schemaVersion,
            ElementCount: elements.Count,
            CountsByType: countsByType,
            Elements: elements,
            Duration: sw.Elapsed,
            Warnings: warnings.Count > 0 ? string.Join("; ", warnings) : null,
            Source: source,
            HasQuantitySets: hasQuantities));
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
    /// One pass over IfcRelDefinesByProperties to build a
    /// (elementEntityLabel → list&lt;IfcElementQuantity&gt;) lookup.
    /// IFC stores quantity sets as a subtype of IfcPropertySetDefinition
    /// so they appear in the same relationship as regular property sets
    /// but need to be cast to IIfcElementQuantity rather than IIfcPropertySet.
    /// </summary>
    private static Dictionary<int, List<IIfcElementQuantity>> BuildQuantityIndex(IModel model)
    {
        var index = new Dictionary<int, List<IIfcElementQuantity>>();
        foreach (var rel in model.Instances.OfType<IIfcRelDefinesByProperties>())
        {
            var def = rel.RelatingPropertyDefinition as IIfcElementQuantity;
            if (def == null) continue;
            foreach (var obj in rel.RelatedObjects)
            {
                if (!index.TryGetValue(obj.EntityLabel, out var list))
                    index[obj.EntityLabel] = list = new List<IIfcElementQuantity>();
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

    // ── Spatial ancestor record ───────────────────────────────────────────────

    private sealed record SpatialAncestors(string? Building, string? Storey, string? Space);

    /// <summary>
    /// One pass over IfcRelContainedInSpatialStructure + IfcRelAggregates to build
    /// a (elementEntityLabel → SpatialAncestors) map that carries building/storey/space
    /// names for every element.
    /// </summary>
    private static Dictionary<int, SpatialAncestors> BuildSpatialAncestorIndex(IModel model)
    {
        // id → parent id via IfcRelAggregates
        var parentOf = new Dictionary<int, int>();
        foreach (var rel in model.Instances.OfType<IIfcRelAggregates>())
        {
            int parentLabel = rel.RelatingObject.EntityLabel;
            foreach (var child in rel.RelatedObjects)
                parentOf[child.EntityLabel] = parentLabel;
        }

        // element id → direct spatial container (storey or space) via IfcRelContainedInSpatialStructure
        var directContainer = new Dictionary<int, int>();
        foreach (var rel in model.Instances.OfType<IIfcRelContainedInSpatialStructure>())
        {
            int containerLabel = rel.RelatingStructure.EntityLabel;
            foreach (var el in rel.RelatedElements)
                directContainer[el.EntityLabel] = containerLabel;
        }

        // Helper: walk up the aggregation chain, returning the Name of the first
        // entity that matches the predicate.
        string? WalkUp(int startLabel, Func<IPersistEntity, string?> extract)
        {
            int cur = startLabel;
            int guard = 20;
            while (guard-- > 0)
            {
                var ent = model.Instances[cur];
                if (ent == null) break;
                var name = extract(ent);
                if (name != null) return name;
                if (!parentOf.TryGetValue(cur, out int parent)) break;
                cur = parent;
            }
            return null;
        }

        var result = new Dictionary<int, SpatialAncestors>();
        foreach (var rel in model.Instances.OfType<IIfcRelContainedInSpatialStructure>())
        {
            int containerLabel = rel.RelatingStructure.EntityLabel;
            string? storey   = WalkUp(containerLabel, e => e is IIfcBuildingStorey bs ? bs.Name?.Value as string : null);
            string? building = WalkUp(containerLabel, e => e is IIfcBuilding   bld ? bld.Name?.Value as string : null);
            // space = direct container when it's an IfcSpace
            string? space = rel.RelatingStructure is IIfcSpace sp
                ? sp.Name?.Value as string : null;

            var ancestors = new SpatialAncestors(building, storey, space);
            foreach (var el in rel.RelatedElements)
                result[el.EntityLabel] = ancestors;
        }
        return result;
    }

    /// <summary>
    /// One pass to build a (hostElementLabel → total opening area m²) map for
    /// net area derivation. Uses IfcRelVoidsElement + BaseQuantities.
    /// </summary>
    private static Dictionary<int, double> BuildOpeningAreaIndex(
        IModel model,
        Dictionary<int, List<IIfcElementQuantity>> qtysByElement)
    {
        var result = new Dictionary<int, double>();

        // IfcRelVoidsElement: arg RelatingBuildingElement = host, RelatedOpeningElement = opening
        foreach (var rel in model.Instances.OfType<IIfcRelVoidsElement>())
        {
            int hostLabel    = rel.RelatingBuildingElement.EntityLabel;
            int openingLabel = rel.RelatedOpeningElement.EntityLabel;

            if (!qtysByElement.TryGetValue(openingLabel, out var qsets)) continue;
            foreach (var qset in qsets)
            {
                foreach (var qty in qset.Quantities.OfType<IIfcQuantityArea>())
                {
                    double area = (double)qty.AreaValue;
                    result[hostLabel] = result.GetValueOrDefault(hostLabel) + area;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Derives net area / volume / cost total from already-populated bag and
    /// the opening-area index, writing DerivedQty.* keys.
    /// </summary>
    private static void ComputeDerivedQuantities(
        int entityLabel,
        Dictionary<string, string> bag,
        Dictionary<int, double> openingAreaByHost)
    {
        static bool TryQty(Dictionary<string, string> b, string key, out double val)
        {
            val = 0;
            return b.TryGetValue(key, out string? s)
                && double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out val);
        }

        double openingArea = openingAreaByHost.GetValueOrDefault(entityLabel);

        // Net side area
        if (TryQty(bag, "Qto_WallBaseQuantities.GrossSideArea", out double grossSide)
            || TryQty(bag, "BaseQuantities.GrossSideArea", out grossSide))
        {
            double net = Math.Max(0, grossSide - openingArea);
            bag["DerivedQty.NetSideArea_m2"] = net.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Net floor area
        if (TryQty(bag, "Qto_SlabBaseQuantities.GrossArea", out double grossFloor)
            || TryQty(bag, "Qto_SpaceBaseQuantities.GrossFloorArea", out grossFloor)
            || TryQty(bag, "BaseQuantities.GrossArea", out grossFloor))
        {
            bag["DerivedQty.NetFloorArea_m2"] = grossFloor.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Net volume
        if (TryQty(bag, "Qto_WallBaseQuantities.GrossVolume", out double grossVol)
            || TryQty(bag, "Qto_SlabBaseQuantities.GrossVolume", out grossVol)
            || TryQty(bag, "BaseQuantities.GrossVolume", out grossVol))
        {
            double voidVol = 0;
            if (openingArea > 0
                && (TryQty(bag, "Qto_WallBaseQuantities.Width", out double width)
                    || TryQty(bag, "BaseQuantities.Width", out width)))
                voidVol = openingArea * width;
            bag["DerivedQty.NetVolume_m3"] = Math.Max(0, grossVol - voidVol)
                .ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Cost total
        if (bag.TryGetValue("CST_UNIT_RATE_TXT", out string? rateStr)
            && double.TryParse(rateStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double rate)
            && rate > 0)
        {
            double? qty = TryQty(bag, "DerivedQty.NetFloorArea_m2", out double fa) ? fa
                        : TryQty(bag, "DerivedQty.NetSideArea_m2",  out double sa) ? sa
                        : TryQty(bag, "DerivedQty.NetVolume_m3",    out double vo) ? vo
                        : (double?)null;
            if (qty.HasValue && qty.Value > 0)
                bag["DerivedQty.CostTotal"] = (rate * qty.Value)
                    .ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
