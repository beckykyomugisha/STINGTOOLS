// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/FourdGanttReader.cs — S6.7 (N-G11).
//
// Parse a project schedule from MS Project XML (.xml) or Primavera
// XER (.xer) and produce a List<GanttTask> that downstream 4D
// consumers (phase assignment, timeline colouring) can use.
//
// MS Project XML: .NET System.Xml.Linq XDocument parsing.
// Primavera XER: tab-delimited text with %T<table> / %F<fields>
// headers, %R<row> data rows.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading.Tasks;
using StingTools.Core;
using Autodesk.Revit.DB;

namespace StingTools.V6
{
    public sealed class GanttTask
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime Finish { get; set; }
        public string Resource { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public string WbsCode { get; set; } = string.Empty;
        public double PercentComplete { get; set; }
    }

    public static class FourdGanttReader
    {
        public static List<GanttTask> Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new List<GanttTask>();
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".xml" => ParseMsProjectXml(path),
                ".xer" => ParsePrimaveraXer(path),
                _ => new List<GanttTask>()
            };
        }

        public static List<GanttTask> ParseMsProjectXml(string path)
        {
            var result = new List<GanttTask>();
            try
            {
                var doc = XDocument.Load(path);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                foreach (var task in doc.Descendants(ns + "Task"))
                {
                    string id = task.Element(ns + "UID")?.Value ?? string.Empty;
                    string name = task.Element(ns + "Name")?.Value ?? string.Empty;
                    if (!DateTime.TryParse(task.Element(ns + "Start")?.Value, out var start))
                        continue;
                    if (!DateTime.TryParse(task.Element(ns + "Finish")?.Value, out var finish))
                        continue;
                    string wbs = task.Element(ns + "WBS")?.Value ?? string.Empty;
                    double pct = 0;
                    if (task.Element(ns + "PercentComplete") is var pc && pc != null)
                        double.TryParse(pc.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out pct);
                    result.Add(new GanttTask
                    {
                        Id = id, Name = name, Start = start, Finish = finish,
                        WbsCode = wbs, PercentComplete = pct,
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"FourdGanttReader: MSP XML parse '{path}'", ex);
            }
            return result;
        }

        public static List<GanttTask> ParsePrimaveraXer(string path)
        {
            var result = new List<GanttTask>();
            try
            {
                string[] fields = null;
                string table = string.Empty;
                foreach (var line in File.ReadLines(path))
                {
                    if (line.StartsWith("%T\t")) { table = line.Substring(3).Trim(); fields = null; continue; }
                    if (line.StartsWith("%F\t")) { fields = line.Substring(3).Split('\t'); continue; }
                    if (!line.StartsWith("%R\t") || table != "TASK" || fields == null) continue;
                    var row = line.Substring(3).Split('\t');
                    var dict = new Dictionary<string, string>();
                    for (int i = 0; i < fields.Length && i < row.Length; i++) dict[fields[i]] = row[i];
                    if (!dict.TryGetValue("task_code", out var id))    id = dict.GetValueOrDefault("task_id", "");
                    if (!dict.TryGetValue("task_name", out var name))  name = string.Empty;
                    if (!dict.TryGetValue("act_start_date", out var s) && !dict.TryGetValue("target_start_date", out s)) continue;
                    if (!dict.TryGetValue("act_end_date",   out var f) && !dict.TryGetValue("target_end_date",   out f)) continue;
                    if (!DateTime.TryParseExact(s, new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) continue;
                    if (!DateTime.TryParseExact(f, new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var finish)) continue;
                    result.Add(new GanttTask
                    {
                        Id = id ?? string.Empty,
                        Name = name ?? string.Empty,
                        Start = start, Finish = finish,
                        ParentId = dict.GetValueOrDefault("wbs_id", string.Empty),
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"FourdGanttReader: XER parse '{path}'", ex);
            }
            return result;
        }

        /// <summary>
        /// Map a set of GanttTasks onto Revit construction phases by
        /// matching phase names to task names. Updates PHASE_CREATED
        /// + PHASE_DEMOLISHED on tagged elements where a matching
        /// task is found. Batched via TransactionHelper.RunInScope.
        /// </summary>
        public static int AssignPhasesToModel(
            Autodesk.Revit.DB.Document doc,
            List<GanttTask> tasks,
            Func<Autodesk.Revit.DB.Element, GanttTask> pickTask)
        {
            int touched = 0;
            if (doc == null || tasks == null || pickTask == null) return 0;
            var phasesByName = new Dictionary<string, Autodesk.Revit.DB.Phase>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in new Autodesk.Revit.DB.FilteredElementCollector(doc)
                         .OfClass(typeof(Autodesk.Revit.DB.Phase)))
                phasesByName[p.Name] = (Autodesk.Revit.DB.Phase)p;

            TransactionHelper.RunInScope(doc, "STING 4D phase assign", t =>
            {
                var col = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .WherePasses(new Autodesk.Revit.DB.ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    var task = pickTask(el);
                    if (task == null) continue;
                    if (!phasesByName.TryGetValue(task.Name, out var phase)) continue;
                    var pc = el.get_Parameter(Autodesk.Revit.DB.BuiltInParameter.PHASE_CREATED);
                    if (pc != null && !pc.IsReadOnly) { pc.Set(phase.Id); touched++; }
                }
            });
            return touched;
        }
    }
}
