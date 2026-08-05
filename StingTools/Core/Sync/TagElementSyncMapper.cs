// ══════════════════════════════════════════════════════════════════════════
//  TagElementSyncMapper.cs — the ONE Revit-element → wire-model mapper.
//
//  WHY THIS FILE EXISTS
//  There used to be two independent hand-maintained field-copy lists building
//  Planscape.Shared.Models.TagElementSync:
//
//    1. PlatformLinkCommands.BuildPluginSyncPayload — built a rich internal
//       TagElementPayload (~30 fields) and then copied TEN of them onto the
//       wire model. RevitElementId was one of the twenty it dropped, so every
//       element reached the server with RevitElementId = 0. The server matches
//       and dedupes rows on RevitElementId, so an entire model collapsed onto a
//       single row that was overwritten once per element. One project, one row,
//       forever — which is why dashboard compliance read 0%.
//
//    2. StingToolsApp.CollectTagElements — a second, differently-incomplete
//       list on the DocumentSaved path.
//
//  Both now call MapElement below. A field added to TagElementSync has exactly
//  one place to be populated, and TagElementSyncFieldCoverageTests fails if a
//  new field is added without deciding how it is filled.
//
//  Threading: reads the Revit API, so callers must be on the Revit API thread.
// ══════════════════════════════════════════════════════════════════════════
using System;
using Autodesk.Revit.DB;
using Planscape.Shared.Models;

namespace StingTools.Core.Sync
{
    internal static class TagElementSyncMapper
    {
        /// <summary>
        /// Project a Revit element onto the Planscape wire model.
        /// </summary>
        /// <param name="doc">Owning document (needed for tier hydration).</param>
        /// <param name="el">Element to project. Never null.</param>
        /// <param name="hydrateTiers">
        /// When true, build the Phase 165 TAG7A-F / T4-T10 tier payload. This is
        /// the expensive part of the mapping — <c>TagConfig.BuildTag7Sections</c>
        /// plus a type lookup and two resolver calls per element — so callers
        /// pass false for untagged elements, where every tier section would be
        /// empty anyway. See <see cref="ShouldHydrateTiers"/>.
        /// </param>
        internal static TagElementSync MapElement(Document doc, Element el, bool hydrateTiers)
        {
            string Get(string param)
            {
                try { return ParameterHelpers.GetString(el, param) ?? ""; }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagElementSyncMapper: read '{param}' on {el?.Id.Value}: {ex.Message}");
                    return "";
                }
            }

            string disc = Get(ParamRegistry.DISC);
            string loc  = Get(ParamRegistry.LOC);
            string zone = Get(ParamRegistry.ZONE);
            string lvl  = Get(ParamRegistry.LVL);
            string sys  = Get(ParamRegistry.SYS);
            string func = Get(ParamRegistry.FUNC);
            string prod = Get(ParamRegistry.PROD);
            string seq  = Get(ParamRegistry.SEQ);
            string tag1 = Get(ParamRegistry.TAG1);
            string tag7 = Get(ParamRegistry.TAG7);
            string status = Get(ParamRegistry.STATUS);
            string rev    = Get(ParamRegistry.REV);

            string cat = "";
            try { cat = ParameterHelpers.GetCategoryName(el) ?? ""; }
            catch (Exception ex) { StingLog.Warn($"TagElementSyncMapper: category on {el?.Id.Value}: {ex.Message}"); }

            string fam = "";
            try { fam = (el as FamilyInstance)?.Symbol?.FamilyName ?? ParameterHelpers.GetFamilyName(el) ?? ""; }
            catch (Exception ex) { StingLog.Warn($"TagElementSyncMapper: family on {el?.Id.Value}: {ex.Message}"); }

            // The cross-host key is the TRUE IFC GlobalId (IFC_GLOBAL_ID_TXT,
            // written by StabilizeIfcGuidsCommand from Revit's
            // IfcGloballyUniqueId), NOT Element.UniqueId. Sent null when absent
            // so the server skips the mapping rather than keying on the wrong id.
            string ifcGid = Get("IFC_GLOBAL_ID_TXT");

            bool isComplete = !string.IsNullOrEmpty(disc) && !string.IsNullOrEmpty(seq);
            bool isFullyResolved = isComplete && !string.IsNullOrEmpty(loc) && !string.IsNullOrEmpty(lvl);

