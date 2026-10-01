using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// Install / labour hours per element: one parameter, CST_INSTALL_HRS.
    ///
    /// <para>DSCH-47 follow-up. CST_LABOUR_HOURS ("Estimated labour hours per element
    /// for install takeoff") and CST_INSTALL_HRS ("Install time in hours per element
    /// for labour takeoff") meant the same thing. CST_INSTALL_HRS is the one the code
    /// uses - the labour-hours engine writes it; the A5 carbon stage, the labour
    /// report, the TAG7 cost line, its display mirror and the IFC map read it - so it
    /// is the owner. CST_LABOUR_HOURS is deprecated (registry <c>deprecated</c> /
    /// <c>replaced_by</c>); its GUID stays bound because older models may hold values
    /// in it.</para>
    ///
    /// <para>Every reader goes through here, so a value typed into the old parameter
    /// on an older model is still read - CST_INSTALL_HRS first, CST_LABOUR_HOURS only
    /// when CST_INSTALL_HRS has no value. Nothing writes the old parameter.</para>
    /// </summary>
    public static class InstallHours
    {
        public const string Owner = ParamRegistry.CST_INSTALL_HRS;
#pragma warning disable CS0618 // the deprecated name is read here, and only here
        public const string Legacy = ParamRegistry.CST_LABOUR_HOURS;
#pragma warning restore CS0618

        /// <summary>The parameter that holds the value on this element: the owner when it
        /// has one, else the deprecated one when that has one, else the owner.</summary>
        public static string SourceOf(Element el)
            => HasValue(el, Owner) || !HasValue(el, Legacy) ? Owner : Legacy;

        /// <summary>Invariant value text (see <see cref="ParameterHelpers.GetValueText"/>).</summary>
        public static string ReadValueText(Element el)
            => ParameterHelpers.GetValueText(el, SourceOf(el));

        /// <summary>Display text (see <see cref="ParameterHelpers.GetDisplayText"/>).</summary>
        public static string ReadDisplayText(Element el)
            => ParameterHelpers.GetDisplayText(el, SourceOf(el));

        /// <summary>Hours as a number, or <paramref name="defaultValue"/>.</summary>
        public static double ReadDouble(Element el, double defaultValue = 0)
            => ParameterHelpers.GetDouble(el, SourceOf(el), defaultValue);

        private static bool HasValue(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || !p.HasValue) return false;
                return p.StorageType != StorageType.String || !string.IsNullOrWhiteSpace(p.AsString());
            }
            catch (System.Exception ex)
            {
                StingLog.Warn($"InstallHours: reading {name} on {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
