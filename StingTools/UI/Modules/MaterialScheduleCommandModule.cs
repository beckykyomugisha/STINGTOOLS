// MAT-SCHED: command registry module for material-schedule button tags.
using StingTools.UI;

namespace StingTools.UI.Modules
{
    internal sealed class MaterialScheduleCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            registry.Register("MaterialSchedule_Export",
                app => StingCommandHandler.RunCommandPublic<Commands.MaterialSchedule.MaterialScheduleExportCommand>(app));

            // The rate editor. Seeded from the schedule itself, so the 21-of-25
            // keys that carry a non-ASCII em dash never have to be typed.
            registry.Register("MaterialSchedule_PriceCommodities",
                app => StingCommandHandler.RunCommandPublic<Commands.MaterialSchedule.MaterialSchedulePriceCommoditiesCommand>(app));
        }
    }
}
