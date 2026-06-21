// ══════════════════════════════════════════════════════════════════════════
//  OmniClassCommands.cs — Phase 198/199 (KUT dual classification).
//
//  OmniClass is the element/product/material/space axis that rides alongside
//  MasterFormat (work results) on the BOQ. CSI_Assign stamps the MasterFormat
//  section; this is its OmniClass twin — a rule-driven assigner + audit that
//  resolves each element to an OmniClass code + title and writes ASS_OMNICLASS_TXT
//  (+ the ArchiCAD-compatible CLS_OMNICLASS_TITLE_TXT when bound).
//
//  Phase 199d (robustness/accuracy sweep):
//   • 3-TIER resolution — (1) an authored native OmniClass Number on the type
//     (Revit's built-in, Table 23) wins; (2) the map heuristic; the audit then
//     measures the residual so a project closes it deliberately.
//   • Per-map "# matchOn: element|room|material" directive (defaults: 13/14→room,
//     41→material, else element) decides what each element is matched on:
//     the element's own category/family/type/sys, its host ROOM name, or its
//     MATERIAL name (with type-name fallback).
//   • Switch-table hygiene — a stamped code from a DIFFERENT table is treated as
//     empty in "fill empty" mode, so switching tables re-classifies cleanly.
//   • Code validation — rows whose Section doesn't start with the active table
//     number are warned (typo / wrong-table overlay row) and skipped.
//   • OmniClass_Audit — read-only dry-run: % classified, unmapped keys, and
//     AMBIGUOUS elements (≥2 rules tie at the top score) → CSV + summary.
//
//  Corporate maps STING_OMNICLASS_<table>_MAP.csv; project overlay
//  <project>/_BIM_COORD/omniclass_map.csv (loaded first so it wins ties).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Classification;

namespace StingTools.Commands.Classification
{
    internal static class OmniClassMap
    {
        private static readonly Regex MatchOnRx =
            new Regex(@"^\s*#\s*matchOn\s*:\s*(element|room|material)\b", RegexOptions.IgnoreCase);

        /// <summary>Load the rules for the active OmniClass table: corporate
        /// STING_OMNICLASS_&lt;table&gt;_MAP.csv + the table-agnostic project overlay
        /// _BIM_COORD/omniclass_map.csv (loaded first so it wins ties). Also extracts
        /// the optional "# matchOn:" directive (overlay wins, else corporate, else null).</summary>
        public static List<CsiRule> Load(Document doc, OmniClassTableInfo table, out int corp, out int overlay, out string matchOn)
        {
            corp = 0; overlay = 0; matchOn = null;
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
                        var lines = File.ReadAllLines(p);
                        var r = CsiMasterFormat.ParseCsvLines(lines);
                        overlay = r.Count; rules.AddRange(r);
                        matchOn = ReadMatchOn(lines) ?? matchOn;   // overlay directive wins
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"OmniClass overlay load: {ex.Message}"); }

            try
            {
                string c = StingToolsApp.FindDataFile(table.MapFile);
                if (!string.IsNullOrEmpty(c) && File.Exists(c))
                {
                    var lines = File.ReadAllLines(c);
                    var r = CsiMasterFormat.ParseCsvLines(lines);
                    corp = r.Count; rules.AddRange(r);
                    if (matchOn == null) matchOn = ReadMatchOn(lines);   // corporate directive as fallback
                }
            }
            catch (Exception ex) { StingLog.Warn($"OmniClass corporate load ({table.MapFile}): {ex.Message}"); }

