using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Every `qr-code` slot in STING_TITLE_BLOCKS.json must sit on the paper and on
    /// empty paper.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// The first cut of the QR slot used `fracAnchor`/`fracSize` resolved against the
    /// family's drawable rect. On A1 that gave the right answer. On A0 it resolved to
    /// y = -29 mm and on A3 portrait to y = -5 mm — OFF THE PAPER, where the stamp is
    /// invisible on screen AND absent from the plot, with nothing reporting a thing.
    /// It was caught by arithmetic before it shipped, which is luck, not process.
    ///
    /// The second risk is subtler: a cell that is free by ANCHOR POINT and occupied by
    /// the text that runs from it. A label anchored at x=754 with a 28-character value
    /// reaches past x=800. Checking anchors only would call that free.
    ///
    /// So this checks the shipped coordinates against conservative text EXTENTS and
    /// against every other slot. A layout edit that walks content under a QR now fails
    /// here rather than on a plotted drawing, where it cannot be recalled.
    ///
    /// It is deliberately a STATIC geometry check. It does not prove the stamper places
    /// the image correctly in Revit — that is ROADMAP QR-1 and needs Revit.
    /// </summary>
    public class TitleBlockQrSlotTests
    {
        private const string QrPurposeTag = "qr-code";

        // ─── loading ─────────────────────────────────────────────────────────

        private static JObject Library()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var p = Path.Combine(dir.FullName, "StingTools", "Data", "STING_TITLE_BLOCKS.json");
                if (File.Exists(p)) return JObject.Parse(File.ReadAllText(p));
                dir = dir.Parent;
            }
            throw new FileNotFoundException(
                "STING_TITLE_BLOCKS.json not found walking up from " + AppContext.BaseDirectory);
        }

        private static Dictionary<string, JToken> Families()
        {
            var map = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var f in (JArray)Library()["families"]) map[(string)f["id"]] = f;
            return map;
        }

        /// <summary>Leaf-wins inheritance: the first non-empty value up the `extends`
        /// chain. Matches how TitleBlockLibrary.Resolve folds a spec.</summary>
        private static JArray Inherited(Dictionary<string, JToken> fams, string id, string key)
        {
            var f = fams.TryGetValue(id, out var x) ? x : null;
            while (f != null)
            {
                if (f[key] is JArray a && a.Count > 0) return a;
                var parent = (string)f["extends"];
                f = parent != null && fams.TryGetValue(parent, out var p) ? p : null;
            }
            return new JArray();
        }

        // ─── geometry ────────────────────────────────────────────────────────

        private readonly struct Box
        {
            public readonly double X0, Y0, X1, Y1;
            public readonly string What;
            public Box(double x0, double y0, double x1, double y1, string what)
            { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; What = what; }
            public bool Overlaps(in Box o) => !(X1 <= o.X0 || o.X1 <= X0 || Y1 <= o.Y0 || o.Y1 <= Y0);
        }

        /// <summary>Conservative bounding box for a label or a static text run.
        ///
        /// The anchor is a POINT; the glyphs run from it, the direction set by hAlign.
        /// A label's value is a runtime parameter we cannot know, so assume a generous
        /// 28 characters — rejecting a candidate cell is cheap, discovering the overlap
        /// on an issued drawing is not.</summary>
        private static Box TextBox(JToken it)
        {
            var a = (JArray)it["anchor"];
            double ax = (double)a[0], ay = (double)a[1];
            double size = it["size"] != null ? (double)it["size"] : 2.0;
            string text = (string)it["text"];
            int chars = text != null ? text.Length : 28;
            double w = chars * size * 0.62;   // ~0.62 em average advance
            double h = size * 1.6;

            string ha = ((string)it["hAlign"] ?? "Left").ToLowerInvariant();
            double x0 = ha == "right" ? ax - w : ha == "center" ? ax - w / 2 : ax;
            double x1 = x0 + w;

            string va = ((string)it["vAlign"] ?? "Middle").ToLowerInvariant();
            double y0 = va == "bottom" ? ay : va == "top" ? ay - h : ay - h / 2;
            double y1 = y0 + h;

            return new Box(x0, y0, x1, y1, (string)it["param"] ?? text ?? "(text)");
        }

        /// <summary>The PHYSICAL paper this family prints on, mm.
        ///
        /// Taken from the family's OWN border lines — the nearest-to-leaf non-empty
        /// `lines` array — NOT from the resolved effective geometry.
        ///
        /// Those are different, and the difference is a real defect this test
        /// uncovered. TitleBlockSpec.Resolve CONCATENATES `Lines` up the extends
        /// chain; it is leaf-wins only for parameters, slots, static text and labels.
        /// So the effective geometry of A3_PORT_common_v2.0 spans 841 x 594 mm —
        /// A1's border, inherited — while the sheet it prints on is 297 x 420.
        /// See Inherited_A1_border_geometry_leaks_into_smaller_sheets.
        ///
        /// For "is the QR cell on the paper?" the PHYSICAL sheet is the right bound,
        /// and it is the stricter one. Using the concatenated extent would let an A3
        /// slot sit at x = 700 mm — off the A3 sheet entirely — and still pass.</summary>
        private static (double w, double h) Paper(Dictionary<string, JToken> fams, string id)
        {
            double w = 0, h = 0;
            foreach (var l in Inherited(fams, id, "lines"))
                foreach (var key in new[] { "from", "to" })
                    if (l[key] is JArray p)
                    {
                        w = Math.Max(w, (double)p[0]);
                        h = Math.Max(h, (double)p[1]);
                    }
            return (w, h);
        }

        /// <summary>Effective geometry extent — what the built .rfa would contain.
        ///
        /// MIRRORS TitleBlockSpec.MergeInto: `lines` concatenate up the chain, EXCEPT
        /// where a spec sets `replacesInheritedGeometry`, which discards everything
        /// above it.
        ///
        /// This walk previously ignored the flag, so it kept reporting the old
        /// pre-fix extents and five of the pinned paper sizes failed. That failure is
        /// the useful kind: a test measuring something different from what the code
        /// does is the defect it was written to catch, one level up.</summary>
        private static (double w, double h) ResolvedLineExtent(Dictionary<string, JToken> fams, string id)
        {
            // Root-first, so a `replacesInheritedGeometry` spec can drop what came
            // before it — exactly as Resolve folds root → … → leaf.
            var chain = new List<JToken>();
            var f = fams.TryGetValue(id, out var x) ? x : null;
            while (f != null)
            {
                chain.Add(f);
                var parent = (string)f["extends"];
                f = parent != null && fams.TryGetValue(parent, out var pf) ? pf : null;
            }
            chain.Reverse();

            var effective = new List<JToken>();
            foreach (var spec in chain)
            {
                var own = spec["lines"] as JArray;
                if ((bool?)spec["replacesInheritedGeometry"] == true) effective.Clear();
                if (own != null) effective.AddRange(own);
            }

            double w = 0, h = 0;
            foreach (var l in effective)
                foreach (var key in new[] { "from", "to" })
                    if (l[key] is JArray p)
                    {
                        w = Math.Max(w, (double)p[0]);
                        h = Math.Max(h, (double)p[1]);
                    }
            return (w, h);
        }

        public static IEnumerable<object[]> FamiliesWithQrSlot()
        {
            foreach (var f in (JArray)Library()["families"])
            {
                var qr = (f["slots"] as JArray)?.FirstOrDefault(
                    s => string.Equals((string)s["purposeTag"], QrPurposeTag, StringComparison.OrdinalIgnoreCase));
                if (qr != null) yield return new object[] { (string)f["id"] };
            }
        }

        // ─── the checks ──────────────────────────────────────────────────────

        [Fact]
        public void Every_family_that_declares_slots_declares_a_qr_slot()
        {
            // `slots` is leaf-wins: a family declaring its own array REPLACES its base's,
            // so a family that declares slots and omits QR silently loses the stamp and
            // falls back to a blind corner. That is the shape of the original defect —
            // a feature quietly absent — so it is asserted rather than left to review.
            var missing = ((JArray)Library()["families"])
                .Where(f => f["slots"] is JArray a && a.Count > 0)
                .Where(f => !((JArray)f["slots"]).Any(
                    s => string.Equals((string)s["purposeTag"], QrPurposeTag, StringComparison.OrdinalIgnoreCase)))
                .Select(f => (string)f["id"])
                .ToList();

            Assert.True(missing.Count == 0,
                "These families declare their own slots[] and so shadow the base's QR slot, " +
                "leaving the stamp with only a blind corner fallback:\n  " + string.Join("\n  ", missing));
        }

        [Theory]
        [MemberData(nameof(FamiliesWithQrSlot))]
        public void Qr_slot_uses_absolute_mm_not_fractions(string familyId)
        {
            var qr = QrSlot(familyId);

            // fracAnchor resolves against the DRAWABLE rect, which excludes the title
            // strip the QR lives in — that is precisely how the first cut landed at
            // y = -29 mm on A0. Absolute mm is the contract.
            Assert.True(qr["fracAnchor"] == null,
                $"{familyId}: the QR slot must not carry fracAnchor — fractional coords " +
                "resolve against the drawable rect and put the stamp off the paper.");
            Assert.True(qr["fracSize"] == null, $"{familyId}: the QR slot must not carry fracSize.");
            Assert.NotNull(qr["anchor"]);
            Assert.NotNull(qr["size"]);
        }

        [Theory]
        [MemberData(nameof(FamiliesWithQrSlot))]
        public void Qr_slot_is_on_the_paper(string familyId)
        {
            var fams = Families();
            var (pw, ph) = Paper(fams, familyId);
            Assert.True(pw > 0 && ph > 0, $"{familyId}: could not determine paper extent from its border lines.");

            var cell = SlotBox(familyId);
            Assert.True(cell.X0 >= 0 && cell.Y0 >= 0,
                $"{familyId}: QR slot starts off the paper at ({cell.X0}, {cell.Y0}).");
            Assert.True(cell.X1 <= pw && cell.Y1 <= ph,
                $"{familyId}: QR slot ends at ({cell.X1}, {cell.Y1}), past the {pw}x{ph} mm sheet.");
        }

        [Theory]
        [MemberData(nameof(FamiliesWithQrSlot))]
        public void Qr_slot_is_big_enough_to_scan(string familyId)
        {
            var cell = SlotBox(familyId);
            double w = cell.X1 - cell.X0, h = cell.Y1 - cell.Y0;

            // A ~25-module code at 12 mm gives a 0.48 mm module, at the edge of what a
            // phone camera resolves on paper at arm's length. Below that it is decoration.
            Assert.True(Math.Min(w, h) >= 12.0,
                $"{familyId}: QR slot is {w}x{h} mm — under 12 mm the modules stop resolving on a print.");
            Assert.True(Math.Abs(w - h) < 0.01,
                $"{familyId}: QR slot is {w}x{h} mm; a QR is square, so a non-square slot wastes one axis.");
        }

        [Theory]
        [MemberData(nameof(FamiliesWithQrSlot))]
        public void Qr_slot_does_not_sit_on_other_content(string familyId)
        {
            var fams = Families();
            var cell = SlotBox(familyId);
            var qr = QrSlot(familyId);
            bool isOverlay = string.Equals((string)qr["category"], "overlay", StringComparison.OrdinalIgnoreCase);

            var hits = new List<string>();

            // Labels and static text — by EXTENT, not by anchor point.
            foreach (var key in new[] { "labels", "staticText" })
                foreach (var it in Inherited(fams, familyId, key))
                {
                    if (it["anchor"] == null) continue;
                    var b = TextBox(it);
                    if (cell.Overlaps(b)) hits.Add($"{key}: {b.What}");
                }

            // Other slots.
            foreach (var s in (JArray)fams[familyId]["slots"])
            {
                if (string.Equals((string)s["purposeTag"], QrPurposeTag, StringComparison.OrdinalIgnoreCase)) continue;
                if (s["anchor"] is not JArray a || s["size"] is not JArray sz) continue;
                var b = new Box((double)a[0], (double)sz[1] * 0 + (double)a[1],
                                (double)a[0] + (double)sz[0], (double)a[1] + (double)sz[1],
                                "slot " + (string)s["id"]);
                if (!cell.Overlaps(b)) continue;

                // An `overlay` QR is DECLARED to sit on top of another slot — the two
                // presentation blocks have a full-bleed RENDER covering the whole sheet,
                // so there is no free cell and the overlap is the design, not a defect.
                // Text is never an acceptable thing to cover, overlay or not.
                if (isOverlay) continue;
                hits.Add(b.What);
            }

            Assert.True(hits.Count == 0,
                $"{familyId}: the QR slot at ({cell.X0}, {cell.Y0})-({cell.X1}, {cell.Y1}) covers:\n  "
                + string.Join("\n  ", hits.Distinct())
                + "\n\nMove the slot, or mark it category \"overlay\" if covering a full-bleed slot is intended "
                + "(that never excuses covering text).");
        }

        [Theory]
        [MemberData(nameof(FamiliesWithQrSlot))]
        public void Qr_slot_respects_the_show_toggle(string familyId)
        {
            // PRJ_TB_SHOW_QR_CODE_BOOL is the operator's opt-out, and the one that matters
            // most on client-facing presentation blocks. A slot that ignores it gives them
            // no way to say no.
            Assert.True((bool?)QrSlot(familyId)["respectShowToggle"] == true,
                $"{familyId}: the QR slot must set respectShowToggle so {"PRJ_TB_SHOW_QR_CODE_BOOL"} can hide it.");
        }

        [Fact]
        public void Root_declares_both_qr_parameters()
        {
            // The stamper writes TB_QR_PAYLOAD_TXT and reads PRJ_TB_SHOW_QR_CODE_BOOL.
            // Both must be minted into every family, which means declaring them on the
            // root every other spec extends. Missing either is the exact prior state:
            // documented everywhere, present nowhere.
            var root = Families()["A1_common_v2.0"];
            var names = ((JArray)root["parameters"]).Select(p => (string)p["name"]).ToList();
            Assert.Contains("TB_QR_PAYLOAD_TXT", names);
            Assert.Contains("PRJ_TB_SHOW_QR_CODE_BOOL", names);
        }

        /// <summary>
        /// TB-LINES-1 — no family may carry geometry larger than its own sheet.
        ///
        /// `Lines` CONCATENATE up the extends chain (they have no natural id, unlike
        /// the id-aware static text and labels). Every size base extends
        /// A1_common_v2.0, so an A3-portrait family used to inherit A1's 841 x 594
        /// border IN ADDITION to its own 297 x 420 one — a built .rfa with an A1
        /// border drawn across an A3 sheet. 22 families were affected.
        ///
        /// Families at the SAME size as A1 were affected too, more quietly: the
        /// assembly, presentation, cover, divider, register and submission blocks
        /// each redraw the full border, so the inherited copy landed exactly on top
        /// of theirs — duplicate overlapping linework, invisible on screen and
        /// doubled in the file.
        ///
        /// `replacesInheritedGeometry` fixes it. This test is the ratchet: it FAILS,
        /// rather than recording, because the defect is now closed and re-opening it
        /// would put an A1 border back on an A3 drawing.
        ///
        /// Found by rendering the spec previews while verifying the A3-portrait QR
        /// cell: the preview header read "paper 841 x 594 mm" on a family whose own
        /// description says "A3 portrait working sheet (297 x 420 mm)".
        /// </summary>
        [Fact]
        public void No_family_carries_geometry_larger_than_its_own_sheet()
        {
            var fams = Families();
            var offenders = new List<string>();

            foreach (var f in (JArray)Library()["families"])
            {
                var id = (string)f["id"];
                var resolved = ResolvedLineExtent(fams, id);
                if (resolved.w <= 0) continue;

                var sheet = SheetSize(fams, id);
                if (sheet.w <= 0) continue;

                if (resolved.w > sheet.w + 0.001 || resolved.h > sheet.h + 0.001)
                    offenders.Add(
                        $"{id}: geometry spans {resolved.w:F0} x {resolved.h:F0} mm "
                        + $"on a {sheet.w:F0} x {sheet.h:F0} mm sheet");
            }

            Assert.True(offenders.Count == 0,
                "These families carry line geometry beyond their own sheet — the A1 border is "
                + "leaking in again (TB-LINES-1). A family that declares its own complete "
                + "border must set \"replacesInheritedGeometry\": true:\n  "
                + string.Join("\n  ", offenders));
        }

        /// <summary>Every family that draws a complete sheet border of its own must
        /// declare that it replaces inherited geometry — otherwise it silently
        /// double-draws its parent's border on top of its own.</summary>
        [Fact]
        public void A_family_drawing_its_own_border_declares_that_it_replaces()
        {
            var missing = new List<string>();

            foreach (var f in (JArray)Library()["families"])
            {
                if (f["extends"] == null) continue;            // the root replaces nothing
                if (f["lines"] is not JArray own || own.Count == 0) continue;

                var e = ExtentOf(own);
                if (!DrawsFullBorder(own, e.w, e.h)) continue; // only adds — must inherit

                if ((bool?)f["replacesInheritedGeometry"] != true)
                    missing.Add($"{(string)f["id"]} ({e.w:F0} x {e.h:F0} mm)");
            }

            Assert.True(missing.Count == 0,
                "These families draw a complete sheet border of their own but do not set "
                + "\"replacesInheritedGeometry\": true, so their parent's border is drawn on "
                + "top of it:\n  " + string.Join("\n  ", missing));
        }

        /// <summary>And the converse: a family that only ADDS to its parent — an info
        /// strip, a banner — must NOT set the flag, or it discards the very border it
        /// is drawing onto and ships a sheet with no outline at all.</summary>
        [Fact]
        public void A_family_that_only_adds_does_not_claim_to_replace()
        {
            var wrong = new List<string>();

            foreach (var f in (JArray)Library()["families"])
            {
                if ((bool?)f["replacesInheritedGeometry"] != true) continue;
                if (f["lines"] is not JArray own || own.Count == 0)
                {
                    wrong.Add($"{(string)f["id"]} declares no lines of its own");
                    continue;
                }
                var e = ExtentOf(own);
                if (!DrawsFullBorder(own, e.w, e.h))
                    wrong.Add($"{(string)f["id"]} draws no complete border ({e.w:F0} x {e.h:F0} mm)");
            }

            Assert.True(wrong.Count == 0,
                "These families claim to replace inherited geometry but do not draw a complete "
                + "sheet border, so they would ship with no outline:\n  "
                + string.Join("\n  ", wrong));
        }

        [Fact]
        public void The_A3_portrait_sheet_is_A3_portrait()
        {
            // The case that started TB-LINES-1, pinned by its numbers. The preview
            // header said 841 x 594 for this family; it is a 297 x 420 sheet.
            var fams = Families();
            var resolved = ResolvedLineExtent(fams, "A3_PORT_common_v2.0");

            Assert.Equal(297, resolved.w, 1);
            Assert.Equal(420, resolved.h, 1);
        }

        [Theory]
        [InlineData("A3_PORT_common_v2.0", 297, 420)]
        [InlineData("A3_LAND_common_v2.0", 420, 297)]
        [InlineData("A2_PORT_common_v2.0", 420, 594)]
        [InlineData("A2_LAND_common_v2.0", 594, 420)]
        [InlineData("A0_PORT_common_v2.0", 841, 1189)]
        [InlineData("A0_LAND_common_v2.0", 1189, 841)]
        [InlineData("STING_TB_COVER_A3_v1.0", 420, 297)]
        [InlineData("STING_TB_A3_PORT_BIM_v2.0", 297, 420)]
        public void Every_paper_size_resolves_to_its_real_dimensions(string familyId, double w, double h)
        {
            // Pinned by VALUE so a regression in the fold is a named failure rather
            // than a subtly oversized border nobody notices until it plots.
            var resolved = ResolvedLineExtent(Families(), familyId);

            Assert.Equal(w, resolved.w, 1);
            Assert.Equal(h, resolved.h, 1);
        }

        /// <summary>The physical sheet and the resolved geometry now AGREE.
        ///
        /// Before TB-LINES-1 they did not: A3 portrait printed on 297 x 420 mm while
        /// its resolved geometry spanned A1's 841 x 594, because the border
        /// concatenated up the chain. The QR test bounded cells by the physical sheet
        /// precisely because it was the stricter of two disagreeing numbers.
        ///
        /// With the leak closed there is only one number, and this asserts that —
        /// on the families where the gap used to be widest. If the fold regresses,
        /// these are the first to notice.</summary>
        [Theory]
        [InlineData("A3_PORT_common_v2.0")]
        [InlineData("A3_LAND_common_v2.0")]
        [InlineData("A2_PORT_common_v2.0")]
        [InlineData("A0_PORT_common_v2.0")]
        [InlineData("STING_TB_CLARIFICATION_A3_v1.0")]
        public void The_physical_sheet_and_the_resolved_geometry_agree(string familyId)
        {
            var fams = Families();
            var physical = Paper(fams, familyId);
            var resolved = ResolvedLineExtent(fams, familyId);

            Assert.Equal(physical.w, resolved.w, 1);
            Assert.Equal(physical.h, resolved.h, 1);
        }

        // ─── helpers ─────────────────────────────────────────

        /// <summary>Extent of a raw line array, mm.</summary>
        private static (double w, double h) ExtentOf(JArray lines)
        {
            double w = 0, h = 0;
            foreach (var l in lines)
                foreach (var key in new[] { "from", "to" })
                    if (l[key] is JArray p)
                    {
                        w = Math.Max(w, (double)p[0]);
                        h = Math.Max(h, (double)p[1]);
                    }
            return (w, h);
        }

        /// <summary>Does this line set close a complete outer border — one line
        /// spanning the full width and one the full height?
        ///
        /// This is how the initial `replacesInheritedGeometry` values were chosen,
        /// and it stays here as the test's own definition of "declares its own
        /// sheet". The SPEC is the contract; this is the check that the spec and the
        /// geometry still agree.</summary>
        private static bool DrawsFullBorder(JArray lines, double w, double h, double tol = 1.0)
        {
            bool horiz = false, vert = false;
            foreach (var l in lines)
            {
                if (l["from"] is not JArray a || l["to"] is not JArray b) continue;
                double ax = (double)a[0], ay = (double)a[1], bx = (double)b[0], by = (double)b[1];
                if (Math.Abs(ay - by) < tol && Math.Abs(Math.Min(ax, bx)) < tol
                    && Math.Abs(Math.Max(ax, bx) - w) < tol) horiz = true;
                if (Math.Abs(ax - bx) < tol && Math.Abs(Math.Min(ay, by)) < tol
                    && Math.Abs(Math.Max(ay, by) - h) < tol) vert = true;
            }
            return horiz && vert;
        }

        /// <summary>The sheet a family prints on: the nearest ancestor (inclusive)
        /// that draws a complete border. A family declaring only an info strip takes
        /// its size base's paper, which is exactly right — the strip is drawn ONTO
        /// that sheet.</summary>
        private static (double w, double h) SheetSize(Dictionary<string, JToken> fams, string id)
        {
            var f = fams.TryGetValue(id, out var x) ? x : null;
            while (f != null)
            {
                if (f["lines"] is JArray own && own.Count > 0)
                {
                    var e = ExtentOf(own);
                    if (DrawsFullBorder(own, e.w, e.h)) return e;
                }
                var parent = (string)f["extends"];
                f = parent != null && fams.TryGetValue(parent, out var pf) ? pf : null;
            }
            return (0, 0);
        }
        // ────────────────

        private static JToken QrSlot(string familyId)
            => ((JArray)Families()[familyId]["slots"]).First(
                s => string.Equals((string)s["purposeTag"], QrPurposeTag, StringComparison.OrdinalIgnoreCase));

        private static Box SlotBox(string familyId)
        {
            var qr = QrSlot(familyId);
            var a = (JArray)qr["anchor"];
            var sz = (JArray)qr["size"];
            return new Box((double)a[0], (double)a[1],
                           (double)a[0] + (double)sz[0], (double)a[1] + (double)sz[1], "QR");
        }
    }
}
