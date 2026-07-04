using StingTools.Core.Variation;
using Xunit;

namespace StingTools.Cost.Tests
{
    // PM-7 — the de-duplicated default-liability-by-reason rule.
    public class VariationLiabilityRulesTests
    {
        [Theory]
        [InlineData(VariationReason.DesignChange, VariationLiability.Designer)]
        [InlineData(VariationReason.ErrorOmission, VariationLiability.Designer)]
        [InlineData(VariationReason.ClientRequest, VariationLiability.Employer)]
        [InlineData(VariationReason.ScopeAddition, VariationLiability.Employer)]
        [InlineData(VariationReason.SiteCondition, VariationLiability.Employer)]
        [InlineData(VariationReason.ContractorProposal, VariationLiability.Shared)]
        [InlineData(VariationReason.Other, VariationLiability.Employer)]
        public void Suggest_maps_reason_to_default_liability(VariationReason reason, VariationLiability expected)
        {
            Assert.Equal(expected, VariationLiabilityRules.Suggest(reason));
        }
    }
}
