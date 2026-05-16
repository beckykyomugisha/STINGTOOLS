// StingTools — Drawing Template Manager · Week 3
//
// DrawingTypeStamper writes two shared parameters onto any view or
// sheet a STING generator creates — so the Project Browser, the
// style-propagation IUpdater, and downstream audits can all see
// which Drawing Type produced which artefact:
//
//   STING_DRAWING_TYPE_ID_TXT   (text)  — the dt.Id
//   STING_STYLE_LOCKED_BOOL     (bool)  — "don't re-apply on edit"
//
// The two parameters are declared in MR_PARAMETERS.txt with stable
// GUIDs (see Data/MR_PARAMETERS.txt). Parameter binding happens once
// per project via LoadSharedParams / StingCompliance wiring; Stamp()
// is a no-op on projects where the binding hasn't happened yet.
//
// Worksharing: Stamp() checks that the element is editable by the
// current user before writing — avoids 'cannot modify element owned
// by another user' exceptions when running batch generators on a
// shared model.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public static class DrawingTypeStamper
    {
        public const string PARAM_DRAWING_TYPE_ID = "STING_DRAWING_TYPE_ID_TXT";
        public const string PARAM_STYLE_LOCKED    = "STING_STYLE_LOCKED_BOOL";
        public const string PARAM_DRAWING_PACKAGE_ID = "STING_DRAWING_PACKAGE_ID_TXT";
        public const string PARAM_SHEET_SEQUENCE     = "STING_SHEET_SEQUENCE_INT";

        /// <summary>
        /// Phase 137 — stamp the drawing-package id onto a view or sheet.
        /// Empty packageId is a no-op (deliberate — callers may pass the
        /// effective id which is empty when the type has no package).
        /// </summary>
        public static bool StampPackage(Element element, string packageId)
        {
            if (element == null || string.IsNullOrWhiteSpace(packageId)) return false;
            return StingTools.Core.ParameterHelpers.SetString(element, PARAM_DRAWING_PACKAGE_ID, packageId, overwrite: true);
        }

        /// <summary>
        /// Phase 137 — stamp the per-package sheet sequence number.
        /// </summary>
        public static bool StampSheetSequence(ViewSheet sheet, int sequence)
        {
            if (sheet == null) return false;
            return StingTools.Core.ParameterHelpers.SetInt(sheet, PARAM_SHEET_SEQUENCE, sequence, overwrite: true);
        }

        /// <summary>
        /// Stamp the DrawingType id onto the given element (view or
        /// sheet). Idempotent — writing the same value twice is a
        /// no-op. Safe to call without an active transaction (the
        /// write is wrapped by the caller's transaction).
        /// </summary>
        public static bool Stamp(Element el, string drawingTypeId)
        {
            if (el == null || string.IsNullOrWhiteSpace(drawingTypeId)) return false;
            if (!IsEditable(el)) return false;

            try
            {
                var p = el.LookupParameter(PARAM_DRAWING_TYPE_ID);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
                var current = p.AsString();
                if (string.Equals(current, drawingTypeId, StringComparison.Ordinal)) return true;
                p.Set(drawingTypeId);
                // FIX-2: a freshly-stamped view changes the set of views the
                // drift detector should scan. Invalidate the per-doc reverse
                // index so the next Scan() includes this view.
                try { DrawingDriftDetector.InvalidateCache(el.Document); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                return true;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"DrawingTypeStamper.Stamp({el.Id}, '{drawingTypeId}'): {ex.Message}");
                return false;
            }
        }

        public static string Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var p = el.LookupParameter(PARAM_DRAWING_TYPE_ID);
                if (p?.StorageType != StorageType.String) return null;
                var raw = p.AsString();
                if (string.IsNullOrEmpty(raw)) return null;
                // GAP-N: ManagedTemplateSyncer reuses this parameter on its
                // managed templates to store "pack=…|cs=…". That value is
                // not a DrawingType id and the registry will return null
                // for it. Reject here so callers don't waste a registry
                // lookup nor accidentally show the pack stamp as a DT id
                // in diagnostics.
                if (raw.StartsWith("pack=", StringComparison.Ordinal)
                    || raw.StartsWith("pack:", StringComparison.Ordinal))
                    return null;
                return raw;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        /// <summary>
        /// GAP-N: raw read for callers that need to inspect the stamp
        /// without the managed-pack suppression — e.g. drift detection
        /// on managed templates checks the pack=…|cs=… payload.
        /// </summary>
        public static string ReadRaw(Element el)
        {
            if (el == null) return null;
            try
            {
                var p = el.LookupParameter(PARAM_DRAWING_TYPE_ID);
                return p?.StorageType == StorageType.String ? p.AsString() : null;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        public static bool IsLocked(Element el)
        {
            if (el == null) return false;
            try
            {
                var p = el.LookupParameter(PARAM_STYLE_LOCKED);
                return p != null
                    && p.StorageType == StorageType.Integer
                    && p.AsInteger() != 0;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        public static void SetLocked(Element el, bool locked)
        {
            if (el == null || !IsEditable(el)) return;
            try
            {
                var p = el.LookupParameter(PARAM_STYLE_LOCKED);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) return;
                p.Set(locked ? 1 : 0);
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"DrawingTypeStamper.SetLocked({el.Id}, {locked}): {ex.Message}");
            }
        }

        /// <summary>
        /// Worksharing guard. ACC-06: previously returned <c>true</c> for
        /// <see cref="CheckoutStatus.NotOwned"/> without distinguishing it
        /// from <see cref="CheckoutStatus.OwnedByOtherUser"/>; the latter
        /// raised opaque "cannot modify" exceptions mid-transaction. The
        /// fix is conservative — only owned-by-current-user is a hard
        /// allow; <c>NotOwned</c> still allows the write because Revit
        /// will auto-checkout, but the caller's <c>try/catch</c> traps
        /// the rare case of a concurrent grab. <c>OwnedByOtherUser</c> is
        /// always a deny + log.
        /// </summary>
        private static bool IsEditable(Element el)
        {
            try
            {
                var doc = el.Document;
                if (doc == null) return false;
                if (!doc.IsWorkshared) return true;
                var status = WorksharingUtils.GetCheckoutStatus(doc, el.Id);
                if (status == CheckoutStatus.OwnedByOtherUser)
                {
                    StingTools.Core.StingLog.Warn(
                        $"DrawingTypeStamper: element {el.Id} owned by another user; skipping write.");
                    return false;
                }
                // NotOwned: Revit auto-checks out on first write; the caller's
                // try/catch around the parameter set captures the rare "raced
                // by another user" case without the validator-only stalling
                // every batch generator inside its own transaction.
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return true; }
        }
    }
}
