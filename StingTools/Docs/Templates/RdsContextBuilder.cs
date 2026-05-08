// Healthcare Pack H-8 — Room Data Sheet token-context builder.
//
// Reads a Room element + ProjectInformation, applies HEALTHCARE_RDS_FIELDMAP.json
// to produce a flat dictionary that MiniWordAdapter feeds into the .docx template.

using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace StingTools.Docs.Templates
{
    public static class RdsContextBuilder
    {
        public static Dictionary<string, object> Build(Document doc, Element room)
        {
            var ctx = new Dictionary<string, object>();
            if (doc == null || room == null) return ctx;

            // Load token map.
            var mapPath = Path.Combine(StingToolsApp.DataPath, "HEALTHCARE_RDS_FIELDMAP.json");
            if (!File.Exists(mapPath))
            {
                StingLog.Warn("HEALTHCARE_RDS_FIELDMAP.json not found");
                return ctx;
            }
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(mapPath)); }
            catch (Exception ex) { StingLog.Error("RDS field map parse failed", ex); return ctx; }

            var tokens = root["tokens"] as JObject;
            if (tokens == null) return ctx;

            var prjInfo = doc.ProjectInformation;
            foreach (var kv in tokens)
            {
                var token = kv.Key;
                var paramName = kv.Value?.ToString();
                if (string.IsNullOrEmpty(paramName)) continue;
                Element src = token.StartsWith("prj.") ? (Element)prjInfo : room;
                ctx[token] = ReadParam(src, paramName) ?? "";
            }

            // Loop containers — empty by default; fillers (commands) populate them.
            ctx["services"]   = new List<Dictionary<string, object>>();
            ctx["equipment"]  = new List<Dictionary<string, object>>();
            ctx["finishes"]   = new List<Dictionary<string, object>>();
            ctx["signatures"] = new List<Dictionary<string, object>>();

            ctx["doc.generated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
            ctx["doc.generator"]    = "STING Healthcare Pack RDS engine";
            return ctx;
        }

        private static string ReadParam(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || !p.HasValue) return null;
                if (p.StorageType == StorageType.String)  return p.AsString();
                if (p.StorageType == StorageType.Double)  return p.AsValueString();
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                return p.AsValueString();
            }
            catch { return null; }
        }
    }
}
