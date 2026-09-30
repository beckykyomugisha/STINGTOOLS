// StingTools — Drawing Template Manager · DrawingTypes_FromScopeBoxes
//
// DTW-41: this was a second, older producer for STING::<drawing-type> scope boxes,
// running beside ProduceViewsFromScopeBoxesCommand (DrawingTypes_ProduceFromScopeBoxes)
// and disagreeing with it on everything that matters:
//   • its own identity stamps (drawing type + the box assigned as crop), so a view one
//     made the other did not find — running both gave every box two views;
//   • ungated TaskDialogs, so inside a workflow preset it stopped and waited for a click;
//   • a "contains" level match, so a box coded L1 produced on Level 10;
//   • an empty catch around the rename; and no sheets at all.
//
// The tag stays — dock-panel buttons, the NLP processor, the Drawing Type Editor and
// saved workflows name it — but it now runs the one producer. A view the old command
// made (stamped with the drawing type, cropped to the box, no production context) is
// adopted by that producer on its first run, not duplicated beside it.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateFromScopeBoxesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
            => new ProduceViewsFromScopeBoxesCommand().Execute(data, ref msg, els);
    }
}
