// INT-02: Command registry module for VIEW tab button tags (Drawing Types, Template Manager styles).
// Inline helpers (DrawingTypesGroupBrowserInline, DrawingTypesSyncStylesInline,
// DrawingTypesFromScopeBoxesInline) remain in StingCommandHandler as private methods
// and are not migrated here — they require access to private state.
using StingTools.UI;

namespace StingTools.UI.Modules
{
    internal sealed class ViewCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            // ── Drawing Types ────────────────────────────────────────────────
            registry.Register("DrawingTypes_Inspect",                app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DrawingTypesInspectCommand>(app));
            registry.Register("DrawingTypes_Reload",                 app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DrawingTypesReloadCommand>(app));
            registry.Register("DrawingTypes_PresentationSetup",      app => StingCommandHandler.RunCommandPublic<Commands.Drawing.PresentationStyleSetupCommand>(app));
            registry.Register("DrawingTypes_Editor",                 app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DrawingTypeEditorCommand>(app));
            registry.Register("DrawingTypes_Renumber",               app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DrawingRenumberCommand>(app));
            registry.Register("DrawingTypes_HealTitleBlocks",        app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DrawingHealTitleBlocksCommand>(app));
            registry.Register("DrawingTypes_Doctor",                 app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DrawingDoctorCommand>(app));
            registry.Register("DrawingTypes_MigrateCsv",             app => StingCommandHandler.RunCommandPublic<Commands.Drawing.TitleBlockMigrateCsvToRecipeCommand>(app));
            registry.Register("DrawingTypes_ProducePerLevel",        app => StingCommandHandler.RunCommandPublic<Commands.Drawing.ProduceViewsPerLevelCommand>(app));
            registry.Register("DrawingTypes_ProduceFromScopeBoxes",  app => StingCommandHandler.RunCommandPublic<Commands.Drawing.ProduceViewsFromScopeBoxesCommand>(app));
            registry.Register("DrawingTypes_ProduceInteriorElevations", app => StingCommandHandler.RunCommandPublic<Commands.Drawing.ProduceInteriorElevationsCommand>(app));
            registry.Register("DrawingTypes_ProduceExteriorElevations", app => StingCommandHandler.RunCommandPublic<Commands.Drawing.ProduceExteriorElevationsCommand>(app));
            registry.Register("DrawingTypes_ProduceSections",        app => StingCommandHandler.RunCommandPublic<Commands.Drawing.ProduceSectionsCommand>(app));
            registry.Register("DrawingTypes_RegenerateTemplates",    app => StingCommandHandler.RunCommandPublic<Commands.Drawing.RegeneratePackTemplatesCommand>(app));
            registry.Register("DrawingTypes_ConvertToManaged",       app => StingCommandHandler.RunCommandPublic<Commands.Drawing.ConvertPackToManagedCommand>(app));
            registry.Register("DrawingTypes_DetachManaged",          app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DetachFromManagedCommand>(app));
            registry.Register("DrawingTypes_ExportPackage",          app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DrawingPackageExportCommand>(app));
            registry.Register("DrawingTypes_SequencePackage",        app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DrawingPackageSequenceCommand>(app));
            registry.Register("DrawingTypes_AuditPackages",          app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DrawingPackageAuditCommand>(app));
            registry.Register("DrawingTypes_PackageAudit",           app => StingCommandHandler.RunCommandPublic<Commands.Drawing.DrawingPackageAuditCommand>(app));
        }
    }
}
