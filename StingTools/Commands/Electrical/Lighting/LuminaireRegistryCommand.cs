using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Photometrics;

namespace StingTools.Commands.Electrical.Lighting
{
    /// <summary>
    /// Two-mode command that manages the project luminaire registry —
    /// the project-scoped CSV mapping each Revit luminaire family/type
    /// to its manufacturer / model / .ies path / CCT / CRI / lumens.
    ///
    /// Modes:
    ///
    /// <list type="number">
    /// <item><b>Scaffold:</b> if the registry CSV doesn't exist yet,
    /// walks every unique luminaire family/type in the project and
    /// writes seeded blank rows so the engineer / designer fills in
    /// the columns once.</item>
    /// <item><b>Apply:</b> if the registry exists, walks every luminaire
    /// family instance, looks up its (family, type), and stamps the
    /// matching entry's photometric file + lumens + watts + CCT + CRI
    /// onto the type's parameters where they're empty. Idempotent.</item>
    /// </list>
    ///
    /// Pairs with <see cref="LightingCalcSheetCommand"/> (which reads the
    /// same data per fixture) and AssignPhotometricCommand (which
    /// previously forced a manual file picker per family).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LuminaireRegistryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string projectFolder = ResolveProjectFolder(doc);
            if (string.IsNullOrEmpty(projectFolder))
            {
                TaskDialog.Show("STING Luminaire Registry",
                    "Save the Revit project first — the registry lives at " +
                    "<project>/_BIM_COORD/luminaire_registry.csv and needs a project location.");
                return Result.Cancelled;
            }
            string regPath = LuminaireRegistry.ResolvePath(projectFolder);
            bool exists = File.Exists(regPath);

            // Collect unique family/type pairs in the project.
            var pairs = new HashSet<(string fam, string typ)>();
            foreach (var fi in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                pairs.Add((fi.Symbol?.FamilyName ?? "—", fi.Symbol?.Name ?? "—"));
            }

            if (pairs.Count == 0)
            {
                TaskDialog.Show("STING Luminaire Registry",
                    "No luminaire family instances in the project yet. Place at least one fixture first.");
                return Result.Cancelled;
            }

            if (!exists)
            {
                // Scaffold mode
                LuminaireRegistry.SaveTemplate(regPath, pairs.OrderBy(p => p.fam).ThenBy(p => p.typ));
                LuminaireRegistry.InvalidateCache();
                var td = new TaskDialog("STING Luminaire Registry — Scaffolded")
                {
                    MainInstruction = $"Created template with {pairs.Count} unique luminaire type(s).",
                    MainContent =
                        $"Path: {regPath}\n\n" +
                        "Open in Excel, fill in Manufacturer / Model / IESPath / Lumens / Watts / CCT / CRI " +
                        "for each row, then re-run this command in Apply mode to push the values onto " +
                        "the matching family types.",
                    CommonButtons = TaskDialogCommonButtons.Ok,
                    AllowCancellation = true
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open registry now");
                if (td.Show() == TaskDialogResult.CommandLink1)
                {
                    try { Process.Start(new ProcessStartInfo(regPath) { UseShellExecute = true }); } catch { }
                }
                return Result.Succeeded;
            }

            // Apply mode — walk fixtures, stamp matched entries onto the family TYPES.
            var registry = LuminaireRegistry.LoadFor(projectFolder);
            if (registry.Entries.Count == 0)
            {
                TaskDialog.Show("STING Luminaire Registry",
                    $"Registry file exists but has no rows.\n{regPath}\n\nFill it in and re-run.");
                return Result.Cancelled;
            }

            int matched = 0, skipped = 0, missing = 0;
            var seenTypes = new HashSet<long>();
            using (var tx = new Transaction(doc, "STING Luminaire Registry Apply"))
            {
                tx.Start();
                foreach (var fi in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .WhereElementIsNotElementType().OfType<FamilyInstance>())
                {
                    try
                    {
                        var sym = fi.Symbol;
                        if (sym == null) { skipped++; continue; }
                        if (!seenTypes.Add(sym.Id.Value)) continue;  // each type once

                        var entry = registry.Find(sym.FamilyName, sym.Name);
                        if (entry == null) { missing++; continue; }

                        // Write to type — applies to every instance via Revit's type system.
                        // Parameter names match MR_PARAMETERS.txt canonical (Phase 188 fix —
                        // earlier draft used ELC_LITE_* names that don't exist in the registry).
                        SetIfEmpty(sym, "ASS_MANUFACTURER_TXT",      entry.Manufacturer);
                        SetIfEmpty(sym, "ASS_MODEL_NR_TXT",          entry.Model);
                        SetIfEmpty(sym, "ELC_PHOTO_FILE_PATH_TXT",   entry.IesPath);
                        SetDoubleIfEmpty(sym, "ELC_PHOTO_LUMENS_NR",     entry.Lumens);
                        SetDoubleIfEmpty(sym, "LTG_FIX_LMP_WATTAGE_W",   entry.Watts);
                        SetDoubleIfEmpty(sym, "ELC_PHOTO_CCT_K",         entry.CctK);
                        SetDoubleIfEmpty(sym, "ELC_PHOTO_CRI_NR",        entry.Cri);
                        SetDoubleIfEmpty(sym, "ELC_PHOTO_BEAM_ANGLE_DEG",entry.BeamAngleDeg);
                        SetIfEmpty(sym, "ELC_IP_RATING_TXT",         entry.IpRating);
                        matched++;
                    }
                    catch (Exception ex) { StingLog.Warn($"LuminaireRegistry apply: {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }

            TaskDialog.Show("STING Luminaire Registry — Applied",
                $"Stamped {matched} type(s) from registry.\n" +
                $"Missing entries (no row in CSV): {missing}\n" +
                $"Skipped: {skipped}\n\n" +
                $"Registry: {regPath}");
            return Result.Succeeded;
        }

        private static string ResolveProjectFolder(Document doc)
        {
            try
            {
                string path = doc?.PathName ?? "";
                if (!string.IsNullOrEmpty(path)) return Path.GetDirectoryName(path);
            }
            catch { }
            try { return OutputLocationHelper.GetOutputDirectory(doc); }
            catch { return null; }
        }

        private static void SetIfEmpty(Element el, string paramName, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return;
                if (string.IsNullOrEmpty(p.AsString())) p.Set(value);
            }
            catch { }
        }

        private static void SetDoubleIfEmpty(Element el, string paramName, double value)
        {
            if (value <= 0) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double && p.AsDouble() <= 0) p.Set(value);
                else if (p.StorageType == StorageType.String && string.IsNullOrEmpty(p.AsString()))
                    p.Set(value.ToString("0.###"));
            }
            catch { }
        }
    }
}
