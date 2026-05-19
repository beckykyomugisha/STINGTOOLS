using StingTools.Core;
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
        public const string PARAM_DRAWING_TYPE_ID   = "STING_DRAWING_TYPE_ID_TXT";
        public const string PARAM_STYLE_LOCKED      = "STING_STYLE_LOCKED_BOOL";
        public const string PARAM_DRAWING_PACKAGE_ID = "STING_DRAWING_PACKAGE_ID_TXT";
        public const string PARAM_SHEET_SEQUENCE    = "STING_SHEET_SEQUENCE_INT";

        // Phase 183 — crop stamps written by DrawingCropApplier so the
        // DriftDetector can spot a profile whose crop kind / margin has
        // moved on but whose views still carry the old derived crop region.
        // Graceful degradation: when these params aren't bound on the
        // project, Stamp/Read are no-ops and crop drift simply isn't
        // surfaced — no functional regression.
        public const string PARAM_CROP_KIND         = "STING_CROP_KIND_TXT";
        public const string PARAM_CROP_MARGIN_MM    = "STING_CROP_MARGIN_MM_TXT";

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
                return p?.StorageType == StorageType.String ? p.AsString() : null;
            }
            catch { return null; }
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
            catch { return false; }
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
        /// Read the raw string value of PARAM_DRAWING_TYPE_ID without
        /// any parsing. Used by ManagedTemplateSyncer to detect managed
        /// template stamps that contain "pack=…|cs=…" prefixes.
        /// </summary>
        public static string ReadRaw(Element el)
        {
            if (el == null) return null;
            try
            {
                var p = el.LookupParameter(PARAM_DRAWING_TYPE_ID);
                return p?.StorageType == StorageType.String ? p.AsString() : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Phase 183 — stamp crop kind + margin onto a view so
        /// <see cref="DrawingDriftDetector"/> can spot bbox-derived crops
        /// that have fallen behind the profile's current crop settings.
        /// Margin is rounded to 2dp and serialised as a string so the
        /// param can stay a simple text shared parameter. Returns true
        /// when both writes succeeded (or were no-op idempotent rewrites).
        /// </summary>
        public static bool StampCrop(Element el, string cropKind, double marginMm)
        {
            if (el == null) return false;
            if (!IsEditable(el)) return false;
            bool any = false;
            try
            {
                var pk = el.LookupParameter(PARAM_CROP_KIND);
                if (pk != null && !pk.IsReadOnly && pk.StorageType == StorageType.String)
                {
                    var current = pk.AsString();
                    var desired = cropKind ?? string.Empty;
                    if (!string.Equals(current, desired, StringComparison.Ordinal))
                        pk.Set(desired);
                    any = true;
                }
                var pm = el.LookupParameter(PARAM_CROP_MARGIN_MM);
                if (pm != null && !pm.IsReadOnly && pm.StorageType == StorageType.String)
                {
                    var desired = marginMm.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    var current = pm.AsString();
                    if (!string.Equals(current, desired, StringComparison.Ordinal))
                        pm.Set(desired);
                    any = true;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"DrawingTypeStamper.StampCrop({el.Id}, '{cropKind}', {marginMm}): {ex.Message}");
                return false;
            }
            return any;
        }

        /// <summary>
        /// Read the (kind, marginMm) pair stamped by <see cref="StampCrop"/>.
        /// Returns (null, null) when either parameter is missing — caller
        /// treats that as "no stamp; can't diff" rather than as drift.
        /// </summary>
        public static (string Kind, double? MarginMm) ReadCrop(Element el)
        {
            if (el == null) return (null, null);
            try
            {
                string kind = null;
                double? margin = null;
                var pk = el.LookupParameter(PARAM_CROP_KIND);
                if (pk?.StorageType == StorageType.String) kind = pk.AsString();
                var pm = el.LookupParameter(PARAM_CROP_MARGIN_MM);
                if (pm?.StorageType == StorageType.String
                    && double.TryParse(pm.AsString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v))
                    margin = v;
                return (kind, margin);
            }
            catch { return (null, null); }
        }

        /// <summary>
        /// Stamp the drawing package id onto a sheet element.
        /// Idempotent. Requires an active transaction from the caller.
        /// </summary>
        public static bool StampPackage(Element el, string packageId)
        {
            if (el == null || string.IsNullOrWhiteSpace(packageId)) return false;
            if (!IsEditable(el)) return false;
            try
            {
                var p = el.LookupParameter(PARAM_DRAWING_PACKAGE_ID);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
                if (string.Equals(p.AsString(), packageId, StringComparison.Ordinal)) return true;
                p.Set(packageId);
                return true;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"DrawingTypeStamper.StampPackage({el.Id}, '{packageId}'): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stamp the sheet sequence number (1-based) onto a sheet element.
        /// Requires an active transaction from the caller.
        /// </summary>
        public static bool StampSheetSequence(Element el, int sequence)
        {
            if (el == null) return false;
            if (!IsEditable(el)) return false;
            try
            {
                var p = el.LookupParameter(PARAM_SHEET_SEQUENCE);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.Integer)
                {
                    if (p.AsInteger() == sequence) return true;
                    p.Set(sequence);
                    return true;
                }
                if (p.StorageType == StorageType.Double)
                {
                    p.Set((double)sequence);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"DrawingTypeStamper.StampSheetSequence({el.Id}, {sequence}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Worksharing guard — true if the current user owns the
        /// element's checkout or if the model is not workshared.
        /// Avoids 'cannot modify' exceptions on federated runs.
        /// </summary>
        private static bool IsEditable(Element el)
        {
            try
            {
                var doc = el.Document;
                if (doc == null) return false;
                if (!doc.IsWorkshared) return true;
                var status = WorksharingUtils.GetCheckoutStatus(doc, el.Id);
                return status == CheckoutStatus.OwnedByCurrentUser
                    || status == CheckoutStatus.NotOwned;
            }
            catch { return true; }
        }
    }
}
