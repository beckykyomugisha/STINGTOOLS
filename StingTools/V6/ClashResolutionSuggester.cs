// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/ClashResolutionSuggester.cs — S6.2 (N-G6).
//
// Given a triaged clash (S6.1 output) generate up to 3 resolution
// candidates that the coordinator picks from in the BCC UI:
//
//   MOVE     shift the smaller element up / down / laterally by a
//            discipline-appropriate step (HVAC 50 mm, pipe 25 mm,
//            conduit 10 mm). Requires the smaller element's
//            connectors tolerate the offset.
//
//   REROUTE  re-run the VoxelGrid + A* pipeline (S3.7-S3.11) with
//            the other element's AABB marked obstacle so the new
//            path avoids it.
//
//   ACCEPT   document rationale + close clash via CLASH_RESOLUTION_
//            STATUS_TXT = "ACCEPTED". No model modification.
//
// Each candidate carries an estimated cost and risk score so the UI
// can sort and colour-code.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.V6
{
    public enum ResolutionAction { Move, Reroute, Accept }

    public sealed class ResolutionCandidate
    {
        public ResolutionAction Action { get; set; }
        public string DescriptionText { get; set; } = string.Empty;
        public double EstimatedCostUsd { get; set; }
        public double RiskScore { get; set; }   // 0..1, lower = safer
        public XYZ SuggestedOffsetFt { get; set; }
        public long AffectedElementId { get; set; }
        public string DisciplineRationale { get; set; } = string.Empty;
    }

    public static class ClashResolutionSuggester
    {
        /// <summary>
        /// Produce up to 3 candidate resolutions for a single clash.
        /// </summary>
        public static List<ResolutionCandidate> Suggest(Document doc, ScoredClash clash)
        {
            var list = new List<ResolutionCandidate>();
            if (doc == null || clash == null) return list;
            var smaller = PickSmaller(doc, clash);
            if (smaller == null) return list;

            var discStep = DisciplineStepFt(smaller);

            // --- MOVE up ---
            list.Add(new ResolutionCandidate
            {
                Action = ResolutionAction.Move,
                DescriptionText = $"Move {smaller.Category?.Name} up {(discStep * 304.8):F0} mm",
                EstimatedCostUsd = 25.0,
                RiskScore = 0.25,
                SuggestedOffsetFt = new XYZ(0, 0, discStep),
                AffectedElementId = smaller.Id.Value,
                DisciplineRationale = "Small vertical offset — fits within CIBSE / BS 8313 plenum allowance",
            });

            // --- REROUTE ---
            list.Add(new ResolutionCandidate
            {
                Action = ResolutionAction.Reroute,
                DescriptionText = "Re-run A* + ACO with other element as obstacle",
                EstimatedCostUsd = 75.0,
                RiskScore = 0.45,
                AffectedElementId = smaller.Id.Value,
                DisciplineRationale = "Voxel grid re-solve — may introduce extra bends",
            });

            // --- ACCEPT ---
            list.Add(new ResolutionCandidate
            {
                Action = ResolutionAction.Accept,
                DescriptionText = "Accept as non-critical; document rationale; flag in BCC",
                EstimatedCostUsd = 0.0,
                RiskScore = clash.Category == "STRUCTURAL" ? 0.85 : 0.3,
                AffectedElementId = smaller.Id.Value,
                DisciplineRationale = clash.Category == "STRUCTURAL"
                    ? "HIGH RISK — structural clashes should almost never be accepted."
                    : "Non-critical services clash — accept with note on coord log.",
            });
            return list;
        }

        /// <summary>
        /// Apply a chosen candidate atomically via TransactionHelper.
        /// Move + Accept paths modify parameters; Reroute hands off to
        /// the voxel-grid pipeline (caller responsibility).
        /// </summary>
        public static bool Apply(Document doc, ResolutionCandidate choice)
        {
            if (doc == null || choice == null) return false;
            try
            {
                TransactionHelper.RunInScope(doc, "STING clash resolution", t =>
                {
                    var el = doc.GetElement(new ElementId(choice.AffectedElementId));
                    if (el == null) return;
                    switch (choice.Action)
                    {
                        case ResolutionAction.Move:
                            if (choice.SuggestedOffsetFt != null)
                                ElementTransformUtils.MoveElement(doc, el.Id, choice.SuggestedOffsetFt);
                            WriteResolution(el, "APPLIED", "MOVE");
                            break;
                        case ResolutionAction.Accept:
                            WriteResolution(el, "ACCEPTED", "ACCEPT");
                            break;
                        case ResolutionAction.Reroute:
                            // handoff to voxel pipeline — caller runs it
                            WriteResolution(el, "PENDING", "REROUTE");
                            break;
                    }
                });
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("ClashResolutionSuggester.Apply failed", ex);
                return false;
            }
        }

        private static Element PickSmaller(Document doc, ScoredClash c)
        {
            var a = doc.GetElement(new ElementId(c.ElementA));
            var b = doc.GetElement(new ElementId(c.ElementB));
            if (a == null) return b;
            if (b == null) return a;
            var va = Vol(a); var vb = Vol(b);
            return va <= vb ? a : b;
        }

        private static double Vol(Element el)
        {
            var bb = el.get_BoundingBox(null);
            if (bb == null) return double.MaxValue;
            return (bb.Max.X - bb.Min.X) * (bb.Max.Y - bb.Min.Y) * (bb.Max.Z - bb.Min.Z);
        }

        private static double DisciplineStepFt(Element el)
        {
            string cat = el.Category?.Name ?? string.Empty;
            if (cat.Contains("Duct")) return 50.0 / 304.8;
            if (cat.Contains("Pipe")) return 25.0 / 304.8;
            if (cat.Contains("Conduit") || cat.Contains("Cable")) return 10.0 / 304.8;
            return 25.0 / 304.8;
        }

        private static void WriteResolution(Element el, string status, string action)
        {
            try
            {
                var ps = el.LookupParameter(ParamRegistry.CLASH_RESOLUTION_STATUS_TXT);
                if (ps != null && !ps.IsReadOnly) ps.Set(status);
                var pa = el.LookupParameter(ParamRegistry.CLASH_RESOLUTION_ACTION_TXT);
                if (pa != null && !pa.IsReadOnly) pa.Set(action);
            }
            catch (Exception ex) { StingLog.Warn("WriteResolution: " + ex.Message); }
        }
    }
}
