// ════════════════════════════════════════════════════════════════════════════
// Bs7671CableSizing — the Revit-free BS 7671 Appendix 4 cable-sizing method.
//
// ELEC-3. The BS 7671 path in CableSizerEngine used to pick the minimum size from
// an uncited threshold ladder (2.5 mm² = 17 A, where Table 4D2A method C gives
// 27 A), multiply by a flat XLPE ×1.18 "insulation factor", apply the PVC
// ambient column to every insulation, ignore grouping, and never read the
// tabulated capacities in STING_WIRE_TABLES.json. This file is the replacement:
//
//   1. It      tabulated current-carrying capacity for the conductor / insulation /
//              cable type / reference method, from a CITED table in the data file.
//              A combination with no table is REFUSED, never approximated.
//   2. Ca Cg Ci Cf   ambient (Table 4B1, per insulation), grouping (Table 4C1),
//              thermal insulation (caller-supplied, Reg 523.9 / Table 52.2) and
//              Cf = 0.725 for a BS 3036 semi-enclosed fuse (Appendix 4 §5.1.1).
//   3. In      the smallest standard device rating with In ≥ Ib (Reg 433.1.1(i)).
//   4. It ≥ In / (Ca·Cg·Ci·Cf)   Appendix 4 §5.1.1 — which, with Iz = It·Ca·Cg·Ci,
//              gives In ≤ Iz (Reg 433.1.1(ii)); for BS EN 60898 / 61009 / 60947-2
//              devices I2 ≤ 1.45·In so 433.1.1(iii) follows, and Cf covers BS 3036.
//   5. VD      mV/A/m × Ib × L / 1000 from the table's own voltage-drop companion
//              (4D2B for 4D2A), single-phase or three-phase column, against the
//              caller's limit. The tabulated mV/A/m is at maximum conductor
//              temperature, so this is conservative (Appendix 4 §6.1 permits a
//              correction for Ib < It; it is deliberately not taken here).
//
// Every factor used is written into Basis so a number on a drawing can be defended.
// NOT covered (and said so in Basis): adiabatic / fault-energy check (Reg 434.5.2),
// earth-fault loop Zs, harmonics on the neutral, and a VD that includes the drop
// upstream of the circuit's origin.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Electrical
{
    /// <summary>One tabulated size row: capacity It and voltage drop mV/A/m.</summary>
    public sealed class Bs7671CapacityRow
    {
        public double CsaMm2 { get; set; }
        public double It1ph { get; set; }
        public double It3ph { get; set; }
        public double MvAm1ph { get; set; }
        public double MvAm3ph { get; set; }
        /// <summary>It_1ph and It_3ph agree between two independent transcriptions.</summary>
        public bool Verified { get; set; }
        /// <summary>mV/A/m agree between two independent transcriptions. Defaults to
        /// <see cref="Verified"/> for data that carries a single flag.</summary>
        public bool MvVerified { get; set; }
    }

    /// <summary>A BS 7671 Appendix 4 capacity table for one reference method.</summary>
    public sealed class Bs7671CapacityTable
    {
        public string Id { get; set; }             // "4D2A"
        public string VoltDropTable { get; set; }  // "4D2B"
        public string Description { get; set; }
        public string Conductor { get; set; }      // "Cu"
        public string Insulation { get; set; }     // "PVC70"
        /// <summary>"Multicore" (4D2A, 4E2A), "SingleCore" (4D1A) or "ArmouredMulticore" (4D4A, 4E4A).</summary>
        public string CableType { get; set; } = Bs7671Data.DefaultCableType;
        public double MaxConductorTempC { get; set; }
        public string InstallMethod { get; set; }  // "C"
        public List<Bs7671CapacityRow> Rows { get; } = new List<Bs7671CapacityRow>();

        /// <summary>Corporate (the shipped, source-checked transcription) or Project (from a
        /// project's _BIM_COORD/bs7671_wire_tables.json override — ELEC-21).</summary>
        public Bs7671Origin Origin { get; set; } = Bs7671Origin.Corporate;
        /// <summary>Project tables: where the figures came from (the override's "source").</summary>
        public string SourceNote { get; set; }
        /// <summary>Project tables: the corporate table id this one replaced, if any.</summary>
        public string ReplacedCorporateId { get; set; }
        /// <summary>Project tables: the two-source attestation, when one was given and valid.</summary>
        public string TwoSourceCheckNote { get; set; }

        /// <summary>How reports cite this table. Corporate: "Table 4D2A". Project: says so,
        /// with what it replaced and its source, so project data never reads as corporate.</summary>
        public string Cite()
        {
            if (Origin == Bs7671Origin.Corporate) return $"Table {Id}";
            var s = new StringBuilder($"project table {Id} (");
            if (!string.IsNullOrEmpty(ReplacedCorporateId)) s.Append($"replaces corporate Table {ReplacedCorporateId}; ");
            s.Append($"source: {SourceNote}");
            if (!string.IsNullOrEmpty(TwoSourceCheckNote)) s.Append($"; two-source check: {TwoSourceCheckNote}");
            return s.Append(')').ToString();
        }

        /// <summary>How reports cite the voltage-drop companion table.</summary>
        public string CiteVoltDrop()
            => Origin == Bs7671Origin.Corporate ? $"Table {VoltDropTable}" : $"project table {VoltDropTable}";
    }

    /// <summary>Where a table or factor came from.</summary>
    public enum Bs7671Origin { Corporate, Project }

    /// <summary>The Appendix 4 data the sizer runs on — parsed from STING_WIRE_TABLES.json.</summary>
    public sealed class Bs7671Data
    {
        public List<Bs7671CapacityTable> Tables { get; } = new List<Bs7671CapacityTable>();
        /// <summary>Insulation → (ambient °C → Ca), Table 4B1.</summary>
        public Dictionary<string, SortedDictionary<double, double>> Ambient { get; }
            = new Dictionary<string, SortedDictionary<double, double>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Arrangement → (circuit count → Cg), Table 4C1.</summary>
        public Dictionary<string, SortedDictionary<int, double>> Grouping { get; }
            = new Dictionary<string, SortedDictionary<int, double>>(StringComparer.OrdinalIgnoreCase);
        public double SemiEnclosedFuseCf { get; set; } = 0.725;

        public const string DefaultCableType = "Multicore";

        // ── Provenance (ELEC-21). Filled by Bs7671TableLayering; empty for plain FromJson. ──
        /// <summary>Set when the data cannot be trusted (a malformed project override, or a
        /// missing corporate file). The sizer refuses with it; nothing is substituted.</summary>
        public string LoadError { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        /// <summary>The project override file applied, or null.</summary>
        public string OverrideFile { get; set; }
        public bool HasProjectLayer { get; set; }
        /// <summary>Human-readable list of what the project layer replaced, added or removed.</summary>
        public List<string> ProjectChanges { get; } = new List<string>();
        /// <summary>Lookup keys (see <see cref="Key"/>) the project layer removed.</summary>
        public HashSet<string> RemovedKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Insulations whose Table 4B1 row came from the project layer.</summary>
        public HashSet<string> AmbientFromProject { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Arrangements whose Table 4C1 row came from the project layer.</summary>
        public HashSet<string> GroupingFromProject { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool CfFromProject { get; set; }

        /// <summary>The lookup identity of a table: what <see cref="FindTable"/> matches on.</summary>
        public static string Key(string conductor, string insulation, string cableType, string method)
            => string.Join("|", (conductor ?? "").Trim().ToUpperInvariant(), (insulation ?? "").Trim().ToUpperInvariant(),
                           (string.IsNullOrWhiteSpace(cableType) ? DefaultCableType : cableType.Trim()).ToUpperInvariant(),
                           NormaliseMethod(method));

        public static string Key(Bs7671CapacityTable t) => Key(t.Conductor, t.Insulation, t.CableType, t.InstallMethod);

        /// <summary>
        /// BS 7671 reference-method letter for a method code. The IEC sub-codes the panels
        /// and stored parameters use name the same columns once the cable type is known:
        /// A1 (single-core in conduit) and A2 (multicore in conduit) are BS method A of
        /// 4D1A and 4D2A respectively; B1 / B2 likewise method B.
        /// </summary>
        public static string NormaliseMethod(string method)
        {
            string m = (method ?? "").Trim().ToUpperInvariant();
            if (m == "A1" || m == "A2") return "A";
            if (m == "B1" || m == "B2") return "B";
            return m;
        }

        /// <summary>The capacity table for a conductor / insulation / cable type / reference method, or null.</summary>
        public Bs7671CapacityTable FindTable(string material, string insulation, string method, string cableType = DefaultCableType)
        {
            string m = NormaliseMethod(method);
            string ct = string.IsNullOrWhiteSpace(cableType) ? DefaultCableType : cableType.Trim();
            string key = ConductorKey(material);
            return Tables.FirstOrDefault(t =>
                string.Equals(t.Conductor, key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Insulation, (insulation ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.CableType, ct, StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormaliseMethod(t.InstallMethod), m, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Tabulated It for an exact tabulated CSA (±0.01 mm²); 0 when the size is not in the table.</summary>
        public static double TabulatedIt(Bs7671CapacityTable table, double csaMm2, int phases)
        {
            var row = table?.Rows.FirstOrDefault(r => Math.Abs(r.CsaMm2 - csaMm2) < 0.01);
            if (row == null) return 0;
            return phases == 3 ? row.It3ph : row.It1ph;
        }

        /// <summary>
        /// The highest tabulated It for this conductor and size across every shipped table
        /// (any insulation, cable type or reference method), and the table it came from.
        /// When the circuit's cable is unknown this is the only honest upper bound: a device
        /// above it is too large for any cable of that size. 0 when no table has the size.
        /// </summary>
        /// <summary>The tables' conductor key for material text: "Cu" / "Al" / "CCA" when the
        /// text is recognised ("copper", "ALUMINIUM" …), else the text as given (matches nothing).</summary>
        private static string ConductorKey(string material)
            => string.IsNullOrWhiteSpace(material) ? "Cu"
             : StingTools.Standards.NEC2023.ConductorMaterialText.TryParse(material, out var m) ? StingTools.Standards.NEC2023.ConductorMaterialText.Label(m) : material.Trim();

        public double MaxTabulatedIt(string material, double csaMm2, int phases, out Bs7671CapacityTable from)
        {
            from = null;
            double best = 0;
            string key = ConductorKey(material);
            foreach (var t in Tables)
            {
                if (!string.Equals(t.Conductor, key, StringComparison.OrdinalIgnoreCase)) continue;
                double it = TabulatedIt(t, csaMm2, phases);
                if (it > best) { best = it; from = t; }
            }
            return best;
        }

        /// <summary>
        /// The highest mV/A/m any loaded table gives for this conductor, size and phase
        /// column, and which table gave it. With no cable record on a circuit this bounds
        /// its voltage drop from above: a drop at or under the limit on this figure is under
        /// it on any loaded table; a drop over it proves nothing. 0 when no table carries it.
        /// </summary>
        public double MaxTabulatedMvAm(string material, double csaMm2, int phases, out Bs7671CapacityTable from)
        {
            from = null;
            double best = 0;
            foreach (var t in Tables)
            {
                if (!string.Equals(t.Conductor, material, StringComparison.OrdinalIgnoreCase)) continue;
                var row = t.Rows.FirstOrDefault(x => Math.Abs(x.CsaMm2 - csaMm2) < 1e-6);
                if (row == null) continue;
                double mv = phases == 3 ? row.MvAm3ph : row.MvAm1ph;
                if (mv > best) { best = mv; from = t; }
            }
            return best;
        }

        /// <summary>Parse the <c>bs7671Appendix4</c> section. Returns an empty set (which
        /// makes the sizer refuse) when the section is absent — never a fallback table.</summary>
        public static Bs7671Data FromJson(JObject root)
        {
            var d = new Bs7671Data();
            var sec = root?["bs7671Appendix4"] as JObject;
            if (sec == null) return d;

            foreach (var t in (sec["capacityTables"] as JArray ?? new JArray()).OfType<JObject>())
                d.Tables.Add(ParseTable(t, strictFlags: false));

            if (sec["ambientTemperatureFactors"] is JObject amb)
                foreach (var p in amb.Properties().Where(p => !p.Name.StartsWith("_") && p.Value is JObject))
                    d.Ambient[p.Name] = ParseAmbient((JObject)p.Value);

            if (sec["groupingFactors"] is JObject grp)
                foreach (var p in grp.Properties().Where(p => !p.Name.StartsWith("_") && p.Value is JObject))
                    d.Grouping[p.Name] = ParseGrouping((JObject)p.Value);

            if (sec["semiEnclosedFuseFactorCf"] != null)
                d.SemiEnclosedFuseCf = sec["semiEnclosedFuseFactorCf"].Value<double>();
            return d;
        }

        /// <summary>One capacity table. <paramref name="strictFlags"/> (project data): a row's
        /// mvVerified must be stated — it does NOT default to verified, which would quietly
        /// mark an override's mV/A/m as checked.</summary>
        internal static Bs7671CapacityTable ParseTable(JObject t, bool strictFlags)
        {
            var table = new Bs7671CapacityTable
            {
                Id = (string)t["id"],
                VoltDropTable = (string)t["voltDropTable"],
                Description = (string)t["description"],
                Conductor = (string)t["conductor"],
                Insulation = (string)t["insulation"],
                MaxConductorTempC = t["maxConductorTempC"]?.Value<double>() ?? 0,
                InstallMethod = (string)t["installMethod"],
                CableType = (string)t["cableType"] ?? DefaultCableType,
            };
            foreach (var r in (t["sizes"] as JArray ?? new JArray()).OfType<JObject>())
            {
                bool verified = Bool(r["verified"]);
                bool mvAbsent = r["mvVerified"] == null || r["mvVerified"].Type == JTokenType.Null;
                table.Rows.Add(new Bs7671CapacityRow
                {
                    CsaMm2 = Num(r["csaMm2"]),
                    It1ph = Num(r["It_1ph"]),
                    It3ph = Num(r["It_3ph"]),
                    MvAm1ph = Num(r["mVAm_1ph"]),
                    MvAm3ph = Num(r["mVAm_3ph"]),
                    Verified = verified,
                    MvVerified = mvAbsent ? (!strictFlags && verified) : Bool(r["mvVerified"]),
                });
            }
            table.Rows.Sort((a, b) => a.CsaMm2.CompareTo(b.CsaMm2));
            return table;
        }

        internal static SortedDictionary<double, double> ParseAmbient(JObject o)
        {
            var map = new SortedDictionary<double, double>();
            foreach (var q in o.Properties().Where(q => !q.Name.StartsWith("_")))
                map[double.Parse(q.Name, CultureInfo.InvariantCulture)] = q.Value.Value<double>();
            return map;
        }

        internal static SortedDictionary<int, double> ParseGrouping(JObject o)
        {
            var map = new SortedDictionary<int, double>();
            foreach (var q in o.Properties().Where(q => !q.Name.StartsWith("_")))
                map[int.Parse(q.Name, CultureInfo.InvariantCulture)] = q.Value.Value<double>();
            return map;
        }

        // A null cell (not transcribed) reads as 0, which the sizer treats as "not carried".
        private static double Num(JToken t) => t == null || t.Type == JTokenType.Null ? 0 : t.Value<double>();
        private static bool Bool(JToken t) => t != null && t.Type == JTokenType.Boolean && t.Value<bool>();
    }

    public sealed class Bs7671SizingInput
    {
        /// <summary>Design current Ib, A.</summary>
        public double DesignCurrentA { get; set; }
        /// <summary>U0 for single-phase (e.g. 230), line voltage for three-phase (e.g. 400).</summary>
        public double VoltageV { get; set; } = 230.0;
        public int Phases { get; set; } = 1;
        public double LengthM { get; set; }
        public string InstallMethod { get; set; } = "C";
        public string Insulation { get; set; } = "PVC70";
        public string Material { get; set; } = "Cu";
        /// <summary>"Multicore", "SingleCore" or "ArmouredMulticore" — picks 4D2A/4E2A, 4D1A or 4D4A/4E4A.</summary>
        public string CableType { get; set; } = Bs7671Data.DefaultCableType;
        public double AmbientTempC { get; set; } = 30.0;
        /// <summary>Number of circuits in the group (1 = not grouped).</summary>
        public int GroupedCircuits { get; set; } = 1;
        /// <summary>Table 4C1 arrangement key: "Bunched", "SingleLayerWall",
        /// "SingleLayerPerforatedTray" or "SingleLayerLadderCleats".</summary>
        public string GroupingArrangement { get; set; } = "Bunched";
        /// <summary>Thermal-insulation factor Ci (Reg 523.9 / Table 52.2); 1.0 when none.</summary>
        public double Ci { get; set; } = 1.0;
        /// <summary>An extra caller-supplied derating (e.g. the feeder panel's "derate"),
        /// multiplied in with the tabulated factors and named in Basis. 1.0 = none.</summary>
        public double ExtraDerate { get; set; } = 1.0;
        public bool SemiEnclosedFuse { get; set; }
        public double VdLimitPct { get; set; } = 5.0;
        /// <summary>Standard device ratings to choose In from, ascending.</summary>
        public int[] DeviceRatingsA { get; set; }
        public string DeviceLabel { get; set; } = "BS EN 60898 MCB";
    }

    public sealed class Bs7671SizingResult
    {
        public bool Sized { get; set; }
        public string Refusal { get; set; } = "";
        public double DesignCurrentA { get; set; }
        public int DeviceRatingA { get; set; }
        public double CsaMm2 { get; set; }
        public double TabulatedItA { get; set; }
        public double IzA { get; set; }
        public double RequiredItA { get; set; }
        public double Ca { get; set; } = 1.0;
        public double Cg { get; set; } = 1.0;
        public double Ci { get; set; } = 1.0;
        public double Cf { get; set; } = 1.0;
        public double MvAm { get; set; }
        public double VoltDropV { get; set; }
        public double VoltDropPct { get; set; }
        /// <summary>The smallest size that carries the current, before voltage drop.</summary>
        public double CapacityOnlyCsaMm2 { get; set; }
        /// <summary>True when the chosen row's It or mV/A/m is not two-source checked.</summary>
        public bool UnverifiedRow => UnverifiedCapacity || UnverifiedVoltDrop;
        /// <summary>The chosen row's It_1ph / It_3ph are not two-source checked.</summary>
        public bool UnverifiedCapacity { get; set; }
        /// <summary>The chosen row's mV/A/m is not two-source checked.</summary>
        public bool UnverifiedVoltDrop { get; set; }
        /// <summary>Tables, factors and assumptions used — for the derivation note.</summary>
        public string Basis { get; set; } = "";
        /// <summary>The capacity table came from the project override, not the corporate file.</summary>
        public bool ProjectTable { get; set; }
        /// <summary>
        /// ELEC-25: BS 7671 Appendix 4 §6.1 voltage drop corrected for the conductor running
        /// below its maximum temperature at Ib, as a separate, optional figure. Null when the
        /// correction does not apply (see <see cref="LoadCorrectionNote"/>). The size is always
        /// chosen on the tabulated figure; this never makes a cable smaller.
        /// </summary>
        public double? LoadCorrectedVoltDropPct { get; set; }
        public double? LoadCorrectionCt { get; set; }
        public string LoadCorrectionNote { get; set; } = "";
        /// <summary>Ca, Cg or Cf came from the project override.</summary>
        public bool ProjectFactors { get; set; }
    }

    public static class Bs7671CableSizer
    {
        public static Bs7671SizingResult Size(Bs7671SizingInput input, Bs7671Data data)
        {
            var r = new Bs7671SizingResult();
            if (input == null) return Refuse(r, "No input.");
            r.DesignCurrentA = input.DesignCurrentA;
            if (data != null && !string.IsNullOrEmpty(data.LoadError))
                return Refuse(r, data.LoadError);
            if (data == null || data.Tables.Count == 0)
                return Refuse(r, "STING_WIRE_TABLES.json has no bs7671Appendix4 capacity tables; nothing was sized.");
            if (input.DesignCurrentA <= 0) return Refuse(r, "Design current Ib must be > 0.");
            if (input.VoltageV <= 0) return Refuse(r, "Voltage must be > 0.");
            if (input.LengthM < 0) return Refuse(r, "Length must be ≥ 0.");

            string material = string.IsNullOrWhiteSpace(input.Material) ? "Cu" : input.Material.Trim();
            if (StingTools.Standards.NEC2023.ConductorMaterialText.IsCopperClad(material))
                return Refuse(r, "BS 7671 sizing: " + StingTools.Standards.NEC2023.ConductorMaterialText.NoBsDataRefusal + ".");
            string insulation = (input.Insulation ?? "").Trim();
            string method = (input.InstallMethod ?? "").Trim();
            string cableType = string.IsNullOrWhiteSpace(input.CableType) ? Bs7671Data.DefaultCableType : input.CableType.Trim();

            var table = data.FindTable(material, insulation, method, cableType);
            if (table == null && data.RemovedKeys.Contains(Bs7671Data.Key(material, insulation, cableType, method)))
                return Refuse(r,
                    $"The project override {data.OverrideFile} removes the table for {material} / {insulation} / {cableType} / " +
                    $"reference method {method}; nothing was sized. Supply a replacement there, or remove the entry.");
            if (table == null)
            {
                string have = string.Join(", ", data.Tables.Select(t =>
                    $"{t.Id} {t.Conductor}/{t.Insulation}/{t.CableType} method {t.InstallMethod}"));
                return Refuse(r,
                    $"No BS 7671 Appendix 4 capacity table is shipped for {material} / {insulation} / {cableType} / " +
                    $"reference method {method}. Available: {have}. Add the table from the printed " +
                    "Appendix 4 to STING_WIRE_TABLES.json — the sizer will not approximate it.");
            }

            // ── Ca — Table 4B1, insulation-specific ─────────────────────────────
            var basis = new StringBuilder();
            if (!data.Ambient.TryGetValue(insulation, out var ambient) || ambient.Count == 0)
                return Refuse(r, $"No Table 4B1 ambient factors for {insulation}.");
            double ta = input.AmbientTempC;
            double ca;
            string caNote;
            double minT = ambient.Keys.First(), maxT = ambient.Keys.Last();
            if (ta <= minT)
            {
                ca = ambient[minT];
                caNote = ta < minT ? $" (ta {ta:0} °C below {minT:0} °C: no uplift taken)" : "";
            }
            else if (ta > maxT)
                return Refuse(r, $"Ambient {ta:0} °C is above the highest Table 4B1 row carried for {insulation} ({maxT:0} °C).");
            else
            {
                double key = ambient.Keys.First(k => k >= ta);   // hotter row = conservative
                ca = ambient[key];
                caNote = key != ta ? $" (ta {ta:0} °C read at the {key:0} °C row)" : "";
            }
            r.Ca = ca;
            bool caProject = data.AmbientFromProject.Contains(insulation);

            // ── Cg — Table 4C1 ──────────────────────────────────────────────────
            int n = Math.Max(1, input.GroupedCircuits);
            double cg = 1.0;
            string cgNote = "1 circuit, not grouped";
            if (n > 1)
            {
                string arr = string.IsNullOrWhiteSpace(input.GroupingArrangement) ? "Bunched" : input.GroupingArrangement;
                if (!data.Grouping.TryGetValue(arr, out var grp) || grp.Count == 0)
                    return Refuse(r, $"No Table 4C1 grouping row '{arr}' in STING_WIRE_TABLES.json.");
                int key;
                if (grp.Keys.Any(k => k >= n)) key = grp.Keys.First(k => k >= n);
                // Single-layer arrangements: no further reduction beyond 9 circuits (Table 4C1).
                else if (arr.StartsWith("SingleLayer", StringComparison.OrdinalIgnoreCase)) key = grp.Keys.Last();
                else return Refuse(r, $"{n} circuits exceeds the largest Table 4C1 '{arr}' row ({grp.Keys.Last()}).");
                cg = grp[key];
                cgNote = $"{n} circuits {arr}" + (key != n ? $" (read at the {key}-circuit row)" : "");
            }
            r.Cg = cg;
            bool cgProject = n > 1 && data.GroupingFromProject.Contains(
                string.IsNullOrWhiteSpace(input.GroupingArrangement) ? "Bunched" : input.GroupingArrangement);

            double ci = input.Ci > 0 && input.Ci <= 1.0 ? input.Ci : 1.0;
            r.Ci = ci;
            double extra = input.ExtraDerate > 0 && input.ExtraDerate <= 1.0 ? input.ExtraDerate : 1.0;
            double cf = input.SemiEnclosedFuse ? data.SemiEnclosedFuseCf : 1.0;
            r.Cf = cf;
            bool cfProject = input.SemiEnclosedFuse && data.CfFromProject;
            r.ProjectTable = table.Origin == Bs7671Origin.Project;
            r.ProjectFactors = caProject || cgProject || cfProject;
            const string po = " [project override]";

            // ── In ≥ Ib ─────────────────────────────────────────────────────────
            int[] ratings = input.DeviceRatingsA ?? new int[0];
            int inA = ratings.Where(x => x >= input.DesignCurrentA).DefaultIfEmpty(0).Min();
            if (inA <= 0)
                return Refuse(r, $"No {input.DeviceLabel} rating ≥ Ib {input.DesignCurrentA:0.0} A.");
            r.DeviceRatingA = inA;

            double factors = ca * cg * ci * extra;
            double requiredIt = inA / (factors * cf);
            r.RequiredItA = requiredIt;
            bool threePh = input.Phases == 3;
            double limit = input.VdLimitPct > 0 ? input.VdLimitPct : 5.0;

            Bs7671CapacityRow winner = null, capacityOnly = null;
            var noMv = new List<double>();
            foreach (var row in table.Rows)
            {
                double it = threePh ? row.It3ph : row.It1ph;
                if (it <= 0 || it < requiredIt) continue;
                if (capacityOnly == null) capacityOnly = row;
                double mv = threePh ? row.MvAm3ph : row.MvAm1ph;
                // A size whose mV/A/m is not carried cannot be checked for voltage drop,
                // so it cannot be selected; it is named in the refusal instead.
                if (mv <= 0) { noMv.Add(row.CsaMm2); continue; }
                double vd = mv * input.DesignCurrentA * input.LengthM / 1000.0;
                if (vd / input.VoltageV * 100.0 <= limit) { winner = row; break; }
            }

            string head =
                $"BS 7671 Appendix 4, {table.Cite()} ({table.Description}, method {table.InstallMethod}, " +
                $"{(threePh ? "three-phase" : "single-phase")} column, {table.CableType}) + {table.CiteVoltDrop()} mV/A/m. " +
                $"Ib={input.DesignCurrentA:0.0} A; In={inA} A {input.DeviceLabel} (In ≥ Ib, Reg 433.1.1). " +
                $"Ca={ca:0.00} Table 4B1 {insulation}{caNote}{(caProject ? po : "")}; Cg={cg:0.00} Table 4C1 ({cgNote}){(cgProject ? po : "")}; Ci={ci:0.00}" +
                (extra < 1.0 ? $"; user derate={extra:0.00}" : "") +
                $"; Cf={cf:0.000}{(input.SemiEnclosedFuse ? " (BS 3036 semi-enclosed fuse)" : "")}{(cfProject ? po : "")}. " +
                $"Required It ≥ In/(Ca·Cg·Ci{(extra < 1.0 ? "·derate" : "")}·Cf) = {requiredIt:0.0} A.";

            if (capacityOnly == null)
            {
                r.Basis = head;
                return Refuse(r, $"No size in {table.Cite()} has It ≥ {requiredIt:0.0} A (largest tabulated " +
                                 $"{table.Rows.Last().CsaMm2:0} mm²). Parallel cables are not sized here.");
            }
            r.CapacityOnlyCsaMm2 = capacityOnly.CsaMm2;
            if (winner == null)
            {
                r.Basis = head;
                string why = $"{capacityOnly.CsaMm2:0.#} mm² carries the current, but no size with tabulated " +
                             $"mV/A/m keeps voltage drop within {limit:0.0}% over {input.LengthM:0} m.";
                if (noMv.Count > 0)
                    why += $" {table.CiteVoltDrop()} mV/A/m is not carried for " +
                           string.Join(", ", noMv.Select(c => $"{c:0.#}")) + " mm² — add it from the printed " +
                           "standard to STING_WIRE_TABLES.json; the sizer will not estimate voltage drop.";
                return Refuse(r, why);
            }

            double itW = threePh ? winner.It3ph : winner.It1ph;
            r.CsaMm2 = winner.CsaMm2;
            r.TabulatedItA = itW;
            r.IzA = itW * factors;
            r.MvAm = threePh ? winner.MvAm3ph : winner.MvAm1ph;
            r.VoltDropV = r.MvAm * input.DesignCurrentA * input.LengthM / 1000.0;
            r.VoltDropPct = r.VoltDropV / input.VoltageV * 100.0;
            r.UnverifiedCapacity = !winner.Verified;
            r.UnverifiedVoltDrop = !winner.MvVerified;
            r.Sized = true;

            var sb = new StringBuilder(head);
            sb.Append($" Selected {winner.CsaMm2:0.#} mm²: It={itW:0.#} A, Iz=It·Ca·Cg·Ci={r.IzA:0.0} A ≥ In {inA} A");
            if (capacityOnly.CsaMm2 < winner.CsaMm2)
                sb.Append($" (current alone needs {capacityOnly.CsaMm2:0.#} mm²; upsized for voltage drop)");
            sb.Append($". VD = {r.MvAm:0.###} mV/A/m × {input.DesignCurrentA:0.0} A × {input.LengthM:0.#} m = " +
                      $"{r.VoltDropV:0.00} V = {r.VoltDropPct:0.00}% of {input.VoltageV:0} V (limit {limit:0.0}%; " +
                      "tabulated mV/A/m at max conductor temperature).");
            ApplyLoadCorrection(r, table, method, ca, cg, input.DesignCurrentA, input.VoltageV, input.LengthM);
            sb.Append(" " + r.LoadCorrectionNote);
            if (r.UnverifiedRow && r.ProjectTable)
            {
                string what = r.UnverifiedCapacity && r.UnverifiedVoltDrop ? "It and mV/A/m"
                            : r.UnverifiedCapacity ? "It" : "mV/A/m";
                sb.Append($" VERIFY: the {winner.CsaMm2:0.#} mm² {what} (project table {table.Id}) is single-source " +
                          $"project data (source: {table.SourceNote}) — confirm against that source before issue.");
            }
            else if (r.UnverifiedRow)
            {
                string what = r.UnverifiedCapacity && r.UnverifiedVoltDrop ? $"It (Table {table.Id}) and mV/A/m (Table {table.VoltDropTable})"
                            : r.UnverifiedCapacity ? $"It (Table {table.Id})" : $"mV/A/m (Table {table.VoltDropTable})";
                sb.Append($" VERIFY: the {winner.CsaMm2:0.#} mm² {what} has not been checked against a second " +
                          "source — confirm against the printed BS 7671 before issue.");
            }
            sb.Append(" Not checked here: adiabatic (Reg 434.5.2), Zs / disconnection time, VD upstream of the circuit origin.");
            if (r.ProjectTable || r.ProjectFactors)
                sb.Append($" Sized on PROJECT data from {data.OverrideFile}, not the corporate BS 7671 transcription.");
            r.Basis = sb.ToString();
            return r;
        }

        /// <summary>
        /// BS 7671 Appendix 4 §6.1 operating-temperature factor for copper:
        /// Ct = [230 + tp − (Ca²·Cg²·Cs²·Cd² − Ib²/It²)(tp − 30)] / (230 + tp), with Cs = Cd = 1
        /// (not buried). When Ib ≥ Ca·Cg·It the conductor is at or above tp and Ct is 1 — the
        /// tabulated figure stands; Ct is never above 1.
        /// </summary>
        public static double LoadCorrectionCt(double tpC, double ca, double cg, double ibA, double itA)
        {
            if (tpC <= 30 || itA <= 0 || ibA < 0) return 1.0;
            double term = ca * ca * cg * cg - (ibA * ibA) / (itA * itA);
            if (term <= 0) return 1.0;
            double ct = (230.0 + tpC - term * (tpC - 30.0)) / (230.0 + tpC);
            return Math.Min(1.0, ct);
        }

        /// <summary>Apply §6.1 where it can be applied honestly, else say why not.</summary>
        internal static void ApplyLoadCorrection(Bs7671SizingResult r, Bs7671CapacityTable table, string method,
            double ca, double cg, double ib, double voltageV, double lengthM)
        {
            string m = (method ?? "").Trim().ToUpperInvariant();
            if (r.CsaMm2 > 16 + 1e-9)
                r.LoadCorrectionNote = "Appendix 4 §6.1 load correction not applied: above 16 mm² it applies to the " +
                                       "resistive component only, and the tables carry the combined mV/A/m.";
            else if (m.StartsWith("D"))
                r.LoadCorrectionNote = "Appendix 4 §6.1 load correction not applied: buried cables need Cs and Cd, " +
                                       "which are not modelled.";
            else if (table.MaxConductorTempC <= 30)
                r.LoadCorrectionNote = "Appendix 4 §6.1 load correction not applied: the table carries no conductor temperature.";
            else
            {
                double ct = LoadCorrectionCt(table.MaxConductorTempC, ca, cg, ib, r.TabulatedItA);
                r.LoadCorrectionCt = ct;
                r.LoadCorrectedVoltDropPct = r.MvAm * ct * ib * lengthM / 1000.0 / voltageV * 100.0;
                r.LoadCorrectionNote = $"Appendix 4 §6.1 (optional): at Ib the conductor runs below {table.MaxConductorTempC:0} °C, " +
                                       $"Ct = {ct:0.000}, so VD = {r.LoadCorrectedVoltDropPct:0.00}%. The size was chosen on the " +
                                       "tabulated figure.";
            }
        }

        private static Bs7671SizingResult Refuse(Bs7671SizingResult r, string why)
        {
            r.Sized = false;
            r.Refusal = why;
            return r;
        }
    }

    /// <summary>
    /// Protective-device selection. ELEC-3/4 of the review: the ×1.25 continuous-load
    /// factor is an NEC rule (210.20(A), 215.3) and must not be applied under BS 7671,
    /// where the rule is Ib ≤ In ≤ Iz (Reg 433.1.1).
    /// </summary>
    public static class ProtectiveDeviceSelection
    {
        /// <summary>BS 3036 semi-enclosed (rewirable) fuse ratings, A. A circuit on a
        /// BS 3036 fuse must choose In from THESE — not from the MCB series — or the
        /// result names a device that does not exist in that form (e.g. a 32 A rewirable).</summary>
        public static readonly int[] Bs3036SemiEnclosedFuseRatingsA = { 5, 15, 20, 30, 45, 60, 100 };

        public const string Bs3036Label = "BS 3036 semi-enclosed fuse";

        public sealed class Selection
        {
            public int ProposedA { get; set; }
            public double MinimumA { get; set; }
            /// <summary>True when the proposed device would not be protected by the cable (In &gt; Iz),
            /// or when no standard rating is large enough. A blocked proposal must not be applied.</summary>
            public bool Blocked { get; set; }
            /// <summary>NEC only: the proposal holds only when conditions this code cannot see
            /// are met — the device is above the conductor ampacity under the 240.4(B)
            /// next-size-up allowance (<see cref="Nec2404BConfirmText"/>), or it is a 10 A
            /// branch-circuit device whose loads 210.23(A) restricts
            /// (<see cref="FlagNecTenAmpBranchCircuit"/>). Not blocked, but not a clean pass
            /// either — the caller must show it.</summary>
            public bool NeedsConfirmation { get; set; }
            /// <summary>False when the conductor was not checked against the device
            /// (no ampacity / Iz was available).</summary>
            public bool ConductorChecked { get; set; }
            public string Note { get; set; } = "";
        }

        /// <summary>NEC 240.4(B)/(C) boundary: the next-size-up allowance stops at 800 A.</summary>
        public const int Nec2404MaxNextSizeUpA = 800;

        /// <summary>The 240.4(B) conditions this code cannot verify from the model.
        /// 240.4(B)(1) is quoted from NFPA 70-2023; the adjustable-trip sentence is the
        /// paragraph after 240.4(B)(3). Wording confirmed 2026-10-02 against the
        /// NFPA report reproducing the NFPA 70-2023 text: Public Input 705-NFPA 70-2023 [Section 240.4], NEC CMP-10 First Draft public-input report, pp. 321-322/533, https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P10_FD_PIResponses.pdf
        /// (the text there is the 2023 edition, reproduced as the base of a 2026-cycle
        /// proposal; only the "(H I)" reference in the 240.4 lead-in is marked as changed).</summary>
        public const string Nec2404BConfirmText =
            "confirm NEC 240.4(B)(1): the conductors are not part of a branch circuit supplying more than one " +
            "receptacle for cord-and-plug-connected portable loads; and, if the device is adjustable-trip, it is " +
            "set no higher than the next standard value above the conductor ampacity with restricted access " +
            "per 240.6(C)";

        /// <summary>NEC 2023 210.18 / 210.23(A): the 10 A branch-circuit rating that the 2023
        /// edition added (with 10 A in Table 240.6(A)).</summary>
        public const int NecTenAmpBranchCircuitA = 10;

        /// <summary>NEC 2023 210.23(A)(1) loads a 10 A branch circuit may supply and
        /// 210.23(A)(2) loads it shall not. Read from the 2023 text reproduced unmarked as the
        /// base of First Revision FR-7637-NFPA 70-2024 [210.23(A)], NEC CMP-2 First Draft
        /// report p. 55/111,
        /// https://docinfofiles.nfpa.org/files/AboutTheCodes/70/70_A2025_NEC_P02_FD_PrelimFR.pdf.</summary>
        public const string Nec21023AConfirmText =
            "a 10 A branch circuit is limited by NEC 210.23(A): it may supply lighting outlets and dwelling-unit " +
            "bathroom / laundry exhaust fans on lighting circuits (or an individual gas fireplace unit), and shall not " +
            "supply receptacle outlets, fixed appliances (except on an individual branch circuit), garage door " +
            "openers or laundry equipment — confirm the load, or use 15 A";

        /// <summary>
        /// The NEC rating list without the 10 A branch-circuit rating, for a branch circuit
        /// whose load 210.23(A) does not permit on 10 A (e.g. receptacles). Feeders and
        /// lighting circuits use the full Table 240.6(A) list.
        /// </summary>
        public static int[] NecRatingsAboveTenAmpBranch(int[] ratingsA)
            => (ratingsA ?? new int[0]).Where(r => r > NecTenAmpBranchCircuitA).ToArray();

        /// <summary>
        /// Marks a 10 A NEC proposal for confirmation when the caller cannot tell what the
        /// branch circuit supplies (210.23(A)). Returns true when it flagged. A blocked
        /// proposal, or any other rating, is left alone.
        /// </summary>
        public static bool FlagNecTenAmpBranchCircuit(Selection sel)
        {
            if (sel == null || sel.Blocked || sel.ProposedA != NecTenAmpBranchCircuitA) return false;
            sel.NeedsConfirmation = true;
            sel.Note = (string.IsNullOrEmpty(sel.Note) ? "" : sel.Note + "; ") + Nec21023AConfirmText;
            return true;
        }

        /// <param name="isNec">NEC: next 240.6(A) rating ≥ Ib (×1.25 when continuous), then
        /// the conductor check of 240.4(B)/(C) when <paramref name="izA"/> is given.
        /// BS 7671: next rating ≥ Ib, never ×1.25, then In ≤ Iz.</param>
        /// <param name="izA">BS 7671: effective cable capacity Iz. NEC: the conductor
        /// ampacity after 310.15 correction. Null/≤0 when not known — the conductor is then
        /// reported as NOT checked, never passed.</param>
        public static Selection Select(double ibA, bool isNec, bool continuous, int[] ratingsA, double? izA, string izBasis = null)
        {
            var s = new Selection();
            double min = isNec && continuous ? ibA * 1.25 : ibA;
            s.MinimumA = min;
            int proposed = (ratingsA ?? new int[0]).Where(x => x >= min).DefaultIfEmpty(0).Min();
            var notes = new List<string>();
            if (!isNec && continuous) notes.Add("125% continuous factor not applied (NEC rule; BS 7671 uses Ib ≤ In ≤ Iz)");
            if (proposed <= 0)
            {
                s.Blocked = true;
                notes.Add($"no standard rating ≥ {min:0.0} A");
                s.Note = string.Join("; ", notes);
                return s;
            }
            s.ProposedA = proposed;
            if (isNec)
            {
                string basis = string.IsNullOrEmpty(izBasis) ? "" : $" [{izBasis}]";
                if (izA.HasValue && izA.Value > 0)
                    NecConductorCheck(s, proposed, izA.Value, ratingsA, basis, notes);
                else
                    notes.Add("conductor NOT checked: no conductor ampacity available, so NEC 240.4 was not applied" + basis);
                s.Note = string.Join("; ", notes);
                return s;
            }
            if (izA.HasValue && izA.Value > 0)
            {
                s.ConductorChecked = true;
                if (proposed > izA.Value)
                {
                    s.Blocked = true;
                    notes.Add($"In {proposed} A > Iz {izA.Value:0.#} A — the cable would not be protected against " +
                              "overload; upsize the cable, do not apply" + (string.IsNullOrEmpty(izBasis) ? "" : $" [{izBasis}]"));
                }
                else notes.Add($"In {proposed} A ≤ Iz {izA.Value:0.#} A" + (string.IsNullOrEmpty(izBasis) ? "" : $" [{izBasis}]"));
            }
            else notes.Add("cable size unknown — In ≤ Iz not checked");
            s.Note = string.Join("; ", notes);
            return s;
        }

        /// <summary>
        /// NEC 240.4 conductor protection (DSCH-30), applied to the proposed device.
        /// <list type="bullet">
        /// <item>Device ≤ ampacity: protected.</item>
        /// <item>Device &gt; 800 A and &gt; ampacity: blocked — 240.4(C) requires ampacity ≥ rating.</item>
        /// <item>Device ≤ 800 A and &gt; ampacity: 240.4(B) permits the next higher standard
        /// rating only when (2) the ampacity does not itself correspond to a standard rating
        /// and the device IS the next one above it, and (1) the circuit is not a multi-outlet
        /// receptacle branch circuit for cord-and-plug portable loads. (2) and (3) are checked
        /// here; (1) and adjustable-trip settings are left to the engineer and the result is
        /// marked <see cref="Selection.NeedsConfirmation"/> — never a silent pass.</item>
        /// </list>
        /// 240.4(D) small-conductor limits and 240.4(E)-(G) taps / transformer secondaries /
        /// specific applications are not decided here.
        /// </summary>
        internal static void NecConductorCheck(Selection s, int proposed, double ampacityA, int[] ratingsA,
            string basis, List<string> notes)
        {
            s.ConductorChecked = true;
            if (proposed <= ampacityA + 1e-9)
            {
                notes.Add($"OCPD {proposed} A ≤ conductor ampacity {ampacityA:0.#} A [NEC 240.4]{basis}");
                return;
            }
            if (proposed > Nec2404MaxNextSizeUpA)
            {
                s.Blocked = true;
                notes.Add($"OCPD {proposed} A > conductor ampacity {ampacityA:0.#} A, and above {Nec2404MaxNextSizeUpA} A " +
                          "NEC 240.4(C) requires ampacity ≥ the device rating — upsize or parallel the conductors, do not apply" + basis);
                return;
            }
            var ratings = ratingsA ?? new int[0];
            bool ampacityIsStandard = ratings.Any(r => Math.Abs(r - ampacityA) < 1e-6);
            if (ampacityIsStandard)
            {
                s.Blocked = true;
                notes.Add($"OCPD {proposed} A > conductor ampacity {ampacityA:0.#} A, and {ampacityA:0.#} A is itself a " +
                          "standard rating, so the 240.4(B)(2) next-size-up allowance does not apply — use the " +
                          $"{ampacityA:0.#} A device or upsize the conductor; do not apply" + basis);
                return;
            }
            int nextAbove = ratings.Where(r => r > ampacityA).DefaultIfEmpty(0).Min();
            if (nextAbove != proposed)
            {
                s.Blocked = true;
                notes.Add($"OCPD {proposed} A is more than one standard rating above the conductor ampacity " +
                          $"{ampacityA:0.#} A (next higher is {nextAbove} A) — NEC 240.4(B) does not permit it; " +
                          "upsize the conductor, do not apply" + basis);
                return;
            }
            s.NeedsConfirmation = true;
            notes.Add($"OCPD {proposed} A > conductor ampacity {ampacityA:0.#} A under the NEC 240.4(B) next-size-up " +
                      $"allowance (≤ {Nec2404MaxNextSizeUpA} A, ampacity not a standard rating) — " + Nec2404BConfirmText + basis);
        }
    }

    /// <summary>
    /// Conductor cross-section from a free-text wire-size string. ELEC-5: the old parser
    /// took the FIRST number, so "2 x 2.5mm²" became 2 mm² (the conductor count).
    /// Rules, in order: the number immediately before "mm" / "mm²" / "mm2"; else an AWG /
    /// kcmil designation ("#12", "12 AWG", "1/0", "250 kcmil") converted to mm²; else the
    /// LAST number in the string. Returns 0 when nothing parses.
    /// </summary>
    public static class WireSizeParser
    {
        private static readonly Regex Mm = new Regex(@"(\d+(?:[.,]\d+)?)\s*(?:mm²|mm2|mm\^2|sq\.?\s*mm|mm)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Kcmil = new Regex(@"(\d+)\s*(?:kcmil|mcm)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex AwgHash = new Regex(@"#\s*(\d{1,2}(?:/0)?)", RegexOptions.CultureInvariant);
        private static readonly Regex AwgWord = new Regex(@"(\d{1,2}(?:/0)?)\s*AWG", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex AnyNumber = new Regex(@"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);

        public static double ParseCsaMm2(string wireSize)
        {
            if (string.IsNullOrWhiteSpace(wireSize)) return 0;
            var m = Mm.Match(wireSize);
            if (m.Success) return ToDouble(m.Groups[1].Value);
            m = Kcmil.Match(wireSize);
            if (m.Success) return Math.Round(ToDouble(m.Groups[1].Value) * 1000.0 * CircularMilMm2, 2);
            m = AwgHash.Match(wireSize);
            if (!m.Success) m = AwgWord.Match(wireSize);
            if (m.Success) return AwgToMm2(m.Groups[1].Value);
            var all = AnyNumber.Matches(wireSize);
            return all.Count > 0 ? ToDouble(all[all.Count - 1].Value) : 0;
        }

        /// <summary>1 circular mil = π/4 × (0.001 in)² = 5.067075e-4 mm².</summary>
        public const double CircularMilMm2 = 5.067074790e-4;

        /// <summary>AWG gauge ("12", "1/0", "4/0") to mm² by the ASTM B258 definition:
        /// d = 0.005 in × 92^((36 − n)/39), n = 0 for 1/0, −1 for 2/0, …</summary>
        public static double AwgToMm2(string awg)
        {
            if (string.IsNullOrWhiteSpace(awg)) return 0;
            awg = awg.Trim();
            int n;
            if (awg.EndsWith("/0"))
            {
                if (!int.TryParse(awg.Substring(0, awg.Length - 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int zeros)) return 0;
                n = 1 - zeros;
            }
            else if (!int.TryParse(awg, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return 0;
            double dIn = 0.005 * Math.Pow(92.0, (36.0 - n) / 39.0);
            double cmil = Math.Pow(dIn * 1000.0, 2);
            return Math.Round(cmil * CircularMilMm2, 2);
        }

        private static double ToDouble(string s)
            => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static readonly Regex NecKcmil = new Regex(@"(\d{3,4})\s*(?:kcmil|mcm)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex NecHash = new Regex(@"#\s*(\d{1,4}(?:/0)?)", RegexOptions.CultureInvariant);
        private static readonly Regex NecAwg = new Regex(@"(\d{1,2}(?:/0)?)\s*AWG", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Sets = new Regex(@"\bsets?\b|\bparallel\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The NEC trade size of the FIRST (phase) conductor in a wire-size string, keyed as
        /// NEC Table 310.16 keys it: "12", "1/0", "250" (kcmil). Null when no AWG / kcmil
        /// designation is found, or when the string describes parallel sets — the ampacity
        /// of a parallel run is not one conductor's, so it must not be read as one.
        /// "#500" (three or more digits after #) is read as kcmil.
        /// </summary>
        public static string ParseNecSize(string wireSize)
        {
            if (string.IsNullOrWhiteSpace(wireSize) || Sets.IsMatch(wireSize)) return null;
            var candidates = new List<(int Index, string Size)>();
            var k = NecKcmil.Match(wireSize);
            if (k.Success) candidates.Add((k.Index, k.Groups[1].Value));
            var h = NecHash.Match(wireSize);
            if (h.Success) candidates.Add((h.Index, h.Groups[1].Value));
            var a = NecAwg.Match(wireSize);
            if (a.Success) candidates.Add((a.Index, a.Groups[1].Value));
            if (candidates.Count == 0) return null;
            string size = candidates.OrderBy(c => c.Index).First().Size;
            if (!size.Contains("/") && int.TryParse(size, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                && n > 40 && n < 250) return null;   // neither an AWG gauge nor a kcmil size
            return size;
        }
    }
}
