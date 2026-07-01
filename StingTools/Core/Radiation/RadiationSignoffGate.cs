// Healthcare Pack H-9 — centralised Qualified Expert (QE) sign-off policy for
// radiation shielding. One home for the rule "no shielding number is
// authoritative without a named QE", reused by every RadCalc* command and both
// radiation validators instead of each re-implementing (or skipping) the check.
//
// Two orthogonal, project-configurable dials — neither needs a recompile:
//   • QE name        — RAD_QE_NAME_TXT, read from the barrier/room element, else
//                       ProjectInformation, else the Healthcare panel input
//                       (HcOptions.RadQeName). Non-empty ⇒ "a QE is on record".
//   • Enforcement    — PRJ_ORG_HEALTH_RAD_QE_ENFORCE_TXT on ProjectInformation.
//                       "BLOCKING" ⇒ unsigned output is a hard stop / findings
//                       escalate to Error. Anything else (incl. unset) ⇒
//                       advisory: output still renders but is stamped DRAFT.
//
// The calculators keep their own NCRP disclaimers; this gate only governs the
// QE sign-off overlay.

using Autodesk.Revit.DB;
using StingTools.Core;
using System;

namespace StingTools.Core.Radiation
{
    public static class RadiationSignoffGate
    {
        /// <summary>Per-element / ProjectInformation Qualified Expert name.</summary>
        public const string QeParam = "RAD_QE_NAME_TXT";

        /// <summary>ProjectInformation enforcement switch. "BLOCKING" ⇒ hard gate.</summary>
        public const string EnforceParam = "PRJ_ORG_HEALTH_RAD_QE_ENFORCE_TXT";

        // ── QE resolution ───────────────────────────────────────────────────

        /// <summary>Reads RAD_QE_NAME_TXT from a single element (no fallback).</summary>
        public static string ReadElementQe(Element el) => ReadString(el, QeParam);

        /// <summary>
        /// Resolves the responsible QE for a calc: element RAD_QE_NAME_TXT →
        /// ProjectInformation RAD_QE_NAME_TXT → Healthcare panel input
        /// (HcOptions.RadQeName). Returns "" when none is on record.
        /// </summary>
        public static string ResolveQe(Document doc, Element element = null)
        {
            var q = ReadString(element, QeParam);
            if (!string.IsNullOrWhiteSpace(q)) return q.Trim();

            q = ReadString(doc?.ProjectInformation, QeParam);
            if (!string.IsNullOrWhiteSpace(q)) return q.Trim();

            q = HcOptions.RadQeName;
            return string.IsNullOrWhiteSpace(q) ? "" : q.Trim();
        }

        /// <summary>True when a QE is on record for this calc (element/project/panel).</summary>
        public static bool IsSigned(Document doc, Element element = null)
            => !string.IsNullOrWhiteSpace(ResolveQe(doc, element));

        /// <summary>Element-strict variant for per-barrier validators (no fallback).</summary>
        public static bool IsElementSigned(Element el)
            => !string.IsNullOrWhiteSpace(ReadElementQe(el));

        // ── Enforcement policy ──────────────────────────────────────────────

        /// <summary>
        /// True when the project has set radiation QE enforcement to BLOCKING via
        /// PRJ_ORG_HEALTH_RAD_QE_ENFORCE_TXT. Lets a project tighten the policy to
        /// a hard gate without any code change; advisory (draft-label only) is the
        /// safe default.
        /// </summary>
        public static bool IsBlocking(Document doc)
            => string.Equals(ReadString(doc?.ProjectInformation, EnforceParam).Trim(),
                             "BLOCKING", StringComparison.OrdinalIgnoreCase);

        // ── Presentation ────────────────────────────────────────────────────

        /// <summary>
        /// Prominent multi-line status banner for TaskDialog / log output.
        /// Unsigned ⇒ a DRAFT (advisory) or BLOCKED (project policy) banner;
        /// signed ⇒ a one-line "reviewed by QE" acknowledgement.
        /// </summary>
        public static string StatusBanner(Document doc, Element element = null)
        {
            string qe = ResolveQe(doc, element);
            if (!string.IsNullOrWhiteSpace(qe))
                return $"Reviewed by Qualified Expert: {qe}. Values remain subject to " +
                       "the QE's certified calculation; STING does not certify.";

            const string bar = "================================================================";
            if (IsBlocking(doc))
                return string.Join(Environment.NewLine, new[]
                {
                    bar,
                    "  BLOCKED — QE SIGN-OFF REQUIRED BY PROJECT POLICY",
                    "  No Qualified Expert on record (RAD_QE_NAME_TXT empty).",
                    "  Project sets PRJ_ORG_HEALTH_RAD_QE_ENFORCE_TXT = BLOCKING.",
                    "  This output must NOT be used until a QE signs off.",
                    bar,
                });

            return string.Join(Environment.NewLine, new[]
            {
                bar,
                "  DRAFT — NOT FOR CONSTRUCTION",
                "  No Qualified Expert on record (RAD_QE_NAME_TXT empty).",
                "  Values are indicative only and must be verified and signed",
                "  off by a Qualified Expert before use.",
                bar,
            });
        }

        /// <summary>"DRAFT" / "BLOCKED (DRAFT)" / "" — for a TaskDialog title suffix.</summary>
        public static string TitleSuffix(Document doc, Element element = null)
        {
            if (IsSigned(doc, element)) return "";
            return IsBlocking(doc) ? " [BLOCKED — DRAFT]" : " [DRAFT]";
        }

        // ── Internal ────────────────────────────────────────────────────────

        private static string ReadString(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || !p.HasValue) return "";
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                return p.AsValueString() ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"RadiationSignoffGate read '{name}' suppressed: {ex.Message}"); return ""; }
        }
    }
}
