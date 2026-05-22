// StingTools — gbXML round-trip load import.
//
// Phase 187h. Hvac_ExportGbxml ships a Revit-side gbXML for TRACE /
// HAP / IES VE / EnergyPlus to consume. The reverse path — import
// per-zone peak loads from the simulator's gbXML EXPORT and stamp
// them back onto STING Spaces — is the missing third leg. This
// command:
//
//   1. Pops an OpenFileDialog for the .xml gbXML.
//   2. Parses <Zone> + <DesignTemperature> + <DesignFlow> / <DesignLoad>
//      elements.
//   3. Joins on Space Number → Name → ElementId (same logic as
//      HvacCompareLoadsCommand).
//   4. Stamps HVC_PEAK_SENS_W + HVC_OA_LS + HVC_LOAD_SOURCE_TXT
//      with the simulator-source label.
//
// Output: panel + per-zone CSV. Differs from HvacCompareLoadsCommand
// in WRITING the values onto Spaces rather than DIFFING — so it can
// replace STING's BlockLoad output entirely when the simulator is
// authoritative.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacImportGbxmlLoadsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                string xmlPath;
                using (var dlg = new OpenFileDialog
                {
                    Filter = "gbXML simulator export|*.xml|All files|*.*",
                    Title = "Pick a TRACE / HAP / IES VE / EnergyPlus gbXML"
                })
                {
                    if (dlg.ShowDialog() != DialogResult.OK) return Result.Cancelled;
                    xmlPath = dlg.FileName;
                }
                if (!File.Exists(xmlPath))
                {
                    TaskDialog.Show("STING HVAC", "Selected file does not exist.");
                    return Result.Cancelled;
                }

                var zones = ParseGbxml(xmlPath, out string parseError);
                if (zones.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — gbXML Import",
                        $"No zones with load data found in {Path.GetFileName(xmlPath)}.\n\n" +
                        $"Parse error: {parseError ?? "(file has no <Zone> elements with PeakCoolingLoad / OutdoorAirFlow)"}");
                    return Result.Cancelled;
                }

                var spaces = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType()
                    .Cast<Space>()
                    .ToList();
                var spaceByNumber = spaces.Where(s => !string.IsNullOrEmpty(s.Number))
                    .GroupBy(s => s.Number.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var spaceByName = spaces.Where(s => !string.IsNullOrEmpty(s.Name))
                    .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var spaceById = spaces.ToDictionary(s => s.Id.Value.ToString(),
                                                    s => s, StringComparer.OrdinalIgnoreCase);

                int stamped = 0, missing = 0;
                string srcLabel = Path.GetFileName(xmlPath);
                using (var tx = new Transaction(doc, "STING gbXML Loads Import"))
                {
                    tx.Start();
                    foreach (var z in zones)
                    {
                        Space sp = null;
                        if (!spaceByNumber.TryGetValue(z.ZoneId, out sp) &&
                            !spaceByName.TryGetValue(z.ZoneId, out sp) &&
                            !spaceById.TryGetValue(z.ZoneId, out sp))
                        { missing++; continue; }
                        try
                        {
                            if (z.PeakCoolingW > 0)
                                ParameterHelpers.SetString(sp, "HVC_PEAK_SENS_W",
                                    $"{z.PeakCoolingW:F0}", overwrite: true);
                            if (z.PeakLatentW > 0)
                                ParameterHelpers.SetString(sp, "HVC_PEAK_LAT_W",
                                    $"{z.PeakLatentW:F0}", overwrite: true);
                            if (z.OaLs > 0)
                                ParameterHelpers.SetString(sp, "HVC_OA_LS",
                                    $"{z.OaLs:F1}", overwrite: true);
                            ParameterHelpers.SetString(sp, "HVC_LOAD_SOURCE_TXT",
                                $"gbXML:{srcLabel}", overwrite: true);
                            stamped++;
                        }
                        catch (Exception ex) { StingLog.Warn($"gbXML stamp {sp.Id}: {ex.Message}"); }
                    }
                    tx.Commit();
                }

                var panel = StingResultPanel.Create("HVAC — gbXML Loads Import");
                panel.SetSubtitle($"source={Path.GetFileName(xmlPath)} · {zones.Count} zones found");
                panel.AddSection("SUMMARY")
                     .Metric("Zones parsed",        zones.Count.ToString())
                     .Metric("Stamped on Spaces",   stamped.ToString())
                     .Metric("Unmatched Zone IDs",  missing.ToString())
                     .Metric("Match rule",          "Space Number → Name → ElementId");

                panel.AddSection("FIRST 20 ZONES");
                foreach (var z in zones.Take(20))
                    panel.Text($"  {z.ZoneId}: cool {z.PeakCoolingW/1000:F1} kW · lat {z.PeakLatentW/1000:F1} kW · OA {z.OaLs:F0} L/s");

                panel.Text("Parses gbXML <Zone> elements with peak / OA child elements. " +
                           "Stamps HVC_PEAK_SENS_W + HVC_PEAK_LAT_W + HVC_OA_LS + " +
                           "HVC_LOAD_SOURCE_TXT='gbXML:<filename>' on matched STING Spaces. " +
                           "Use Hvac_CompareLoads for a non-destructive diff instead.");
                panel.Show();
                try { StingHvacPanel.Instance?.PushRunRow($"gbXML import ({stamped} stamped)", "⬤"); }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacImportGbxmlLoadsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Parser ──────────────────────────────────────────────────

        private class ZoneRow
        {
            public string ZoneId      = "";
            public double PeakCoolingW;
            public double PeakLatentW;
            public double OaLs;
        }

        private static List<ZoneRow> ParseGbxml(string path, out string parseError)
        {
            parseError = null;
            var rows = new List<ZoneRow>();
            try
            {
                var doc = XDocument.Load(path);
                // gbXML zones live at /gbXML/Campus/Building/Zone (or
                // /gbXML/Zone in older schema versions). LocalName match
                // bypasses namespace differences across exporters.
                var zoneEls = doc.Descendants()
                    .Where(e => e.Name.LocalName == "Zone")
                    .ToList();
                foreach (var z in zoneEls)
                {
                    var row = new ZoneRow
                    {
                        ZoneId = (string)z.Attribute("zoneIdRef")
                              ?? (string)z.Attribute("id")
                              ?? z.Elements().FirstOrDefault(c => c.Name.LocalName == "Name")?.Value?.Trim()
                              ?? ""
                    };
                    foreach (var child in z.Elements())
                    {
                        string ln = child.Name.LocalName;
                        // Loads can appear as <PeakCoolingLoad unit="kW">5.6</PeakCoolingLoad>
                        // or PeakCoolingSensible / DesignCoolingLoad — accept all variants.
                        if (ln.IndexOf("PeakCooling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ln.IndexOf("DesignCooling", StringComparison.OrdinalIgnoreCase) >= 0)
                            row.PeakCoolingW = ToWatts(child);
                        else if (ln.IndexOf("PeakLatent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 ln.IndexOf("DesignLatent", StringComparison.OrdinalIgnoreCase) >= 0)
                            row.PeakLatentW  = ToWatts(child);
                        else if (ln.IndexOf("OutdoorAir", StringComparison.OrdinalIgnoreCase) >= 0)
                            row.OaLs         = ToLs(child);
                    }
                    if (!string.IsNullOrEmpty(row.ZoneId) &&
                        (row.PeakCoolingW > 0 || row.OaLs > 0))
                        rows.Add(row);
                }
            }
            catch (Exception ex) { parseError = ex.Message; }
            return rows;
        }

        private static double ToWatts(XElement el)
        {
            if (!double.TryParse(el.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                return 0;
            string unit = ((string)el.Attribute("unit") ?? "").ToLowerInvariant();
            return unit switch
            {
                "kw"       => v * 1000.0,
                "btu/h"    => v * 0.2931,
                "tons"     => v * 3517.0,
                "w" or ""  => v,
                _          => v
            };
        }

        private static double ToLs(XElement el)
        {
            if (!double.TryParse(el.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                return 0;
            string unit = ((string)el.Attribute("unit") ?? "").ToLowerInvariant();
            return unit switch
            {
                "cfm"           => v * 0.4719,
                "m3/h" or "cmh" => v / 3.6,
                "m3/s"          => v * 1000.0,
                "l/s" or ""     => v,
                _               => v
            };
        }
    }
}
