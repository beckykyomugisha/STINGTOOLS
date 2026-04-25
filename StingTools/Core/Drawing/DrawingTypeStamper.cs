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
