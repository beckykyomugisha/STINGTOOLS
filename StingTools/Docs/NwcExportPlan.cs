// NwcExportPlan — what an Export Centre NWC export actually asks Revit for. Revit-free, so the
// rules are tested (StingTools.Tags.Tests).
//
// NW-1 (2026-10-01). The profile defaults were Scope "Selected" and Coordinates "Project":
//  * "Selected" mapped to NavisworksExportScope.SelectedElements, but the Export Centre never sets
//    a selection (it runs batch and unattended), so every default NWC export asked for an empty
//    selection - Revit either refused or wrote an empty file.
//  * "Project" is not a Navisworks option (the exporter offers Shared or Internal) and mapped to
//    Internal. Models federated in Navisworks Manage or ACC Model Coordination align on SHARED
//    coordinates; Internal put every discipline at its own origin.
// New profiles default to the whole model in shared coordinates. A saved profile carrying the old
// values is read the same way, and the note says so - it is never silently re-meant.

using System;
using System.Collections.Generic;

namespace StingTools.Docs
{
    public sealed class NwcExportPlan
    {
        /// <summary>NavisworksExportScope member name: Model, View or SelectedElements.</summary>
        public string Scope { get; private set; } = "Model";
        /// <summary>NavisworksCoordinates member name: Shared or Internal.</summary>
        public string Coordinates { get; private set; } = "Shared";
        public bool ExportElementIds { get; private set; } = true;
        /// <summary>What was read differently from the profile's label, for the export log.</summary>
        public List<string> Notes { get; } = new List<string>();

        public const string DefaultScope = "Entire";
        public const string DefaultCoordinates = "Shared";

        public static NwcExportPlan For(string scope, string coordinates, bool exportElementIds)
        {
            var p = new NwcExportPlan { ExportElementIds = exportElementIds };
            switch ((scope ?? "").Trim().ToLowerInvariant())
            {
                case "":
                case "entire":
                case "model":
                    p.Scope = "Model"; break;
                case "currentview":
                case "view":
                    p.Scope = "View"; break;
                case "selected":
                case "selection":
                    p.Scope = "Model";
                    p.Notes.Add("Scope 'Selected' exported the whole model: the Export Centre runs without a selection, " +
                                "so a selection export would be empty. Choose 'CurrentView' with a 3D view named 'Navisworks' to limit it.");
                    break;
                default:
                    p.Scope = "Model";
                    p.Notes.Add($"Scope '{scope}' is not known (Entire / CurrentView); the whole model was exported.");
                    break;
            }
            switch ((coordinates ?? "").Trim().ToLowerInvariant())
            {
                case "internal":
                    p.Coordinates = "Internal"; break;
                case "shared":
                case "":
                    p.Coordinates = "Shared"; break;
                default:
                    p.Coordinates = "Shared";
                    p.Notes.Add($"Coordinates '{coordinates}' is not a Navisworks option (Shared / Internal); shared coordinates were used, " +
                                "which is what Navisworks and ACC Model Coordination federate on. Set 'Internal' on the profile to keep the old placement.");
                    break;
            }
            if (!exportElementIds)
                p.Notes.Add("Element IDs are off: Navisworks clash results and ACC Model Coordination cannot be traced back to Revit elements.");
            return p;
        }
    }
}