            var row = new TagElementSync
            {
                RevitElementId = el.Id.Value,
                UniqueId       = el.UniqueId ?? "",
                IfcGlobalId    = Blank(ifcGid),
                Disc = disc, Loc = loc, Zone = zone, Lvl = lvl,
                Sys = sys, Func = func, Prod = prod, Seq = seq,
                Tag1 = tag1,
                Tag7 = Blank(tag7),
                CategoryName = cat,
                FamilyName   = fam,
                Status = Blank(status),
                Rev    = Blank(rev),
                IsComplete      = isComplete,
                IsFullyResolved = isFullyResolved,
                IsDeleted       = false,
                LastModifiedUtc = ResolveLastModifiedUtc(el),
            };

            if (hydrateTiers) HydrateTiers(doc, el, row, cat);
            return row;
        }

        /// <summary>
        /// Build the row for an element that has been deleted from the model.
        /// The element object is gone by the time we learn about it, so only the
        /// id survives — everything else is left at its default and the server
        /// is expected to key on <see cref="TagElementSync.RevitElementId"/>.
        /// </summary>
        internal static TagElementSync MapDeleted(long revitElementId) => new()
        {
            RevitElementId = revitElementId,
            IsDeleted = true,
            LastModifiedUtc = DateTime.UtcNow,
        };

        /// <summary>
        /// Whether the expensive tier hydration is worth running for this
        /// element. Sync is no longer gated on tagging, so a full sweep now
        /// visits every element in the model; running BuildTag7Sections on
        /// untagged elements would add a multi-second Revit-thread stall on a
        /// large model to produce nothing but empty strings.
        /// </summary>
        internal static bool ShouldHydrateTiers(Element el)
        {
            try { return !string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.TAG1)); }
            catch { return false; }
        }

        private static void HydrateTiers(Document doc, Element el, TagElementSync row, string categoryName)
        {
            try
            {
                row.Tag7A = Blank(ParameterHelpers.GetString(el, ParamRegistry.TAG7A));
                row.Tag7B = Blank(ParameterHelpers.GetString(el, ParamRegistry.TAG7B));
                row.Tag7C = Blank(ParameterHelpers.GetString(el, ParamRegistry.TAG7C));
                row.Tag7D = Blank(ParameterHelpers.GetString(el, ParamRegistry.TAG7D));
                row.Tag7E = Blank(ParameterHelpers.GetString(el, ParamRegistry.TAG7E));
                row.Tag7F = Blank(ParameterHelpers.GetString(el, ParamRegistry.TAG7F));
            }
            catch (Exception ex) { StingLog.Warn($"TagElementSyncMapper: TAG7A-F on {el?.Id.Value}: {ex.Message}"); }

            try
            {
                var tokens = new[] { row.Disc, row.Loc, row.Zone, row.Lvl, row.Sys, row.Func, row.Prod, row.Seq };
                var tier = TagConfig.BuildTag7Sections(doc, el, categoryName, tokens);
                if (tier != null)
                {
                    row.T4Commissioning = Blank(tier.SectionT4);
                    row.T5Cost          = Blank(tier.SectionT5);
                    row.T6Carbon        = Blank(tier.SectionT6);
                    row.T7Fabrication   = Blank(tier.SectionT7);
                    row.T8ClashTriage   = Blank(tier.SectionT8);
                    row.T9AsBuilt       = Blank(tier.SectionT9);
                    row.T10Compliance   = Blank(tier.SectionT10);
                }
            }
            catch (Exception ex) { StingLog.Warn($"TagElementSyncMapper: tier sections on {el?.Id.Value}: {ex.Message}"); }

            try
            {
                Element typeEl = doc?.GetElement(el.GetTypeId());
                row.ParaDepth   = TagConfig.ReadActiveParagraphDepth(typeEl, el);
                row.PatternMode = Blank(TagConfig.ResolveActivePatternMode(typeEl, el));
            }
            catch (Exception ex) { StingLog.Warn($"TagElementSyncMapper: depth/pattern on {el?.Id.Value}: {ex.Message}"); }
        }

        /// <summary>
        /// Wall-clock UTC of the element's most recent STING token modification.
        /// Prefers the ASS_TAG_MODIFIED_DT audit stamp written by
        /// TagPipelineHelper.RunFullPipeline, falling back to now so the server
        /// always sees a non-null timestamp for last-write-wins reconciliation.
        /// </summary>
        internal static DateTime ResolveLastModifiedUtc(Element el)
        {
            if (el == null) return DateTime.UtcNow;
            try
            {
                string stamp = ParameterHelpers.GetString(el, "ASS_TAG_MODIFIED_DT");
                if (!string.IsNullOrWhiteSpace(stamp)
                    && DateTime.TryParse(stamp,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal
                            | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                {
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolveLastModifiedUtc: ASS_TAG_MODIFIED_DT parse failed on {el.Id.Value}: {ex.Message}");
            }
            return DateTime.UtcNow;
        }

        private static string Blank(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
