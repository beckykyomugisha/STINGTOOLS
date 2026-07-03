using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.BOQ;
using Xunit;

namespace StingTools.Scheduling.Tests
{
    /// <summary>
    /// P0-7 consolidation invariant — there is ONE costing procedure.
    ///
    /// The 4D/5D cost estimate (Scheduling4DEngine.GenerateCostEstimate) and the
    /// BIMManager 5D export both source their line items from the canonical BOQ
    /// (BuildBOQDocument). The element→rate→quantity path is shared by
    /// construction (every consumer calls BuildLineItemFromElement), which needs
    /// the Revit API and is verified in-Revit. What CAN be pinned headlessly is
    /// the SEAM both paths cross: Boq5DEstimateAssembler. These tests prove the
    /// 5D estimate (a) sums the canonical per-line totals verbatim — it never
    /// re-derives qty×rate — and (b) applies the ONE markup waterfall
    /// (BoqTotals.Compute, incl VAT), so its numbers cannot drift from the BOQ.
    /// </summary>
    public class Boq5DConsolidationTests
    {
        private static Boq5DMarkup Markup() => new Boq5DMarkup
        {
            // Prelims supplied as an absolute (the BOQ resolves works×prelim% or
            // the itemised schedule upstream); 1000 here = 10% of the 10,000 works.
            PrelimsAbsoluteUgx = 1000,
            PrelimPct = 10,
            OverheadPct = 8,
            ContingencyPct = 10,
            VatPct = 18
        };

        private static List<Boq5DRow> SampleRows() => new List<Boq5DRow>
        {
            new Boq5DRow { Category = "Walls", Discipline = "A", Quantity = 40, Unit = "m²", RateUgx = 100, LineTotalUgx = 4000, Description = "Wall" },
            new Boq5DRow { Category = "Walls", Discipline = "A", Quantity = 10, Unit = "m²", RateUgx = 100, LineTotalUgx = 1000, Description = "Wall" },
            new Boq5DRow { Category = "Floors", Discipline = "S", Quantity = 50, Unit = "m²", RateUgx = 100, LineTotalUgx = 5000, Description = "Floor" },
        };

        [Fact]
        public void Subtotal_Sums_Canonical_Line_Totals()
        {
            var rows = SampleRows();
            var est = Boq5DEstimateAssembler.Assemble(rows, Markup(), "P", "UGX", "now");

            double expected = rows.Sum(r => r.LineTotalUgx); // 10,000
            Assert.Equal(expected, (double)est["subtotal"], 3);
        }

        [Fact]
        public void Line_Total_Is_Summed_Not_Recomputed_From_Qty_Times_Rate()
        {
            // A loaded/override rate: the canonical LineTotal deliberately is NOT
            // Quantity × RateUgx. The assembler must carry the canonical total,
            // never multiply qty by rate itself.
            var rows = new List<Boq5DRow>
            {
                new Boq5DRow { Category = "Steel", Discipline = "S", Quantity = 10, Unit = "kg", RateUgx = 100, LineTotalUgx = 1500 }
            };
            var est = Boq5DEstimateAssembler.Assemble(rows, Markup(), "P", "UGX", "now");

            var line = (JArray)est["line_items"];
            // total is the canonical 1500, not 10 × 100 = 1000.
            Assert.Equal(1500, (double)line[0]["total"], 3);
            Assert.Equal(1500, (double)est["subtotal"], 3);
        }

        [Fact]
        public void Grand_Total_Uses_The_One_Markup_Waterfall_Incl_Vat()
        {
            var rows = SampleRows();
            var m = Markup();
            var est = Boq5DEstimateAssembler.Assemble(rows, m, "P", "UGX", "now");

            double works = rows.Sum(r => r.LineTotalUgx);
            var canonical = BoqTotals.Compute(works, m.PrelimsAbsoluteUgx,
                m.OverheadPct, m.ContingencyPct, m.VatPct);

            // The 5D grand total IS the canonical Contract Sum (incl VAT) — not
            // the old flat, VAT-less "Σ + Σ×p%" formula.
            Assert.Equal(canonical.GrandTotal, (double)est["grand_total"], 3);
            Assert.Equal(canonical.Vat, (double)est["vat"], 3);
            Assert.Equal(canonical.Overhead, (double)est["overhead_profit"], 3);
            Assert.Equal(canonical.Contingency, (double)est["contingency"], 3);
            Assert.Equal(canonical.NetExVat, (double)est["net_ex_vat"], 3);
        }

        [Fact]
        public void Old_Flat_VatLess_Formula_Is_No_Longer_Used()
        {
            var rows = SampleRows();
            var m = Markup();
            var est = Boq5DEstimateAssembler.Assemble(rows, m, "P", "UGX", "now");

            double works = rows.Sum(r => r.LineTotalUgx); // 10,000
            // The retired fork computed: works + works×prelim% + works×cont% +
            // works×ohp%, with NO VAT and every % off the bare subtotal.
            double legacyFlat = works
                + works * m.PrelimPct / 100.0
                + works * m.ContingencyPct / 100.0
                + works * m.OverheadPct / 100.0;
            Assert.NotEqual(legacyFlat, (double)est["grand_total"], 0);
        }

        [Fact]
        public void Rows_Group_By_Category_With_Summed_Quantity()
        {
            var est = Boq5DEstimateAssembler.Assemble(SampleRows(), Markup(), "P", "UGX", "now");
            var lines = (JArray)est["line_items"];

            // Walls (40 + 10) collapse to one line; Floors a second. 2 lines.
            Assert.Equal(2, lines.Count);
            var walls = lines.Single(l => (string)l["category"] == "Walls");
            Assert.Equal(50, (double)walls["quantity"], 3);
            Assert.Equal(5000, (double)walls["total"], 3);
            // Value-weighted unit rate = 5000 / 50 = 100.
            Assert.Equal(100, (double)walls["unit_rate"], 3);
        }

        [Fact]
        public void Discipline_Totals_Reconcile_To_Subtotal()
        {
            var est = Boq5DEstimateAssembler.Assemble(SampleRows(), Markup(), "P", "UGX", "now");
            var disc = (JObject)est["discipline_totals"];
            double sum = disc.Properties().Sum(p => (double)p.Value);
            Assert.Equal((double)est["subtotal"], sum, 3);
        }

        [Fact]
        public void Consumer_Works_Total_Equals_Boq_Line_Total_Sum()
        {
            // THE invariant: the works subtotal a 5D consumer sees equals the sum
            // of the canonical BOQ line totals it was handed — a single source of
            // truth, no parallel take-off in between.
            var rows = SampleRows();
            var est = Boq5DEstimateAssembler.Assemble(rows, Markup(), "P", "UGX", "now");

            double boqLineTotalSum = rows.Sum(r => r.LineTotalUgx);
            Assert.Equal(boqLineTotalSum, (double)est["subtotal"], 3);
        }

        [Fact]
        public void Zero_Quantity_Category_Does_Not_Divide_By_Zero()
        {
            var rows = new List<Boq5DRow>
            {
                new Boq5DRow { Category = "Provisional", Discipline = "G", Quantity = 0, Unit = "item", RateUgx = 0, LineTotalUgx = 7500 }
            };
            var est = Boq5DEstimateAssembler.Assemble(rows, Markup(), "P", "UGX", "now");
            var line = (JArray)est["line_items"];
            Assert.Equal(7500, (double)line[0]["total"], 3);
            Assert.Equal(0, (double)line[0]["unit_rate"], 3);
        }
    }
}
