// StingTools — Drawing Template Manager · what production decided for a view or sheet
//
// Two decisions the producer makes once and must not undo on a re-run:
//
//   * DTW-196 — the scale a view was fitted to its sheet slot. A re-run re-applies the
//     drawing type, and with it the type's scale; the view is already on its sheet, so
//     placement does not run and nothing fitted it again. The view jumped back to a
//     scale that does not fit its slot.
//   * DTW-209 — the name production gave a sheet. A reused sheet's name is refreshed
//     after a level rename only when nobody has edited it since; the stored name tells
//     a generated name from a hand-edited one.
//
// Kept in Extensible Storage on the view / sheet, not in shared parameters: nothing else
// reads them, and no parameter has to be bound for them to work.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core.Storage;

namespace StingTools.Core.Drawing
{
    internal static class ProducedViewState
    {
        private static readonly Guid SchemaGuid = new Guid("5b7c2e19-8d4a-4f63-a1e0-6c93d2b7f418");
        private const string SchemaName    = "StingProducedViewState";
        private const string FieldFitted   = "FittedScale";    // int — scale the view was fitted to (0 = none)
        private const string FieldFitBase  = "FitBaseScale";   // int — the type scale the fit started from
        private const string FieldSheetName = "GeneratedSheetName"; // string — the name production last gave a sheet

        internal sealed class State
        {
            public int FittedScale;
            public int FitBaseScale;
            public string GeneratedSheetName;
        }

        private static Schema GetOrCreate()
        {
            var existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;
            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName(SchemaName);
            sb.SetVendorId(StingSchemaBuilder.VendorId);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Vendor);
            sb.AddSimpleField(FieldFitted, typeof(int)).SetDocumentation("Scale production fitted the view to its sheet slot (DTW-196).");
            sb.AddSimpleField(FieldFitBase, typeof(int)).SetDocumentation("Drawing-type scale the fit started from (DTW-196).");
            sb.AddSimpleField(FieldSheetName, typeof(string)).SetDocumentation("Sheet name production last generated (DTW-209).");
            return sb.Finish();
        }

        internal static State Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var e = el.GetEntity(schema);
                if (e == null || !e.IsValid()) return null;
                return new State
                {
                    FittedScale = e.Get<int>(FieldFitted),
                    FitBaseScale = e.Get<int>(FieldFitBase),
                    GeneratedSheetName = e.Get<string>(FieldSheetName),
                };
            }
            catch (Exception ex) { StingLog.Warn($"ProducedViewState.Read {el.Id}: {ex.Message}"); return null; }
        }

        private static bool Write(Element el, Action<State> change)
        {
            if (el == null) return false;
            try
            {
                var s = Read(el) ?? new State();
                change(s);
                var schema = GetOrCreate();
                var e = new Entity(schema);
                e.Set(FieldFitted, s.FittedScale);
                e.Set(FieldFitBase, s.FitBaseScale);
                e.Set(FieldSheetName, s.GeneratedSheetName ?? "");
                el.SetEntity(e);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"ProducedViewState.Write {el.Id}: {ex.Message}"); return false; }
        }

        /// <summary>DTW-196: record the scale <paramref name="view"/> was fitted to, from <paramref name="baseScale"/>.</summary>
        internal static bool RecordFit(View view, int fittedScale, int baseScale)
            => Write(view, s => { s.FittedScale = fittedScale; s.FitBaseScale = baseScale; });

        /// <summary>DTW-215: forget a fit that no longer describes <paramref name="view"/> (its type scale changed).</summary>
        internal static bool ClearFit(View view)
            => Write(view, s => { s.FittedScale = 0; s.FitBaseScale = 0; });

        /// <summary>
        /// DTW-215: the element that holds <paramref name="view"/>'s fit — the view itself,
        /// or for a dependent its primary (a dependent's scale is its parent's, and only the
        /// parent is fitted).
        /// </summary>
        internal static View FitHolder(View view)
        {
            if (view == null) return null;
            try
            {
                var pid = view.GetPrimaryViewId();
                if (pid != null && pid != ElementId.InvalidElementId && view.Document.GetElement(pid) is View primary)
                    return primary;
            }
            catch (Exception ex) { StingLog.Warn($"ProducedViewState.FitHolder {view.Id}: {ex.Message}"); }
            return view;
        }

        /// <summary>DTW-209: record the name production gave <paramref name="sheet"/>.</summary>
        internal static bool RecordSheetName(ViewSheet sheet, string name)
            => Write(sheet, s => s.GeneratedSheetName = name ?? "");
    }
}
