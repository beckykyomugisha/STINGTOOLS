// Phase 139 E1 — Placement rules Excel exporter.
//
// Writes one sheet per discipline pack + a SCHEMA sheet listing all
// fields and valid values.  Header row frozen, AutoFilter enabled,
// invalid rows highlighted.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ClosedXML.Excel;

namespace StingTools.Core.Placement.Excel
{
    public static class PlacementRulesExcelExporter
    {
        // Order of columns mirrors PlacementRule field declaration order
        // for human readability.
        private static readonly string[] FieldOrder = new[]
        {
            nameof(PlacementRule.RuleId), nameof(PlacementRule.RuleKind),
            nameof(PlacementRule.SourcePack), nameof(PlacementRule.CategoryFilter),
            nameof(PlacementRule.VariantHint), nameof(PlacementRule.FamilyTypeRegex),
            nameof(PlacementRule.RoomFilter), nameof(PlacementRule.ExcludeRoomFilter),
            nameof(PlacementRule.RoomDepartmentFilter),
            nameof(PlacementRule.MinAreaM2), nameof(PlacementRule.MaxAreaM2),
            nameof(PlacementRule.LevelFilter), nameof(PlacementRule.PhaseFilter),
            nameof(PlacementRule.WorksetFilter),
            nameof(PlacementRule.AnchorType), nameof(PlacementRule.MountingReference),
            nameof(PlacementRule.OffsetXMm), nameof(PlacementRule.OffsetYMm),
            nameof(PlacementRule.OffsetZMm), nameof(PlacementRule.RotationDeg),
            nameof(PlacementRule.ToleranceMm), nameof(PlacementRule.MountingHeightMm),
            nameof(PlacementRule.SideConstraint),
            nameof(PlacementRule.MinSpacingMm), nameof(PlacementRule.MaxPerRoom),
            nameof(PlacementRule.PerAreaM2), nameof(PlacementRule.PerOccupant),
            nameof(PlacementRule.PerLinearMetre), nameof(PlacementRule.PerBed),
            nameof(PlacementRule.PerWorkstation), nameof(PlacementRule.PerPupil),
            nameof(PlacementRule.PerToiletCubicle), nameof(PlacementRule.OccupancyParamName),
            nameof(PlacementRule.BuildingTypeTable),
            nameof(PlacementRule.DependsOn), nameof(PlacementRule.RelativeTo),
            nameof(PlacementRule.CoPlaceWith), nameof(PlacementRule.ConflictsWith),
            nameof(PlacementRule.Priority), nameof(PlacementRule.StandardRef),
            nameof(PlacementRule.UniclassPr), nameof(PlacementRule.Notes),
            nameof(PlacementRule.BuildingType), nameof(PlacementRule.ApplicableStandards),
            nameof(PlacementRule.IpRatingMin), nameof(PlacementRule.WetZoneExclusion),
            nameof(PlacementRule.AccessibilityCheck), nameof(PlacementRule.HeightStandard),
            nameof(PlacementRule.CoverageRadiusMm), nameof(PlacementRule.MaxSpacingMm),
            nameof(PlacementRule.WallClearanceMm), nameof(PlacementRule.ObstructionClearanceMm),
            nameof(PlacementRule.GuaranteeCoverage),
            nameof(PlacementRule.RoutingMode), nameof(PlacementRule.RouteOffsetMm),
            nameof(PlacementRule.RouteFace), nameof(PlacementRule.RouteMinBendRadiusMm),
            nameof(PlacementRule.RouteSegmentCategory),
            nameof(PlacementRule.SillHeightMm), nameof(PlacementRule.HeadHeightMm),
            nameof(PlacementRule.CillToFloorMm), nameof(PlacementRule.ToughenedGlazingRequired),
            nameof(PlacementRule.GlazingSpec),
            nameof(PlacementRule.PostAuditTag), nameof(PlacementRule.RequiresCOBieFields),
            nameof(PlacementRule.RequiresIfcMapping), nameof(PlacementRule.MaintenanceClearance),
            // Phase 139.2 S — manufacturer + two-phase + cluster + plaster + tile + structural columns.
            nameof(PlacementRule.ManufacturerCode), nameof(PlacementRule.CatalogueRef),
            nameof(PlacementRule.BoxDepthMm), nameof(PlacementRule.GangCount),
            nameof(PlacementRule.ModulePitchMm),
            nameof(PlacementRule.MountType), nameof(PlacementRule.InsertionOrigin),
            nameof(PlacementRule.PlasterOffsetMode), nameof(PlacementRule.PlasterOffsetFixedMm),
            nameof(PlacementRule.TwoPhaseEnabled), nameof(PlacementRule.ConstructionPhase),
            nameof(PlacementRule.CompletionPhase), nameof(PlacementRule.BoxFamilyTypeRegex),
            nameof(PlacementRule.BoxLocationIdParam),
            nameof(PlacementRule.IsClusterMember), nameof(PlacementRule.ClusterGroupId),
            nameof(PlacementRule.ClusterSlotIndex), nameof(PlacementRule.ClusterTotalSlots),
            nameof(PlacementRule.ClusterFrameWidthMm),
            nameof(PlacementRule.CeilingTileSnap), nameof(PlacementRule.TileGridSpacingXMm),
            nameof(PlacementRule.TileGridSpacingYMm),
            nameof(PlacementRule.StructuralFixingCheck), nameof(PlacementRule.JoistClearanceMm),
            nameof(PlacementRule.EmitNogginRequirement),
            nameof(PlacementRule.WetZoneExclude), nameof(PlacementRule.WetZoneClass),
            nameof(PlacementRule.HeightStandardRef),
        };

