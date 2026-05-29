// INT-02: Command registry module for all ORGANISE tab button tags.
using StingTools.UI;

namespace StingTools.UI.Modules
{
    internal sealed class OrganiseCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            // ── Tag Ops ─────────────────────────────────────────────────────
            registry.Register("TagSelected",               app => StingCommandHandler.RunCommandPublic<Organise.TagSelectedCommand>(app));
            registry.Register("ReTag",                     app => StingCommandHandler.RunCommandPublic<Organise.ReTagCommand>(app));
            registry.Register("DeleteTags",                app => StingCommandHandler.RunCommandPublic<Organise.DeleteTagsCommand>(app));
            registry.Register("RenumberTags",              app => StingCommandHandler.RunCommandPublic<Organise.RenumberTagsCommand>(app));
            registry.Register("GraitecNumbering",          app => StingCommandHandler.RunCommandPublic<Organise.SmartNumberingCommand>(app));
            registry.Register("CopyTags",                  app => StingCommandHandler.RunCommandPublic<Organise.CopyTagsCommand>(app));
            registry.Register("SwapTags",                  app => StingCommandHandler.RunCommandPublic<Organise.SwapTagsCommand>(app));
            registry.Register("FixDuplicates",             app => StingCommandHandler.RunCommandPublic<Organise.FixDuplicateTagsCommand>(app));
            registry.Register("FindDuplicates",            app => StingCommandHandler.RunCommandPublic<Organise.FindDuplicateTagsCommand>(app));
            registry.Register("HealthFixAll",              app => StingCommandHandler.RunCommandPublic<Organise.FixDuplicateTagsCommand>(app));
            registry.Register("RetagStale",                app => StingCommandHandler.RunCommandPublic<Organise.RetagStaleCommand>(app));

            // ── Leaders ─────────────────────────────────────────────────────
            registry.Register("ToggleTagOrientation",      app => StingCommandHandler.RunCommandPublic<Organise.ToggleTagOrientationCommand>(app));
            registry.Register("AlignTextRight",            app => StingCommandHandler.RunCommandPublic<Organise.AlignTagTextCommand>(app));
            registry.Register("AutoAlignLeaderText",       app => StingCommandHandler.RunCommandPublic<Organise.AutoAlignLeaderTextCommand>(app));
            registry.Register("ArrangeRadial",             app => StingCommandHandler.RunCommandPublic<Organise.AlignTagsCommand>(app));
            registry.Register("ResetTagPositions",         app => StingCommandHandler.RunCommandPublic<Organise.ResetTagPositionsCommand>(app));
            registry.Register("AddLeaders",                app => StingCommandHandler.RunCommandPublic<Organise.AddLeadersCommand>(app));
            registry.Register("RemoveLeaders",             app => StingCommandHandler.RunCommandPublic<Organise.RemoveLeadersCommand>(app));
            registry.Register("ToggleLeaders",             app => StingCommandHandler.RunCommandPublic<Organise.ToggleLeadersCommand>(app));
            registry.Register("PinTags",                   app => StingCommandHandler.RunCommandPublic<Organise.PinTagsCommand>(app));
            registry.Register("AttachLeader",              app => StingCommandHandler.RunCommandPublic<Organise.AttachLeaderCommand>(app));
            registry.Register("SelectTagsWithLeaders",     app => StingCommandHandler.RunCommandPublic<Organise.SelectTagsWithLeadersCommand>(app));
            registry.Register("EqualizeLeaderLengths",     app => StingCommandHandler.RunCommandPublic<Organise.EqualizeLeaderLengthsCommand>(app));
            registry.Register("LeaderEqualise",            app => StingCommandHandler.RunCommandPublic<Organise.SnapLeaderElbowCommand>(app));
            registry.Register("LeaderCombine",             app => StingCommandHandler.RunCommandPublic<Organise.AddLeadersCommand>(app));
            registry.Register("LeaderAdd",                 app => StingCommandHandler.RunCommandPublic<Organise.AddLeadersCommand>(app));
            registry.Register("LeaderSpacing",             app => StingCommandHandler.RunCommandPublic<Organise.AlignTagsCommand>(app));
            registry.Register("BrainSmHV",                app => StingCommandHandler.RunCommandPublic<Organise.ToggleTagOrientationCommand>(app));
            registry.Register("BrainSmOr",                app => StingCommandHandler.RunCommandPublic<Organise.ToggleTagOrientationCommand>(app));
            registry.Register("BrainSmAl",                app => StingCommandHandler.RunCommandPublic<Organise.AlignTagsCommand>(app));

