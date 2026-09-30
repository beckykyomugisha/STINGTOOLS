// The drawing-type pre-flight fails only on errors in types the project uses. An error in
// a type it never produces must be reported without failing the setup step, or the gate
// fails on every project and stops meaning anything.

using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class PreflightScopeTests
    {
        [Fact]
        public void An_error_in_a_type_the_project_does_not_use_does_not_fail()
        {
            var v = PreflightScope.Split(new[] { "health-medgas-pln-A1-1to100" }, new[] { "elec-power-A1-1to100" });
            Assert.False(v.Fails);
            Assert.Equal(new[] { "health-medgas-pln-A1-1to100" }, v.Unused);
        }

        [Fact]
        public void An_error_in_a_used_type_fails_and_names_it()
        {
            var v = PreflightScope.Split(new[] { "elec-power-A1-1to100", "pres-3d-axon-A1" },
                                         new[] { "ELEC-POWER-A1-1TO100" });
            Assert.True(v.Fails);
            Assert.Equal(new[] { "elec-power-A1-1to100" }, v.Blocking);
            Assert.Equal(new[] { "pres-3d-axon-A1" }, v.Unused);
        }

        [Fact]
        public void No_errors_never_fails()
            => Assert.False(PreflightScope.Split(new string[0], new[] { "mep-plan-A1-1to100" }).Fails);

        [Fact]
        public void Nulls_and_blanks_are_ignored()
        {
            var v = PreflightScope.Split(new[] { null, " ", "x" }, null);
            Assert.Equal(new[] { "x" }, v.Unused);
            Assert.False(v.Fails);
        }
    }
}
