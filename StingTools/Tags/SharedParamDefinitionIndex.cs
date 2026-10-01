using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Tags
{
    /// <summary>
    /// TAGFAM-6: a shared-parameter file indexed by definition name, built in one walk.
    ///
    /// Four commands each carried a private <c>FindSharedDefinition</c> that walked every
    /// group and definition of the file through the Revit API for every name it looked up
    /// (~3,300 definitions in MR_PARAMETERS.txt), inside per-parameter and per-family loops.
    /// Build this once per opened <see cref="DefinitionFile"/> and look names up in it.
    /// First match by exact (ordinal) name wins — the rule every copy used.
    /// </summary>
    internal static class SharedParamDefinitionIndex
    {
        public static Dictionary<string, ExternalDefinition> Build(DefinitionFile defFile)
            => Build(defFile, out _, out _);

        public static Dictionary<string, ExternalDefinition> Build(
            DefinitionFile defFile, out int groupCount, out int definitionCount)
        {
            groupCount = 0;
            definitionCount = 0;
            var byName = new Dictionary<string, ExternalDefinition>(StringComparer.Ordinal);
            if (defFile == null) return byName;

            foreach (DefinitionGroup grp in defFile.Groups)
            {
                groupCount++;
                foreach (Definition d in grp.Definitions)
                {
                    definitionCount++;
                    if (d is ExternalDefinition ed && !string.IsNullOrEmpty(ed.Name) && !byName.ContainsKey(ed.Name))
                        byName[ed.Name] = ed;
                }
            }
            return byName;
        }

        /// <summary>The definition named <paramref name="name"/>, or null.</summary>
        public static ExternalDefinition Find(Dictionary<string, ExternalDefinition> index, string name)
            => index != null && !string.IsNullOrEmpty(name) && index.TryGetValue(name, out var d) ? d : null;
    }
}
