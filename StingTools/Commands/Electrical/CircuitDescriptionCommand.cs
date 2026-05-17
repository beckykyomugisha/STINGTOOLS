using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Auto-fills the writable Load Name on every power circuit by
    /// concatenating up to three caller-selected source values
    /// (equipment name / mark / room / system / custom load name)
    /// joined by a configurable separator. Optionally Title-Cases the
    /// result. Sources are pulled from the dock-panel's
    /// CIRCUIT DESCRIPTION AUTO-FILL card via
    /// <see cref="StingElectricalCommandHandler.CurrentDescOptions"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CircuitDescriptionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var opts = StingElectricalCommandHandler.CurrentDescOptions
                       ?? new DescriptionAutoFillOptions
                       { Source1 = "Connected equipment name", Source2 = "Equipment mark/tag",
                         Source3 = "Room/space name", Separator = " — ", TitleCase = true };

            var systems = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .Where(IsPowerCircuit)
                .ToList();

            int updated = 0, skipped = 0, errors = 0;
            using (var tx = new Transaction(doc, "STING Circuit Descriptions"))
            {
                tx.Start();
                foreach (var sys in systems)
                {
                    try
                    {
                        string built = BuildDescription(doc, sys, opts);
                        if (string.IsNullOrEmpty(built)) { skipped++; continue; }

                        // ElectricalSystem.LoadName is the writable display field
                        // shown in panel schedules' "Circuit Description" column.
                        try
                        {
                            sys.LoadName = built;
                            updated++;
                        }
                        catch (Exception ex)
                        {
                            // Some Revit versions expose LoadName as a parameter; fall back.
                            var p = sys.LookupParameter("Load Name");
                            if (p != null && !p.IsReadOnly) { p.Set(built); updated++; }
                            else { StingLog.Warn($"LoadName write failed on {sys.Name}: {ex.Message}"); errors++; }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CircuitDescription: {ex.Message}");
                        errors++;
                    }
                }
                tx.Commit();
            }

            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Electrical",
                $"Updated {updated} circuit description(s).\nSkipped: {skipped}\nErrors: {errors}");
            return Result.Succeeded;
        }

        private static bool IsPowerCircuit(ElectricalSystem s)
        {
            try { return s.SystemType == ElectricalSystemType.PowerCircuit; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return true; }
        }

        /// <summary>
        /// Build a circuit description preview without writing to the model.
        /// Returns one row per power circuit with the proposed text.
        /// </summary>
        public static List<(string circuitRef, string proposed)> PreviewDescriptions(
            Document doc, string src1, string src2, string src3, string separator, bool titleCase)
        {
            var result = new List<(string, string)>();
            if (doc == null) return result;
            var opts = new DescriptionAutoFillOptions
            { Source1 = src1, Source2 = src2, Source3 = src3, Separator = separator, TitleCase = titleCase };

            try
            {
                foreach (var sys in new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .Where(IsPowerCircuit))
                {
                    string ref_ = SafeCircuitRef(sys);
                    string built = BuildDescription(doc, sys, opts);
                    if (!string.IsNullOrEmpty(built))
                        result.Add((ref_, built));
                }
            }
            catch (Exception ex) { StingLog.Warn($"PreviewDescriptions: {ex.Message}"); }
            return result;
        }

        private static string SafeCircuitRef(ElectricalSystem sys)
        {
            try
            {
                string panel = sys.PanelName ?? "";
                string num = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "";
                return string.IsNullOrEmpty(panel) ? num : $"{panel}-{num}";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }

        private static string BuildDescription(Document doc, ElectricalSystem sys,
            DescriptionAutoFillOptions opts)
        {
            string a = ResolveSource(doc, sys, opts.Source1);
            string b = ResolveSource(doc, sys, opts.Source2);
            string c = ResolveSource(doc, sys, opts.Source3);
            string sep = string.IsNullOrEmpty(opts.Separator) ? " — " : opts.Separator;

            var parts = new[] { a, b, c }
                .Select(p => (p ?? "").Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string joined = string.Join(sep, parts);
            return opts.TitleCase ? ToTitleCase(joined) : joined;
        }

        private static string ResolveSource(Document doc, ElectricalSystem sys, string src)
        {
            if (string.IsNullOrEmpty(src) || src.StartsWith("(none)", StringComparison.OrdinalIgnoreCase))
                return "";
            try
            {
                var first = sys.Elements?.Cast<Element>().FirstOrDefault();
                if (src.StartsWith("Connected equipment name", StringComparison.OrdinalIgnoreCase))
                    return first?.Name ?? "";
                if (src.StartsWith("Equipment mark", StringComparison.OrdinalIgnoreCase))
                    return first?.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
                if (src.StartsWith("Room/space", StringComparison.OrdinalIgnoreCase))
                    return RoomNameFor(doc, first);
                if (src.StartsWith("System name", StringComparison.OrdinalIgnoreCase))
                    return sys.Name ?? "";
                if (src.StartsWith("Load name", StringComparison.OrdinalIgnoreCase))
                    return sys.LoadName ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"ResolveSource '{src}': {ex.Message}"); }
            return "";
        }

        private static string RoomNameFor(Document doc, Element el)
        {
            if (el == null) return "";
            try
            {
                if (el is FamilyInstance fi && fi.Room is Room r) return r.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try
            {
                var loc = (el.Location as LocationPoint)?.Point;
                if (loc != null)
                {
                    var room = doc.GetRoomAtPoint(loc);
                    if (room != null) return room.Name ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "";
        }

        private static string ToTitleCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try { return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower()); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return s; }
        }
    }
}
