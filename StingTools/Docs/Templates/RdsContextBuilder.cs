// Healthcare Pack H-8 — Room Data Sheet token-context builder.
//
// Reads a Room element + ProjectInformation, applies HEALTHCARE_RDS_FIELDMAP.json
// to produce a TokenContext that MiniWordAdapter consumes:
//   room.* tokens land in the Doc bucket
//   prj.*  tokens land in the Project bucket
//   loop containers (services / equipment / finishes / signatures) land in Loops

using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace Planscape.Docs.Templates
{
    public static class RdsContextBuilder
    {
        public static TokenContext Build(Document doc, Element room)
        {
            var ctx = new TokenContext();
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
                var value = ReadParam(src, paramName) ?? "";

                // Strip the prefix and route to the right bucket.
                if (token.StartsWith("room."))      ctx.Doc[token.Substring(5)]     = value;
                else if (token.StartsWith("prj."))  ctx.Project[token.Substring(4)] = value;
                else                                ctx.Doc[token]                   = value;
            }

            // Loop containers — empty by default; downstream commands populate them.
            ctx.Loops["services"]   = new List<Dictionary<string, object>>();
            ctx.Loops["equipment"]  = new List<Dictionary<string, object>>();
            ctx.Loops["finishes"]   = new List<Dictionary<string, object>>();
            ctx.Loops["signatures"] = new List<Dictionary<string, object>>();

            ctx.Doc["generated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
            ctx.Doc["generator"]    = "STING Healthcare Pack RDS engine";
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }
    }
}
