// StingTools — Drawing Template Manager · view types from data
//
// Drawing types can name the Revit view type their views are created with
// (DrawingType.ViewFamilyTypeName — "STING - Section", "STING - Elevation" …).
// This command creates every named type the catalogue asks for that the
// project lacks, by duplicating the project's first view type of the right
// family. It never renames or edits an existing type, so running it twice is
// a no-op, and a type the user already made under that name is left alone.
//
// Section-head / elevation-marker graphics are the duplicated type's own until
// someone sets them: the STING marker families (sectionMarker.family) ship
// nowhere yet, so there is nothing to point the new types at.
//
// NOT VERIFIED IN REVIT.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EnsureViewTypesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var uiapp = data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) { msg = "No document open."; return Result.Failed; }
            if (doc.IsFamilyDocument) { msg = "Run this from a project, not a family."; return Result.Failed; }

            // (name -> family) wanted by the catalogue; first drawing type to ask wins,
            // and a name asked for under two families is reported rather than guessed.
            var wanted = new Dictionary<string, ViewFamily>(StringComparer.OrdinalIgnoreCase);
            var problems = new List<string>();
            var lib = DrawingTypeRegistry.GetLibrary(doc);
            foreach (var dt in lib?.DrawingTypes ?? new List<DrawingType>())
            {
                var name = dt?.ViewFamilyTypeName?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                var famName = ViewFamilyTypeChoice.FamilyForPurpose(dt.Purpose);
                if (famName == null || !Enum.TryParse(famName, out ViewFamily fam))
                {
                    problems.Add($"{dt.Id}: purpose '{dt.Purpose}' makes no single kind of view, so '{name}' cannot be created from it.");
                    continue;
                }
                if (wanted.TryGetValue(name, out var had) && had != fam)
                    problems.Add($"'{name}' is asked for as both {had} and {fam}; created as {had}.");
                else wanted[name] = fam;
            }

            var all = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().ToList();
            int created = 0, present = 0;
            var createdNames = new List<string>();

            using (var tx = new Transaction(doc, "STING Ensure View Types"))
            {
                tx.Start();
                foreach (var kv in wanted.OrderBy(k => k.Key))
                {
                    var existing = all.FirstOrDefault(t => string.Equals(t.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        present++;
                        if (existing.ViewFamily != kv.Value)
                            problems.Add($"'{kv.Key}' already exists as a {existing.ViewFamily} type, not {kv.Value} — rename one of them.");
                        continue;
                    }
                    var source = all.FirstOrDefault(t => t.ViewFamily == kv.Value);
                    if (source == null) { problems.Add($"'{kv.Key}': the project has no {kv.Value} view type to copy."); continue; }
                    try
                    {
                        if (source.Duplicate(kv.Key) is ViewFamilyType made)
                        {
                            all.Add(made);
                            created++;
                            createdNames.Add($"{kv.Key}  (from '{source.Name}')");
                        }
                        else problems.Add($"'{kv.Key}': duplicate returned nothing.");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"EnsureViewTypes '{kv.Key}': {ex.Message}");
                        problems.Add($"'{kv.Key}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            string body = $"View types the drawing catalogue names: {wanted.Count}\n" +
                          $"Already present: {present}\nCreated: {created}" +
                          (createdNames.Count > 0 ? "\n  " + string.Join("\n  ", createdNames) : "") +
                          (problems.Count > 0 ? "\n\nNot done:\n  " + string.Join("\n  ", problems) : "");
            StingLog.Info("EnsureViewTypes: " + body.Replace('\n', ' '));
            TaskDialog.Show("STING — View Types", body);
            return problems.Count > 0 && created == 0 && present == 0 ? Result.Failed : Result.Succeeded;
        }
    }
}
