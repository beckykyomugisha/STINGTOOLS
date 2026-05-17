// INT-02: Command registry module for all SELECT tab button tags.
// Migrated from the giant switch in StingCommandHandler.Execute.
using StingTools.UI;
using Autodesk.Revit.UI;

namespace StingTools.UI.Modules
{
    internal sealed class SelectCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            // ── Category selectors ──────────────────────────────────────────
            registry.Register("SelectLighting",         app => StingCommandHandler.RunCommandPublic<Select.SelectLightingCommand>(app));
            registry.Register("SelectElectrical",       app => StingCommandHandler.RunCommandPublic<Select.SelectElectricalCommand>(app));
            registry.Register("SelectMechanical",       app => StingCommandHandler.RunCommandPublic<Select.SelectMechanicalCommand>(app));
            registry.Register("SelectPlumbing",         app => StingCommandHandler.RunCommandPublic<Select.SelectPlumbingCommand>(app));
            registry.Register("SelectAirTerminals",     app => StingCommandHandler.RunCommandPublic<Select.SelectAirTerminalsCommand>(app));
            registry.Register("SelectFurniture",        app => StingCommandHandler.RunCommandPublic<Select.SelectFurnitureCommand>(app));
            registry.Register("SelectDoors",            app => StingCommandHandler.RunCommandPublic<Select.SelectDoorsCommand>(app));
            registry.Register("SelectWindows",          app => StingCommandHandler.RunCommandPublic<Select.SelectWindowsCommand>(app));
            registry.Register("SelectRooms",            app => StingCommandHandler.RunCommandPublic<Select.SelectRoomsCommand>(app));
            registry.Register("SelectSprinklers",       app => StingCommandHandler.RunCommandPublic<Select.SelectSprinklersCommand>(app));
            registry.Register("SelectPipes",            app => StingCommandHandler.RunCommandPublic<Select.SelectPipesCommand>(app));
            registry.Register("SelectDucts",            app => StingCommandHandler.RunCommandPublic<Select.SelectDuctsCommand>(app));
            registry.Register("SelectConduits",         app => StingCommandHandler.RunCommandPublic<Select.SelectConduitsCommand>(app));
            registry.Register("SelectCableTrays",       app => StingCommandHandler.RunCommandPublic<Select.SelectCableTraysCommand>(app));
            registry.Register("SelectAllTaggable",      app => StingCommandHandler.RunCommandPublic<Select.SelectAllTaggableCommand>(app));
            registry.Register("SelectCustomCategory",   app => StingCommandHandler.RunCommandPublic<Select.SelectCustomCategoryCommand>(app));

            // ── State selectors ─────────────────────────────────────────────
            registry.Register("SetSelectionScope",      app => StingCommandHandler.RunCommandPublic<Select.SetSelectionScopeCommand>(app));
            registry.Register("SelectUntagged",         app => StingCommandHandler.RunCommandPublic<Select.SelectUntaggedCommand>(app));
            registry.Register("SelectTagged",           app => StingCommandHandler.RunCommandPublic<Select.SelectTaggedCommand>(app));
            registry.Register("SelectEmptyMark",        app => StingCommandHandler.RunCommandPublic<Select.SelectEmptyMarkCommand>(app));
            registry.Register("SelectPinned",           app => StingCommandHandler.RunCommandPublic<Select.SelectPinnedCommand>(app));
            registry.Register("SelectUnpinned",         app => StingCommandHandler.RunCommandPublic<Select.SelectUnpinnedCommand>(app));
            registry.Register("SelectByLevel",          app => StingCommandHandler.RunCommandPublic<Select.SelectByLevelCommand>(app));
            registry.Register("SelectByRoom",           app => StingCommandHandler.RunCommandPublic<Select.SelectByRoomCommand>(app));
            registry.Register("SelectStale",            app => StingCommandHandler.RunCommandPublic<Select.SelectStaleElementsCommand>(app));
            registry.Register("QuickTagPreview",        app => StingCommandHandler.RunCommandPublic<Select.QuickTagPreviewCommand>(app));
            registry.Register("BulkParamWrite",         app => StingCommandHandler.RunCommandPublic<Select.BulkParamWriteCommand>(app));

            // ── Color commands ──────────────────────────────────────────────
            registry.Register("ColorByParameter",       app => StingCommandHandler.RunCommandPublic<Select.ColorByParameterCommand>(app));
            registry.Register("ClearColorOverrides",    app => StingCommandHandler.RunCommandPublic<Select.ClearColorOverridesCommand>(app));
            registry.Register("SaveColorPreset",        app => StingCommandHandler.RunCommandPublic<Select.SaveColorPresetCommand>(app));
            registry.Register("LoadColorPreset",        app => StingCommandHandler.RunCommandPublic<Select.LoadColorPresetCommand>(app));
            registry.Register("CreateFiltersFromColors",app => StingCommandHandler.RunCommandPublic<Select.CreateFiltersFromColorsCommand>(app));
            registry.Register("GradientApply",          app => StingCommandHandler.RunCommandPublic<Select.ColorByParameterCommand>(app));

            // ── Tag selector ────────────────────────────────────────────────
            registry.Register("TagSelector",               app => StingCommandHandler.RunCommandPublic<Select.TagSelectorCommand>(app));
            registry.Register("SelectTagsByText",          app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByTextCommand>(app));
            registry.Register("SelectTagsByTextSize",      app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByTextSizeCommand>(app));
            registry.Register("SelectTagsByArrowhead",     app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByArrowheadCommand>(app));
            registry.Register("SelectTagsByLeaderState",   app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByLeaderStateCommand>(app));
            registry.Register("SelectTagsByFamily",        app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByFamilyCommand>(app));
            registry.Register("SelectTagsByHostCategory",  app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByHostCategoryCommand>(app));
            registry.Register("SelectTagsByOrientation",   app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByOrientationCommand>(app));
            registry.Register("SelectTagsByDiscipline",    app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByDisciplineCodeCommand>(app));
            registry.Register("SelectTagsByLineWeight",    app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByLineWeightCommand>(app));
            registry.Register("SelectTagsByElbowAngle",    app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByElbowAngleCommand>(app));
            registry.Register("SelectTagsByToken",         app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByTokenCommand>(app));
            registry.Register("SelectOverlappingTags",     app => StingCommandHandler.RunCommandPublic<Select.SelectOverlappingTagsCommand>(app));
            registry.Register("SelectTagsByDisciplineCode",app => StingCommandHandler.RunCommandPublic<Select.SelectTagsByDisciplineCodeCommand>(app));
            registry.Register("SaveSelectionSet",          app => StingCommandHandler.RunCommandPublic<Select.SaveSelectionSetCommand>(app));
            registry.Register("RecallSelectionSet",        app => StingCommandHandler.RunCommandPublic<Select.RecallSelectionSetCommand>(app));
            registry.Register("ManageSelectionSets",       app => StingCommandHandler.RunCommandPublic<Select.ManageSelectionSetsCommand>(app));
        }
    }
}
