// StingTools — Drawing Template Manager · a view name nothing else has
//
// DrawingProducer tried "Name", "Name_(2)" … "Name_(99)" and, if all were taken, returned
// the last one tried — a name that exists. Revit refuses a duplicate view name, the
// rename was caught and logged, and the view kept Revit's default name ("Floor Plan 7")
// while the report said it was produced. There is now no cap short of an absurd one,
// and running out is an answer (null) the caller reports rather than a taken name.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;

namespace StingTools.Core.Drawing
{
    public static class ViewNameUniquifier
    {
        /// <summary>Far beyond any real model; only a runaway loop reaches it.</summary>
        public const int Limit = 100000;

        /// <summary>
        /// <paramref name="baseName"/>, else "baseName_(2)", "_(3)" … — the first that
        /// <paramref name="exists"/> says is free. Null when none is free within
        /// <see cref="Limit"/>; never a name that exists.
        /// </summary>
        public static string Next(string baseName, Func<string, bool> exists, int limit = Limit)
        {
            if (string.IsNullOrEmpty(baseName) || exists == null) return baseName;
            if (!exists(baseName)) return baseName;
            for (int n = 2; n <= limit; n++)
            {
                var candidate = $"{baseName}_({n})";
                if (!exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