            return rules;
        }

        private static string ReadMatchOn(IEnumerable<string> lines)
        {
            foreach (var l in lines ?? Enumerable.Empty<string>())
            {
                var m = MatchOnRx.Match(l ?? "");
                if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
            }
            return null;
        }

        /// <summary>The match mode in effect: map directive &gt; table default &gt; "element".</summary>
        public static string EffectiveMatchMode(OmniClassTableInfo table, string directive)
            => !string.IsNullOrEmpty(directive) ? directive
             : !string.IsNullOrEmpty(table.MatchMode) ? table.MatchMode : "element";

        /// <summary>Warn (log) on shipped/overlay rows whose Section doesn't belong to the
        /// active table (typo or wrong-table overlay row). Returns the count of bad rows.</summary>
        public static int CountMisfiledRows(IReadOnlyList<CsiRule> rules, string tableNumber)
        {
            int bad = 0;
            string prefix = tableNumber + "-";
            foreach (var r in rules)
            {
                string s = (r.Section ?? "").Trim();
                if (s.Length == 0) continue;
                if (!s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    bad++;
                    StingLog.Warn($"OmniClass map: code '{s}' ({r.Category}) is not a Table {tableNumber} code — check the active table / overlay.");
                }
            }
            return bad;
        }

        /// <summary>Normalise an OmniClass code to canonical single-spaced form so a
        /// hand-typed "23-17  11 00" can't create a phantom distinct BOQ group.</summary>
        public static string Normalize(string code) =>
            string.IsNullOrWhiteSpace(code) ? "" : Regex.Replace(code.Trim(), "\\s+", " ");

        /// <summary>Tier-1 — the element type's authored native OmniClass Number (Revit's
        /// built-in param IS Table 23). Returned only when it matches the active table.</summary>
        public static string AuthoredNative(Document doc, Element el, string tableNumber)
        {
            if (tableNumber != "23") return null;   // the built-in param is Table 23 only
            try
            {
                Element type = doc.GetElement(el.GetTypeId());
                Parameter p = type?.get_Parameter(BuiltInParameter.OMNICLASS_CODE)
                              ?? el.get_Parameter(BuiltInParameter.OMNICLASS_CODE);
                string v = p?.AsString();
                if (!string.IsNullOrWhiteSpace(v) && v.Trim().StartsWith("23-")) return Normalize(v);
            }
            catch { }
            return null;
        }

        /// <summary>The element's primary material name (structural material first, then the
        /// first compound-structure material), for material-axis classification. Empty when
        /// the element carries no material — the caller falls back to the type name.</summary>
        public static string PrimaryMaterialName(Document doc, Element el)
        {
            try
            {
                ElementId mid = ElementId.InvalidElementId;
                var sp = el.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                if (sp != null && sp.StorageType == StorageType.ElementId) mid = sp.AsElementId();
                if (mid == null || mid == ElementId.InvalidElementId)
                {
                    var ids = el.GetMaterialIds(false);
                    if (ids != null && ids.Count > 0) mid = ids.First();
                }
                if (mid != null && mid != ElementId.InvalidElementId)
                    return (doc.GetElement(mid) as Material)?.Name ?? "";
            }
            catch { }
            return "";
        }
    }

    // ── result of resolving one element (shared by Assign + Audit) ──────────
    internal sealed class OmniResolveResult
    {
        public string Code;        // null ⇒ unresolved
        public string Title;
        public string Source;      // "native" | "map"
        public string UnmappedKey; // category / room / material — for the unmapped report
        public int TieCount;       // >1 ⇒ ambiguous (map only)
    }

    internal static class OmniResolver
    {
        /// <summary>Resolve one element for the active table + match mode. Tier-1 native first
        /// (table 23), then the map heuristic on element / room / material input.</summary>
        public static OmniResolveResult Resolve(Document doc, Element el, OmniClassTableInfo table,
            string matchMode, IReadOnlyList<CsiRule> rules)
        {
            var res = new OmniResolveResult();

            // Tier-1 — authored native code wins (author-on-type → 100%).
            string native = OmniClassMap.AuthoredNative(doc, el, table.Number);
            if (native != null) { res.Code = native; res.Title = ""; res.Source = "native"; res.UnmappedKey = ""; return res; }

            string cat = ParameterHelpers.GetCategoryName(el);
            CsiRule rule = null; int tie = 0;
            if (string.Equals(matchMode, "room", StringComparison.OrdinalIgnoreCase))
            {
                // Classify the host ROOM by name (fed into family+type; the real category
                // is passed too so a future category-specific spatial row could match).
                var room = ParameterHelpers.GetRoomAtElement(doc, el);
                string roomName = room?.Name ?? "";
                res.UnmappedKey = string.IsNullOrEmpty(roomName) ? "(no room)" : roomName;
                if (!string.IsNullOrEmpty(roomName))
                    rule = CsiMasterFormat.Resolve(rules, cat, roomName, roomName, "", out _, out tie);
            }
            else if (string.Equals(matchMode, "material", StringComparison.OrdinalIgnoreCase))
            {
                // Prefer the element's actual MATERIAL name; fall back to the type name
                // (legacy keyword path). The real category is passed so the map's
                // near-certain category fallbacks (rebar→steel, curtain panel→glass) fire.
                string mat = OmniClassMap.PrimaryMaterialName(doc, el);
                string typeName = ParameterHelpers.GetFamilySymbolName(el);
                string input = !string.IsNullOrEmpty(mat) ? mat : typeName;
                res.UnmappedKey = !string.IsNullOrEmpty(mat) ? mat
                    : (string.IsNullOrEmpty(typeName) ? "(no material)" : "type:" + typeName);
                if (!string.IsNullOrEmpty(input))
                    rule = CsiMasterFormat.Resolve(rules, cat, input, input, "", out _, out tie);
                else  // no name at all → category fallback still has a chance
                    rule = CsiMasterFormat.Resolve(rules, cat, "", "", "", out _, out tie);
            }
            else // element
            {
                string fam = ParameterHelpers.GetFamilyName(el);
                string type = ParameterHelpers.GetFamilySymbolName(el);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                res.UnmappedKey = cat;
                rule = CsiMasterFormat.Resolve(rules, cat, fam, type, sys, out _, out tie);
            }

            if (rule != null) { res.Code = OmniClassMap.Normalize(rule.Section); res.Title = rule.Title; res.Source = "map"; res.TieCount = tie; }
            return res;
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

            var table = OmniClassTables.Resolve(ClassificationReader.OmniClassTable(doc));
            var rules = OmniClassMap.Load(doc, table, out int corp, out int overlay, out string directive);
            if (rules.Count == 0)
            {
                TaskDialog.Show("OmniClass Assign", $"No OmniClass map found for {table.Label}. Ship " +
                    $"{table.MapFile} in data/ or add _BIM_COORD/omniclass_map.csv.");
                return Result.Succeeded;
            }
            string mode = OmniClassMap.EffectiveMatchMode(table, directive);
            int misfiled = OmniClassMap.CountMisfiledRows(rules, table.Number);

            var picker = new TaskDialog("OmniClass Assign")
            {
                MainInstruction = $"Write OmniClass {table.Label} code to elements",
                MainContent = $"{rules.Count} rules ({corp} corporate + {overlay} project). Match on: {mode}. " +
                    (misfiled > 0 ? $"⚠ {misfiled} map row(s) are not Table {table.Number} codes (see log). " : "") +
                    "Choose write mode:",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                AllowCancellation = true
            };
            picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fill empty only",
                "Write where ASS_OMNICLASS_TXT is blank OR holds a code from a different table");
            picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Overwrite all", "Re-resolve and overwrite existing values");
            var choice = picker.Show();
            if (choice == TaskDialogResult.Cancel) return Result.Cancelled;
            bool overwrite = choice == TaskDialogResult.CommandLink2;

            var scope = CsiMap.Scope(ctx.UIDoc, doc, out string scopeLabel);
            int assigned = 0, skippedSet = 0, unresolved = 0, native = 0, restamped = 0;
            var unmapped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (var t = new Transaction(doc, "STING OmniClass Assign"))
            {
                t.Start();
                foreach (var el in scope)
                {
                    var r = OmniResolver.Resolve(doc, el, table, mode, rules);
                    if (r.Code == null)
                    {
                        unresolved++;
                        unmapped.TryGetValue(r.UnmappedKey, out int c); unmapped[r.UnmappedKey] = c + 1;
                        continue;
                    }
                    // Switch-table hygiene: a stamped code from a DIFFERENT table counts as
                    // empty in fill-mode, so switching tables re-classifies it instead of leaving
                    // a mixed-table column.
                    string existing = ParameterHelpers.GetString(el, ParamRegistry.OMNICLASS);
                    bool hasSet = !string.IsNullOrEmpty(existing);
                    bool otherTable = hasSet && !string.Equals(OmniClassTables.TableOf(existing), table.Number, StringComparison.OrdinalIgnoreCase);
                    if (!overwrite && hasSet && !otherTable) { skippedSet++; continue; }
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, el)) continue;
                    if (otherTable) restamped++;

                    bool w1 = ParameterHelpers.SetString(el, ParamRegistry.OMNICLASS, r.Code, overwrite: true);
                    if (!string.IsNullOrEmpty(r.Title))
                        ParameterHelpers.SetString(el, "CLS_OMNICLASS_TITLE_TXT", r.Title, overwrite: true);
                    if (w1) { assigned++; if (r.Source == "native") native++; }
                }
                t.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"OmniClass {table.Label}   (match on: {mode})");
            sb.AppendLine($"Scope: {scopeLabel}   Mode: {(overwrite ? "overwrite" : "fill empty")}");
            sb.AppendLine($"Assigned:        {assigned}   (of which native-authored: {native})");
            sb.AppendLine($"Re-stamped from another table: {restamped}");
            sb.AppendLine($"Skipped (set):   {skippedSet}");
            sb.AppendLine($"Unresolved:      {unresolved}");
            if (unmapped.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(mode == "room" ? "Unmapped rooms (add rows to _BIM_COORD/omniclass_map.csv):"
                    : mode == "material" ? "Unmapped materials (add rows to _BIM_COORD/omniclass_map.csv):"
                    : "Unmapped categories (add rows to _BIM_COORD/omniclass_map.csv):");
                foreach (var kv in unmapped.OrderByDescending(k => k.Value).Take(15))
                    sb.AppendLine($"   {kv.Value,5}  {kv.Key}");
            }
            new TaskDialog("OmniClass Assign")
            {
                MainInstruction = $"{assigned} element(s) assigned an OmniClass {table.Label} code",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"OmniClass_Assign ({table.Label}, {mode}): {assigned} assigned ({native} native), {unresolved} unresolved ({scopeLabel})");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class OmniClassAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var table = OmniClassTables.Resolve(ClassificationReader.OmniClassTable(doc));
            var rules = OmniClassMap.Load(doc, table, out int corp, out int overlay, out string directive);
            if (rules.Count == 0)
            {
                TaskDialog.Show("OmniClass Audit", $"No OmniClass map found for {table.Label}.");
                return Result.Succeeded;
            }
            string mode = OmniClassMap.EffectiveMatchMode(table, directive);
            int misfiled = OmniClassMap.CountMisfiledRows(rules, table.Number);

            var scope = CsiMap.Scope(ctx.UIDoc, doc, out string scopeLabel);
            int total = 0, classified = 0, nativeCount = 0, ambiguous = 0;
            var unmapped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ambig = new List<string>();

            foreach (var el in scope)
            {
                total++;
                var r = OmniResolver.Resolve(doc, el, table, mode, rules);
                if (r.Code == null) { unmapped.TryGetValue(r.UnmappedKey, out int c); unmapped[r.UnmappedKey] = c + 1; continue; }
                classified++;
                if (r.Source == "native") nativeCount++;
                else if (r.TieCount > 1) { ambiguous++; if (ambig.Count < 200) ambig.Add($"{el.Id.Value}\t{ParameterHelpers.GetCategoryName(el)}\t{r.UnmappedKey}\t{r.Code}\ttied={r.TieCount}"); }
            }

            double pct = total == 0 ? 0 : 100.0 * classified / total;
            string csv = null;
            try
            {
                string dir = OutputLocationHelper.GetOutputDirectory(doc);
                csv = Path.Combine(dir, $"omniclass_audit_T{table.Number}.csv");
                var lines = new List<string> { "Section,Kind,Count" };
                lines.Add($",Total,{total}");
                lines.Add($",Classified,{classified}");
                lines.Add($",Native,{nativeCount}");
                lines.Add($",Ambiguous,{ambiguous}");
                lines.Add($",Misfiled rows,{misfiled}");
                lines.Add("");
                lines.Add("UNMAPPED,Key,Count");
                foreach (var kv in unmapped.OrderByDescending(k => k.Value)) lines.Add($"unmapped,\"{kv.Key}\",{kv.Value}");
                lines.Add("");
                lines.Add("AMBIGUOUS,ElementId\tCategory\tKey\tCode\tTie");
                lines.AddRange(ambig.Select(a => "ambiguous," + a.Replace(",", " ")));
                File.WriteAllLines(csv, lines);
            }
            catch (Exception ex) { StingLog.Warn($"OmniClass audit CSV: {ex.Message}"); csv = null; }

            var sb = new StringBuilder();
            sb.AppendLine($"OmniClass {table.Label}   (match on: {mode})");
            sb.AppendLine($"Scope: {scopeLabel}");
            sb.AppendLine($"Classified:  {classified} / {total}   ({pct:F1} %)");
            sb.AppendLine($"  native-authored: {nativeCount}   map: {classified - nativeCount}");
            sb.AppendLine($"Unmapped:    {total - classified}");
            sb.AppendLine($"Ambiguous (≥2 rules tie — needs a more-specific rule): {ambiguous}");
            if (misfiled > 0) sb.AppendLine($"⚠ Map rows not in Table {table.Number}: {misfiled} (see log)");
            if (unmapped.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Top unmapped keys:");
                foreach (var kv in unmapped.OrderByDescending(k => k.Value).Take(12))
                    sb.AppendLine($"   {kv.Value,5}  {kv.Key}");
            }
            if (csv != null) sb.AppendLine($"\nFull report: {csv}");

            new TaskDialog("OmniClass Audit")
            {
                MainInstruction = $"{pct:F0}% of {total} element(s) classified for {table.Label}",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"OmniClass_Audit ({table.Label}): {classified}/{total} classified, {ambiguous} ambiguous ({scopeLabel})");
            return Result.Succeeded;
        }
    }
}
