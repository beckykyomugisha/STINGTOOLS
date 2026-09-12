// ══════════════════════════════════════════════════════════════════════════
//  CompoundLayerPlan.cs — what build-up a register row asks for, and every
//  substitution made getting there.
//
//  Extracted out of CompoundTypeCreator.BuildLayers so the decisions are
//  provable without Revit. The decisions were the defect: BuildLayers invented
//  thicknesses, dropped layers and substituted materials, and returned a plain
//  list of layers that said nothing about any of it. A run that read forty real
//  thicknesses and a run that invented forty printed the same line.
//
//  ── MEASURED ON THE SHIPPED REGISTER, 2026-09-09 ───────────────────────────
//
//  TWO DENOMINATORS, and conflating them is how the old comment stayed wrong.
//
//  Across the FILES — BLE_MATERIALS.csv (815) and MEP_MATERIALS.csv (464):
//
//      rows declaring no layer at all     326 BLE + 120 MEP = 446 of 1,279
//
//  The comment this replaces said the fallback "covers 165+ BLE rows with no
//  layer data". It is 326 — out by nearly two-fold, and that comment was the
//  only record anywhere.
//
//  Across the rows a SHIPPED COMMAND actually reads — the four host-type
//  commands select 380 of the 815 BLE rows by MAT_ELEMENT_TYPE:
//
//      built from their declared layers    329
//      homogeneous, row declares none       51   (all Walls)
//      thickness invented                    0
//      layer dropped                         0
//      R-value in a material column          0
//
//  So NONE of the substitutions below fires on any row the commands process.
//  That is the argument for making them FATAL rather than a footnote: it costs
//  nothing today, and the next register will not be this one. The bad cells are
//  real but all sit in rows nothing selects — 9 on A-FIN finishes, 172 on MEP
//  dampers and equipment that have no layers to build, 92 R-values written into
//  a material column. Pinned in CompoundLayerPlanTests so the day one moves into
//  a filter, a test fails instead of a 10 mm default appearing.
//
//  ── THE COUNT-AS-INDEX DEFECT ──────────────────────────────────────────────
//  CountActualLayers `continue`d past a blank slot and returned a COUNT, which
//  the caller then used as an INDEX BOUND — so a row populating slots 2,3,4 was
//  read as slots 1,2,3: a blank material at slot 1 (hence the invented 10 mm)
//  and slot 4 silently dropped. Two shipped rows are in that shape,
//  WL-024 GYPSUM DRYWALL SYSTEM 25MM and WL-037 FIBER CEMENT BOARD 15MM.
//  Slots are now read by POSITION and blanks skipped, so sparse is just sparse.
//
//  ── WHAT THIS DOES NOT EXPLAIN, STATED BECAUSE IT MATTERS ──────────────────
//  It does not explain the 87 floor types that all became one 100 mm layer of
//  "Concrete, Cast-in-Place gray". Measured: all 95 FLR-* rows carry a valid
//  layer 1, so the fallback fires for NONE of them; BuildLayers only ever uses a
//  material named in the CSV or the row's own, never a Revit stock material; and
//  ParseThickness returns the row's declared thickness (50 / 75 / 40 mm) with a
//  final fallback of 10 mm, never 100. One identical 100 mm layer across 87
//  differently-declared rows is the signature of a DUPLICATED BASE TYPE whose
//  structure was never replaced — which is consistent with the catch path this
//  change also fixes, and is not established as what happened.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.Materials
{
    /// <summary>One layer the register asks for, in millimetres and its own words.</summary>
    public sealed class PlannedLayer
    {
        /// <summary>Which of the five MAT_LAYER_n slots this came from, 1-based.</summary>
        public int Slot;
        public string Material = "";
        public double ThicknessMm;
        /// <summary>The register's function word, verbatim. Mapping it to Revit's enum is
        /// the caller's business — this stays Revit-free.</summary>
        public string Function = "";
    }

    public enum LayerIssueKind
    {
        /// <summary>A thickness was missing, blank or unparseable and a number was put in
        /// its place. A quantity nobody chose.</summary>
        ThicknessInvented,
        /// <summary>A declared thickness below Revit's ~0.8 mm minimum was raised to 1 mm.
        /// Revit's constraint, not a guess — reported, never fatal.</summary>
        ThicknessFloored,
        /// <summary>A layer was left out of the build-up entirely.</summary>
        LayerDropped,
        /// <summary>The row declares layers and none of them survived.</summary>
        NoLayerSurvived,
    }

    public sealed class LayerIssue
    {
        public LayerIssueKind Kind;
        public int Slot;
        public string Material = "";
        public string Detail = "";

        /// <summary>True when this issue means the type was NOT built as declared.
        /// The 1 mm floor is the only one that is not: it is Revit's own minimum, and a
        /// row declaring 0.5 mm cannot be honoured by any caller.</summary>
        public bool IsFatal => Kind != LayerIssueKind.ThicknessFloored;

        public override string ToString()
            => $"{Kind} at layer {Slot}" + (Material.Length > 0 ? $" '{Material}'" : "")
             + (Detail.Length > 0 ? ": " + Detail : "");
    }

    public enum LayerPlanOutcome
    {
        /// <summary>Built from the layers the row declares.</summary>
        Declared,
        /// <summary>The row declares no layers at all. A single homogeneous layer is the
        /// honest reading of that — but of the ROW's own material and thickness.</summary>
        NoLayersDeclared,
        /// <summary>The row DOES declare layers and none survived. A parse failure, not a
        /// homogeneous material — the old code could not tell these two apart and
        /// homogenised both.</summary>
        ParseFailure,
    }

    public sealed class CompoundLayerPlan
    {
        public LayerPlanOutcome Outcome;
        public List<PlannedLayer> Layers = new List<PlannedLayer>();
        public List<LayerIssue> Issues = new List<LayerIssue>();

        /// <summary>True when the layers are exactly what the row asked for. A caller that
        /// prints one count for this and its opposite is how the substitutions stayed
        /// invisible for as long as they did.</summary>
        public bool AsDeclared => Issues.All(i => !i.IsFatal)
                               && Outcome != LayerPlanOutcome.ParseFailure;

        public IEnumerable<LayerIssue> Fatal => Issues.Where(i => i.IsFatal);

        public string Describe()
        {
            if (Layers.Count == 0) return "no layers";
            return string.Join(" + ", Layers.Select(l => $"{l.ThicknessMm:0.#} mm {l.Material}"));
        }
    }

    public static class CompoundLayerPlanner
    {
        /// <summary>Revit's compound structure will not take a layer thinner than about
        /// 0.8 mm; 1 mm is the shipped floor.</summary>
        public const double MinLayerMm = 1.0;

        /// <summary>
        /// Above this a "thickness" is read as a cable cross-section in mm² stored in the
        /// thickness column — a real shape in MEP_MATERIALS.csv. It fires on NO row of
        /// either shipped file today, so it is a guard rather than a fix; the layer is
        /// dropped and the drop is FATAL, because a 600 mm foundation layer would leave
        /// the same way and leaving quietly is the whole defect here.
        /// </summary>
        public const double MaxLayerMm = 500.0;

        public const int MaxLayers = 5;

        /// <param name="slotMaterial">Layer material by slot, 1..5; missing entries blank.</param>
        /// <param name="slotThickness">The raw thickness CELL by slot — raw, because
        /// "unparseable" and "absent" are different findings and only the text says which.</param>
        /// <param name="slotFunction">Layer function word by slot.</param>
        /// <param name="rowMaterial">The row's own MAT_NAME material, used only when the
        /// row declares no layers at all.</param>
        /// <param name="rowThicknessMm">The row's own MAT_THICKNESS_MM, same condition.</param>
        public static CompoundLayerPlan Plan(
            IReadOnlyList<string> slotMaterial,
            IReadOnlyList<string> slotThickness,
            IReadOnlyList<string> slotFunction,
            string rowMaterial,
            double rowThicknessMm)
        {
            var plan = new CompoundLayerPlan();
            int declaredSlots = 0;

            for (int i = 0; i < MaxLayers; i++)
            {
                string mat = At(slotMaterial, i).Trim();
                string thickText = At(slotThickness, i).Trim();
                string func = At(slotFunction, i).Trim();
                int slot = i + 1;

                // A slot with no material is not a layer. Read by POSITION and skip —
                // the old reader counted populated slots and then used the count as an
                // index bound, so a row populating 2,3,4 was read as 1,2,3.
                if (mat.Length == 0) continue;

                // An R-value in the MATERIAL column is a thermal figure, not a material.
                // The slot is declared, so it counts as a layer the row asked for — and
                // losing it is therefore a drop, not a blank.
                if (mat.StartsWith("R-", StringComparison.OrdinalIgnoreCase))
                {
                    declaredSlots++;
                    plan.Issues.Add(new LayerIssue
                    {
                        Kind = LayerIssueKind.LayerDropped, Slot = slot, Material = mat,
                        Detail = "the material column holds an R-value, not a material",
                    });
                    continue;
                }

                declaredSlots++;

                bool parsed = false;
                double mm = 0;
                if (thickText.Length > 0 && !thickText.StartsWith("R-", StringComparison.OrdinalIgnoreCase))
                    parsed = double.TryParse(thickText, NumberStyles.Float,
                                             CultureInfo.InvariantCulture, out mm);

                if (parsed && mm > MaxLayerMm)
                {
                    plan.Issues.Add(new LayerIssue
                    {
                        Kind = LayerIssueKind.LayerDropped, Slot = slot, Material = mat,
                        Detail = $"{mm:0.#} mm exceeds {MaxLayerMm:0} mm, read as a cross-section "
                               + "in mm² stored in the thickness column",
                    });
                    continue;
                }

                if (!parsed || mm <= 0)
                {
                    // NOT silently defaulted. The old code put 10 mm here with no warning,
                    // and a 10 mm layer nobody asked for measures, prices and carbon-counts
                    // exactly like one somebody did.
                    plan.Issues.Add(new LayerIssue
                    {
                        Kind = LayerIssueKind.ThicknessInvented, Slot = slot, Material = mat,
                        Detail = thickText.Length == 0
                            ? "the thickness cell is empty"
                            : $"the thickness cell reads '{thickText}', which is not a number",
                    });
                    continue;
                }

                if (mm < MinLayerMm)
                {
                    plan.Issues.Add(new LayerIssue
                    {
                        Kind = LayerIssueKind.ThicknessFloored, Slot = slot, Material = mat,
                        Detail = $"{mm:0.###} mm raised to Revit's {MinLayerMm:0} mm minimum",
                    });
                    mm = MinLayerMm;
                }

                plan.Layers.Add(new PlannedLayer
                { Slot = slot, Material = mat, ThicknessMm = mm, Function = func });
            }

            if (plan.Layers.Count > 0)
            {
                plan.Outcome = LayerPlanOutcome.Declared;
                return plan;
            }

            if (declaredSlots > 0)
            {
                // The row asked for layers and none survived. Homogenising here is what
                // made a parse failure indistinguishable from a genuinely single-material
                // row — and both came out as the default material at the default thickness.
                plan.Outcome = LayerPlanOutcome.ParseFailure;
                plan.Issues.Add(new LayerIssue
                {
                    Kind = LayerIssueKind.NoLayerSurvived, Slot = 0,
                    Detail = $"the row declares {declaredSlots} layer(s) and none could be read",
                });
                return plan;
            }

            // Genuinely no layers declared — 618 of the 1,279 shipped rows. A single
            // homogeneous layer is the honest reading, but of the ROW's own material and
            // thickness, which the old fallback threw away in favour of the caller's
            // defaults.
            plan.Outcome = LayerPlanOutcome.NoLayersDeclared;
            double t = rowThicknessMm;
            if (t < MinLayerMm)
            {
                if (t > 0)
                    plan.Issues.Add(new LayerIssue
                    {
                        Kind = LayerIssueKind.ThicknessFloored, Slot = 1, Material = rowMaterial ?? "",
                        Detail = $"{t:0.###} mm raised to Revit's {MinLayerMm:0} mm minimum",
                    });
                else
                    plan.Issues.Add(new LayerIssue
                    {
                        Kind = LayerIssueKind.ThicknessInvented, Slot = 1, Material = rowMaterial ?? "",
                        Detail = "the row states no usable total thickness either",
                    });
                t = MinLayerMm;
            }
            plan.Layers.Add(new PlannedLayer
            { Slot = 1, Material = rowMaterial ?? "", ThicknessMm = t, Function = "Structure" });
            return plan;
        }

        private static string At(IReadOnlyList<string> a, int i)
            => a != null && i < a.Count ? (a[i] ?? "") : "";

        /// <summary>
        /// One line a caller can print. Deliberately says what was NOT built as well as
        /// what was — a tally that reports only successes is how this stayed invisible.
        /// </summary>
        public static string Summary(IReadOnlyCollection<CompoundLayerPlan> plans)
        {
            if (plans == null || plans.Count == 0) return "No rows planned.";
            int declared = plans.Count(p => p.Outcome == LayerPlanOutcome.Declared);
            int homo = plans.Count(p => p.Outcome == LayerPlanOutcome.NoLayersDeclared);
            int failed = plans.Count(p => p.Outcome == LayerPlanOutcome.ParseFailure);
            int invented = plans.Sum(p => p.Issues.Count(i => i.Kind == LayerIssueKind.ThicknessInvented));
            int dropped = plans.Sum(p => p.Issues.Count(i => i.Kind == LayerIssueKind.LayerDropped));
            int floored = plans.Sum(p => p.Issues.Count(i => i.Kind == LayerIssueKind.ThicknessFloored));

            return $"{plans.Count} row(s): {declared} built from their declared layers, "
                 + $"{homo} single-layer because the row declares none, "
                 + $"{failed} declare layers that could not be read.\n"
                 + $"{invented} thickness(es) could not be read, {dropped} layer(s) dropped, "
                 + $"{floored} raised to Revit's 1 mm minimum.";
        }
    }
}