            // ── Analysis ────────────────────────────────────────────────────
            registry.Register("TagStats",                  app => StingCommandHandler.RunCommandPublic<Organise.TagStatsCommand>(app));
            registry.Register("AnalyseScore",              app => StingCommandHandler.RunCommandPublic<Organise.TagStatsCommand>(app));
            registry.Register("HealthReport",              app => StingCommandHandler.RunCommandPublic<Organise.TagStatsCommand>(app));
            registry.Register("AuditTagsCSV",              app => StingCommandHandler.RunCommandPublic<Organise.AuditTagsCSVCommand>(app));
            registry.Register("AuditTags",                 app => StingCommandHandler.RunCommandPublic<Organise.AuditTagsCSVCommand>(app));
            registry.Register("AnomalyExport",             app => StingCommandHandler.RunCommandPublic<Organise.AuditTagsCSVCommand>(app));
            registry.Register("SelectByDiscipline",        app => StingCommandHandler.RunCommandPublic<Organise.SelectByDisciplineCommand>(app));
            registry.Register("TagRegisterExport",         app => StingCommandHandler.RunCommandPublic<Organise.TagRegisterExportCommand>(app));
            registry.Register("HighlightInvalid",          app => StingCommandHandler.RunCommandPublic<Organise.HighlightInvalidCommand>(app));
            registry.Register("ClearOverrides",            app => StingCommandHandler.RunCommandPublic<Organise.ClearOverridesCommand>(app));
            registry.Register("AnomalyAutoFix",            app => StingCommandHandler.RunCommandPublic<Organise.AnomalyAutoFixCommand>(app));
            registry.Register("DisciplineComplianceReport",app => StingCommandHandler.RunCommandPublic<Organise.DisciplineComplianceReportCommand>(app));

            // ── Annotation Color ────────────────────────────────────────────
            registry.Register("ColorTagsByDiscipline",     app => StingCommandHandler.RunCommandPublic<Organise.ColorTagsByDisciplineCommand>(app));
            registry.Register("SetTagTextColor",           app => StingCommandHandler.RunCommandPublic<Organise.SetTagTextColorCommand>(app));
            registry.Register("SetLeaderColor",            app => StingCommandHandler.RunCommandPublic<Organise.SetLeaderColorCommand>(app));
            registry.Register("SplitTagLeaderColor",       app => StingCommandHandler.RunCommandPublic<Organise.SplitTagLeaderColorCommand>(app));
            registry.Register("ClearAnnotationColors",     app => StingCommandHandler.RunCommandPublic<Organise.ClearAnnotationColorsCommand>(app));

            // ── Tag Appearance ──────────────────────────────────────────────
            registry.Register("TagAppearance",             app => StingCommandHandler.RunCommandPublic<Organise.TagAppearanceCommand>(app));
            registry.Register("SetTagBox",                 app => StingCommandHandler.RunCommandPublic<Organise.SetTagBoxAppearanceCommand>(app));
            registry.Register("QuickTagStyle",             app => StingCommandHandler.RunCommandPublic<Organise.QuickTagStyleCommand>(app));
            registry.Register("SetTagLineWeight",          app => StingCommandHandler.RunCommandPublic<Organise.SetTagLineWeightCommand>(app));
            registry.Register("ColorTagsByParam",          app => StingCommandHandler.RunCommandPublic<Organise.ColorTagsByParameterCommand>(app));

            // ── Tag Type ────────────────────────────────────────────────────
            registry.Register("SwapTagType",               app => StingCommandHandler.RunCommandPublic<Organise.SwapTagTypeCommand>(app));

            // ── Clustering ──────────────────────────────────────────────────
            registry.Register("ClusterTags",               app => StingCommandHandler.RunCommandPublic<Organise.ClusterTagsCommand>(app));
            registry.Register("DeclusterTags",             app => StingCommandHandler.RunCommandPublic<Organise.DeclusterTagsCommand>(app));
            registry.Register("SetDisplayMode",            app => StingCommandHandler.RunCommandPublic<Organise.SetDisplayModeCommand>(app));
            registry.Register("ApplyClonedTags",           app => StingCommandHandler.RunCommandPublic<Organise.ApplyClonedTagsCommand>(app));
            registry.Register("JSONExport",                app => StingCommandHandler.RunCommandPublic<Organise.JSONExportCommand>(app));
        }
    }
}
