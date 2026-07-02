// Healthcare Pack HC-DEF-01 — radiation shielding write-back (QE-gated, audit-trailed).
//
// The RadCalc* commands compute NCRP 147 shielding but only DISPLAY the result;
// nothing was ever persisted to the model. This command stamps the computed
// design onto a user-selected barrier element (Wall / Door / Window / Generic
// Model — the categories RadShieldValidator reads and RAD_DISTANCE_M_NR binds to).
//
// The write is gated through RadiationSignoffGate so it NEVER silently certifies:
//   • RAD_SHIELD_STATUS_TXT = APPROVED only when a Qualified Expert is on record
//     (RAD_QE_NAME_TXT / project / panel), otherwise DRAFT.
//   • RAD_LEAD_REQ_MM_NR carries the computed required Pb; the provided
//     RAD_LEAD_MM_NR is only seeded when empty (an existing provided value is
//     preserved so RadShieldValidator can still flag under-provision).
//   • RAD_COMPUTED_DT / RAD_COMPUTED_BY_TXT record who/when (audit stamp).
//
// Inputs are read from each barrier element first, falling back to the Healthcare
// panel (HcOptions.Rad*) — reusing the exact inputs the RadCalc* commands use.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Radiation;
using StingTools.Standards.NCRP147;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StingTools.Commands.Radiation
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RadShieldWriteBackCommand : IExternalCommand
    {
        private const double ConservativeDefaultDistanceM = 1.0;

        private static readonly HashSet<BuiltInCategory> BarrierCats = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows, BuiltInCategory.OST_GenericModel
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx.Doc;
                var uidoc = ctx.UIDoc;

                var selIds = uidoc.Selection.GetElementIds();
                var barriers = (selIds ?? new List<ElementId>())
                    .Select(id => doc.GetElement(id))
                    .Where(el => el?.Category != null && BarrierCats.Contains((BuiltInCategory)el.Category.Id.Value))
                    .ToList();

                if (barriers.Count == 0)
                {
                    TaskDialog.Show("Radiation Shielding Write-back",
                        "Select one or more barrier elements (Wall / Door / Window / Generic Model), then run this command.");
                    return Result.Cancelled;
                }

                // Project-tunable conservative default distance (mirrors RadShieldValidator).
                double projDefaultDist = GetDouble(doc.ProjectInformation, "PRJ_ORG_HEALTH_RAD_DEFAULT_DIST_M") ?? 0;
                double defaultDist = projDefaultDist > 0 ? projDefaultDist : ConservativeDefaultDistanceM;
                int kVp = HcOptions.RadKvp > 0 ? (int)HcOptions.RadKvp : 125;
                string user = doc.Application?.Username ?? "";
                string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

                int approved = 0, draft = 0;
                using (var tx = new Transaction(doc, "STING Radiation Shielding Write-back"))
                {
                    tx.Start();
                    foreach (var el in barriers)
                    {
                        // Inputs: element param → Healthcare-panel fallback → sensible default.
                        string barrierType = FirstNonEmpty(GetStr(el, "RAD_BARRIER_TYPE_TXT"), "PRIMARY");
                        double workload = GetDouble(el, "RAD_WORKLOAD_MAWK_NR") ?? HcOptions.RadW;
                        double useFactor = GetDouble(el, "RAD_USE_FACTOR_NR") ?? (HcOptions.RadU > 0 ? HcOptions.RadU : 0.25);
                        double occFactor = GetDouble(el, "RAD_OCC_FACTOR_NR") ?? (HcOptions.RadT > 0 ? HcOptions.RadT : 1.0);
                        double distance = GetDouble(el, "RAD_DISTANCE_M_NR") is double dEl && dEl > 0 ? dEl
                                          : (HcOptions.RadD > 0 ? HcOptions.RadD : defaultDist);
                        string goal = FirstNonEmpty(GetStr(el, "RAD_DOSE_DESIGN_GOAL_TXT"),
                                        string.Equals(HcOptions.RadArea, "Uncontrolled", StringComparison.OrdinalIgnoreCase)
                                            ? "UNCONTROLLED" : "CONTROLLED");
                        double provided = GetDouble(el, "RAD_LEAD_MM_NR") ?? 0;

                        var calc = NCRP147Calculator.Compute(barrierType, goal, workload, useFactor, occFactor,
                                                             distance, kVp, provided);
                        double required = calc.LeadMmRequired;

                        // QE gate — APPROVED only with a QE on record, else DRAFT. Never certifies silently.
                        bool signed = RadiationSignoffGate.IsSigned(doc, el);
                        string status = signed ? "APPROVED" : "DRAFT";
                        if (signed) approved++; else draft++;

                        // Persist the design inputs actually used…
                        ParameterHelpers.SetString(el, "RAD_BARRIER_TYPE_TXT", barrierType, overwrite: true);
                        SetDouble(el, "RAD_WORKLOAD_MAWK_NR", workload);
                        SetDouble(el, "RAD_USE_FACTOR_NR", useFactor);
                        SetDouble(el, "RAD_OCC_FACTOR_NR", occFactor);
                        SetDouble(el, "RAD_DISTANCE_M_NR", distance);
                        ParameterHelpers.SetString(el, "RAD_DOSE_DESIGN_GOAL_TXT", goal, overwrite: true);
                        // …the computed required Pb (always)…
                        SetDouble(el, "RAD_LEAD_REQ_MM_NR", required);
                        // …seed the PROVIDED lead only when empty, so an existing provided value
                        // is preserved for RadShieldValidator to check against.
                        if (provided <= 0) SetDouble(el, "RAD_LEAD_MM_NR", required);
                        // …and the QE-gated status + audit stamp.
                        ParameterHelpers.SetString(el, "RAD_SHIELD_STATUS_TXT", status, overwrite: true);
                        ParameterHelpers.SetString(el, "RAD_COMPUTED_DT", nowIso, overwrite: true);
                        ParameterHelpers.SetString(el, "RAD_COMPUTED_BY_TXT", user, overwrite: true);

                        StingLog.Info($"RadWriteBack: {el.Category?.Name} {el.Id} barrier={barrierType} " +
                                      $"required={required:F2}mm provided={(provided > 0 ? provided.ToString("F2") : "seeded")} " +
                                      $"status={status} d={distance:F1}m");
                    }
                    tx.Commit();
                }

                var sb = new StringBuilder();
                sb.AppendLine(RadiationSignoffGate.StatusBanner(doc)).AppendLine();
                sb.AppendLine($"Stamped {barriers.Count} barrier element(s):");
                sb.AppendLine($"  APPROVED (QE on record): {approved}");
                sb.AppendLine($"  DRAFT (no QE — not certified): {draft}");
                sb.AppendLine().AppendLine("Written: RAD_BARRIER_TYPE_TXT, RAD_WORKLOAD_MAWK_NR, RAD_USE_FACTOR_NR, " +
                    "RAD_OCC_FACTOR_NR, RAD_DISTANCE_M_NR, RAD_DOSE_DESIGN_GOAL_TXT, RAD_LEAD_REQ_MM_NR (computed), " +
                    "RAD_LEAD_MM_NR (seeded if empty), RAD_SHIELD_STATUS_TXT, RAD_COMPUTED_DT, RAD_COMPUTED_BY_TXT.");
                TaskDialog.Show("STING — Radiation Shielding Write-back" + RadiationSignoffGate.TitleSuffix(doc), sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RadShieldWriteBackCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string FirstNonEmpty(string a, string b) => string.IsNullOrWhiteSpace(a) ? b : a;

        private static string GetStr(Element el, string name)
        {
            var p = el?.LookupParameter(name);
            return (p != null && p.HasValue && p.StorageType == StorageType.String) ? (p.AsString() ?? "") : "";
        }

        private static double? GetDouble(Element el, string name)
        {
            var p = el?.LookupParameter(name);
            if (p == null || !p.HasValue) return null;
            if (p.StorageType == StorageType.Double) return p.AsDouble();
            if (p.StorageType == StorageType.Integer) return p.AsInteger();
            if (p.StorageType == StorageType.String &&
                double.TryParse(p.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
            return null;
        }

        // No ParameterHelpers.SetDouble exists — write NUMBER (Double) params, tolerating
        // a TEXT-typed binding by writing the invariant string form.
        private static void SetDouble(Element el, string name, double value)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double) p.Set(value);
                else if (p.StorageType == StorageType.Integer) p.Set((int)Math.Round(value));
                else if (p.StorageType == StorageType.String) p.Set(value.ToString("F3", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { StingLog.Warn($"RadWriteBack SetDouble '{name}' suppressed: {ex.Message}"); }
        }
    }
}
