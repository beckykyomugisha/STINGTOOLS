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
// Their GUIDs live in the shared-parameter files (MR_PARAMETERS.txt); the
// code binds and reads by NAME. The unread *_GUID copies that used to sit
// here were deleted (DSCH-46b) - one of the three matched no shipped file.

using System;

namespace StingTools.Core.DesignOptions
{
    public static class DesignOptionParams
    {
        public const string OPTION_TXT      = "ASS_DESIGN_OPTION_TXT";
        public const string OPTION_SET_TXT  = "ASS_OPTION_SET_TXT";
        public const string OPTION_PRIM_INT = "ASS_OPTION_PRIMARY_BOOL";

        public const string MAIN_MODEL_LABEL = "Main Model";
    }
}
