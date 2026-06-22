// StingTools — MEP System Filter command (Phase D).
//
//   MEP_GenerateSystemFilters — turn the Phase A system-type definitions into
//   abbreviation-keyed AEC filters ("STING - Sys: …"), persist them to the project
//   override <project>/_BIM_COORD/aec_filters.json (so they become first-class —
//   visible to the AEC filter registry, View Style Packs, and Drawing Types), and
//   create the matching ParameterFilterElements in the document so they're usable
//   immediately. Idempotent (merge by id; FindOrCreate by name).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepGenerateSystemFiltersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var rules = MepSystemTypeRegistry.Get(doc);
                var defs = MepSystemFilterGenerator.Generate(rules);
                if (defs.Count == 0)
                {
                    TaskDialog.Show("STING MEP",
                        "No enabled system-type definitions with an abbreviation + colour to generate filters from. " +
                        "Check STING_MEP_SYSTEM_TYPES.json (or the project override).");
                    return Result.Cancelled;
                }

                // 1. Persist to the project AEC-filter override (so packs / drawing types see them).
                string persistPath = null; int persisted = 0; string persistNote = "";
                try { persisted = PersistToProject(doc, defs, out persistPath); }
                catch (Exception ex) { persistNote = ex.Message; StingLog.Warn($"MEP filter persist: {ex.Message}"); }
                AecFilterRegistry.Reload(doc);

                // 2. Create the ParameterFilterElements in the document.
                int created = 0, existing = 0, failed = 0;
                var rows = new List<string>();
                using (var t = new Transaction(doc, "STING Generate MEP System Filters"))
                {
                    t.Start();
                    foreach (var def in defs)
                    {
                        var fr = AecFilterFactory.FindOrCreate(doc, def);
                        if (fr == null || !fr.Ok) { failed++; rows.Add($"✖ {def.Name}  {fr?.Error}"); continue; }
                        if (fr.Created) created++; else existing++;
                        rows.Add($"{(fr.Created ? "✚" : "◉")} {def.Name}");
                    }
                    t.Commit();
                }

                var panel = StingResultPanel.Create("MEP — Generate System Filters");
                panel.SetSubtitle(
                    $"{defs.Count} abbreviation-keyed filters · {created} created · {existing} existing · {failed} failed · " +
                    (persistPath != null ? $"{persisted} written to project override" : "not persisted (unsaved project)"));

                panel.AddSection("SUMMARY")
                     .Metric("Generated", defs.Count.ToString())
                     .Metric("Created in model", created.ToString())
                     .Metric("Already existed", existing.ToString())
                     .Metric("Failed", failed.ToString())
                     .Metric("Persisted to override", persistPath != null ? persisted.ToString() : "—",
                             persistPath != null ? Path.GetFileName(persistPath) : persistNote);

                panel.AddSection("FILTERS");
                foreach (var r in rows.Take(60)) panel.Text(r);

                panel.AddSection("NEXT");
                panel.Text("These 'STING - Sys: …' filters key on ASS_MEP_SYS_NAME_TXT begins-with '<abbr>-' so they " +
                           "distinguish CHWF / LTHWF / CWF etc. — reference them from a View Style Pack, or run " +
                           "MEP_ApplyMepCoordination to colour the active view automatically.");
                panel.Show();

                StingLog.Info($"MEP system filters: generated={defs.Count} created={created} existing={existing} persisted={persisted}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepGenerateSystemFiltersCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Merge the generated definitions (by id) into
        /// <project>/_BIM_COORD/aec_filters.json. Returns the count written.
        /// </summary>
        private static int PersistToProject(Document doc, List<AecFilterDefinition> defs, out string path)
        {
            path = null;
            if (doc == null || string.IsNullOrEmpty(doc.PathName)) return 0; // unsaved — can't persist
            string dir = Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(dir)) return 0;
            string coord = Path.Combine(dir, "_BIM_COORD");
            Directory.CreateDirectory(coord);
            path = Path.Combine(coord, "aec_filters.json");

            AecFilterLibrary lib = null;
            if (File.Exists(path))
            {
                try { lib = JsonConvert.DeserializeObject<AecFilterLibrary>(File.ReadAllText(path)); }
                catch (Exception ex) { StingLog.Warn($"MEP filter persist: existing override unreadable, recreating — {ex.Message}"); }
            }
            lib ??= new AecFilterLibrary();
            lib.Filters ??= new List<AecFilterDefinition>();

            var byId = lib.Filters
                .Where(f => !string.IsNullOrEmpty(f.Id))
                .ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var d in defs) byId[d.Id] = d;
            lib.Filters = byId.Values.ToList();

            File.WriteAllText(path, JsonConvert.SerializeObject(lib, Formatting.Indented));
            return defs.Count;
        }
    }
}
