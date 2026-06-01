#nullable enable annotations
// StingTools — IfcGuidStabilityCheck.cs (Prompt 12 — surface the Stabilize prerequisite)
//
// Cross-host identity (Prompt 7) depends on every synced/exported element
// carrying a STABLE true IFC GlobalId in IFC_GLOBAL_ID_TXT. That value is a
// snapshot written by StabilizeIfcGuidsCommand and only equals the exported
// file's GlobalId while the model stays stable through export. Until now the
// prerequisite was only documented (StabilizeIfcGuidsCommand comment) and
// detected post-hoc server-side; it was never surfaced in the user's normal
// push/export flow.
//
// This helper provides a cheap, client-side precheck — the local equivalent of
// the server's GLOBALID_DRIFT signal — that the BCC tag-sync push and the IFC
// export run before proceeding:
//   • Missing : an element that can carry IFC_GLOBAL_ID_TXT but it's empty
//               (never stabilised / never IFC-exported).
//   • Stale   : STING_STALE_BOOL == 1 — the stale-marker IUpdater flagged a
//               geometry change since the element was last processed, so its
//               GlobalId snapshot may now post-date the geometry (drift).
//
// When either is non-zero it shows a NON-BLOCKING warning with a one-click
// "Run Stabilize IFC GUIDs now". A clean model shows nothing.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Interop
{
    public static class IfcGuidStabilityCheck
    {
        private const string IfcGuidParam = "IFC_GLOBAL_ID_TXT";

        // Bound the scan so a manual push/export on a huge model can't stall on
        // the precheck. The first `Cap` param-bearing elements are a sufficient
        // sample to decide "this model hasn't been stabilised / has drifted".
        private const int Cap = 20000;

        public sealed class Report
        {
            public int Relevant;   // elements that can carry IFC_GLOBAL_ID_TXT
            public int Missing;    // …but it's empty
            public int Stale;      // STING_STALE_BOOL == 1 (geometry changed since last process)
            public bool Capped;
            /// <summary>True when the user should be prompted to stabilise.</summary>
            public bool NeedsStabilise => Missing > 0 || Stale > 0;
        }

        /// <summary>
        /// Scan the model for unstabilised / drifted elements. When
        /// <paramref name="taggedOnly"/> is true, only elements carrying an
        /// ASS_TAG_1 (the set the cross-host sync pushes) are considered; the IFC
        /// export passes false to consider every exportable element.
        /// </summary>
        public static Report Evaluate(Document doc, bool taggedOnly)
        {
            var r = new Report();
            if (doc == null) return r;

            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (r.Relevant >= Cap) { r.Capped = true; break; }

                // Only judge elements that can actually carry the IFC GlobalId.
                var p = el.LookupParameter(IfcGuidParam);
                if (p == null) continue;

                if (taggedOnly)
                {
                    string t1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(t1)) continue;
                }

                r.Relevant++;
                if (string.IsNullOrWhiteSpace(p.AsString())) r.Missing++;
                if (ParameterHelpers.GetInt(el, ParamRegistry.STALE, 0) == 1) r.Stale++;
            }
            return r;
        }

        /// <summary>
        /// Non-blocking gate. When the model needs stabilising, prompts with a
        /// one-click "Run Stabilize IFC GUIDs now" plus "Continue anyway" /
        /// "Cancel". Returns true to proceed with the push/export, false to abort.
        /// A clean model returns true with no prompt.
        /// </summary>
        public static bool ConfirmStabilised(
            Document doc, Report r, string flowName, ExternalCommandData? commandData = null)
        {
            if (r == null || !r.NeedsStabilise) return true;  // clean → no nag

            var parts = new List<string>();
            if (r.Missing > 0) parts.Add($"{r.Missing} element(s) have no stable IFC GlobalId (never stabilised / exported)");
            if (r.Stale > 0)   parts.Add($"{r.Stale} element(s) changed geometry since their GlobalId snapshot (possible drift)");
            string capNote = r.Capped ? $"\n(Checked the first {Cap:N0} elements.)" : "";

            var td = new TaskDialog("STING — IFC GlobalId check")
            {
                MainInstruction = "Run 'Stabilize IFC GUIDs' before " + flowName + "?",
                MainContent =
                    string.Join("\n", parts) +
                    "\n\nCross-host identity (matching this element in Blender / ArchiCAD) keys on the " +
                    "true IFC GlobalId. Stabilising snapshots each element's IfcGloballyUniqueId into " +
                    "IFC_GLOBAL_ID_TXT so it matches the exported IFC file." + capNote,
                AllowCancellation = true,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Run Stabilize IFC GUIDs now", "Then continue with " + flowName + ".");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Continue anyway", "Proceed without stabilising (cross-host links may not resolve).");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.DefaultButton = TaskDialogResult.CommandLink1;

            var choice = td.Show();
            if (choice == TaskDialogResult.CommandLink1)
            {
                try
                {
                    string msg = "";
                    new StabilizeIfcGuidsCommand().Execute(commandData, ref msg, new ElementSet());
                }
                catch (System.Exception ex)
                {
                    StingLog.Warn($"IfcGuidStabilityCheck: inline Stabilize failed: {ex.Message}");
                }
                return true;   // proceed after stabilising
            }
            if (choice == TaskDialogResult.CommandLink2) return true;  // continue anyway
            return false;  // Cancel → abort the push/export
        }
    }
}
