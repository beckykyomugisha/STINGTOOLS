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
        }
    }
}
