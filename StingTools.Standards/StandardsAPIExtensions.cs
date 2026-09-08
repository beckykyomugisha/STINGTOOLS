// ============================================================================
// StandardsAPI extension methods - the project-configured-standard wrappers.
//
// Split out of ProjectStandardsManager.cs (KUT-8) with no change to a single
// line of their bodies. They were the only thing in that file that referenced
// StandardsAPI and its result classes, which made the preset table impossible
// to link into a Revit-free test project without dragging 1,400 lines of
// calculation code along with it. The file's own #region already named them as
// a separate concern; this makes the boundary real.
// ============================================================================

namespace StingTools.Standards
{
    #region StandardsAPI Extension Methods

    /// <summary>
    /// Extension methods for StandardsAPI integration with ProjectStandardsManager.
    /// </summary>
    public static class StandardsAPIExtensions
    {
        /// <summary>
        /// Calculate cable size using project-configured electrical standard.
        /// </summary>
        public static CableSizeResult CalculateCableSizeWithProjectStandard(
            double voltageV,
            double currentA,
            double lengthM,
            string conductorType = "Copper",
            string insulationType = "THHN",
            int conduitFill = 3,
            double ambientTempC = 30)
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Electrical);
            return StandardsAPI.CalculateCableSize(voltageV, currentA, lengthM, conductorType, insulationType, conduitFill, ambientTempC, standard);
        }

        /// <summary>
        /// Verify circuit breaker using project-configured electrical standard.
        /// </summary>
        public static CircuitBreakerResult VerifyCircuitBreakerWithProjectStandard(
            double loadCurrentA,
            double voltageV,
            string breakerType = "MCCB",
            bool typeCoordination = false)
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Electrical);
            return StandardsAPI.VerifyCircuitBreaker(loadCurrentA, voltageV, breakerType, standard, typeCoordination);
        }

        /// <summary>
        /// Calculate plumbing pipe size using project-configured plumbing standard.
        /// </summary>
        public static PipeSizeResult CalculatePipeSizeWithProjectStandard(
            double flowRateGPM,
            double lengthFt,
            string pipeType = "Copper",
            int numberOfFixtures = 0)
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Plumbing);
            return StandardsAPI.CalculatePlumbingPipeSize(flowRateGPM, lengthFt, pipeType, standard, numberOfFixtures);
        }

        /// <summary>
        /// Calculate drainage size using project-configured plumbing standard.
        /// </summary>
        public static DrainageSizeResult CalculateDrainageSizeWithProjectStandard(
            int numberOfFixtures,
            string fixtureType,
            double pipeSlope = 0.25)
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Plumbing);
            return StandardsAPI.CalculateDrainageSize(numberOfFixtures, fixtureType, pipeSlope, standard);
        }

        /// <summary>
        /// Design sprinkler system using project-configured fire protection standard.
        /// </summary>
        public static SprinklerResult DesignSprinklerSystemWithProjectStandard(
            double floorAreaM2,
            string occupancyType,
            string hazardClassification = "Light")
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.FireProtection);
            return StandardsAPI.DesignSprinklerSystem(floorAreaM2, occupancyType, hazardClassification, standard);
        }

        /// <summary>
        /// Design steel beam using project-configured structural standard.
        /// </summary>
        public static BeamDesignResult DesignSteelBeamWithProjectStandard(
            double spanM,
            double totalLoadKN,
            string loadType = "Uniform",
            string steelGrade = "A992")
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Structural);
            string designMethod = standard.Contains("AISC") ? "LRFD" : "Eurocode";
            return StandardsAPI.DesignSteelBeam(spanM, totalLoadKN, loadType, steelGrade, designMethod);
        }
    }

    #endregion
}
