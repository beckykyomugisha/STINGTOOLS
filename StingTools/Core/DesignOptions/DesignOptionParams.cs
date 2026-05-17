// StingTools — Design Option shared parameter constants.
//
// Three new shared parameters are written by RunFullPipeline so every
// existing schedule, dashboard, legend, and Excel export becomes
// option-aware without bespoke wiring:
//
//   ASS_DESIGN_OPTION_TXT   — option name (or "Main Model" if null)
//   ASS_OPTION_SET_TXT      — name of the parent option set, or empty
//   ASS_OPTION_PRIMARY_BOOL — 1 if the element lives in a primary option
//                             OR in the main model, else 0
//
// GUIDs are stable v5 UUIDs in the Planscape namespace
// a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a so users get the same shared param
// even when the txt fragment is loaded out of band.

using System;

namespace StingTools.Core.DesignOptions
{
    public static class DesignOptionParams
    {
        public const string OPTION_TXT      = "ASS_DESIGN_OPTION_TXT";
        public const string OPTION_SET_TXT  = "ASS_OPTION_SET_TXT";
        public const string OPTION_PRIM_INT = "ASS_OPTION_PRIMARY_BOOL";

        public static readonly Guid OPTION_TXT_GUID      = new Guid("a7c0b2e4-d000-4001-9c7e-7f6e5d4c3b2a");
        public static readonly Guid OPTION_SET_TXT_GUID  = new Guid("a7c0b2e4-d000-4002-9c7e-7f6e5d4c3b2a");
        public static readonly Guid OPTION_PRIM_INT_GUID = new Guid("a7c0b2e4-d000-4003-9c7e-7f6e5d4c3b2a");

        public const string MAIN_MODEL_LABEL = "Main Model";
    }
}
