// Healthcare Pack H-8 — Room Data Sheet renderer.
// Façade over MiniWordAdapter: writes one .docx per room into
// <project>/_BIM_COORD/healthcare/rds/<roomNum>.docx

using Autodesk.Revit.DB;
using StingTools.Core;
using System;
using System.IO;

namespace Planscape.Docs.Templates
{
    public static class RdsRenderer
    {
        /// <summary>Render a single Room into a per-room .docx. Returns the file path.</summary>
        public static string Render(Document doc, Element room, string templatePath = null)
        {
            if (doc == null || room == null) return null;

            var docPath = doc.PathName;
            var root = string.IsNullOrEmpty(docPath) ? Path.GetTempPath() : Path.GetDirectoryName(docPath);
            var outDir = Path.Combine(root ?? Path.GetTempPath(), "_BIM_COORD", "healthcare", "rds");
            Directory.CreateDirectory(outDir);

            var roomNum = ReadString(room, "ASS_ROOM_NUM_TXT")
                          ?? ReadString(room, "Number")
                          ?? room.Id.IntegerValue.ToString();
            var safe = SafeName(roomNum);
            var outPath = Path.Combine(outDir, $"{safe}_RDS.docx");

            var tpl = templatePath
                      ?? Path.Combine(StingToolsApp.DataPath, "_template_sources", "healthcare_rds.docx");
            if (!File.Exists(tpl))
            {
                StingLog.Warn($"RDS template missing at {tpl} — RDS not rendered");
                return null;
            }

            var ctx = RdsContextBuilder.Build(doc, room);
            try
            {
                MiniWordAdapter.Render(tpl, ctx, outPath);
                StingLog.Info($"RDS rendered: {outPath}");
                return outPath;
            }
            catch (Exception ex)
            {
                StingLog.Error($"RDS render failed for room {roomNum}", ex);
                return null;
            }
        }

        private static string ReadString(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return null;
                if (p.StorageType == StorageType.String) return p.AsString();
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                return p.AsValueString();
            } catch { return null; }
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "room";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
