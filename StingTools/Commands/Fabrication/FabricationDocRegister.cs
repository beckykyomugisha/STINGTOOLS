// StingTools v4 MVP — link generated SP- sheets to the Document
// Register (#12). Invoked from the Fabrication Workspace dialog
// after Generate Package completes; each ViewSheet becomes a row in
// _BIM_COORD/document_register.json with suitability S0 and type SP
// (Shop Drawing) so it shows up automatically in the Document
// Management Center and any transmittal bundles.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.Commands.Fabrication
{
    public static class FabricationDocRegister
    {
        public static int PushSheets(Document doc, IEnumerable<ElementId> sheetIds)
        {
            if (doc == null) return 0;
            int added = 0;
            try
            {
                string path = BIMManagerEngine.GetBIMManagerFilePath(doc, "document_register.json");
                var arr = BIMManagerEngine.LoadJsonArray(path);

                // CreateDocumentEntry writes "doc_id" (not "id"); also
                // accept rows authored pre-rename just in case.
                var existingIds = new HashSet<string>(
                    arr.OfType<JObject>()
                       .Select(j => (string)(j["doc_id"] ?? j["id"]))
                       .Where(s => !string.IsNullOrEmpty(s)),
                    StringComparer.OrdinalIgnoreCase);

                var pi = doc.ProjectInformation;
                string project = pi?.Number ?? "PRJ";
                if (project.Length > 6) project = project.Substring(0, 6);

                foreach (var id in sheetIds)
                {
                    if (!(doc.GetElement(id) is ViewSheet vs)) continue;
                    string number = vs.SheetNumber ?? "";
                    string name   = vs.Name ?? "";
                    string docId = string.IsNullOrWhiteSpace(number)
                        ? BIMManagerEngine.GenerateDocumentName(project, "Z", "ZZ", "ZZ", "SP", "Z", "Zz_99",
                            (arr.Count + 1 + added).ToString("D4"))
                        : number;
                    if (existingIds.Contains(docId)) continue;

                    var entry = BIMManagerEngine.CreateDocumentEntry(
                        docId, name, "SP", "Z", "S0", "WIP", "OUT");
                    entry["revision"]       = Core.PhaseAutoDetect.DetectProjectRevision(doc) ?? "P01";
                    entry["sheet_id"]       = id.Value;
                    entry["sheet_number"]   = number;
                    entry["generated_by"]   = "FabricationEngine v4";
                    entry["generated_at"]   = DateTime.UtcNow.ToString("o");
                    arr.Add(entry);
                    added++;
                }

                if (added > 0) BIMManagerEngine.SaveJsonFile(path, arr);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationDocRegister.PushSheets: {ex.Message}");
            }
            return added;
        }
    }
}
