using StingTools.Core;
// StingTools Phase 177 — Toilet-Room Placer Service.
//
// Orchestrates the FixturePlacementEngine specifically for sanitary
// accommodation, adding:
//
//   • BS 6465-1:2006+A1:2009 occupant-ratio table to compute how many
//     WCs / urinals / basins each room requires.
//   • Accessible / unisex WC count checks per Approved Doc M (2015) and
//     Equality Act 2010.
//   • Multi-stall public-WC stall count estimation from room area.
//   • Post-placement diagnostic report surfaced in StingResultPanel.
//   • Optional PlumbingFixtureRouter pass to auto-route waste and supply.
//
// The service does NOT add rules or modify the JSON rule set — it uses the
// rules already loaded by PlacementRuleLoader (including toilet-fixtures.json).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System.Text.RegularExpressions;

namespace StingTools.Core.Placement
{
    // ──────────────────────────────────────────────────────────────────────
    // BS 6465 occupant provision models
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Occupant gender split assumption for BS 6465 calculation.</summary>
    public enum OccupantSplit { Equal5050, AllMale, AllFemale, MaleHeavy7030, FemaleHeavy3070 }

    /// <summary>BS 6465-1 building use classification.</summary>
    public enum BuildingUse
    {
        Office,
        Retail,
        Restaurant,
        EducationPrimary,
        EducationSecondary,
        Healthcare,
        Residential,
        Industrial,
        Assembly,
        Sports,
        Hotel,
    }

    /// <summary>
    /// Minimum fixture provision required for a sanitary accommodation
    /// area per BS 6465-1:2006+A1:2009 occupant tables.
    /// </summary>
    public class BS6465Provision
    {
        public int OccupantCount         { get; set; }
        public int MinWcsMale            { get; set; }
        public int MinWcsFemale          { get; set; }
        public int MinUrinalsMale        { get; set; }
        public int MinBasins             { get; set; }
        public int MinAccessibleWcs      { get; set; }
        public bool BabyChangeRequired   { get; set; }
        public BuildingUse Use           { get; set; }
        public OccupantSplit Split       { get; set; }

        public string Summary()
        {
            return $"BS 6465-1 for {OccupantCount} occupants ({Use}/{Split}): " +
                   $"WC M{MinWcsMale}/F{MinWcsFemale}, " +
                   $"Urinals {MinUrinalsMale}, " +
                   $"Basins {MinBasins}, " +
                   $"Accessible {MinAccessibleWcs}";
        }
    }

    /// <summary>
    /// Result of one ToiletRoomPlacerService run.
    /// </summary>
    public class ToiletRoomPlacementResult
    {
        public PlacementResult PlacementResult  { get; set; }
        public BS6465Provision Provision        { get; set; }
        public List<string> ComplianceGaps      { get; } = new List<string>();
        public List<string> Warnings            { get; } = new List<string>();
        public int RoomsProcessed               { get; set; }
        public int FixturesPlaced               { get; set; }

        public bool IsCompliant => ComplianceGaps.Count == 0;

        public string ReportText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== STING Toilet-Room Placement Report (Phase 177) ===");
            sb.AppendLine();
            if (Provision != null) sb.AppendLine(Provision.Summary());
            sb.AppendLine($"Rooms processed : {RoomsProcessed}");
            sb.AppendLine($"Fixtures placed : {FixturesPlaced}");
            sb.AppendLine($"Dry run         : {PlacementResult?.DryRun}");
            sb.AppendLine();
            if (ComplianceGaps.Any())
            {
                sb.AppendLine("COMPLIANCE GAPS:");
                foreach (var g in ComplianceGaps) sb.AppendLine($"  ⚠ {g}");
            }
            else sb.AppendLine("Compliance: PASS — all BS 6465 provisions met.");
            if (Warnings.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var w in Warnings) sb.AppendLine($"  • {w}");
            }
            return sb.ToString();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Main service
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// High-level toilet-room fixture placement orchestrator.
    ///
    /// Typical usage:
    /// <code>
    ///   var svc = new ToiletRoomPlacerService
    ///   {
    ///       OccupantCount = 120,
    ///       Use = BuildingUse.Office,
    ///       Split = OccupantSplit.Equal5050,
    ///       DryRun = false,
    ///       AutoRoutePlumbing = true,
    ///   };
    ///   using (var txn = new Transaction(doc, "STING Place Toilet Fixtures"))
    ///   {
    ///       txn.Start();
    ///       var result = svc.PlaceAll(doc, txn);
    ///       txn.Commit();
    ///       // show result.ReportText() in StingResultPanel
    ///   }
    /// </code>
    /// </summary>
    public class ToiletRoomPlacerService
    {
        // ── Configuration ────────────────────────────────────────────────

