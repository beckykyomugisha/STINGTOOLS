// StingTools — every action of the dock panel's DRAWING TYPES section, as data
//
// The Drawing Type Editor used to carry its own hand-picked toolbar of eight
// buttons while the dock section grew to forty-one, so most drawing-type work
// could not be reached from the editor at all. This list is what the editor's
// "All Actions" tab is built from.
//
// It is a second statement of the dock XAML — so DrawingTypeActionsTests reads
// StingDockPanel.xaml and fails if the two disagree on any group, label or tag.
// Add a button to the dock section and the test says to add it here, and the
// editor picks it up with no other change.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class DrawingTypeActions
    {
        public sealed class Entry
        {
            public Entry(string group, string label, string tag) { Group = group; Label = label; Tag = tag; }
            public string Group { get; }
            /// <summary>The button text — the same text the dock panel shows.</summary>
            public string Label { get; }
            /// <summary>The dispatch tag — the same one StingCommandHandler switches on.</summary>
            public string Tag { get; }
        }

        public const string MainGroup = "Drawing types";

        /// <summary>
        /// The dock's own "Edit Types" button opens the editor; inside the editor it
        /// would open a second copy, so it is the one dock button not listed.
        /// </summary>
        public const string EditorTag = "DrawingTypes_Editor";

        public static readonly IReadOnlyList<Entry> All = new List<Entry>
        {
            new Entry(MainGroup, "Inspect", "DrawingTypes_Inspect"),
            new Entry(MainGroup, "Type Marks (preview)", "TypeMark_Preview"),
            new Entry(MainGroup, "Type Marks (assign)", "TypeMark_Assign"),
            new Entry(MainGroup, "Type Schedules", "TypeSchedule_Create"),
            new Entry(MainGroup, "Reload JSON", "DrawingTypes_Reload"),
            new Entry(MainGroup, "Pres Setup", "DrawingTypes_PresentationSetup"),
            new Entry(MainGroup, "Group Browser", "DrawingTypes_GroupBrowser"),
            new Entry(MainGroup, "Audit Style Refs", "DrawingTypes_AuditStyleRefs"),
            new Entry(MainGroup, "Sync Styles", "DrawingTypes_SyncStyles"),
            new Entry(MainGroup, "↓ Export to Excel", "DrawingTypes_ExportExcel"),
            new Entry(MainGroup, "↑ Import from Excel", "DrawingTypes_ImportExcel"),

            new Entry("Advanced drawing-type ops", "Heal TBs", "DrawingTypes_HealTitleBlocks"),
            new Entry("Advanced drawing-type ops", "Renumber", "DrawingTypes_Renumber"),
            new Entry("Advanced drawing-type ops", "Doctor", "DrawingTypes_Doctor"),
            new Entry("Advanced drawing-type ops", "Migrate CSV", "DrawingTypes_MigrateCsv"),
            new Entry("Advanced drawing-type ops", "Migrate Params", "DrawingTypes_MigrateParams"),
            new Entry("Advanced drawing-type ops", "Audit Params", "DrawingTypes_AuditLegacyParams"),
            new Entry("Advanced drawing-type ops", "Sync Rev", "DrawingTypes_SyncRevisions"),
            new Entry("Advanced drawing-type ops", "Re-Stamp", "DrawingTypes_BulkReStamp"),

            new Entry("Scope boxes", "Suggest From Scope Boxes", "DrawingTypes_SuggestFromScopeBoxes"),
            new Entry("Scope boxes", "Scope Box Manager", "ScopeBoxManager"),
            new Entry("Scope boxes", "Scope Box Planner", "ScopeBox_Planner"),
            new Entry("Scope boxes", "Register Seeds", "ScopeBox_RegisterSeeds"),
            new Entry("Scope boxes", "Import Seeds", "ScopeBox_ImportSeeds"),
            new Entry("Scope boxes", "Colour Boxes", "ScopeBox_Colour"),
            new Entry("Scope boxes", "Clear Box Colours", "ScopeBox_ClearColour"),
            new Entry("Scope boxes", "Produce From Areas", "ScopeBox_ProduceAreas"),
            new Entry("Scope boxes", "Generate From Scope Boxes", "DrawingTypes_FromScopeBoxes"),

            new Entry("Production", "Produce Per Level", "DrawingTypes_ProducePerLevel"),
            new Entry("Production", "Produce Sections", "DrawingTypes_ProduceSections"),
            new Entry("Production", "Exterior Elevations", "DrawingTypes_ProduceExteriorElevations"),
            new Entry("Production", "Interior Elevations", "DrawingTypes_ProduceInteriorElevations"),
            new Entry("Production", "From Scope Boxes (Produce)", "DrawingTypes_ProduceFromScopeBoxes"),
            new Entry("Production", "Regenerate Templates", "DrawingTypes_RegenerateTemplates"),
            new Entry("Production", "→ Managed Mode", "DrawingTypes_ConvertToManaged"),
            new Entry("Production", "Detach Managed", "DrawingTypes_DetachManaged"),

            new Entry("Packages", "Export Package", "DrawingTypes_ExportPackage"),
            new Entry("Packages", "Sequence Package", "DrawingTypes_SequencePackage"),
            new Entry("Packages", "Audit Packages", "DrawingTypes_AuditPackages"),
            new Entry("Packages", "⚡ Produce & Export", "DrawingTypes_ProduceAndExport"),
        };

        /// <summary>Groups in dock order.</summary>
        public static IEnumerable<IGrouping<string, Entry>> Groups() => All.GroupBy(a => a.Group);

        public static Entry ByTag(string tag) => All.FirstOrDefault(a => a.Tag == tag);
    }
}
