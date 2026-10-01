using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>TAGACC-20: executable coverage for the TAGACC-7 / 8 SYS rules.</summary>
    public class SysConnectorChoiceTests
    {
        private static ConnectorService S(string code, ServiceDomain d, bool primary = false)
            => new ConnectorService(code, primary, d);

        [Fact]
        public void Ahu_takes_its_air_service_whatever_order_the_family_author_used()
        {
            // AHU: chilled water first, then supply air — the category's domain (air) wins.
            var services = new[] { S("CHW", ServiceDomain.Piping), S("HVAC", ServiceDomain.Hvac), S("HVAC", ServiceDomain.Hvac) };
            Assert.Equal("HVAC", SysConnectorChoice.Choose(services, SysConnectorChoice.PreferredDomain("Mechanical Equipment")));
        }

        [Fact]
        public void Boiler_is_not_its_gas_connection()
        {
            // Boiler (Mechanical Equipment, no air connector): gas first, LTHW second.
            var services = new[] { S("GAS", ServiceDomain.Piping), S("HWS", ServiceDomain.Piping) };
            Assert.Equal("HWS", SysConnectorChoice.Choose(services, SysConnectorChoice.PreferredDomain("Mechanical Equipment")));
        }

        [Fact]
        public void Sink_takes_its_first_piping_service()
        {
            var services = new[] { S("SAN", ServiceDomain.Piping), S("DCW", ServiceDomain.Piping), S("DHW", ServiceDomain.Piping) };
            Assert.Equal("SAN", SysConnectorChoice.Choose(services, SysConnectorChoice.PreferredDomain("Plumbing Fixtures")));
        }

        [Fact]
        public void A_primary_connector_outranks_everything()
        {
            var services = new[] { S("HVAC", ServiceDomain.Hvac), S("CHW", ServiceDomain.Piping, primary: true) };
            Assert.Equal("CHW", SysConnectorChoice.Choose(services, ServiceDomain.Hvac));
        }

        [Fact]
        public void Only_auxiliary_services_fall_back_to_the_first()
        {
            var services = new[] { S("DRN", ServiceDomain.Piping), S("GAS", ServiceDomain.Piping) };
            Assert.Equal("DRN", SysConnectorChoice.Choose(services, ServiceDomain.Undefined));
        }

        [Fact]
        public void Empty_codes_are_ignored_and_nothing_gives_null()
        {
            Assert.Null(SysConnectorChoice.Choose(new[] { S("", ServiceDomain.Hvac), S(null, ServiceDomain.Piping) }, ServiceDomain.Hvac));
            Assert.Null(SysConnectorChoice.Choose(null, ServiceDomain.Hvac));
        }

        [Theory]
        [InlineData("Mechanical Equipment", ServiceDomainName.Hvac)]
        [InlineData("Plumbing Fixtures", ServiceDomainName.Piping)]
        [InlineData("Lighting Fixtures", ServiceDomainName.Electrical)]
        [InlineData("Walls", ServiceDomainName.Undefined)]
        [InlineData(null, ServiceDomainName.Undefined)]
        public void Preferred_domain_by_category(string category, ServiceDomainName expected)
            => Assert.Equal(expected.ToString(), SysConnectorChoice.PreferredDomain(category).ToString());

        public enum ServiceDomainName { Undefined, Hvac, Piping, Electrical }

        [Theory]
        [InlineData("HWS", "HYDRONIC SUPPLY", 280.15, "CHW")]   // 7 °C design: chilled water
        [InlineData("HWS", "HYDRONIC SUPPLY", 288.15, "CHW")]   // exactly 15 °C
        [InlineData("HWS", "HYDRONIC SUPPLY", 288.16, "HWS")]   // just above
        [InlineData("HWS", "HYDRONIC RETURN", 355.15, "HWS")]   // 82 °C LTHW
        [InlineData("HWS", "HYDRONIC SUPPLY", null, "HWS")]     // temperature unknown: unchanged
        [InlineData("HWS", "HYDRONIC SUPPLY", 0.0, "HWS")]      // unset (0): unchanged
        [InlineData("HWS", "HEATING FLOW", 280.15, "HWS")]      // not from the word HYDRONIC
        [InlineData("DCW", "HYDRONIC SUPPLY", 280.15, "DCW")]   // only HWS is refined
        public void Hydronic_is_chilled_water_at_or_below_15C(string code, string source, double? kelvin, string expected)
            => Assert.Equal(expected, SysConnectorChoice.RefineHydronic(code, source, kelvin));
    }
}
