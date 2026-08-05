// PC-14 — Learn Placement.
//
// Walks every placed FamilyInstance in the active document whose
// category appears in PlacementRulesViewModel.AnchorTypes scope (today
// the 14 categories the rule library targets), derives a coarse
// (Category, RoomTypeKeyword) → (anchor, mounting height, offset)
// signature from where the instances actually sit, and emits a
// project-level override JSON next to the .rvt with Priority = 90.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.UI;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LearnPlacementV4Command : IExternalCommand
    {
        // Categories the learn pass inspects. Mirrors the shipped rule library.
        private static readonly BuiltInCategory[] LearnCats = new[]
        {
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_SpecialityEquipment,
            // PC-14 also cluster on the architectural categories from PC-18.
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_GenericModel,
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            if (string.IsNullOrEmpty(doc?.PathName))
            {
                TaskDialog.Show("STING — Learn Placement", "Save the project on disk first; the learned overrides land beside the .rvt.");
                return Result.Cancelled;
            }

            try
            {
                var clusters = new Dictionary<string, ClusterStats>(StringComparer.OrdinalIgnoreCase);

                foreach (var cat in LearnCats)
                {
                    var col = new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType();
                    foreach (var el in col)
                    {
                        if (!(el is FamilyInstance fi)) continue;
                        var loc = fi.Location as LocationPoint;
                        if (loc == null) continue;
                        var pt = loc.Point;

                        Room room = doc.GetRoomAtPoint(pt) as Room;
                        if (room == null)
                        {
                            // Try elevated point (some categories sit at ceiling height).
                            try { room = doc.GetRoomAtPoint(new XYZ(pt.X, pt.Y, pt.Z - 1.0)) as Room; } catch { }
                        }
                        if (room == null) continue;

                        string roomName = room.Name ?? "";
                        string keyword  = ExtractKeyword(roomName);
                        string catName  = el.Category?.Name ?? "";

                        string key = catName + "::" + keyword;
                        if (!clusters.TryGetValue(key, out var c))
                        {
                            c = new ClusterStats { CategoryName = catName, RoomKeyword = keyword };
                            clusters[key] = c;
                        }
                        c.Count++;
                        // Mounting height: Z minus room min Z (FFL).
                        var bb = room.get_BoundingBox(null);
                        if (bb != null)
                        {
                            double mountFt = pt.Z - bb.Min.Z;
                            c.MountFtAcc += mountFt;
                        }
                        // Anchor heuristic: closer to centroid than to wall → ROOM_CENTRE; near boundary → WALL_MIDPOINT.
                        if (bb != null)
                        {
                            var centroid = (bb.Min + bb.Max) * 0.5;
                            double cx = pt.X - centroid.X, cy = pt.Y - centroid.Y;
                            double dCentre = Math.Sqrt(cx * cx + cy * cy);
                            double halfX = (bb.Max.X - bb.Min.X) * 0.5;
                            double halfY = (bb.Max.Y - bb.Min.Y) * 0.5;
                            double minHalf = Math.Min(halfX, halfY);
                            if (dCentre < minHalf * 0.4) c.RoomCentreVotes++;
                            else c.WallVotes++;
                        }
                    }
                }

                if (clusters.Count == 0)
                {
                    TaskDialog.Show("STING — Learn Placement",
                        "No placed instances of the learn-pass categories found in the model. Place a few real-world fixtures first.");
                    return Result.Cancelled;
                }

                var rules = new List<PlacementRule>();
                foreach (var c in clusters.Values.OrderByDescending(x => x.Count))
                {
                    if (c.Count < 2) continue; // need ≥2 samples to call it a pattern
                    string anchor = c.RoomCentreVotes > c.WallVotes ? "ROOM_CENTRE" : "WALL_MIDPOINT";
                    double mountMm = (c.MountFtAcc / Math.Max(1, c.Count)) * 304.8;
                    rules.Add(new PlacementRule
                    {
                        RuleId           = $"learned-{c.CategoryName}-{c.RoomKeyword}".Replace(" ", "-").ToLowerInvariant(),
                        CategoryFilter   = c.CategoryName,
                        RoomFilter       = string.IsNullOrEmpty(c.RoomKeyword) ? "" : $"(?i){c.RoomKeyword}",
                        AnchorType       = anchor,
                        MountingHeightMm = Math.Round(mountMm / 50.0) * 50.0,
                        SideConstraint   = "EITHER",
                        MinSpacingMm     = 1500.0,
                        Priority         = 90,
                        Notes            = $"Learned from {c.Count} sample(s) on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.",
                    });
                }

                if (rules.Count == 0)
                {
                    TaskDialog.Show("STING — Learn Placement",
                        $"Walked {clusters.Count} cluster(s) but every cluster had <2 samples. Place at least 2 instances of the same category in similarly-named rooms.");
                    return Result.Cancelled;
                }

                string dir = Path.GetDirectoryName(doc.PathName);
                string path = Path.Combine(dir, "STING_PLACEMENT_RULES.learned.json");
                var set = new PlacementRuleSet
                {
                    Version = "v4",
                    Description = $"Learned by LearnPlacementV4Command on {DateTime.UtcNow:O}; review and merge into .project.json before relying on the rules.",
                    Rules = rules,
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(set, Formatting.Indented));

                var td = new TaskDialog("STING — Learn Placement")
                {
                    MainInstruction = $"Wrote {rules.Count} learned rule(s).",
                    MainContent =
                        $"Path: {path}\n\n" +
                        "Review then either:\n" +
                        "  · Open Placement Centre → Import… and pick this file, or\n" +
                        "  · Rename to STING_PLACEMENT_RULES.project.json to make the rules win automatically.\n\n" +
                        $"Clusters inspected: {clusters.Count}",
                    CommonButtons = TaskDialogCommonButtons.Close,
                };
                td.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("LearnPlacementV4Command failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string ExtractKeyword(string roomName)
        {
            if (string.IsNullOrEmpty(roomName)) return "";
            var lower = roomName.ToLowerInvariant();
            // Pick the first known keyword that matches the room name. Order
            // matters: more-specific names beat generic ones.
            foreach (var kw in new[]
            {
                "kitchen", "bathroom", "wc", "toilet", "shower", "wetroom",
                "office", "study", "meeting", "conference", "board", "huddle",
                "corridor", "circulation", "passage", "stair", "lobby",
                "lab", "laboratory", "classroom", "lecture", "library",
                "ward", "bedroom", "patient", "plant", "boiler", "server",
                "comms", "idf", "mdf", "reception", "entrance", "exit",
                "canteen", "cafeteria", "dining", "cafe", "restaurant",
                "warehouse", "store", "garage", "carpark",
            })
            {
                if (lower.Contains(kw)) return kw;
            }
            // Fallback: first alphabetical token.
            var m = Regex.Match(lower, "[a-z]+");
            return m.Success ? m.Value : "";
        }

        private class ClusterStats
        {
            public string CategoryName { get; set; }
            public string RoomKeyword  { get; set; }
            public int Count;
            public double MountFtAcc;
            public int RoomCentreVotes;
            public int WallVotes;
        }
    }
}
