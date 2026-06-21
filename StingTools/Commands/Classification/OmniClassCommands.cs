// ══════════════════════════════════════════════════════════════════════════
//  OmniClassCommands.cs — Phase 198 (KUT dual classification).
//
//  OmniClass is the element/product axis that rides alongside MasterFormat
//  (work results) on the BOQ. CSI_Assign stamps the MasterFormat section
//  (CSI_SECTION_TXT); this is its OmniClass twin — a rule-driven assigner that
//  resolves each element to an OmniClass code + title from a map CSV and writes
//  ASS_OMNICLASS_TXT (+ the ArchiCAD-compatible CLS_OMNICLASS_TITLE_TXT when
//  bound), so the BOQ's OmniClass column fills without hand-authoring every
//  family or relying on an ArchiCAD-IFC import.
//
//  The map ships OmniClass Table 21 (Elements) codes — the elemental breakdown
//  that complements MasterFormat work results. Same CSV grammar as the CSI map
//  (Category, FamilyRegex, TypeRegex, Sys, Section, Title) so the shared
//  CsiMasterFormat parser + resolver are reused; the "Section" column carries
//  the OmniClass number. Corporate baseline at Data/STING_OMNICLASS_MAP.csv;
//  project overlay at <project>/_BIM_COORD/omniclass_map.csv (loaded first so it
//  wins ties).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Classification;

namespace StingTools.Commands.Classification
{
    internal static class OmniClassMap
    {
        /// <summary>Load the rules for the active OmniClass table: corporate
        /// STING_OMNICLASS_&lt;table&gt;_MAP.csv + the table-agnostic project overlay
        /// _BIM_COORD/omniclass_map.csv (loaded first so it wins ties).</summary>
        public static List<CsiRule> Load(Document doc, OmniClassTableInfo table, out int corp, out int overlay)
        {
            corp = 0; overlay = 0;
            var rules = new List<CsiRule>();
            // Project overlay first so it wins ties (Resolve takes the earliest on a tie).
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, "_BIM_COORD", "omniclass_map.csv");
                    if (File.Exists(p))
                    {
                        var r = CsiMasterFormat.ParseCsvLines(File.ReadAllLines(p));
                        overlay = r.Count; rules.AddRange(r);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"OmniClass overlay load: {ex.Message}"); }

            try
            {
                string c = StingToolsApp.FindDataFile(table.MapFile);
                if (!string.IsNullOrEmpty(c) && File.Exists(c))
                {
                    var r = CsiMasterFormat.ParseCsvLines(File.ReadAllLines(c));
                    corp = r.Count; rules.AddRange(r);
                }
            }
            catch (Exception ex) { StingLog.Warn($"OmniClass corporate load ({table.MapFile}): {ex.Message}"); }

            return rules;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OmniClassAssignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Phase 199 — the active OmniClass table (default 21 Elements; switchable
            // to 13 Spaces / 23 Products via classification_policy.json).
            var table = OmniClassTables.Resolve(ClassificationReader.OmniClassTable(doc));
            var rules = OmniClassMap.Load(doc, table, out int corp, out int overlay);
            if (rules.Count == 0)
            {
                TaskDialog.Show("OmniClass Assign", $"No OmniClass map found for {table.Label}. Ship " +
                    $"{table.MapFile} in data/ or add _BIM_COORD/omniclass_map.csv.");
                return Result.Succeeded;
            }

            var picker = new TaskDialog("OmniClass Assign")
            {
                MainInstruction = $"Write OmniClass {table.Label} code to elements",
                MainContent = $"{rules.Count} rules ({corp} corporate + {overlay} project). " +
                    (table.IsSpatial ? "Spatial table — elements inherit their host room's space code. " : "") +
                    "Choose write mode:",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                AllowCancellation = true
            };
            picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fill empty only", "Only write where ASS_OMNICLASS_TXT is blank");
            picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Overwrite all", "Re-resolve and overwrite existing values");
            var choice = picker.Show();
            if (choice == TaskDialogResult.Cancel) return Result.Cancelled;
            bool overwrite = choice == TaskDialogResult.CommandLink2;

            var scope = CsiMap.Scope(ctx.UIDoc, doc, out string scopeLabel);
            int assigned = 0, skippedSet = 0, unresolved = 0;
            var unmappedCats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (var t = new Transaction(doc, "STING OmniClass Assign"))
            {
                t.Start();
                foreach (var el in scope)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    CsiRule rule;
                    string unmappedKey;
                    if (table.IsSpatial)
                    {
                        // Table 13 (Spaces) — classify the element's HOST ROOM by function.
                        // Feed the room name as the match input; the spatial map keys its
                        // FamilyRegex against it (Category "*").
                        var room = ParameterHelpers.GetRoomAtElement(doc, el);
                        string roomName = room?.Name ?? "";
                        unmappedKey = string.IsNullOrEmpty(roomName) ? "(no room)" : roomName;
                        rule = string.IsNullOrEmpty(roomName) ? null
                            : CsiMasterFormat.Resolve(rules, "*", roomName, roomName, "");
                    }
                    else
                    {
                        string fam = ParameterHelpers.GetFamilyName(el);
                        string type = ParameterHelpers.GetFamilySymbolName(el);
                        string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                        unmappedKey = cat;
                        rule = CsiMasterFormat.Resolve(rules, cat, fam, type, sys);
                    }
                    if (rule == null)
                    {
                        unresolved++;
                        unmappedCats.TryGetValue(unmappedKey, out int c); unmappedCats[unmappedKey] = c + 1;
                        continue;
                    }
                    if (!overwrite && !string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.OMNICLASS)))
                    { skippedSet++; continue; }
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, el)) continue;

                    bool w1 = ParameterHelpers.SetString(el, ParamRegistry.OMNICLASS, rule.Section, overwrite: true);
                    // Title is best-effort — CLS_OMNICLASS_TITLE_TXT is the param the BOQ
                    // reads for the OmniClass title; SetString no-ops when it isn't bound.
                    if (!string.IsNullOrEmpty(rule.Title))
                        ParameterHelpers.SetString(el, "CLS_OMNICLASS_TITLE_TXT", rule.Title, overwrite: true);
                    if (w1) assigned++;
                }
                t.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"OmniClass {table.Label}");
            sb.AppendLine($"Scope: {scopeLabel}   Mode: {(overwrite ? "overwrite" : "fill empty")}");
            sb.AppendLine($"Assigned:        {assigned}");
            sb.AppendLine($"Skipped (set):   {skippedSet}");
            sb.AppendLine($"Unresolved:      {unresolved}");
            if (unmappedCats.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(table.IsSpatial
                    ? "Unmapped rooms (add rows to _BIM_COORD/omniclass_map.csv):"
                    : "Unmapped categories (add rows to _BIM_COORD/omniclass_map.csv):");
                foreach (var kv in unmappedCats.OrderByDescending(k => k.Value).Take(15))
                    sb.AppendLine($"   {kv.Value,5}  {kv.Key}");
            }
            new TaskDialog("OmniClass Assign")
            {
                MainInstruction = $"{assigned} element(s) assigned an OmniClass {table.Label} code",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"OmniClass_Assign ({table.Label}): {assigned} assigned, {unresolved} unresolved ({scopeLabel})");
            return Result.Succeeded;
        }
    }
}
