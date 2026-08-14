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
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

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

                string srcLabel = Path.GetFileName(xmlPath);

                // ── Pre-apply delta pass (Tier-3 3.4) ────────────────────
                // Before touching the model, compute per-zone deltas of the
                // incoming simulator value against the STING BlockLoad value
                // already on the Space (HVC_PEAK_SENS_W; HVC_LOAD_COOLING_KW
                // used as a secondary source). Zones with no prior STING value
                // are flagged "new". The user reviews the diff + CSV and must
                // explicitly confirm before any stamping happens.
                var deltas = new List<DeltaRow>();
                int missing = 0;
                foreach (var z in zones)
                {
                    Space sp = null;
                    if (!spaceByNumber.TryGetValue(z.ZoneId, out sp) &&
                        !spaceByName.TryGetValue(z.ZoneId, out sp) &&
                        !spaceById.TryGetValue(z.ZoneId, out sp))
                    { missing++; deltas.Add(new DeltaRow { ZoneId = z.ZoneId, Matched = false, NewCoolingW = z.PeakCoolingW }); continue; }

                    double priorW = ReadStingCoolingW(sp);
                    bool isNew = priorW <= 0;
                    double deltaPct = (!isNew && z.PeakCoolingW > 0)
                        ? 100.0 * (z.PeakCoolingW - priorW) / priorW : double.NaN;
                    deltas.Add(new DeltaRow
                    {
                        ZoneId = z.ZoneId, Matched = true, IsNew = isNew,
                        PriorCoolingW = priorW, NewCoolingW = z.PeakCoolingW,
                        NewLatentW = z.PeakLatentW, NewOaLs = z.OaLs, DeltaPct = deltaPct,
                        SpaceId = sp.Id
                    });
                }

                int matchedCount = deltas.Count(d => d.Matched);
                int newCount     = deltas.Count(d => d.Matched && d.IsNew);
                int changedCount = deltas.Count(d => d.Matched && !d.IsNew);
                string deltaCsv  = WriteDeltaCsv(doc, deltas, srcLabel);

                // Build the diff summary (worst / new zones first).
                var sbDiff = new StringBuilder();
                sbDiff.AppendLine($"Source: {srcLabel}");
                sbDiff.AppendLine($"Zones parsed: {zones.Count}  ·  matched Spaces: {matchedCount}  ·  " +
                                  $"unmatched: {missing}");
                sbDiff.AppendLine($"Of matched: {newCount} NEW (no prior STING value), {changedCount} with a prior value.");
                sbDiff.AppendLine();
                sbDiff.AppendLine("Per-zone Δ (STING vs incoming gbXML sensible cooling), worst 15:");
                foreach (var d in deltas.Where(x => x.Matched)
                             .OrderByDescending(x => x.IsNew ? double.PositiveInfinity
                                                              : Math.Abs(double.IsNaN(x.DeltaPct) ? 0 : x.DeltaPct))
                             .Take(15))
                {
                    if (d.IsNew)
                        sbDiff.AppendLine($"  {d.ZoneId}: NEW → {d.NewCoolingW/1000:F1} kW");
                    else
                        sbDiff.AppendLine($"  {d.ZoneId}: STING {d.PriorCoolingW/1000:F1} kW → " +
                                          $"gbXML {d.NewCoolingW/1000:F1} kW  (Δ {d.DeltaPct,+6:+0.0;-0.0;0.0} %)");
                }
                if (deltaCsv != null) { sbDiff.AppendLine(); sbDiff.AppendLine($"Full diff CSV: {deltaCsv}"); }

                if (matchedCount == 0)
                {
                    TaskDialog.Show("STING HVAC — gbXML Import",
                        sbDiff.ToString() + "\nNo Spaces matched — nothing to stamp.");
                    return Result.Cancelled;
                }

                // Require explicit confirmation before overwriting Space loads.
                var confirm = new TaskDialog("STING HVAC — gbXML Import (review before applying)")
                {
                    MainInstruction = $"Overwrite loads on {matchedCount} Space(s)?",
                    MainContent = sbDiff.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel
                };
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Apply — stamp the incoming gbXML values onto matched Spaces");
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Cancel — keep the CSV diff, change nothing");
                var choice = confirm.Show();
                if (choice != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;

                int stamped = 0;
                using (var tx = new Transaction(doc, "STING gbXML Loads Import"))
                {
                    tx.Start();
                    foreach (var z in zones)
                    {
                        Space sp = null;
                        if (!spaceByNumber.TryGetValue(z.ZoneId, out sp) &&
                            !spaceByName.TryGetValue(z.ZoneId, out sp) &&
                            !spaceById.TryGetValue(z.ZoneId, out sp))
                        { continue; }
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
                     .Metric("New (no prior value)", newCount.ToString())
                     .Metric("Changed (had value)", changedCount.ToString())
                     .Metric("Unmatched Zone IDs",  missing.ToString())
                     .Metric("Diff CSV",            deltaCsv ?? "(not written)")
                     .Metric("Match rule",          "Space Number → Name → ElementId");

                panel.AddSection("FIRST 20 ZONES");
                foreach (var z in zones.Take(20))
                    panel.Text($"  {z.ZoneId}: cool {z.PeakCoolingW/1000:F1} kW · lat {z.PeakLatentW/1000:F1} kW · OA {z.OaLs:F0} L/s");

                panel.Text("Parses gbXML <Zone> elements with peak / OA child elements. " +
                           "Computes a per-zone delta against the existing STING BlockLoad " +
                           "value (HVC_PEAK_SENS_W / HVC_LOAD_COOLING_KW), writes a diff CSV, " +
                           "and requires explicit confirmation before stamping HVC_PEAK_SENS_W + " +
                           "HVC_PEAK_LAT_W + HVC_OA_LS + HVC_LOAD_SOURCE_TXT='gbXML:<filename>' " +
                           "on matched Spaces. Zones with no prior STING value are shown as 'new'. " +
                           "Use Hvac_CompareLoads for a purely non-destructive diff.");
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

        // Per-zone pre-apply delta (Tier-3 3.4).
        private class DeltaRow
        {
            public string    ZoneId = "";
            public bool      Matched;
            public bool      IsNew;          // matched but no prior STING value
            public double    PriorCoolingW;  // existing HVC_PEAK_SENS_W
            public double    NewCoolingW;    // incoming gbXML sensible cooling
            public double    NewLatentW;
            public double    NewOaLs;
            public double    DeltaPct;       // NaN when new / no prior
            public ElementId SpaceId;
        }

        /// <summary>
        /// Read the existing STING BlockLoad cooling value on a Space, in W.
        /// Prefers HVC_PEAK_SENS_W (BlockLoad sensible stamp); falls back to
        /// HVC_LOAD_COOLING_KW (kW → W) when the sensible stamp is absent.
        /// Returns 0 when neither carries a value (→ treated as "new").
        /// </summary>
        private static double ReadStingCoolingW(Space sp)
        {
            double w = ReadParamDouble(sp, "HVC_PEAK_SENS_W");
            if (w > 0) return w;
            double kw = ReadParamDouble(sp, "HVC_LOAD_COOLING_KW");
            return kw > 0 ? kw * 1000.0 : 0;
        }

        private static double ReadParamDouble(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    return v;
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Write the per-zone STING-vs-gbXML delta table via OutputLocationHelper.
        /// Returns the path, or null on failure.
        /// </summary>
        private static string WriteDeltaCsv(Document doc, List<DeltaRow> rows, string srcLabel)
        {
            try
            {
                string path = OutputLocationHelper.GetTimestampedPath(doc, "STING_gbXML_delta", ".csv");
                var sb = new StringBuilder();
                sb.AppendLine($"# gbXML delta report — source: {srcLabel}");
                sb.AppendLine("ZoneId,Matched,IsNew,PriorCoolingKw,NewCoolingKw,DeltaPct,NewLatentKw,NewOaLs,SpaceId");
                foreach (var d in rows)
                    sb.AppendLine($"\"{d.ZoneId}\",{d.Matched},{d.IsNew}," +
                                  $"{d.PriorCoolingW/1000:F2},{d.NewCoolingW/1000:F2}," +
                                  $"{(double.IsNaN(d.DeltaPct) ? "" : d.DeltaPct.ToString("F1", CultureInfo.InvariantCulture))}," +
                                  $"{d.NewLatentW/1000:F2},{d.NewOaLs:F1}," +
                                  $"{(d.SpaceId?.Value.ToString() ?? "")}");
                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"WriteDeltaCsv: {ex.Message}"); return null; }
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
