using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Placement;
using Xunit;

namespace StingTools.Placement.Tests
{
    /// <summary>
    /// The advisory validator's job is to tell the user when a rule field they
    /// set cannot take effect. These tests pin both halves of that contract:
    /// the advisory fires when the field is genuinely inert, and stays silent
    /// when the field is on a code path that consumes it.
    /// </summary>
    public class PlacementAdvisoryValidatorTests
    {
        private static List<string> Run(params PlacementRule[] rules)
            => PlacementAdvisoryValidator.Validate(rules, null);

        private static PlacementRule Rule(string id = "r1") => new PlacementRule
        {
            RuleId = id,
            CategoryFilter = "Electrical Fixtures",
            RoutingMode = "NONE",
        };

        // ── Router-only fields on a non-routing rule ─────────────────

        [Fact]
        public void MinSlopePercent_OnNonRoutingRule_IsReported()
        {
            var r = Rule(); r.MinSlopePercent = 1.5;
            Assert.Contains(Run(r), a => a.Contains("MinSlopePercent") && a.Contains("no effect"));
        }

        [Fact]
        public void MinSlopePercent_OnRoutingRule_IsSilent()
        {
            var r = Rule(); r.MinSlopePercent = 1.5; r.RoutingMode = "AUTO_PIPE";
            Assert.DoesNotContain(Run(r), a => a.Contains("MinSlopePercent"));
        }

        [Fact]
        public void EmitSupports_OnNonRoutingRule_IsReported()
        {
            var r = Rule(); r.EmitSupports = true;
            Assert.Contains(Run(r), a => a.Contains("EmitSupports"));
        }

        [Fact]
        public void ExposureClass_OnNonRoutingRule_IsReported()
        {
            var r = Rule(); r.ExposureClass = "XC2";
            Assert.Contains(Run(r), a => a.Contains("ExposureClass"));
        }

        [Fact]
        public void PushableFields_OnNonRoutingRule_AreReportedAsPushOnlyNotDead()
        {
            // Material / insulation / diameter still reach the family types via
            // "Push to Families" — the advisory must say so rather than implying
            // the value is discarded.
            var r = Rule();
            r.Material = "Copper";
            r.InsulationThicknessMm = 25;
            r.NominalDiameterMm = 32;

            var advisories = Run(r);
            Assert.Contains(advisories, a => a.Contains("Material") && a.Contains("Push to Families"));
            Assert.Contains(advisories, a => a.Contains("InsulationThicknessMm") && a.Contains("Push to Families"));
            Assert.Contains(advisories, a => a.Contains("NominalDiameterMm") && a.Contains("Push to Families"));
        }

        [Fact]
        public void RoutingRule_ProducesNoRouterFieldAdvisories()
        {
            var r = Rule();
            r.RoutingMode = "WALL_FOLLOWER";
            r.Material = "Copper";
            r.MinSlopePercent = 1.0;
            r.EmitSupports = true;
            r.ExposureClass = "XC2";
            r.NominalDiameterMm = 32;
            r.InsulationThicknessMm = 25;

            Assert.Empty(Run(r));
        }

        // ── Lighting-grid-only field ─────────────────────────────────

        [Fact]
        public void MinUniformityRatio_OnNonGridAnchor_IsReported()
        {
            var r = Rule(); r.MinUniformityRatio = 0.7; r.AnchorType = "ROOM_CENTRE";
            Assert.Contains(Run(r), a => a.Contains("MinUniformityRatio"));
        }

        [Theory]
        [InlineData("LIGHTING_GRID")]
        [InlineData("LUX_GRID")]
        [InlineData("EN12464")]
        [InlineData("lighting_grid")]
        public void MinUniformityRatio_OnGridAnchor_IsSilent(string anchor)
        {
            var r = Rule(); r.MinUniformityRatio = 0.7; r.AnchorType = anchor;
            Assert.DoesNotContain(Run(r), a => a.Contains("MinUniformityRatio"));
        }

        // ── Maintenance clearance class code ─────────────────────────

        [Fact]
        public void MaintenanceClearance_UnknownCode_IsReported()
        {
            // The old double-typed field made "600" a plausible thing to type;
            // MaintenanceAccessValidator would silently skip it.
            var r = Rule(); r.MaintenanceClearance = "600";
            Assert.Contains(Run(r), a => a.Contains("MaintenanceClearance"));
        }

        [Theory]
        [InlineData("FRONT_600")]
        [InlineData("FRONT_1000")]
        [InlineData("SIDES_300")]
        [InlineData("TOP_900")]
        public void MaintenanceClearance_KnownCode_IsSilent(string code)
        {
            var r = Rule(); r.MaintenanceClearance = code;
            Assert.DoesNotContain(Run(r), a => a.Contains("MaintenanceClearance"));
        }

        // ── Glazing spec vs toughened flag ───────────────────────────

        [Fact]
        public void ToughenedGlazingRequired_WithNoSpec_IsReported()
        {
            var r = Rule(); r.ToughenedGlazingRequired = true; r.GlazingSpec = "";
            Assert.Contains(Run(r), a => a.Contains("GlazingSpec is empty"));
        }

        [Fact]
        public void ToughenedGlazingRequired_WithContradictorySpec_IsReported()
        {
            var r = Rule(); r.ToughenedGlazingRequired = true; r.GlazingSpec = "CLEAR";
            Assert.Contains(Run(r), a => a.Contains("not a toughened spec"));
        }

        [Theory]
        [InlineData("TOUGHENED")]
        [InlineData("LAMINATED")]
        public void ToughenedGlazingRequired_WithSatisfyingSpec_IsSilent(string spec)
        {
            var r = Rule(); r.ToughenedGlazingRequired = true; r.GlazingSpec = spec;
            Assert.DoesNotContain(Run(r), a => a.Contains("Toughened"));
        }

        // ── Scoping ──────────────────────────────────────────────────

        [Fact]
        public void RulesThatPlacedNothing_AreSkipped()
        {
            // A rule that never fired already reports itself through the per-rule
            // diagnostics; repeating it here would be noise.
            var fired = Rule("fired");   fired.MinSlopePercent = 1.5;
            var idle  = Rule("idle");    idle.MinSlopePercent  = 1.5;

            var result = new PlacementResult();
            result.CountsByRule["fired"] = 3;
            result.CountsByRule["idle"]  = 0;

            var advisories = PlacementAdvisoryValidator.Validate(new[] { fired, idle }, result);
            Assert.Contains(advisories, a => a.Contains("fired"));
            Assert.DoesNotContain(advisories, a => a.Contains("idle"));
        }

        [Fact]
        public void CleanRuleSetProducesNoAdvisories()
        {
            Assert.Empty(Run(Rule()));
        }

        [Fact]
        public void NullAndEmptyInputsAreSafe()
        {
            Assert.Empty(PlacementAdvisoryValidator.Validate(null, null));
            Assert.Empty(PlacementAdvisoryValidator.Validate(new PlacementRule[0], null));
            Assert.Empty(PlacementAdvisoryValidator.Validate(new PlacementRule[] { null }, null));
        }
    }
}
