// WorkflowRegistry.cs — template engine v1.1 (S15).
//
// Loads every WorkflowDefinition JSON from _BIM_COORD/workflows/ and provides
// lookup by id. Extraction of embedded defaults is handled by
// EmbeddedTemplates.ExtractDefaultWorkflows (S11/S15).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace Planscape.Docs.Workflow
{
    public class WorkflowRegistry
    {
        public string WorkflowsDir { get; }
        public Dictionary<string, WorkflowDefinition> Workflows { get; } =
            new Dictionary<string, WorkflowDefinition>(StringComparer.OrdinalIgnoreCase);

        private WorkflowRegistry(string dir) { WorkflowsDir = dir; }

        public static WorkflowRegistry Load(Document doc)
        {
            string root = ResolveProjectRoot(doc);
            string dir  = Path.Combine(root, "_BIM_COORD", "workflows");
            Directory.CreateDirectory(dir);

            var reg = new WorkflowRegistry(dir);
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var wf = JsonConvert.DeserializeObject<WorkflowDefinition>(File.ReadAllText(file));
                    if (wf != null && !string.IsNullOrEmpty(wf.Id))
                        reg.Workflows[wf.Id] = wf;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"WorkflowRegistry: failed to load '{file}': {ex.Message}");
                }
            }
            return reg;
        }

        public WorkflowDefinition Get(string id)
            => Workflows.TryGetValue(id ?? "", out var wf) ? wf : null;

        public IEnumerable<WorkflowDefinition> All() => Workflows.Values;

        private static string ResolveProjectRoot(Document doc)
        {
            // Folder consolidation: nest "_BIM_COORD" inside the unified
            // project root's _data folder rather than as a sibling of the .rvt.
            try
            {
                string consolidated = StingTools.Core.ProjectFolderEngine.GetDataPath(doc);
                if (!string.IsNullOrEmpty(consolidated)) return consolidated;
            }
            catch { /* fall through to legacy lookup */ }
            try
            {
                string p = doc?.PathName;
                if (!string.IsNullOrEmpty(p))
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { /* ignored */ }
            return Path.Combine(Path.GetTempPath(), "Planscape", "BIMCoord");
        }
    }
}
