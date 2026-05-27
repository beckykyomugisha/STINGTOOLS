#nullable enable
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager.PlatformEvents;

/// <summary>
/// Reference handler for the K2 drainer — applies a "param.stamp" event:
/// write a text parameter onto the element identified by the event's
/// canonical IFC GlobalId. Payload: { "paramName": "...", "value": "..." }.
///
/// Demonstrates the full closed loop: resolve element from the canonical id
/// (via the Revit IFC GUID parameter — the local mirror of K1's mapping),
/// then mutate under the transaction the drainer opened. Real feature
/// handlers (clash.resolved, workorder.completed, twin.alert) follow the same
/// shape and register themselves with <see cref="PlatformEventRegistry"/>.
/// </summary>
public sealed class ParamStampEventHandler : IApplyPlatformEvent
{
    public const string Type = "param.stamp";

    public string EventType => Type;

    // Stamping a parameter changes model data, so guard it against stale base.
    public bool RevisionSensitive => true;

    public PlatformEventApplyResult Apply(Document doc, PlatformEventDto ev)
    {
        if (string.IsNullOrWhiteSpace(ev.TargetIfcGlobalId))
            return PlatformEventApplyResult.Rejected("param.stamp requires a TargetIfcGlobalId");

        JObject payload;
        try { payload = JObject.Parse(ev.PayloadJson ?? "{}"); }
        catch { return PlatformEventApplyResult.Rejected("payload is not valid JSON"); }

        var paramName = (string?)payload["paramName"];
        var value     = (string?)payload["value"] ?? "";
        if (string.IsNullOrWhiteSpace(paramName))
            return PlatformEventApplyResult.Rejected("payload.paramName is required");

        var el = FindByIfcGuid(doc, ev.TargetIfcGlobalId!);
        if (el == null)
            return PlatformEventApplyResult.Rejected($"no element with IFC GUID {ev.TargetIfcGlobalId}");

        if (!ParameterHelpers.SetString(el, paramName!, value, overwrite: true))
            return PlatformEventApplyResult.Failed($"could not write '{paramName}' on element {el.Id}");

        StingLog.Info($"param.stamp: {paramName}='{value}' on element {el.Id} (IFC {ev.TargetIfcGlobalId})");
        return PlatformEventApplyResult.Applied($"stamped {paramName} on {el.Id}");
    }

    private static Element? FindByIfcGuid(Document doc, string ifcGuid)
    {
        // The Revit IFC GUID parameter is the local mirror of the canonical id
        // K1 stores server-side. Scan model elements for the match.
        return new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .FirstOrDefault(e =>
                e.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString() == ifcGuid);
    }
}
