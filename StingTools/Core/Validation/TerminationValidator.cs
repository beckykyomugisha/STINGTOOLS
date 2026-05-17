// StingTools v4 MVP — TerminationValidator.
//
// Asserts every open connector on a pipe, duct, conduit or cable
// tray is either connected to another element OR explicitly capped
// (the host element carries ASS_TERM_CAPPED_BOOL = 1 or
// ASS_TERM_REASON_TXT = "CAPPED" / "REDUCER" / "BLANK").
//
// This is a stricter sibling of ConnectivityValidator: connectivity
// reports any open connector; termination reports only those that
// have not been explicitly acknowledged.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Validation
{
    public class TerminationValidator
    {
        public string Name => "TerminationValidator";
        private const string ValidatorTag = "TerminationValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;
            try
            {
                var cats = new[]
                {
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_Conduit,
                    BuiltInCategory.OST_CableTray,
                };
                var filter = new ElementMulticategoryFilter(cats);
                var col = new FilteredElementCollector(doc).WherePasses(filter)
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    try { CheckTermination(el, results); }
                    catch (Exception ex) { StingLog.Warn($"TerminationValidator: {el?.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"TerminationValidator: scan failed: {ex.Message}"); }
            return results;
        }

        private void CheckTermination(Element el, List<ValidationResult> results)
        {
            if (!(el is MEPCurve mep)) return;
            var set = mep.ConnectorManager?.Connectors;
            if (set == null) return;
            int open = 0;
            foreach (Connector c in set)
            {
                if (c == null) continue;
                bool isConnected = false;
                try { isConnected = c.IsConnected; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (!isConnected) open++;
            }
            if (open == 0) return;

            if (IsExplicitlyCapped(el)) return;

            // §5.3 — honour family-declared termination type. When
            // TERM_TYPE_TXT declares Cap / Elbow90 / Transition / Blank we
            // treat the element as explicitly terminated.
            var routing = Routing.RoutingParamReader.Read(el);
            string term = (routing.TerminationType ?? "").ToUpperInvariant();
            if (term == "CAP" || term == "CAPPED" || term == "ELBOW90" ||
                term == "TRANSITION" || term == "BLANK") return;

            // §5.3 — family-declared connector count sanity check. When
            // the family says "2 connectors" but the element reports more
            // open ones, that's a flag regardless of the cap state.
            string extra = "";
            if (routing.ConnectorCount > 0 && open > routing.ConnectorCount)
                extra = $" (family CONN_COUNT_INT = {routing.ConnectorCount}, observed {open})";

            results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                "TERM.OPEN.UNCAPPED",
                $"{open} open connector(s) without an explicit cap / blank / reducer{extra}",
                ValidatorTag));
        }

        private static bool IsExplicitlyCapped(Element el)
        {
            try
            {
                var pBool = el.LookupParameter("ASS_TERM_CAPPED_BOOL");
                if (pBool != null && pBool.StorageType == StorageType.Integer && pBool.AsInteger() == 1) return true;

                var pReason = el.LookupParameter("ASS_TERM_REASON_TXT");
                if (pReason != null && pReason.StorageType == StorageType.String)
                {
                    string r = (pReason.AsString() ?? "").ToUpperInvariant();
                    if (r == "CAPPED" || r == "REDUCER" || r == "BLANK") return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }
    }
}
