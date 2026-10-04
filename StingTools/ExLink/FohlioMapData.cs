using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.ExLink
{
    // The Revit-free half of FohlioMap: the data model and how a project file is read.
    // Split out (KUT deep review, 2026-10) so StingTools.Boq.Tests can load the SHIPPED KUT
    // map through the real class — the earlier test read a JObject and never exercised it,
    // which is how two defects lived here:
    //
    //  * Newtonsoft's default ObjectCreationHandling.Auto REUSES a list initialised in the
    //    class and APPENDS the file's items to it. The KUT map lists all eight default columns
    //    plus four of its own, so the KUT export carried twenty columns with Item Tag,
    //    Manufacturer, Model and Fohlio Ref twice, the import diffed each written field twice,
    //    and no project could ever narrow the category list.
    //  * With no map file at all, the six default categories still answered IsFfeCategory, so a
    //    project that does not use Fohlio billed every plumbing fixture, light fitting, casework
    //    and specialty-equipment item as Owner-procured FF&E — at cost, outside OH&P and
    //    contingency.

    /// <summary>One column in the Fohlio ↔ STING/Revit mapping.</summary>
    public class FohlioColumn
    {
        /// <summary>Header used in the Fohlio import/export sheet.</summary>
        public string Header { get; set; } = "";
        /// <summary>STING/Revit parameter, or a "$"-pseudo (Family / Type / Category / Room).</summary>
        public string Param { get; set; } = "";
        /// <summary>True if Fohlio_Import may write this value back into the model.</summary>
        public bool WriteBack { get; set; }
    }

    public partial class FohlioMap
    {
        /// <summary>FF&E categories exchanged with Fohlio.</summary>
        public List<string> Categories { get; set; } = new List<string>
        {
            "Furniture", "Furniture Systems", "Casework", "Plumbing Fixtures",
            "Lighting Fixtures", "Specialty Equipment"
        };

        public List<FohlioColumn> Columns { get; set; } = new List<FohlioColumn>
        {
            new FohlioColumn { Header = "Item Tag",      Param = "ASS_TAG_1_TXT" },
            new FohlioColumn { Header = "Category",      Param = "$Category" },
            new FohlioColumn { Header = "Product",       Param = "$Type" },
            new FohlioColumn { Header = "Family",        Param = "$Family" },
            new FohlioColumn { Header = "Manufacturer",  Param = "ASS_MANUFACTURER_TXT", WriteBack = true },
            new FohlioColumn { Header = "Model",         Param = "ASS_MODEL_REF_TXT",    WriteBack = true },
            new FohlioColumn { Header = "Room",          Param = "$Room" },
            new FohlioColumn { Header = "Fohlio Ref",    Param = "FOHLIO_REF_TXT",       WriteBack = true },
        };

        // ---- FF&E BOQ treatment --------------------------------------------------
        // How Fohlio FF&E is carried in the bill. An item is exactly ONE of these -
        // never double-counted.
        //   "ffe"                    DEFAULT - transparent Owner-procured FF&E category
        //                            (NRM1 group 8 / ICMS): a visible model line, priced
        //                            at cost from the Fohlio register, flagged so the
        //                            spec gate does not chase a specification the
        //                            contractor never writes
        //   "measured"               contractor-supplied - a normal priced line
        //   "ownerSupplied-excluded" out of this bill entirely (the element is skipped)
        //   "pcSum"                  explicit contractual provisional / prime-cost sum
        public string BoqTreatment { get; set; } = "ffe";
        public Dictionary<string, string> BoqTreatmentByCategory { get; set; }

        /// <summary>True only when this map was read from the project's own fohlio_map.json.
        /// The built-in defaults describe what an export would contain; they are not a
        /// statement that this project procures anything through Fohlio.</summary>
        [JsonIgnore]
        public bool LoadedFromProject { get; set; }

        /// <summary>Canonical treatment for a category - delegates to the pure,
        /// unit-tested <see cref="StingTools.BOQ.FfeTreatment"/>.</summary>
        public string TreatmentFor(string category)
            => StingTools.BOQ.FfeTreatment.Resolve(category, BoqTreatment, BoqTreatmentByCategory);

        /// <summary>Is this category Owner FF&E for the BOQ? Never, unless the project has a
        /// Fohlio map: a project without Fohlio has no FF&E link to price from.</summary>
        public bool IsFfeCategory(string category)
        {
            if (!LoadedFromProject) return false;
            if (string.IsNullOrEmpty(category) || Categories == null) return false;
            foreach (var c in Categories)
                if (string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static readonly JsonSerializerSettings ParseSettings = new JsonSerializerSettings
        {
            // A list in the file REPLACES the default, never appends to it.
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        /// <summary>Read a project fohlio_map.json. Lists in the file replace the defaults;
        /// keys the file omits keep them. Throws on malformed JSON — the caller decides how
        /// loudly to say so.</summary>
        public static FohlioMap Parse(string json)
        {
            var map = JsonConvert.DeserializeObject<FohlioMap>(json, ParseSettings) ?? new FohlioMap();
            map.Categories = map.Categories ?? new List<string>();
            map.Columns = map.Columns ?? new List<FohlioColumn>();
            map.LoadedFromProject = true;
            return map;
        }
    }
}
