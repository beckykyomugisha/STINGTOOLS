// ══════════════════════════════════════════════════════════════════════════
//  MaterialRegistry.cs — the governed material register, read as data.
//
//  StingTools ships 1,279 rows of material data — BLE_MATERIALS.csv (815) and
//  MEP_MATERIALS.csv (464) — carrying per material a code, an ISO 19650 id, a
//  category, a total thickness, FIVE layer slots (material / thickness /
//  function), a unit cost, a standard reference, and an identity class. Three
//  separate parts of the plugin answer "what is this material?" from
//  hand-written word lists instead, and the layer columns were being discarded
//  by whatever seeded a project catalogue from the NAME column alone.
//
//  This is the reader. It is deliberately not a classifier.
//
//  ── WHAT THIS DOES AND DOES NOT CONSUME ────────────────────────────────────
//
//  The register's FACTS are dependable and are what this exists to expose:
//  MAT_CODE, MAT_ISO_19650_ID, MAT_CATEGORY, MAT_THICKNESS_MM, the five layer
//  slots, MAT_COST_UNIT_UGX, MAT_STANDARD. They are measurements and
//  identifiers; a wrong one is a typo, not a judgement.
//
//  BLE_APP-IDENTITY-CLASS is a JUDGEMENT, and measured against the material
//  names in the same rows it is a TRADE bucket rather than a substance. That
//  reading was checked, not assumed (2026-09-09, all 1,279 rows against the
//  needle table in MaterialClassPlanner):
//
//      348 agree · 85 conflict · 342 the register answers where the needles do
//      not · 504 rows carry Ceiling / Flooring / Lining / Generic, which name a
//      role or nothing at all
//
//  and the 85 conflicts do not fall on one side:
//
//      the register is RIGHT   MASONRY PAINT WHITE → Paint (needles: Masonry)
//                              26 × CEMENT PLASTER / RENDER → Concrete (Gypsum)
//                              FIBERGLASS ACOUSTIC TILE → Insulation (Ceramic)
//                              EXPOSED AGGREGATE 100MM → Concrete (Stone)
//      the register is WRONG   GRANITE SKIRTING 100MM → Wood
//                              MARBLE SKIRTING 150MM → Wood
//                              CEMENT SKIRTING PREMIUM 150MM → Wood
//                              PVC SKIRTING 80MM → Wood
//                              LIGHTWEIGHT SCREED 40MM → Metal
//                              EXTERIOR TIMBER CLADDING 35MM → Metal
//                              FLOOR MOUNTED CLOSE COUPLED WC → Glass
//                              ANTI-SLIP EPOXY 5MM → Concrete
//
//  A skirting is Wood whatever it is made of; a cladding is Metal; sanitaryware
//  is Glass. Nor does agreement rate identify the safe classes — Concrete agrees
//  only 45% of the time and is mostly RIGHT, while Wood agrees 84% and is badly
//  wrong where it differs. **There is no rule derivable from this data that
//  consumes the class column without importing confident wrong answers**, so
//  this class exposes <see cref="MaterialRow.IdentityClass"/> and
//  <see cref="ToRevitClass"/> for RECONCILIATION and refuses to be the answer.
//  See MaterialRegisterReconciliation, whose CSV is how a human settles it.
//
//  ── REVIT-FREE ─────────────────────────────────────────────────────────────
//  Takes CSV TEXT, not paths, so the shipped register is assertable outside
//  Revit — the same split ProdExclusionPolicy uses, and the reason its data
//  defects were catchable at all.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.Materials
{
    /// <summary>One declared layer of a register row.</summary>
    public sealed class MaterialRegisterLayer
    {
        /// <summary>1..5, as the column is named.</summary>
        public int Index;
        public string Material = "";
        /// <summary>Millimetres. 0 when the column held no usable number.</summary>
        public double ThicknessMm;
        /// <summary>The register's own function word — SUBSTRATE, FINISH 1, STRUCTURE …
        /// Kept verbatim; mapping it to Revit's enum is the caller's business.</summary>
        public string Function = "";

        public override string ToString()
            => $"{Index}: {Material} {ThicknessMm:0.#}mm ({Function})";
    }

    public sealed class MaterialRow
    {
        /// <summary>Which file it came from — "BLE" or "MEP". Reported on a duplicate.</summary>
        public string Source = "";
        public string Code = "";
        public string Iso19650Id = "";
        public string Name = "";
        public string Category = "";
        /// <summary>MAT_THICKNESS_MM. 0 when absent.</summary>
        public double ThicknessMm;
        /// <summary>The declared build-up, layer 1 first. Empty when the row declares none.</summary>
        public List<MaterialRegisterLayer> Layers = new List<MaterialRegisterLayer>();
        /// <summary>MAT_COST_UNIT_UGX. 0 when absent.</summary>
        public double CostUnitUgx;
        public string Standard = "";
        /// <summary>BLE_APP-IDENTITY-CLASS, VERBATIM and in the register's own
        /// vocabulary — which is not Revit's. Read <see cref="ToRevitClass"/> and the
        /// file header before treating this as an answer.</summary>
        public string IdentityClass = "";

        /// <summary>Sum of the declared layers, which is not always MAT_THICKNESS_MM —
        /// the difference is one of the things the register audit reports.</summary>
        public double LayerThicknessSumMm => Layers?.Sum(l => Math.Max(0, l.ThicknessMm)) ?? 0;

        public override string ToString() => $"{Code} {Name}";
    }

    public sealed class MaterialRegistry
    {
        private readonly Dictionary<string, MaterialRow> _byName;
        private readonly Dictionary<string, MaterialRow> _byCode;

        /// <summary>Every row, in file order: BLE then MEP.</summary>
        public IReadOnlyList<MaterialRow> Rows { get; }

        /// <summary>
        /// Problems found while loading, reported rather than swallowed. A duplicate
        /// MAT_NAME is in here, NOT resolved by last-wins: two rows claiming one name is a
        /// data question, and picking one silently is how a register stops being governed.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        public int Count => Rows.Count;

        private MaterialRegistry(List<MaterialRow> rows, List<string> warnings)
        {
            Rows = rows;
            Warnings = warnings;
            _byName = new Dictionary<string, MaterialRow>(StringComparer.OrdinalIgnoreCase);
            _byCode = new Dictionary<string, MaterialRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                if (!string.IsNullOrWhiteSpace(r.Name) && !_byName.ContainsKey(r.Name.Trim()))
                    _byName[r.Name.Trim()] = r;
                if (!string.IsNullOrWhiteSpace(r.Code) && !_byCode.ContainsKey(r.Code.Trim()))
                    _byCode[r.Code.Trim()] = r;
            }
        }

        /// <summary>An empty register — what a caller gets when the CSVs are not deployed.
        /// Deliberately not null: every lookup answers "not in the register", which is the
        /// same thing a missing name means, so no caller needs a null check to be correct.
        /// The MISSING FILE is reported through <see cref="Warnings"/> instead.</summary>
        public static MaterialRegistry Empty
            => new MaterialRegistry(new List<MaterialRow>(), new List<string>());

        /// <param name="bleCsv">Text of BLE_MATERIALS.csv, or null if not deployed.</param>
        /// <param name="mepCsv">Text of MEP_MATERIALS.csv, or null if not deployed.</param>
        public static MaterialRegistry Parse(string bleCsv, string mepCsv)
        {
            var rows = new List<MaterialRow>();
            var warnings = new List<string>();
            var seenName = new Dictionary<string, MaterialRow>(StringComparer.OrdinalIgnoreCase);

            foreach (var (text, source) in new[] { (bleCsv, "BLE"), (mepCsv, "MEP") })
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    warnings.Add($"{source}_MATERIALS.csv is not deployed — {source} materials "
                               + "will not be found in the register.");
                    continue;
                }

                var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                var header = FirstDataLine(lines, out int headerIndex);
                if (header == null)
                {
                    warnings.Add($"{source}_MATERIALS.csv has no header row.");
                    continue;
                }

                var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var headerFields = SplitCsvLine(header);
                for (int i = 0; i < headerFields.Count; i++)
                {
                    string h = (headerFields[i] ?? "").Trim().TrimStart('﻿');
                    if (h.Length > 0 && !col.ContainsKey(h)) col[h] = i;
                }

                // Located by NAME, never by position: these files carry 71 columns and grow.
                foreach (string need in new[] { "MAT_NAME", "MAT_CODE", "BLE_APP-IDENTITY-CLASS" })
                    if (!col.ContainsKey(need))
                        warnings.Add($"{source}_MATERIALS.csv has no '{need}' column — "
                                   + "rows will load with that field blank.");

                int loaded = 0;
                for (int i = headerIndex + 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    var f = SplitCsvLine(line);
                    if (f.Count < 2) continue;

                    string name = Get(f, col, "MAT_NAME").Trim();
                    if (name.Length == 0) continue;

                    var row = new MaterialRow
                    {
                        Source = source,
                        Code = Get(f, col, "MAT_CODE").Trim(),
                        Iso19650Id = Get(f, col, "MAT_ISO_19650_ID").Trim(),
                        Name = name,
                        Category = Get(f, col, "MAT_CATEGORY").Trim(),
                        ThicknessMm = Num(Get(f, col, "MAT_THICKNESS_MM")),
                        CostUnitUgx = Num(Get(f, col, "MAT_COST_UNIT_UGX")),
                        Standard = Get(f, col, "MAT_STANDARD").Trim(),
                        IdentityClass = Get(f, col, "BLE_APP-IDENTITY-CLASS").Trim(),
                    };

                    for (int n = 1; n <= 5; n++)
                    {
                        string lm = Get(f, col, $"MAT_LAYER_{n}_MATERIAL").Trim();
                        if (lm.Length == 0) continue;
                        row.Layers.Add(new MaterialRegisterLayer
                        {
                            Index = n,
                            Material = lm,
                            ThicknessMm = Num(Get(f, col, $"MAT_LAYER_{n}_THICKNESS_MM")),
                            Function = Get(f, col, $"MAT_LAYER_{n}_FUNCTION").Trim(),
                        });
                    }

                    if (seenName.TryGetValue(name, out var prior))
                        warnings.Add($"duplicate MAT_NAME '{name}': {prior.Source} {prior.Code} "
                                   + $"and {source} {row.Code}. The FIRST is used for lookups; "
                                   + "two rows claiming one name is a data question, not a "
                                   + "precedence one.");
                    else
                        seenName[name] = row;

                    rows.Add(row);
                    loaded++;
                }

                if (loaded == 0)
                    warnings.Add($"{source}_MATERIALS.csv has a header and no rows.");
            }

            return new MaterialRegistry(rows, warnings);
        }

        /// <summary>The row for a material NAME, trimmed and case-insensitive, or null.
        /// Name rather than code, because a Revit material and a Revit type carry the name
        /// and nothing carries the code.</summary>
        public MaterialRow ByName(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName)) return null;
            return _byName.TryGetValue(materialName.Trim(), out var r) ? r : null;
        }

        public MaterialRow ByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            return _byCode.TryGetValue(code.Trim(), out var r) ? r : null;
        }

        public bool Contains(string materialName) => ByName(materialName) != null;

        // ══════════════════════════════════════════════════════════════════════
        //  Register class → Revit class, declared in one place
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The register's class vocabulary and Revit's are neither identical nor nested:
        ///
        /// <code>
        ///   register only   Ceiling  Flooring  Lining        (element ROLES, not substances)
        ///   register only   Plaster  Carpet    Fabric        (translatable — below)
        ///   Revit only      Gypsum Ceramic Stone Membrane Textile Earth Liquid Gas
        ///   both            Concrete Masonry Metal Wood Glass Plastic Paint Insulation Generic
        /// </code>
        ///
        /// <para>Returns null for anything that does not name a substance — the three role
        /// words and <c>Generic</c>. 504 of the 1,279 rows are in that group, which is why
        /// a mapping that guessed at them would be importing 504 shrugs.</para>
        /// </summary>
        public static string ToRevitClass(string registerClass)
        {
            string c = (registerClass ?? "").Trim();
            if (c.Length == 0) return null;

            // Translatable: a different word for the same substance.
            if (Eq(c, "Plaster")) return "Gypsum";
            if (Eq(c, "Carpet") || Eq(c, "Fabric")) return "Textile";

            // NOT mappable. A ceiling is a place, a flooring is a place, a lining is a
            // position in a build-up, and Generic is the absence of an answer.
            if (Eq(c, "Ceiling") || Eq(c, "Flooring") || Eq(c, "Lining") || Eq(c, "Generic"))
                return null;

            // Shared vocabulary — same word, same meaning.
            return MaterialClassPlanner.RevitClasses
                       .FirstOrDefault(rc => Eq(rc, c));
        }

        /// <summary>The register class words that <see cref="ToRevitClass"/> deliberately
        /// refuses. Exposed so a test can assert the refusal rather than restate the list.</summary>
        public static readonly string[] ClassesThatNameNoSubstance =
            { "Ceiling", "Flooring", "Lining", "Generic" };

        private static bool Eq(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // ══════════════════════════════════════════════════════════════════════
        //  CSV
        // ══════════════════════════════════════════════════════════════════════

        private static string FirstDataLine(string[] lines, out int index)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (string.IsNullOrWhiteSpace(l) || l.TrimStart().StartsWith("#")) continue;
                index = i;
                return l;
            }
            index = -1;
            return null;
        }

        private static string Get(List<string> fields, Dictionary<string, int> col, string name)
            => col.TryGetValue(name, out int i) && i < fields.Count ? (fields[i] ?? "") : "";

        private static double Num(string s)
            => double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
               ? d : 0;

        /// <summary>RFC 4180. Material names and specification cells carry commas and
        /// quotes, and these files have 71 columns — a Split(',') shifts every field after
        /// the first quoted one, silently.</summary>
        internal static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < (line ?? "").Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') { cur.Append(c); continue; }
                    if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; continue; }
                    quoted = false;
                }
                else if (c == '"' && cur.Length == 0) quoted = true;
                else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            fields.Add(cur.ToString());
            return fields;
        }
    }
}
