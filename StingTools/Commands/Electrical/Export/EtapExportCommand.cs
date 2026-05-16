using System;
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
    /// IEC 61968 / 61970 CIM XML export for ETAP. Emits cim:Substation per
    /// panel and cim:EnergyConsumer per circuit. The full CIM schema is
    /// large; Phase 179 ships the load-flow subset ETAP needs to spin up
    /// a power-flow study from a Revit model.
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
            string outPath = Path.Combine(outDir, $"STING_ETAP_CIM_{DateTime.Now:yyyyMMdd-HHmm}.xml");

            XNamespace cim = "http://iec.ch/TC57/2013/CIM-schema-cim16#";
            XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

            var doc2 = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("Network",
                    new XAttribute(XNamespace.Xmlns + "cim", cim.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "rdf", rdf.NamespaceName),
                    model.Panels.Select(p =>
                        new XElement(cim + "Substation",
                            new XAttribute(rdf + "ID", Sanitise(p.PanelName)),
                            new XElement(cim + "IdentifiedObject.name", p.PanelName ?? ""),
                            new XElement(cim + "Substation.ratedKV",
                                (p.VoltageV / 1000.0).ToString("0.00000",
                                    System.Globalization.CultureInfo.InvariantCulture)))),
                    model.Circuits.Select(c =>
                        new XElement(cim + "EnergyConsumer",
                            new XAttribute(rdf + "ID", Sanitise(c.CircuitId)),
                            new XElement(cim + "IdentifiedObject.name", c.CircuitId ?? ""),
                            new XElement(cim + "EnergyConsumer.pfixed",
                                c.LoadKW.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)),
                            new XElement(cim + "EnergyConsumer.qfixed",
                                c.LoadKVAR.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))))));
            try { doc2.Save(outPath); }
            catch (Exception ex)
            {
                StingLog.Error($"ETAP CIM write: {ex.Message}", ex);
                TaskDialog.Show("STING ETAP Export", $"Save failed: {ex.Message}");
                return Result.Failed;
            }
            TaskDialog.Show("STING ETAP Export",
                $"CIM XML exported for ETAP:\n{outPath}\n\n" +
                $"{model.Panels.Count} substation(s) · {model.Circuits.Count} load(s)");
            return Result.Succeeded;
        }

        private static string Sanitise(string s) => Regex.Replace(s ?? "ID", @"\W", "_");
    }
}