        public static void Export(List<PlacementRule> rules, string filePath)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            using (var wb = new XLWorkbook())
            {
                var byPack = rules.GroupBy(r => string.IsNullOrEmpty(r.SourcePack) ? "Baseline" : r.SourcePack)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
                foreach (var grp in byPack)
                {
                    string sheetName = SafeSheetName(grp.Key);
                    var ws = wb.AddWorksheet(sheetName);
                    WriteHeaderRow(ws);
                    int rowIdx = 2;
                    foreach (var rule in grp)
                    {
                        WriteRule(ws, rowIdx, rule);
                        rowIdx++;
                    }
                    ws.Range(1, 1, 1, FieldOrder.Length).SetAutoFilter();
                    ws.SheetView.FreezeRows(1);
                    ws.Columns().AdjustToContents();
                }
                WriteSchemaSheet(wb);
                wb.SaveAs(filePath);
            }
        }

        private static void WriteHeaderRow(IXLWorksheet ws)
        {
            for (int i = 0; i < FieldOrder.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = FieldOrder[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1F, 0x38, 0x64);
            }
        }

        private static void WriteRule(IXLWorksheet ws, int row, PlacementRule rule)
        {
            var t = typeof(PlacementRule);
            bool invalid = string.IsNullOrEmpty(rule.CategoryFilter);
            bool dirty   = rule.Priority < 50;
            for (int i = 0; i < FieldOrder.Length; i++)
            {
                var prop = t.GetProperty(FieldOrder[i], BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) continue;
                var val = prop.GetValue(rule);
                var cell = ws.Cell(row, i + 1);
                if (val == null) { /* empty */ }
                else if (val is bool b) cell.Value = b ? "TRUE" : "FALSE";
                else if (val is double d) cell.Value = d;
                else if (val is int    n) cell.Value = n;
                else if (val is string[] sa) cell.Value = string.Join("|", sa);
                else if (val is List<string> ls) cell.Value = string.Join("|", ls);
                else if (val is Enum e) cell.Value = e.ToString();
                else cell.Value = val.ToString();
            }
            if (invalid)
                ws.Range(row, 1, row, FieldOrder.Length).Style.Fill.BackgroundColor = XLColor.FromArgb(0xFF, 0xE0, 0xE0);
            else if (dirty)
                ws.Range(row, 1, row, FieldOrder.Length).Style.Fill.BackgroundColor = XLColor.FromArgb(0xFF, 0xF2, 0xCC);
        }

        private static void WriteSchemaSheet(XLWorkbook wb)
        {
            var ws = wb.AddWorksheet("SCHEMA");
            ws.Cell(1, 1).Value = "Field";
            ws.Cell(1, 2).Value = "Type";
            ws.Cell(1, 3).Value = "Notes";
            ws.Range(1, 1, 1, 3).Style.Font.Bold = true;
            int row = 2;
            var t = typeof(PlacementRule);
            foreach (var name in FieldOrder)
            {
                var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) continue;
                ws.Cell(row, 1).Value = name;
                ws.Cell(row, 2).Value = prop.PropertyType.Name;
                ws.Cell(row, 3).Value = ""; // Reserved for future docs
                row++;
            }
            ws.Columns().AdjustToContents();
        }

        private static string SafeSheetName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Sheet";
            var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ').ToArray());
            return clean.Length > 31 ? clean.Substring(0, 31) : clean;
        }
    }
}
