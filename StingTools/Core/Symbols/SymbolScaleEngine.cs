// StingTools — view-scale → symbol-tier mapping (Phase 175)

using System;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Symbols
{
    public static class SymbolScaleEngine
    {
        public static string GetScaleTier(View view)
        {
            if (view == null) return "standard";
            try { return SymbolStandardRegistry.GetScaleTier(view.Scale); }
            catch (System.Exception ex)
            {
                StingTools.Core.StingLog.Warn($"GetScaleTier: {ex.Message}");
                return "standard";
            }
        }

        public static string GetScaleTier(int scale)
            => SymbolStandardRegistry.GetScaleTier(scale);

        public static bool ShouldSimplify(View view, int elementCount)
        {
            if (view == null) return false;
            try { return view.Scale > 200 && elementCount > 50; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
    }
}
