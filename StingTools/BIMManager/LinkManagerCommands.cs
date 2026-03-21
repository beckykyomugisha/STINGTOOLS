using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  G4: STING Link Manager Commands
    //
    //  Manage Revit linked models: audit, reload, path validation, element count,
    //  naming compliance, and link health dashboard.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: LinkManagerEngine ──

    internal static class LinkManagerEngine
    {
        /// <summary>Audit all linked models in the document.</summary>
        internal static LinkAuditResult AuditLinks(Document doc)
        {
            var result = new LinkAuditResult();

            // Revit links
            var linkTypes = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>().ToList();
            foreach (var lt in linkTypes)
            {
                var info = new LinkInfo
                {
                    Name = lt.Name,
                    LinkType = "Revit",
                    TypeId = lt.Id
                };

                try
                {
                    var extRef = lt.GetExternalFileReference();
                    if (extRef != null && extRef.GetLinkedFileStatus() != LinkedFileStatus.Invalid)
                    {
                        info.Status = extRef.GetLinkedFileStatus().ToString();
                        var modelPath = extRef.GetAbsolutePath();
                        info.Path = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                        info.IsLoaded = extRef.GetLinkedFileStatus() == LinkedFileStatus.Loaded;
                        if (!string.IsNullOrEmpty(info.Path))
                            info.FileExists = File.Exists(info.Path);
                    }
                    else
                    {
                        info.Status = "Unknown";
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"LinkAudit type '{lt.Name}': {ex.Message}");
                    info.Status = "Error";
                }

                // Count instances
                info.InstanceCount = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>().Count(i => i.GetTypeId() == lt.Id);

                // Check naming compliance (ISO 19650)
                info.NamingCompliant = ValidateISO19650FileName(lt.Name);

                result.RevitLinks.Add(info);
            }

            // CAD links
            var cadLinks = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
            foreach (var cad in cadLinks)
            {
                var cadInfo = new LinkInfo
                {
                    Name = cad.Name ?? "(unnamed CAD)",
                    LinkType = cad.IsLinked ? "CAD Link" : "CAD Import",
                    TypeId = cad.Id,
                    Status = cad.IsLinked ? "Linked" : "Imported",
                    IsLoaded = true,
                    InstanceCount = 1
                };
                result.CADLinks.Add(cadInfo);
            }

            // Calculate summary
            result.TotalRevitLinks = result.RevitLinks.Count;
            result.TotalCADLinks = result.CADLinks.Count;
            result.LoadedLinks = result.RevitLinks.Count(l => l.IsLoaded);
            result.BrokenLinks = result.RevitLinks.Count(l => !l.FileExists && !string.IsNullOrEmpty(l.Path));
            result.NonCompliantNames = result.RevitLinks.Count(l => !l.NamingCompliant);

            return result;
        }

        /// <summary>Check if filename follows ISO 19650 naming convention.</summary>
        internal static bool ValidateISO19650FileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            // ISO 19650 naming: fields separated by hyphens, minimum 4 fields
            // Pattern: Project-Originator-Volume-Type-Discipline-Number
            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            var parts = nameOnly.Split('-');
            return parts.Length >= 4;
        }

        /// <summary>Get detailed element statistics from a linked model.</summary>
        internal static Dictionary<string, int> GetLinkedModelStats(RevitLinkInstance linkInstance)
        {
            var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var linkedDoc = linkInstance.GetLinkDocument();
                if (linkedDoc == null) return stats;

                var elements = new FilteredElementCollector(linkedDoc).WhereElementIsNotElementType();
                foreach (var el in elements)
                {
                    string cat = el.Category?.Name ?? "(no category)";
                    stats[cat] = stats.GetValueOrDefault(cat) + 1;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetLinkedModelStats: {ex.Message}"); }
            return stats;
        }
    }

    // ── Data types ──

    internal class LinkAuditResult
    {
        public List<LinkInfo> RevitLinks { get; set; } = new List<LinkInfo>();
        public List<LinkInfo> CADLinks { get; set; } = new List<LinkInfo>();
        public int TotalRevitLinks { get; set; }
        public int TotalCADLinks { get; set; }
        public int LoadedLinks { get; set; }
        public int BrokenLinks { get; set; }
        public int NonCompliantNames { get; set; }
    }

    internal class LinkInfo
    {
        public string Name { get; set; } = "";
        public string LinkType { get; set; } = "";
        public ElementId TypeId { get; set; } = ElementId.InvalidElementId;
        public string Path { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsLoaded { get; set; }
        public bool FileExists { get; set; } = true;
        public bool NamingCompliant { get; set; }
        public int InstanceCount { get; set; }
    }

    #endregion

    #region ── Commands ──

    /// <summary>
    /// Audit all linked models: status, path, naming compliance, element counts.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LinkAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var result = LinkManagerEngine.AuditLinks(doc);

            if (result.TotalRevitLinks == 0 && result.TotalCADLinks == 0)
            {
                TaskDialog.Show("Link Audit", "No linked models found in this project.");
                return Result.Succeeded;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Link Audit — {result.TotalRevitLinks} Revit + {result.TotalCADLinks} CAD links\n");

            if (result.BrokenLinks > 0)
                sb.AppendLine($"⚠ {result.BrokenLinks} broken link(s) detected!\n");

            sb.AppendLine("── Revit Links ──");
            foreach (var link in result.RevitLinks)
            {
                string status = link.IsLoaded ? "Loaded" : link.FileExists ? "Unloaded" : "BROKEN";
                string iso = link.NamingCompliant ? "" : " [Non-ISO]";
                sb.AppendLine($"  [{status}] {link.Name}{iso}");
                sb.AppendLine($"         {link.InstanceCount} instance(s), Path: {(string.IsNullOrEmpty(link.Path) ? "(no path)" : link.Path)}");
            }

            if (result.CADLinks.Count > 0)
            {
                sb.AppendLine("\n── CAD Links/Imports ──");
                foreach (var cad in result.CADLinks)
                    sb.AppendLine($"  [{cad.Status}] {cad.Name}");
            }

            sb.AppendLine($"\n── Summary ──");
            sb.AppendLine($"  Loaded: {result.LoadedLinks}/{result.TotalRevitLinks}");
            sb.AppendLine($"  Broken: {result.BrokenLinks}");
            sb.AppendLine($"  Non-ISO names: {result.NonCompliantNames}");
            sb.AppendLine($"  CAD imports: {result.CADLinks.Count(c => c.LinkType == "CAD Import")}");

            TaskDialog.Show("Link Audit", sb.ToString());
            StingLog.Info($"LinkAudit: {result.TotalRevitLinks} Revit, {result.TotalCADLinks} CAD, {result.BrokenLinks} broken");
            return Result.Succeeded;
        }
    }

    /// <summary>Export link audit to CSV.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LinkAuditExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = LinkManagerEngine.AuditLinks(ctx.Doc);
            string path = OutputLocationHelper.GetTimestampedPath(ctx.Doc, "LinkAudit", ".csv");

            var sb = new StringBuilder();
            sb.AppendLine("Type,Name,Status,Loaded,FileExists,NamingCompliant,Instances,Path");
            foreach (var link in result.RevitLinks)
                sb.AppendLine($"Revit,\"{link.Name}\",{link.Status},{link.IsLoaded},{link.FileExists},{link.NamingCompliant},{link.InstanceCount},\"{link.Path}\"");
            foreach (var cad in result.CADLinks)
                sb.AppendLine($"CAD,\"{cad.Name}\",{cad.Status},{cad.IsLoaded},,,{cad.InstanceCount},");

            File.WriteAllText(path, sb.ToString());
            TaskDialog.Show("Link Audit Export", $"Exported to:\n{path}");
            return Result.Succeeded;
        }
    }

    /// <summary>Show linked model element statistics.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LinkStatsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var linkInstances = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>().ToList();
            if (linkInstances.Count == 0) { TaskDialog.Show("STING", "No linked models found."); return Result.Succeeded; }

            var items = linkInstances.Select(l => l.Name).Distinct().ToList();
            var picked = StingListPicker.Show("Link Statistics", "Select a linked model:", items);
            if (picked == null) return Result.Succeeded;

            var instance = linkInstances.FirstOrDefault(l => l.Name == picked);
            if (instance == null) return Result.Succeeded;

            var stats = LinkManagerEngine.GetLinkedModelStats(instance);
            if (stats.Count == 0) { TaskDialog.Show("Link Stats", "Could not access linked document (may not be loaded)."); return Result.Succeeded; }

            var sb = new StringBuilder();
            sb.AppendLine($"Element Statistics for: {instance.Name}\n");
            sb.AppendLine($"Total elements: {stats.Sum(s => s.Value):N0}\n");
            foreach (var kvp in stats.OrderByDescending(s => s.Value).Take(25))
                sb.AppendLine($"  {kvp.Key,-35} {kvp.Value,7:N0}");
            if (stats.Count > 25) sb.AppendLine($"  ... and {stats.Count - 25} more categories");

            TaskDialog.Show("Link Statistics", sb.ToString());
            return Result.Succeeded;
        }
    }

    #endregion
}
