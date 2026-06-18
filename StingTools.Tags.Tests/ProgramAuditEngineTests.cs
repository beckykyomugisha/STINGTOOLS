using System.Collections.Generic;
using StingTools.Core.Validation;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Covers the pure join + area/count comparison logic of the Program Audit
    /// comparator (Revit/Excel IO lives in ProgramAuditCommand and is not under
    /// test here).
    /// </summary>
    public class ProgramAuditEngineTests
    {
        private static ProgramRow P(string name, string number, double? areaM2, int? count = null, string bldg = "")
            => new ProgramRow { RoomName = name, RoomNumber = number, RequiredAreaM2 = areaM2, RequiredCount = count, Building = bldg };

        private static ModelRoomRow M(string name, string number, double areaM2, long id)
            => new ModelRoomRow { Name = name, Number = number, AreaM2 = areaM2, ElementId = id };

        // ── Normalize ───────────────────────────────────────────────────
        [Theory]
        [InlineData("Office 1.2", "office12")]
        [InlineData("  CHAPEL  ", "chapel")]
        [InlineData("Baptistry-A", "baptistrya")]
        [InlineData(null, "")]
        public void Normalize_strips_case_space_punctuation(string input, string expected)
            => Assert.Equal(expected, ProgramAuditEngine.Normalize(input));

        // ── Join order: number wins over name ───────────────────────────
        [Fact]
        public void Join_prefers_room_number_over_name()
        {
            var program = new List<ProgramRow> { P("Office", "1.05", 20.0) };
            // Two model rooms: one matches by name, one by number. Number must win.
            var model = new List<ModelRoomRow>
            {
                M("Office", "9.99", 100.0, 1),   // name match (wrong area)
                M("Misc", "105", 20.0, 2),       // number match (right area, normalised "105")
            };
            var r = ProgramAuditEngine.Compare(program, model, 5.0);
            var row = r.Rows.Find(x => x.Status != ProgramAuditStatus.ExtraInModel);
            Assert.Equal(2, row.ModelElementId);
            Assert.Equal(ProgramAuditStatus.Compliant, row.Status);
        }

        // ── Area bands ──────────────────────────────────────────────────
        [Fact]
        public void Area_within_tolerance_is_compliant()
        {
            var r = ProgramAuditEngine.Compare(
                new List<ProgramRow> { P("Room A", "A1", 100.0) },
                new List<ModelRoomRow> { M("Room A", "A1", 104.0, 1) }, 5.0);
            Assert.Equal(ProgramAuditStatus.Compliant, r.Rows[0].Status);
            Assert.Equal(1, r.Compliant);
        }

        [Fact]
        public void Area_over_and_under_tolerance_are_flagged()
        {
            var over = ProgramAuditEngine.Compare(
                new List<ProgramRow> { P("Room A", "A1", 100.0) },
                new List<ModelRoomRow> { M("Room A", "A1", 120.0, 1) }, 5.0);
            Assert.Equal(ProgramAuditStatus.OverArea, over.Rows[0].Status);
            Assert.Equal(20.0, over.Rows[0].DeltaPct);

            var under = ProgramAuditEngine.Compare(
                new List<ProgramRow> { P("Room A", "A1", 100.0) },
                new List<ModelRoomRow> { M("Room A", "A1", 80.0, 1) }, 5.0);
            Assert.Equal(ProgramAuditStatus.UnderArea, under.Rows[0].Status);
            Assert.Equal(-20.0, under.Rows[0].DeltaPct);
        }

        // ── Missing / extra ─────────────────────────────────────────────
        [Fact]
        public void Unmatched_template_row_is_missing_and_unmatched_room_is_extra()
        {
            var program = new List<ProgramRow> { P("Vestry", "V1", 15.0) };
            var model = new List<ModelRoomRow> { M("Plant Room", "P1", 40.0, 7) };
            var r = ProgramAuditEngine.Compare(program, model, 5.0);
            Assert.Equal(1, r.Missing);
            Assert.Equal(1, r.Extra);
            Assert.Contains(r.Rows, x => x.Status == ProgramAuditStatus.MissingFromModel && x.RoomName == "Vestry");
            Assert.Contains(r.Rows, x => x.Status == ProgramAuditStatus.ExtraInModel && x.ModelElementId == 7);
        }

        // ── Normalised-name fallback join ───────────────────────────────
        [Fact]
        public void Falls_back_to_normalised_name_when_number_absent()
        {
            var program = new List<ProgramRow> { P("Sealing Room", "", 25.0) };
            var model = new List<ModelRoomRow> { M("sealing-room", "", 25.5, 3) };
            var r = ProgramAuditEngine.Compare(program, model, 5.0);
            Assert.Equal(ProgramAuditStatus.Compliant, r.Rows[0].Status);
            Assert.Equal(3, r.Rows[0].ModelElementId);
            Assert.Equal(0, r.Missing);
            Assert.Equal(0, r.Extra);
        }

        // ── Count comparison ────────────────────────────────────────────
        [Fact]
        public void Required_count_mismatch_is_reported()
        {
            var program = new List<ProgramRow> { P("Classroom", "C1", 30.0, count: 3) };
            var model = new List<ModelRoomRow>
            {
                M("Classroom", "C1", 30.0, 1),
                M("Classroom", "C2", 30.0, 2), // 2 rooms vs required 3
            };
            var r = ProgramAuditEngine.Compare(program, model, 5.0);
            Assert.Equal(1, r.CountMismatch);
            Assert.Equal(2, r.Rows[0].ActualCount);
            Assert.Contains("count 2/3", r.Rows[0].Note);
            // The second classroom is unmatched by a program row → extra
            Assert.Equal(1, r.Extra);
        }

        [Fact]
        public void No_required_area_still_matches_compliant()
        {
            var r = ProgramAuditEngine.Compare(
                new List<ProgramRow> { P("Corridor", "X1", null) },
                new List<ModelRoomRow> { M("Corridor", "X1", 12.0, 1) }, 5.0);
            Assert.Equal(ProgramAuditStatus.Compliant, r.Rows[0].Status);
        }
    }
}
