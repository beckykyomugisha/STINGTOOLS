// StingTools — Drawing Template Manager
//
// DrawingPurposeViewKind — the ONE table that says which kind of Revit view
// a drawing type's purpose produces when the type declares no
// productionRules of its own.
//
// Revit-free on purpose: StingTools.Tags.Tests compiles this file and checks
// that every purpose in the shipped catalogue and every DrawingPurpose
// constant maps explicitly, and that every producible kind is one
// DrawingProducer.CreateViewByType can actually create.
//
// Why it exists (D-7): DrawingProducer.SynthesizeSingleRule used a switch
// whose default arm was "FloorPlan". Schematic, Clarification, Legend, Spool
// and Coordination all fell into it, so a riser schematic with no rules was
// produced as a floor plan — no error, just the wrong drawing. An unknown
// purpose now resolves to nothing and the caller reports it.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// View-kind vocabulary — the strings <c>ProductionRule.ViewType</c>
    /// carries and <c>DrawingProducer</c> switches on.
    /// </summary>
    public static class DrawingViewKind
    {
        public const string FloorPlan = "FloorPlan";
        public const string Rcp       = "RCP";
        public const string Section   = "Section";
        public const string Elevation = "Elevation";
        public const string Detail    = "Detail";
        public const string ThreeD    = "ThreeD";
        public const string Drafting  = "DraftingView";
        public const string Schedule  = "Schedule";
        public const string Legend    = "Legend";
    }

    public static class DrawingPurposeViewKind
    {
        // Purpose -> view kind. Case-insensitive, like DrawingPurpose values
        // everywhere else ("Values are case-insensitive on the way in").
        private static readonly Dictionary<string, string> _map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { DrawingPurpose.Plan,         DrawingViewKind.FloorPlan },
                { DrawingPurpose.Rcp,          DrawingViewKind.Rcp },
                { DrawingPurpose.Section,      DrawingViewKind.Section },
                { DrawingPurpose.Elevation,    DrawingViewKind.Elevation },
                { DrawingPurpose.Detail,       DrawingViewKind.Detail },
                { DrawingPurpose.ThreeD,       DrawingViewKind.ThreeD },
                { DrawingPurpose.Schedule,     DrawingViewKind.Schedule },

                // Not to scale ("scale": "NA") and model-independent: a riser /
                // single-line / distribution schematic is drafted, and the slot
                // vocabulary's "Schematic" term already accepts a DraftingView.
                { DrawingPurpose.Schematic,    DrawingViewKind.Drafting },

                // RFI / markup sketch. Types that need a model plan under the
                // markup (clar-markup-A1's first slot) should declare
                // productionRules; the single-view default is the sketch.
                { DrawingPurpose.Clarification, DrawingViewKind.Drafting },

                // Every shipped Coordination type leads with a plan overlay
                // (mep-coord's rule 0, coord-clash's "Overlay" slot, elec-coord's
                // "Main" slot are all Plan). Multi-view coordination sets declare
                // their extra ISO / section views as productionRules.
                { DrawingPurpose.Coordination, DrawingViewKind.FloorPlan },

                // Spool sheets are isometric-led. The real fabrication path
                // (ShopDrawingComposer -> AssemblyViewBuilder) builds assembly
                // views itself; DrawingProducer cannot create assembly views,
                // so the single-view default is the ISO, which needs no Level
                // context (a FloorPlan would fail without one).
                { DrawingPurpose.Spool,        DrawingViewKind.ThreeD },

                // Mapped explicitly so it is never mistaken for a plan — but it
                // is NOT producible (see NotProducibleReason).
                { DrawingPurpose.Legend,       DrawingViewKind.Legend },
            };

        /// <summary>Kinds <c>DrawingProducer.CreateViewByType</c> can create.
        /// Kept honest by a test that reads that switch.</summary>
        public static readonly string[] ProducibleKinds =
        {
            DrawingViewKind.FloorPlan, DrawingViewKind.Rcp, DrawingViewKind.Section,
            DrawingViewKind.Elevation, DrawingViewKind.Detail, DrawingViewKind.ThreeD,
            DrawingViewKind.Drafting, DrawingViewKind.Schedule,
        };

        private static readonly Dictionary<string, string> _notProducible =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { DrawingViewKind.Legend,
                  "the Revit API has no call that creates a legend view — only View.Duplicate of an " +
                  "existing one. Create the legend in Revit and place it on the sheet, or give the " +
                  "drawing type productionRules for the views it should produce." },
            };

        /// <summary>Every purpose this table maps (read-only view).</summary>
        public static IReadOnlyCollection<string> MappedPurposes => _map.Keys;

        /// <summary>
        /// Resolve a purpose to its view kind. Returns false for a null, blank
        /// or unknown purpose — the caller must report it; there is no default.
        /// </summary>
        public static bool TryResolve(string purpose, out string viewKind)
        {
            viewKind = null;
            var key = (purpose ?? string.Empty).Trim();
            if (key.Length == 0) return false;
            return _map.TryGetValue(key, out viewKind);
        }

        public static bool IsProducible(string viewKind)
            => !string.IsNullOrEmpty(viewKind)
               && ProducibleKinds.Contains(viewKind, StringComparer.OrdinalIgnoreCase);

        /// <summary>Why a mapped kind cannot be produced, or null when it can
        /// (or when the kind is not one this table knows).</summary>
        public static string NotProducibleReason(string viewKind)
            => viewKind != null && _notProducible.TryGetValue(viewKind, out var why) ? why : null;

        /// <summary>
        /// Compose the single-view decision for a drawing type with no
        /// productionRules. Returns the kind to produce, or null with a
        /// human-readable <paramref name="problem"/> explaining why nothing
        /// can be produced — never a guessed default.
        /// </summary>
        public static string ResolveForProduction(string drawingTypeId, string purpose, out string problem)
        {
            problem = null;
            if (!TryResolve(purpose, out var kind))
            {
                problem = $"Drawing type '{drawingTypeId}' has purpose '{purpose ?? "(none)"}', which maps to no " +
                          "view kind, and it declares no productionRules — nothing was produced. Known purposes: " +
                          string.Join(", ", DrawingPurpose.All) + ".";
                return null;
            }
            if (!IsProducible(kind))
            {
                problem = $"Drawing type '{drawingTypeId}' (purpose '{purpose}') needs a {kind} view, which " +
                          "cannot be produced automatically: " +
                          (NotProducibleReason(kind) ?? "no producer exists for this view kind.");
                return null;
            }
            return kind;
        }
    }
}