        public int            OccupantCount      { get; set; } = 50;
        public BuildingUse    Use                { get; set; } = BuildingUse.Office;
        public OccupantSplit  Split              { get; set; } = OccupantSplit.Equal5050;
        public bool           DryRun             { get; set; } = true;
        public bool           AutoRoutePlumbing  { get; set; } = false;

        /// <summary>
        /// Regex filter applied to Room.Name — when set, only rooms whose
        /// names match are included.  Leave null to use the rule-level
        /// RoomFilter on each rule (recommended).
        /// </summary>
        public string         RoomNameOverride   { get; set; } = null;

        // ── Internals ────────────────────────────────────────────────────

        private static readonly System.Text.RegularExpressions.Regex
            ToiletRoomRegex = new System.Text.RegularExpressions.Regex(
                @"(?i)toilet|wc|bathroom|washroom|lavatory|cloakroom|shower|wetroom|sanitary",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Discover all toilet rooms in the document, calculate the
        /// BS 6465 provision, run placement, check compliance.
        /// The caller must provide an open <see cref="Transaction"/>.
        /// </summary>
        public ToiletRoomPlacementResult PlaceAll(Document doc, Transaction txn)
        {
            var result = new ToiletRoomPlacementResult();

            // 1. Collect toilet rooms.
            var rooms = CollectToiletRooms(doc);
            result.RoomsProcessed = rooms.Count;

            if (rooms.Count == 0)
            {
                result.Warnings.Add(
                    "No toilet / WC / bathroom rooms found in the model. " +
                    "Ensure rooms are bounded, placed, and named appropriately.");
                return result;
            }

            // 2. Compute BS 6465 provision target.
            result.Provision = ComputeProvision(OccupantCount, Use, Split);

            // 3. Load placement rules (toilet-fixtures pack auto-merged).
            var rules = PlacementRuleLoader.LoadDefaults();

            // 4. Run placement engine.
            var roomIds = rooms.Select(r => r.Id).ToList();
            var pr = FixturePlacementEngine.PlaceFixturesInScope(doc, roomIds, rules, DryRun);
            result.PlacementResult = pr;
            result.FixturesPlaced  = pr.PlacedIds.Count;

            foreach (var w in pr.Warnings)
                result.Warnings.Add(w);

            // 5. Compliance check against provision.
            CheckCompliance(result, pr, rooms, doc);

            // 6. Optional plumbing routing.
            if (AutoRoutePlumbing && !DryRun && pr.PlacedIds.Count > 0)
                RouteNewFixtures(doc, pr, txn, result);

            return result;
        }

        // ── Room discovery ───────────────────────────────────────────────

        private List<Room> CollectToiletRooms(Document doc)
        {
            var filter = string.IsNullOrEmpty(RoomNameOverride)
                ? ToiletRoomRegex
                : new System.Text.RegularExpressions.Regex(
                      RoomNameOverride,
                      System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                      System.Text.RegularExpressions.RegexOptions.Compiled);

            return new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .OfType<Room>()
                .Where(r =>
                {
                    if (r.Area < 0.01) return false; // unplaced / zero-area
                    string name = r.Name ?? "";
                    return filter.IsMatch(name);
                })
                .ToList();
        }

        // ── BS 6465-1 provision calculator ──────────────────────────────

        /// <summary>
        /// Calculates minimum fixture provision per BS 6465-1:2006+A1:2009
        /// Tables 1–8 and Approved Doc M.
        /// </summary>
        public static BS6465Provision ComputeProvision(
            int occupants,
            BuildingUse use,
            OccupantSplit split)
        {
            var p = new BS6465Provision
            {
                OccupantCount = occupants,
                Use           = use,
                Split         = split,
            };

            // Gender split (male / female counts).
            (int male, int female) = SplitOccupants(occupants, split);

            switch (use)
            {
                case BuildingUse.Office:
                    // BS 6465-1 Table 1 — offices.
                    p.MinWcsMale   = WcRatio(male,   1, 1, 2, 25);   // 1 per 25 (max 2 if ≤25)
                    p.MinWcsFemale = WcRatio(female, 1, 1, 2, 20);   // 1 per 20
                    p.MinUrinalsMale = UrinalRatio(male, 1, 25);      // 1 per 25
                    p.MinBasins      = BasinRatio(male + female, 1, 20); // 1 per 20
                    break;

                case BuildingUse.Retail:
                    // BS 6465-1 Table 3.
                    p.MinWcsMale   = WcRatio(male,   1, 1, 2, 50);
                    p.MinWcsFemale = WcRatio(female, 1, 1, 3, 25);
                    p.MinUrinalsMale = UrinalRatio(male, 1, 50);
                    p.MinBasins      = BasinRatio(male + female, 1, 25);
                    break;

                case BuildingUse.Restaurant:
                    // BS 6465-1 Table 4.
                    p.MinWcsMale   = WcRatio(male,   1, 1, 2, 50);
                    p.MinWcsFemale = WcRatio(female, 1, 1, 3, 25);
                    p.MinUrinalsMale = UrinalRatio(male, 1, 25);
                    p.MinBasins      = BasinRatio(male + female, 1, 40);
                    break;

                case BuildingUse.EducationPrimary:
                    // BS 6465-1 Table 5a — primary (per BS 6465-3).
                    p.MinWcsMale   = Math.Max(1, (int)Math.Ceiling(male   / 10.0));
                    p.MinWcsFemale = Math.Max(1, (int)Math.Ceiling(female / 10.0));
                    p.MinUrinalsMale = Math.Max(1, (int)Math.Ceiling(male / 20.0));
                    p.MinBasins      = Math.Max(1, (int)Math.Ceiling((male + female) / 10.0));
                    break;

                case BuildingUse.EducationSecondary:
                    // BS 6465-1 Table 5b.
                    p.MinWcsMale   = Math.Max(1, (int)Math.Ceiling(male   / 20.0));
                    p.MinWcsFemale = Math.Max(1, (int)Math.Ceiling(female / 10.0));
                    p.MinUrinalsMale = Math.Max(1, (int)Math.Ceiling(male / 25.0));
                    p.MinBasins      = Math.Max(1, (int)Math.Ceiling((male + female) / 15.0));
                    break;

                case BuildingUse.Healthcare:
                    // HTM 04-01 / NHS Estates — 1 WC per 8 patients (ward baseline).
                    p.MinWcsMale   = Math.Max(1, (int)Math.Ceiling(male   / 8.0));
                    p.MinWcsFemale = Math.Max(1, (int)Math.Ceiling(female / 8.0));
                    p.MinUrinalsMale = 0; // healthcare wards: no urinals (infection control)
                    p.MinBasins      = male + female; // 1 basin per bed / person
                    break;

                case BuildingUse.Residential:
                    // Approved Doc G: 1 WC + 1 basin + 1 bath or shower per dwelling.
                    p.MinWcsMale   = 1;
                    p.MinWcsFemale = 0; // unisex
                    p.MinUrinalsMale = 0;
                    p.MinBasins      = 1;
                    break;

                case BuildingUse.Industrial:
                    // BS 6465-1 Table 2.
                    p.MinWcsMale   = WcRatio(male,   1, 1, 2, 25);
                    p.MinWcsFemale = WcRatio(female, 1, 1, 3, 25);
                    p.MinUrinalsMale = UrinalRatio(male, 1, 25);
                    p.MinBasins      = BasinRatio(male + female, 1, 15);
                    break;

                case BuildingUse.Assembly:
                    // BS 6465-1 Table 6 (theatre / cinema / sports).
                    p.MinWcsMale   = WcRatio(male,   1, 1, 2, 100);
                    p.MinWcsFemale = WcRatio(female, 1, 1, 3,  50);
                    p.MinUrinalsMale = UrinalRatio(male, 1, 50);
                    p.MinBasins      = BasinRatio(male + female, 1, 50);
                    break;

                case BuildingUse.Sports:
                    // BS 6465-1 Table 7 (changing rooms / sports facilities).
                    p.MinWcsMale   = WcRatio(male,   1, 1, 2, 30);
                    p.MinWcsFemale = WcRatio(female, 1, 1, 3, 20);
                    p.MinUrinalsMale = UrinalRatio(male, 1, 20);
                    p.MinBasins      = BasinRatio(male + female, 1, 10);
                    break;

                case BuildingUse.Hotel:
                    // HTM hotel standard / BS 6465-1 Table 3 variant.
                    p.MinWcsMale   = WcRatio(male,   1, 1, 2, 100);
                    p.MinWcsFemale = WcRatio(female, 1, 1, 3,  50);
                    p.MinUrinalsMale = UrinalRatio(male, 1, 100);
                    p.MinBasins      = BasinRatio(male + female, 1, 60);
                    break;

                default:
                    // Generic fallback.
                    p.MinWcsMale   = Math.Max(1, (int)Math.Ceiling(male   / 25.0));
                    p.MinWcsFemale = Math.Max(1, (int)Math.Ceiling(female / 20.0));
                    p.MinUrinalsMale = Math.Max(1, (int)Math.Ceiling(male / 25.0));
                    p.MinBasins      = Math.Max(1, (int)Math.Ceiling((male + female) / 20.0));
                    break;
            }

            // Approved Doc M: at least 1 accessible WC where >5 WCs provided,
            // and at least 1 when publicly accessible regardless of count.
            int totalWcs = p.MinWcsMale + p.MinWcsFemale;
            p.MinAccessibleWcs = (totalWcs >= 5 || IsPublicUse(use)) ? 1 : 0;

            // Baby changing: required in public buildings serving families
            // (Equality Act 2010 / Building Regs M4).
            p.BabyChangeRequired = IsPublicUse(use) && occupants >= 50;

            return p;
        }

        // ── Compliance check ─────────────────────────────────────────────

        private static void CheckCompliance(
            ToiletRoomPlacementResult result,
            PlacementResult pr,
            List<Room> rooms,
            Document doc)
        {
            var prov = result.Provision;
            if (prov == null) return;

            // Count placed fixtures by rule id prefix.
            int wcPlaced       = CountByRulePrefix(pr, "toilet-wc");
            int urinalPlaced   = CountByRulePrefix(pr, "toilet-urinal");
            int basinPlaced    = CountByRulePrefix(pr, "toilet-basin");
            int accessPlaced   = CountByRulePrefix(pr, "toilet-wc-comfort") +
                                 CountByRulePrefix(pr, "toilet-wc-wall-hung");
            int babyPlaced     = CountByRulePrefix(pr, "toilet-baby");

            int minWcTotal = prov.MinWcsMale + prov.MinWcsFemale;

            if (wcPlaced < minWcTotal)
                result.ComplianceGaps.Add(
                    $"WC count: placed {wcPlaced}, required {minWcTotal} " +
                    $"(M:{prov.MinWcsMale} + F:{prov.MinWcsFemale}) per BS 6465-1.");

            if (urinalPlaced < prov.MinUrinalsMale)
                result.ComplianceGaps.Add(
                    $"Urinals: placed {urinalPlaced}, required {prov.MinUrinalsMale} per BS 6465-1.");

            if (basinPlaced < prov.MinBasins)
                result.ComplianceGaps.Add(
                    $"Basins: placed {basinPlaced}, required {prov.MinBasins} per BS 6465-1.");

            if (prov.MinAccessibleWcs > 0 && accessPlaced < prov.MinAccessibleWcs)
                result.ComplianceGaps.Add(
                    $"Accessible WC: placed {accessPlaced}, required {prov.MinAccessibleWcs} " +
                    $"per Approved Doc M / Equality Act 2010.");

            if (prov.BabyChangeRequired && babyPlaced < 1)
                result.ComplianceGaps.Add(
                    "Baby changing station required per Equality Act 2010 / Approved Doc M vol.2 cl.5.2 — none placed.");
        }

        // ── Optional plumbing routing ────────────────────────────────────

        private void RouteNewFixtures(
            Document doc,
            PlacementResult pr,
            Transaction txn,
            ToiletRoomPlacementResult result)
        {
            try
            {
                var router = new Routing.PlumbingFixtureRouter { DryRun = false };
                var fixtures = pr.PlacedIds
                    .Select(id => doc.GetElement(id) as FamilyInstance)
                    .Where(fi => fi != null &&
                                 fi.Category?.Name.IndexOf("Plumbing",
                                     StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                if (fixtures.Count == 0)
                {
                    result.Warnings.Add(
                        "Auto-route: no plumbing fixtures found in placed set " +
                        "(specialty equipment does not require pipe routing).");
                    return;
                }

                var routeResult = router.RouteAll(doc, fixtures, txn);
                result.Warnings.AddRange(routeResult.WarningMessages);
                result.Warnings.AddRange(routeResult.FailureMessages);
                StingLog.Info($"ToiletRoomPlacer: auto-routed {routeResult.PipesCreated} pipes, " +
                              $"{routeResult.AavsPlaced} AAVs from {fixtures.Count} plumbing fixtures.");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Auto-route failed: {ex.Message}");
                StingLog.Warn($"ToiletRoomPlacerService.RouteNewFixtures: {ex.Message}");
            }
        }

        // ── BS 6465 formula helpers ──────────────────────────────────────

        private static (int male, int female) SplitOccupants(int total, OccupantSplit split)
        {
            return split switch
            {
                OccupantSplit.AllMale         => (total, 0),
                OccupantSplit.AllFemale       => (0, total),
                OccupantSplit.MaleHeavy7030   => (total * 70 / 100, total * 30 / 100),
                OccupantSplit.FemaleHeavy3070 => (total * 30 / 100, total * 70 / 100),
                _                             => (total / 2, total - total / 2), // 50/50
            };
        }

        /// <summary>
        /// Generic WC ratio: min 1 for any non-zero occupant count,
        /// then 1 extra per <paramref name="perN"/> thereafter.
        /// </summary>
        private static int WcRatio(int n, int minAtOne, int minAtTwo, int addPerN, int perN)
        {
            if (n <= 0) return 0;
            if (n <= perN) return minAtOne;
            if (n <= 2 * perN) return minAtTwo;
            return minAtTwo + (int)Math.Ceiling((double)(n - 2 * perN) / perN);
        }

        private static int UrinalRatio(int n, int minAtOne, int perN)
        {
            if (n <= 0) return 0;
            return Math.Max(minAtOne, (int)Math.Ceiling((double)n / perN));
        }

        private static int BasinRatio(int n, int minAtOne, int perN)
        {
            if (n <= 0) return 0;
            return Math.Max(minAtOne, (int)Math.Ceiling((double)n / perN));
        }

        private static bool IsPublicUse(BuildingUse use)
            => use == BuildingUse.Retail
            || use == BuildingUse.Restaurant
            || use == BuildingUse.Assembly
            || use == BuildingUse.Sports
            || use == BuildingUse.Hotel;

        private static int CountByRulePrefix(PlacementResult pr, string prefix)
        {
            int count = 0;
            foreach (var kv in pr.CountsByRule)
                if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    count += kv.Value;
            return count;
        }
    }
}
