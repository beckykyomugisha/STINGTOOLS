// HC-16: Command registry module for Healthcare tab tags (HC_*).
// Migrates the HC_* switch arm in StingCommandHandler to the ICommandModule
// framework introduced by INT-02.
//
// HC_FacilityConfig stays in StingCommandHandler.HandleHcFacilityConfig
// because it dispatches to a private inline dialog method.
using StingTools.UI;

namespace StingTools.UI.Modules
{
    internal sealed class HealthcareCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            registry.Register("HC_RunAll",                 app => StingCommandHandler.RunCommandPublic<Commands.Healthcare.HealthcareRunAllValidatorsCommand>(app));
            registry.Register("HC_PressureAudit",          app => StingCommandHandler.RunCommandPublic<Commands.Healthcare.HealthcarePressureAuditCommand>(app));
            registry.Register("HC_MgpsVerify",             app => StingCommandHandler.RunCommandPublic<Commands.MedGas.MgasVerifyCommand>(app));
            registry.Register("HC_WaterFlush",             app => StingCommandHandler.RunCommandPublic<Commands.Healthcare.HealthcareWaterSafetyCommand>(app));
            registry.Register("HC_AntiLigatureAudit",      app => StingCommandHandler.RunCommandPublic<Commands.Healthcare.Specialist.AntiLigatureAuditCommand>(app));
            registry.Register("HC_RdsCompleteness",        app => StingCommandHandler.RunCommandPublic<Commands.Healthcare.HealthcareRdsCompletenessCommand>(app));
            registry.Register("HC_RadiationAudit",         app => StingCommandHandler.RunCommandPublic<Commands.Healthcare.HealthcareRadShieldAuditCommand>(app));
            registry.Register("HC_AdjacencyAudit",         app => StingCommandHandler.RunCommandPublic<Commands.Adjacency.AdjacencyAuditCommand>(app));
            registry.Register("HC_EesResilience",          app => StingCommandHandler.RunCommandPublic<Commands.Healthcare.HealthcareEesResilienceCommand>(app));
            registry.Register("HC_CommissioningChecklist", app => StingCommandHandler.RunCommandPublic<Commands.Healthcare.HealthcareRunAllValidatorsCommand>(app));
            registry.Register("HC_Workflow",               app => StingCommandHandler.RunCommandPublic<Core.WorkflowPresetCommand>(app));
            registry.Register("HC_HbnAutoPopulate",        app => StingCommandHandler.RunCommandPublic<Commands.Healthcare.HbnRoomAutoPopulatorCommand>(app));
        }
    }
}
