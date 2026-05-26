using StingTools.Core;
// StingTools — family augmentation engine (Phase 175)
//
// Injects STING_SYMBOL_ID / STING_SYMBOL_STANDARD / STING_HOST_ELEMENT_ID
// onto loaded model families so existing project content participates in
// the symbol system without being recreated. Augmentation is reversible
// via RollbackAugmentation.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    public sealed class AugmentationResult
    {
        public string FamilyName { get; set; }
        public bool Success { get; set; }
        public bool AlreadyAugmented { get; set; }
        public string ConceptId { get; set; }
        public string Warning { get; set; }
    }

    public static class FamilyAugmentationEngine
    {
        /// <summary>
        /// Augment every Family loaded in the project. Synchronous, but the
        /// optional <paramref name="isCancelled"/> predicate lets the
        /// caller short-circuit between families (StingProgressDialog
        /// passes <c>() =&gt; progress.IsCancelled</c> here so the user
        /// can stop a 200-family run).
        /// <para>
        /// EditFamily / LoadFamily must run on the Revit API thread; this
        /// method is therefore not thread-safe. Callers that need a
        /// non-blocking UI should pump the progress dialog from the API
        /// thread itself (see <c>AugmentProjectFamiliesCommand</c>).
        /// </para>
        /// </summary>
        public static List<AugmentationResult> AugmentProjectFamilies(Document doc,
            IProgress<string> progress = null, Func<bool> isCancelled = null)
        {
            var results = new List<AugmentationResult>();
            if (doc == null) return results;
            try
            {
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(IsAugmentableCategory)
                    .ToList();

                int idx = 0;
                foreach (var fam in families)
                {
                    if (isCancelled != null && isCancelled())
                    {
                        StingTools.Core.StingLog.Info(
                            $"AugmentProjectFamilies cancelled at {idx}/{families.Count}");
                        break;
                    }
                    idx++;
                    progress?.Report($"Augmenting {idx}/{families.Count}: {fam.Name}");
                    results.Add(AugmentFamily(doc, fam));
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("AugmentProjectFamilies", ex);
            }
            return results;
        }

        /// <summary>
        /// Filter to families whose category could plausibly host a STING
        /// symbol concept. Skips title blocks, profiles, detail items,
        /// annotation symbols, and other non-MEP / non-architectural
        /// content so a 1,000-family library run only touches the ~200
        /// that matter.
        /// </summary>
        private static bool IsAugmentableCategory(Family fam)
        {
            try
            {
                var cat = fam.FamilyCategory;
                if (cat == null) return false;
                long catId;
                try { catId = cat.Id?.Value ?? 0L; }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"IsAugmentableCategory id: {ex.Message}"); return false; }

                switch ((BuiltInCategory)catId)
                {
                    case BuiltInCategory.OST_MechanicalEquipment:
                    case BuiltInCategory.OST_ElectricalEquipment:
                    case BuiltInCategory.OST_ElectricalFixtures:
                    case BuiltInCategory.OST_LightingFixtures:
                    case BuiltInCategory.OST_FireAlarmDevices:
                    case BuiltInCategory.OST_Sprinklers:
                    case BuiltInCategory.OST_PlumbingFixtures:
                    case BuiltInCategory.OST_PipeAccessory:
                    case BuiltInCategory.OST_DuctAccessory:
                    case BuiltInCategory.OST_DuctTerminal:
                    case BuiltInCategory.OST_DataDevices:
                    case BuiltInCategory.OST_CommunicationDevices:
                    case BuiltInCategory.OST_NurseCallDevices:
                    case BuiltInCategory.OST_SecurityDevices:
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"IsAugmentableCategory: {ex.Message}");
                return false;
            }
        }

        public static AugmentationResult AugmentFamily(Document doc, Family fam)
        {
            var r = new AugmentationResult { FamilyName = fam?.Name ?? "<unknown>" };
            if (doc == null || fam == null) { r.Warning = "null doc/family"; return r; }

            // Identify concept candidate by category.
            try
            {
                string catName = fam.FamilyCategory?.Name;
                var candidates = SymbolConceptRegistry.GetConceptsForCategory(catName);
                var concept = candidates?.FirstOrDefault();
                if (concept == null)
                {
                    r.Warning = $"No concept matches category '{catName}'.";
                    return r;
                }
                r.ConceptId = concept.ConceptId;
            }
            catch (Exception ex) { r.Warning = $"category resolution: {ex.Message}"; return r; }

            Document famDoc = null;
            try
            {
                famDoc = doc.EditFamily(fam);
                if (famDoc == null) { r.Warning = "EditFamily returned null"; return r; }

                using (var tx = new Transaction(famDoc, "STING Augment Family"))
                {
                    tx.Start();
                    var fm = famDoc.FamilyManager;
                    bool already = fm.get_Parameter("STING_SYMBOL_ID") != null;
                    r.AlreadyAugmented = already;
                    if (!already)
                    {
                        AddTextParam(fm, "STING_SYMBOL_ID");
                        AddTextParam(fm, "STING_SYMBOL_STANDARD");
                        AddTextParam(fm, "STING_HOST_ELEMENT_ID");
                    }
                    // Default value of conceptId on each type if param empty.
                    var idParam = fm.get_Parameter("STING_SYMBOL_ID");
                    if (idParam != null && fm.Types != null)
                    {
                        foreach (FamilyType t in fm.Types)
                        {
                            try
                            {
                                fm.CurrentType = t;
                                if (string.IsNullOrEmpty(fm.CurrentType?.AsString(idParam)))
                                    fm.Set(idParam, r.ConceptId);
                            }
                            catch (Exception ex2) { StingTools.Core.StingLog.Warn($"AugmentFamily set type: {ex2.Message}"); }
                        }
                    }
                    tx.Commit();
                }

                famDoc.LoadFamily(doc, new ReuseLoadOptions());
                r.Success = true;
            }
            catch (Exception ex2)
            {
                r.Warning = ex2.Message;
                StingTools.Core.StingLog.Warn($"AugmentFamily {fam.Name}: {ex2.Message}");
            }
            finally
            {
                try { famDoc?.Close(false); } catch (Exception ex2) { StingTools.Core.StingLog.Warn($"AugmentFamily close: {ex2.Message}"); }
            }
            return r;
        }

        public static bool RollbackAugmentation(Document doc, Family fam)
        {
            if (doc == null || fam == null) return false;
            Document famDoc = null;
            try
            {
                famDoc = doc.EditFamily(fam);
                if (famDoc == null) return false;
                using (var tx = new Transaction(famDoc, "STING Rollback Augment"))
                {
                    tx.Start();
                    var fm = famDoc.FamilyManager;
                    foreach (var name in new[] { "STING_SYMBOL_ID", "STING_SYMBOL_STANDARD", "STING_HOST_ELEMENT_ID" })
                    {
                        try
                        {
                            var p = fm.get_Parameter(name);
                            if (p != null) fm.RemoveParameter(p);
                        }
                        catch (Exception ex) { StingTools.Core.StingLog.Warn($"RollbackAugmentation rm {name}: {ex.Message}"); }
                    }
                    tx.Commit();
                }
                famDoc.LoadFamily(doc, new ReuseLoadOptions());
                return true;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"RollbackAugmentation {fam.Name}: {ex.Message}");
                return false;
            }
            finally
            {
                try { famDoc?.Close(false); } catch (Exception ex2) { StingTools.Core.StingLog.Warn($"Rollback close: {ex2.Message}"); }
            }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static void AddTextParam(FamilyManager fm, string name)
        {
            try
            {
                if (fm.get_Parameter(name) != null) return;
                fm.AddParameter(name, GroupTypeId.IdentityData, SpecTypeId.String.Text, isInstance: true);
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AddTextParam {name}: {ex.Message}");
            }
        }

        private sealed class ReuseLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            { overwriteParameterValues = true; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family; overwriteParameterValues = true; return true;
            }
        }
    }
}
