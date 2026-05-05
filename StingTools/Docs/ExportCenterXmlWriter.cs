using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ExportCenterXmlWriter — custom XML serialiser for the Export Centre.
    //
    //  Revit's API has no general-purpose "sheet to XML" exporter, so we roll
    //  one. Every selected sheet (or the project-info block, depending on
    //  XmlExportSettings.Scope) becomes one <sheet> element with parameters
    //  filtered by the user-selected groups.
    //
    //  Output format is intentionally simple and stable so downstream tools
    //  (ETL, reporting, dashboards) can parse it without an XSD.
    // ════════════════════════════════════════════════════════════════════════════

    internal static class ExportCenterXmlWriter
    {
        internal static bool Write(Document doc, List<ElementId> sheetIds, string outputPath, XmlExportSettings settings)
        {
            try
            {
                var writerSettings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    Encoding = new System.Text.UTF8Encoding(false),
                };

                using var w = XmlWriter.Create(outputPath, writerSettings);
                w.WriteStartDocument();
                w.WriteStartElement("export");
                w.WriteAttributeString("schema", "sting-export-centre/1");
                w.WriteAttributeString("generatedUtc", DateTime.UtcNow.ToString("o"));
                w.WriteAttributeString("documentTitle", doc?.Title ?? "");

                if (settings.IncludeProjectInfo) WriteProjectInfo(w, doc);

                if (settings.Scope == "ProjectInfoOnly")
                {
                    // Done — only project info requested.
                    w.WriteEndElement();
                    w.WriteEndDocument();
                    return File.Exists(outputPath);
                }

                w.WriteStartElement("sheets");
                foreach (var eid in sheetIds ?? new List<ElementId>())
                {
                    var sheet = doc.GetElement(eid) as ViewSheet;
                    if (sheet == null) continue;
                    WriteSheet(w, doc, sheet, settings);
                }
                w.WriteEndElement(); // sheets

                w.WriteEndElement(); // export
                w.WriteEndDocument();
                return File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                StingLog.Warn("XmlWriter.Write: " + ex.Message);
                return false;
            }
        }

        private static void WriteProjectInfo(XmlWriter w, Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return;
                w.WriteStartElement("project");
                W(w, "name",    pi.Name);
                W(w, "number",  pi.Number);
                W(w, "client",  pi.ClientName);
                W(w, "address", pi.Address);
                W(w, "status",  pi.Status);
                w.WriteStartElement("orgParams");
                foreach (string p in new[]
                {
                    "PRJ_ORG_PROJECT_COD_TXT", "PRJ_ORG_ORIGINATOR_COD_TXT",
                    "PRJ_ORG_COMPANY_NAME_TXT", "PRJ_ORG_CLIENT_NAME_TXT",
                    "PRJ_ORG_PHASE_TXT", "PRJ_ORG_CLASS_TXT",
                })
                {
                    var par = pi.LookupParameter(p);
                    if (par != null && par.HasValue)
                    {
                        w.WriteStartElement("param");
                        w.WriteAttributeString("name", p);
                        w.WriteString(SafeAsString(par));
                        w.WriteEndElement();
                    }
                }
                w.WriteEndElement(); // orgParams
                w.WriteEndElement(); // project
            }
            catch (Exception ex) { StingLog.Warn("XmlWriter.WriteProjectInfo: " + ex.Message); }
        }

        private static void WriteSheet(XmlWriter w, Document doc, ViewSheet sheet, XmlExportSettings settings)
        {
            w.WriteStartElement("sheet");
            w.WriteAttributeString("id", sheet.Id.Value.ToString());
            w.WriteAttributeString("number", sheet.SheetNumber ?? "");
            w.WriteAttributeString("title",  sheet.Name ?? "");
            w.WriteAttributeString("discipline", ExportCenterEngine.GetDisciplinePrefix(sheet.SheetNumber));

            // Revisions
            try
            {
                var revs = sheet.GetAllRevisionIds();
                if (revs != null && revs.Count > 0)
                {
                    w.WriteStartElement("revisions");
                    foreach (var rid in revs)
                    {
                        if (doc.GetElement(rid) is Revision r)
                        {
                            w.WriteStartElement("revision");
                            w.WriteAttributeString("seq", r.SequenceNumber.ToString());
                            w.WriteAttributeString("date", r.RevisionDate ?? "");
                            w.WriteString(r.Description ?? "");
                            w.WriteEndElement();
                        }
                    }
                    w.WriteEndElement();
                }
            }
            catch { }

            // Parameters by group
            var groups = settings.ParameterGroups ?? new List<string>();
            w.WriteStartElement("params");
            foreach (Parameter p in sheet.Parameters)
            {
                if (p == null) continue;
                if (!IncludeByGroup(p, groups)) continue;
                w.WriteStartElement("param");
                w.WriteAttributeString("name", p.Definition?.Name ?? "");
                // Definition.ParameterGroup was removed in Revit 2024+; use the ForgeTypeId.
                w.WriteAttributeString("group", p.Definition?.GetGroupTypeId()?.TypeId ?? "");
                w.WriteString(SafeAsString(p));
                w.WriteEndElement();
            }
            w.WriteEndElement(); // params

            // Viewports — list each placed view's id and name.
            try
            {
                var vps = new FilteredElementCollector(doc, sheet.Id)
                    .OfClass(typeof(Viewport)).Cast<Viewport>().ToList();
                if (vps.Count > 0)
                {
                    w.WriteStartElement("viewports");
                    foreach (var vp in vps)
                    {
                        var view = doc.GetElement(vp.ViewId) as View;
                        if (view == null) continue;
                        w.WriteStartElement("viewport");
                        w.WriteAttributeString("viewId",   vp.ViewId.Value.ToString());
                        w.WriteAttributeString("viewName", view.Name ?? "");
                        w.WriteAttributeString("viewType", view.ViewType.ToString());
                        w.WriteAttributeString("scale",    view.Scale.ToString());
                        w.WriteEndElement();
                    }
                    w.WriteEndElement();
                }
            }
            catch { }

            w.WriteEndElement(); // sheet
        }

        private static bool IncludeByGroup(Parameter p, List<string> groups)
        {
            if (groups == null || groups.Count == 0) return true;
            string g = p.Definition?.GetGroupTypeId()?.TypeId ?? "";
            foreach (var want in groups)
            {
                if (g.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                // Simple semantic mapping for the user-facing labels in the dialog.
                if (want == "Identity"   && g.Contains("IDENTITY", StringComparison.OrdinalIgnoreCase)) return true;
                if (want == "Location"   && g.Contains("CONSTRAINTS", StringComparison.OrdinalIgnoreCase)) return true;
                if (want == "Dimensions" && g.Contains("GEOMETRY", StringComparison.OrdinalIgnoreCase)) return true;
                if (want == "Revisions"  && p.Definition?.Name?.Contains("Revision", StringComparison.OrdinalIgnoreCase) == true) return true;
            }
            return false;
        }

        private static string SafeAsString(Parameter p)
        {
            try
            {
                return p.StorageType switch
                {
                    StorageType.String   => p.AsString() ?? "",
                    StorageType.Integer  => p.AsInteger().ToString(),
                    StorageType.Double   => p.AsValueString() ?? p.AsDouble().ToString("G"),
                    StorageType.ElementId => p.AsValueString() ?? p.AsElementId().Value.ToString(),
                    _ => p.AsValueString() ?? "",
                };
            }
            catch { return ""; }
        }

        private static void W(XmlWriter w, string name, string value)
        {
            w.WriteStartElement(name);
            w.WriteString(value ?? "");
            w.WriteEndElement();
        }
    }
}
