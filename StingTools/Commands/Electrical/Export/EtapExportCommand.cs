using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Export
{
    /// <summary>
    /// DATA HAND-OFF DRAFT for ETAP, loosely shaped on IEC 61970 CIM RDF/XML:
    /// a Substation per panel, an EnergyConsumer per circuit and an
    /// ACLineSegment per feeder. It is NOT a validated CIM profile and NOT a
    /// native ETAP import file — ETAP's import mapping has to be configured and
    /// the result reviewed. What it does guarantee: an rdf:RDF root and
    /// document-unique rdf:IDs (circuit numbers repeat per panel, which used to
    /// produce duplicate IDs).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class EtapExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var model = ExternalExportEngine.Build(doc);
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            try { outDir = Path.Combine(outDir, "electrical"); Directory.CreateDirectory(outDir); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            string outPath = Path.Combine(outDir, $"STING_ETAP_CIM_DRAFT_{DateTime.Now:yyyyMMdd-HHmm}.xml");

            XNamespace cim = "http://iec.ch/TC57/2013/CIM-schema-cim16#";
            XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

            var ids = new CimIdAllocator();
            int duplicatePanelNames = 0;
            var panelEls = new List<XElement>();
            var seenPanels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in model.Panels)
            {
                // First panel of a name owns the name key (feeders reference panels by
                // name); a second panel with the same name still gets a unique ID.
                bool first = seenPanels.Add(p.PanelName ?? "");
                if (!first) duplicatePanelNames++;
                string pid = ids.For(first ? "P|" + p.PanelName : null, "PNL", p.PanelName);
                panelEls.Add(new XElement(cim + "Substation",
                    new XAttribute(rdf + "ID", pid),
                    new XElement(cim + "IdentifiedObject.name", p.PanelName ?? ""),
                    new XElement(cim + "Substation.ratedKV",
                        (p.VoltageV / 1000.0).ToString("0.00000", CultureInfo.InvariantCulture))));
            }

            var circuitEls = model.Circuits.Select(c =>
                new XElement(cim + "EnergyConsumer",
                    // Panel + circuit: "1" exists on every panel.
                    new XAttribute(rdf + "ID", ids.For(null, "CCT", c.PanelName, c.CircuitId)),
                    new XElement(cim + "IdentifiedObject.name", $"{c.PanelName}/{c.CircuitId}"),
                    new XElement(cim + "EnergyConsumer.pfixed",
                        c.LoadKW.ToString("0.000", CultureInfo.InvariantCulture)),
                    new XElement(cim + "EnergyConsumer.qfixed",
                        c.LoadKVAR.ToString("0.000", CultureInfo.InvariantCulture)))).ToList();

            var feederEls = model.Feeders.Select(f =>
            {
                string fId = ids.For(null, "FDR", f.UpstreamPanel, f.DownstreamPanel);
                string up = ids.For("P|" + f.UpstreamPanel, "PNL", f.UpstreamPanel);
                string dn = ids.For("P|" + f.DownstreamPanel, "PNL", f.DownstreamPanel);
                return new XElement(cim + "ACLineSegment",
                    new XAttribute(rdf + "ID", fId),
                    new XElement(cim + "IdentifiedObject.name", $"{f.UpstreamPanel}→{f.DownstreamPanel}"),
                    new XElement(cim + "Conductor.length",
                        f.LengthM.ToString("0.00", CultureInfo.InvariantCulture)),
                    new XElement(cim + "ACLineSegment.r",
                        f.ResistanceOhm.ToString("0.000000", CultureInfo.InvariantCulture)),
                    new XElement(cim + "ACLineSegment.x",
                        f.ReactanceOhm.ToString("0.000000", CultureInfo.InvariantCulture)),
                    new XElement(cim + "Terminal",
                        new XAttribute(rdf + "ID", fId + "_T1"),
                        new XElement(cim + "Terminal.ConductingEquipment",
                            new XAttribute(rdf + "resource", "#" + up))),
                    new XElement(cim + "Terminal",
                        new XAttribute(rdf + "ID", fId + "_T2"),
                        new XElement(cim + "Terminal.ConductingEquipment",
                            new XAttribute(rdf + "resource", "#" + dn))));
            }).ToList();

            var doc2 = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XComment(" STING data hand-off draft - not a validated CIM profile and not a native ETAP import file. Review before use. "),
                new XElement(rdf + "RDF",
                    new XAttribute(XNamespace.Xmlns + "cim", cim.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "rdf", rdf.NamespaceName),
                    panelEls, circuitEls, feederEls));
            try { doc2.Save(outPath); }
            catch (Exception ex)
            {
                StingLog.Error($"ETAP CIM write: {ex.Message}", ex);
                TaskDialog.Show("STING ETAP Export", $"Save failed: {ex.Message}");
                return Result.Failed;
            }
            TaskDialog.Show("STING ETAP Export",
                $"DATA HAND-OFF DRAFT written:\n{outPath}\n\n" +
                $"{model.Panels.Count} panel(s) · {model.Circuits.Count} load(s) · {model.Feeders.Count} feeder(s)\n\n" +
                "This is CIM-shaped RDF/XML, NOT a native ETAP import file and not a validated CIM " +
                "profile (no topology nodes, no equipment ratings beyond kV/kW/kvar/R/X). Map it in " +
                "ETAP's import tool and review every element before use." +
                (duplicatePanelNames > 0
                    ? $"\n\n⚠ {duplicatePanelNames} panel(s) share a name with another panel — feeders to them " +
                      "resolve to the first; rename panels for an unambiguous hand-off."
                    : ""));
            return Result.Succeeded;
        }

    }
}
