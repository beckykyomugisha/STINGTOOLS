// TypeMarkSequencer.cs — G-20. Generate door/window TYPE marks (DR-01, WIN-03).
//
// WHY THIS EXISTS
// ---------------
// Nothing in STING ever generated a type mark. ParameterHelpers.cs:3884 only
// MIRRORS an existing ALL_MODEL_TYPE_MARK into ASS_TYPE_MARK_TXT — if the Type
// Mark is blank, the STING parameter stays blank, and every door schedule column
// downstream is empty. Marks were hand-entered or absent.
//
// THE ONE RULE THAT MATTERS: MONOTONIC, NEVER REUSED
// --------------------------------------------------
// A deleted DR-03 stays retired. The next allocation is DR-04, never DR-03. There
// is deliberately NO gap reuse.
//
// Two different things called DR-03 across two revisions is precisely the defect
// this whole workstream exists to stop — it is the same shape as reusing a
// register ID or a parameter GUID. A drawing issued at Rev A showing DR-03 as a
// fire door, and Rev B showing DR-03 as a cupboard door, is unresolvable after
// the fact. A gap in the sequence costs nothing; a reused mark costs a site
// query and possibly a wrong door.
//
// HAND-ENTERED MARKS ARE NEVER OVERWRITTEN
// ----------------------------------------
// An existing Type Mark is authored data. It is ADOPTED into the store and the
// sequence continues past it, so a project that already numbered its doors keeps
// its numbers and new types carry on from the highest.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Drawing
{
    /// <summary>One allocation the sequencer would make, or did.</summary>
    public sealed class TypeMarkAssignment
    {
        public ElementId TypeId;
        public string CategoryName = "";
        public string ProdCode = "";
        public string TypeName = "";
        public string Mark = "";
        /// <summary>Assigned | Adopted | AlreadyMarked | Collision | Skipped</summary>
        public string Outcome = "";
        public string Note = "";
    }

    public sealed class TypeMarkResult
    {
        public readonly List<TypeMarkAssignment> Assignments = new List<TypeMarkAssignment>();
        public readonly List<string> Warnings = new List<string>();
        public int Assigned, Adopted, AlreadyMarked, Collisions;
        public bool PreviewOnly;
    }

    /// <summary>
    /// Persisted high-water mark per (category, PROD code). Diffable JSON so a
    /// reviewer can see the sequence move; project-scoped so two projects never
    /// share a counter; survives reload because it is on disk, not in memory.
    /// </summary>
    internal sealed class TypeMarkStore
    {
        [JsonProperty("version")] public int Version { get; set; } = 1;

        /// <summary>key "Doors|DR" -> highest number ever ISSUED (never decremented).</summary>
        [JsonProperty("highWater")]
        public Dictionary<string, int> HighWater { get; set; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every mark ever issued, so a retired one is never re-offered.</summary>
        [JsonProperty("issued")]
        public Dictionary<string, List<string>> Issued { get; set; } =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }

    public static class TypeMarkSequencer
    {
        private const string FileName = "type_mark_sequences.json";
        private const int PadWidth = 2;   // DR-01 .. DR-99, then DR-100 naturally

        // Categories the generator covers. The PROD code is NOT hardcoded here —
        // it is resolved per TYPE through TagConfig.GetFamilyAwareProdCode, the same
        // path the tag pipeline uses, against the 124-entry ProdMap.
        //
        // Hardcoding "DR"/"WIN" would let the type mark and the PROD segment of the
        // instance's ISO tag disagree the moment either side changed — a
        // controlled-vocabulary break. Resolving through the same function makes that
        // impossible by construction, and means any product code works, not just two.
        private static readonly BuiltInCategory[] Covered =
        {
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
        };

        // Prefix is greedy up to the final "-<digits>", because a resolved PROD may
        // carry a material suffix (MaterialProdOverrideRegistry appends -STL/-CON/…),
        // so "DR-STL-04" is a legitimate mark with prefix "DR-STL".
        private static readonly Regex MarkRx =
            new Regex(@"^(.+)-(\d+)$", RegexOptions.Compiled);

        // ---------------------------------------------------------------- store

        private static string StorePath(Document doc)
        {
            try { return StingPaths.MetaFile(doc, "_BIM_COORD", FileName); }
            catch (Exception ex) { StingLog.Warn($"TypeMarkSequencer store path: {ex.Message}"); return null; }
        }

        private static TypeMarkStore Load(Document doc)
        {
            string p = StorePath(doc);
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return new TypeMarkStore();
            try
            {
                var s = JsonConvert.DeserializeObject<TypeMarkStore>(File.ReadAllText(p));
                return s ?? new TypeMarkStore();
            }
            catch (Exception ex)
            {
                // Never silently start from zero: that would re-issue retired marks,
                // which is the one thing this class exists to prevent.
                StingLog.Error($"TypeMarkSequencer: store at '{p}' is unreadable — refusing to allocate", ex);
                return null;
            }
        }

        private static bool Save(Document doc, TypeMarkStore store, TypeMarkResult r)
        {
            string p = StorePath(doc);
            if (string.IsNullOrEmpty(p)) { r.Warnings.Add("No project path — sequence not persisted."); return false; }
            try
            {
                File.WriteAllText(p, JsonConvert.SerializeObject(store, Formatting.Indented));
                return true;
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"Could not write {FileName}: {ex.Message}");
                StingLog.Error("TypeMarkSequencer save", ex);
                return false;
            }
        }

        private static string Key(string category, string prod) => category + "|" + prod;

        // ------------------------------------------------------------- allocate

        /// <summary>
        /// Assign type marks to every door/window TYPE that has none.
        /// <paramref name="preview"/> true reports what it WOULD do and writes nothing —
        /// neither the model nor the store.
        /// </summary>
        public static TypeMarkResult Run(Document doc, bool preview)
        {
            var r = new TypeMarkResult { PreviewOnly = preview };
            if (doc == null) { r.Warnings.Add("No document."); return r; }

            var store = Load(doc);
            if (store == null)
            {
                r.Warnings.Add(
                    $"{FileName} exists but could not be parsed. Allocation is REFUSED rather than "
                  + "restarted from zero, because restarting would re-issue marks that are already on "
                  + "issued drawings. Fix or delete the file deliberately.");
                return r;
            }

            foreach (var bic in Covered)
            {
                var allTypes = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsElementType()
                    .ToElements();
                if (allTypes.Count == 0) continue;

                string catName = Category.GetCategory(doc, bic)?.Name ?? bic.ToString();

                // Resolve PROD per TYPE through the tag pipeline, then group. Two
                // families in one category can legitimately carry different PROD
                // codes, and each gets its own sequence.
                var byProd = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in allTypes)
                {
                    string prodCode = null;
                    try { prodCode = TagConfig.GetFamilyAwareProdCode(t, catName); }
                    catch (Exception ex) { StingLog.Warn($"TypeMarkSequencer PROD for {SafeName(t)}: {ex.Message}"); }

                    if (string.IsNullOrWhiteSpace(prodCode))
                    {
                        // No PROD means no controlled prefix. Inventing one would put a
                        // mark on the drawing that no tag can ever agree with.
                        r.Assignments.Add(new TypeMarkAssignment
                        {
                            TypeId = t.Id, CategoryName = catName, TypeName = SafeName(t),
                            Outcome = "Skipped",
                            Note = "no PROD code resolved from ProdMap — no mark assigned, and no prefix invented",
                        });
                        continue;
                    }
                    if (!byProd.TryGetValue(prodCode, out var list))
                        byProd[prodCode] = list = new List<Element>();
                    list.Add(t);
                }

                foreach (var prodGroup in byProd)
                {
                string prod = prodGroup.Key;
                var types = prodGroup.Value;
                string key = Key(catName, prod);

                if (!store.HighWater.TryGetValue(key, out int high)) high = 0;
                if (!store.Issued.TryGetValue(key, out var issued))
                    issued = store.Issued[key] = new List<string>();
                var issuedSet = new HashSet<string>(issued, StringComparer.OrdinalIgnoreCase);

                // Pass 1 — ADOPT existing marks. Never overwrite authored data, and
                // make sure the sequence continues PAST them.
                var unmarked = new List<Element>();
                foreach (var t in types)
                {
                    string existing = ReadTypeMark(t);
                    if (string.IsNullOrWhiteSpace(existing)) { unmarked.Add(t); continue; }

                    var a = new TypeMarkAssignment
                    {
                        TypeId = t.Id, CategoryName = catName, ProdCode = prod,
                        TypeName = SafeName(t), Mark = existing,
                    };

                    var m = MarkRx.Match(existing.Trim());
                    if (m.Success && string.Equals(m.Groups[1].Value, prod, StringComparison.OrdinalIgnoreCase))
                    {
                        int n = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                        if (n > high) high = n;
                        if (issuedSet.Add(existing)) issued.Add(existing);
                        a.Outcome = "Adopted";
                        a.Note = $"existing mark adopted; sequence continues from {prod}-{high:D2}";
                        r.Adopted++;
                    }
                    else
                    {
                        // A mark that does not fit the grammar is still authored data.
                        // Leave it, and say so — it will not participate in numbering.
                        a.Outcome = "AlreadyMarked";
                        a.Note = m.Success
                            ? $"prefix '{m.Groups[1].Value}' is not '{prod}' — left untouched, not counted"
                            : "does not match <PROD>-<n> — left untouched, not counted";
                        r.AlreadyMarked++;
                    }
                    r.Assignments.Add(a);
                }

                // Pass 2 — ALLOCATE, monotonically, in a stable order so a preview and
                // the subsequent run agree.
                foreach (var t in unmarked.OrderBy(SafeName, StringComparer.OrdinalIgnoreCase))
                {
                    string mark;
                    int guard = 0;
                    do
                    {
                        high++;
                        mark = $"{prod}-{high.ToString("D" + PadWidth, CultureInfo.InvariantCulture)}";
                        guard++;
                    }
                    while (issuedSet.Contains(mark) && guard < 10000);   // never reuse

                    var a = new TypeMarkAssignment
                    {
                        TypeId = t.Id, CategoryName = catName, ProdCode = prod,
                        TypeName = SafeName(t), Mark = mark, Outcome = "Assigned",
                    };

                    if (guard >= 10000)
                    {
                        a.Outcome = "Collision";
                        a.Note = "could not find an unissued mark after 10,000 attempts";
                        r.Collisions++;
                    }
                    else
                    {
                        issuedSet.Add(mark);
                        issued.Add(mark);
                        r.Assigned++;
                    }
                    r.Assignments.Add(a);
                }

                store.HighWater[key] = high;
                }   // per-PROD group
            }

            if (preview) return r;   // wrote nothing, to model or store

            using (var tx = new Transaction(doc, "STING Assign Type Marks"))
            {
                tx.Start();
                foreach (var a in r.Assignments.Where(x => x.Outcome == "Assigned"))
                {
                    var t = doc.GetElement(a.TypeId);
                    if (t == null) { a.Outcome = "Skipped"; a.Note = "type no longer in document"; continue; }
                    var p = t.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
                    if (p == null || p.IsReadOnly)
                    {
                        a.Outcome = "Skipped";
                        a.Note = "ALL_MODEL_TYPE_MARK missing or read-only on this type";
                        r.Assigned--;
                        continue;
                    }
                    try { p.Set(a.Mark); }
                    catch (Exception ex)
                    {
                        a.Outcome = "Skipped"; a.Note = ex.Message; r.Assigned--;
                        r.Warnings.Add($"{a.TypeName}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            Save(doc, store, r);
            return r;
        }

        /// <summary>
        /// 3.9 — verify the join. For every placed instance, the PROD prefix of its
        /// TYPE's Type Mark must equal segment 7 of its own <c>ASS_TAG_1_TXT</c>.
        /// <para>
        /// A difference is a controlled-vocabulary break, not cosmetics: the schedule
        /// would order a "DR-04" that the model tags as something else, and the two
        /// cannot both be right. Reported per type, never auto-corrected — which side
        /// is wrong depends on which was issued.
        /// </para>
        /// </summary>
        public static List<string> VerifyJoin(Document doc)
        {
            var problems = new List<string>();
            if (doc == null) return problems;
            var seen = new HashSet<long>();

            foreach (var bic in Covered)
            {
                foreach (var inst in new FilteredElementCollector(doc)
                             .OfCategory(bic).WhereElementIsNotElementType())
                {
                    try
                    {
                        var tid = inst.GetTypeId();
                        if (tid == null || tid == ElementId.InvalidElementId) continue;
                        if (!seen.Add(tid.Value)) continue;   // one report per type

                        string mark = ReadTypeMark(doc.GetElement(tid));
                        if (string.IsNullOrWhiteSpace(mark)) continue;
                        var m = MarkRx.Match(mark.Trim());
                        if (!m.Success) continue;
                        string markPrefix = m.Groups[1].Value;

                        string tag = ParameterHelpers.GetString(inst, ParamRegistry.TAG1);
                        if (string.IsNullOrWhiteSpace(tag)) continue;

                        // ISO 19650: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ. PROD is
                        // second from the end, so index from the right — a project
                        // whose separator appears inside a segment still resolves.
                        var seg = tag.Split(new[] { ParamRegistry.Separator }, StringSplitOptions.None);
                        if (seg.Length < 2) continue;
                        string tagProd = seg[seg.Length - 2];

                        if (!string.Equals(markPrefix, tagProd, StringComparison.OrdinalIgnoreCase))
                            problems.Add(
                                $"{SafeName(doc.GetElement(tid))}: Type Mark '{mark}' has prefix "
                              + $"'{markPrefix}' but ASS_TAG_1_TXT segment 7 is '{tagProd}' "
                              + $"(instance {inst.Id}). Controlled-vocabulary break — the schedule and "
                              + "the tag disagree about what this product is.");
                    }
                    catch (Exception ex) { StingLog.Warn($"TypeMarkSequencer.VerifyJoin: {ex.Message}"); }
                }
            }
            return problems;
        }

        private static string ReadTypeMark(Element t)
        {
            try { return t?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString(); }
            catch { return null; }
        }

        private static string SafeName(Element e)
        {
            try { return e?.Name ?? ""; } catch { return ""; }
        }
    }
}
