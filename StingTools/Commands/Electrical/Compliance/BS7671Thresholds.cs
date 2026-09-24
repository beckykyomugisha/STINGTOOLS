using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Commands.Electrical.Compliance
{
    /// <summary>
    /// The BS 7671 thresholds the compliance engine reads, and the layering
    /// that builds them: corporate <c>STING_BS7671_DISCONNECTION.json</c>, then
    /// a project override at <c>&lt;project&gt;/_BIM_COORD/bs7671_disconnection.json</c>
    /// whose keys win one by one (ROADMAP ELEC-14).
    ///
    /// The corporate Ze values are UK DNO maximum declared figures. They are
    /// not UMEME's, and they are not a measurement - a project outside the UK
    /// (or any project with a declared / measured Ze) should say so in the
    /// override, e.g. <c>{ "earthingSystems": { "TN-C-S": { "ZeOhm": 0.25 } } }</c>.
    ///
    /// Revit-free on purpose: the merge is where a typo turns into a silent
    /// default, so it is tested outside Revit.
    /// </summary>
    public class BS7671Thresholds
    {
        /// <summary>
        /// BS 7671:2018+A2:2022 Reg 411.4.4: minimum voltage factor Cmin = 0.95,
        /// used in Zs × Ia ≤ U0 × Cmin (it accounts for supply-voltage variation;
        /// the Table 41.2-41.4 maxima already include it). The shipped JSON
        /// carries the same value under "cMin"; this is the fallback only.
        /// </summary>
        public const double DefaultCmin = 0.95;

        public double NominalUo { get; set; } = 230;
        public double Cmin { get; set; } = DefaultCmin;
        public Dictionary<string, double> Ze { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> IaMultiplier { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> AdiabaticK { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> DisconnectFinal { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> DisconnectDistribution { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<RcdScenario> RcdScenarios { get; } = new();

        /// <summary>Which file each Ze came from ("corporate" / "project"), keyed by earthing system.</summary>
        public Dictionary<string, string> ZeSource { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Files applied, in order.</summary>
        public List<string> Sources { get; } = new();
        /// <summary>Values that were present but rejected (out of range / wrong type).</summary>
        public List<string> Warnings { get; } = new();

        public void SeedDefaults()
        {
            NominalUo = 230;
            Cmin = DefaultCmin;
            // UK DNO maximum declared Ze: TN-C-S (PME) 0.35 ohm, TN-S 0.8 ohm.
            Ze["TN-S"] = 0.80; Ze["TN-C-S"] = 0.35; Ze["TT"] = 21.0; Ze["IT"] = 100.0;
            foreach (var k in Ze.Keys.ToList()) ZeSource[k] = "built-in default";
            IaMultiplier["MCB_B"] = 5; IaMultiplier["MCB_C"] = 10; IaMultiplier["MCB_D"] = 20;
            IaMultiplier["RCBO_B"] = 5; IaMultiplier["RCBO_C"] = 10;
            IaMultiplier["MCCB"] = 10; IaMultiplier["ACB"] = 8;
            AdiabaticK["Cu/PVC"] = 115; AdiabaticK["Cu/XLPE"] = 143;
            AdiabaticK["Al/PVC"] = 76;  AdiabaticK["Al/XLPE"] = 94;
            DisconnectFinal["TN-S"] = 0.4; DisconnectFinal["TN-C-S"] = 0.4; DisconnectFinal["TT"] = 0.2;
            DisconnectDistribution["TN-S"] = 5.0; DisconnectDistribution["TN-C-S"] = 5.0; DisconnectDistribution["TT"] = 1.0;
        }

        /// <summary>
        /// Apply one JSON layer. Only keys that are present change anything;
        /// dictionaries merge per entry; an rcdRequiredScenarios array replaces
        /// the list. <paramref name="sourceLabel"/> is recorded against every Ze it sets.
        /// </summary>
        public void Apply(JObject root, string sourceLabel)
        {
            if (root == null) return;
            Sources.Add(sourceLabel);

            if (TryNumber(root["nominalUoV"], "nominalUoV", out double uo))
            {
                if (uo > 0) NominalUo = uo; else Warnings.Add($"{sourceLabel}: nominalUoV {uo} rejected (must be > 0)");
            }
            if (TryNumber(root["cMin"], "cMin", out double cmin))
            {
                if (cmin > 0 && cmin <= 1) Cmin = cmin;
                else Warnings.Add($"{sourceLabel}: cMin {cmin} rejected (must be in (0, 1])");
            }
            foreach (var sys in (root["earthingSystems"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                if (!TryNumber(sys.Value?["ZeOhm"], $"earthingSystems.{sys.Name}.ZeOhm", out double ze)) continue;
                if (ze < 0) { Warnings.Add($"{sourceLabel}: {sys.Name} Ze {ze} rejected (negative)"); continue; }
                Ze[sys.Name] = ze;
                ZeSource[sys.Name] = sourceLabel;
            }
            foreach (var m in (root["iaMultipliers"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                if (m.Value.Type == JTokenType.Float || m.Value.Type == JTokenType.Integer)
                    IaMultiplier[m.Name] = m.Value.Value<double>();
            foreach (var k in (root["adiabaticK"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                if (TryNumber(k.Value, $"adiabaticK.{k.Name}", out double kv)) AdiabaticK[k.Name] = kv;
            foreach (var d in (root["disconnectionTimesSec"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                if (TryNumber(d.Value?["final_le32A"], "final_le32A", out double f)) DisconnectFinal[d.Name] = f;
                if (TryNumber(d.Value?["distribution"], "distribution", out double dd)) DisconnectDistribution[d.Name] = dd;
            }
            if (root["rcdRequiredScenarios"] is JArray scen)
            {
                RcdScenarios.Clear();
                foreach (var s in scen)
                    RcdScenarios.Add(new RcdScenario
                    {
                        Scenario = s["scenario"]?.ToString() ?? "",
                        IMaxMA   = s["imaxMA"]?.Value<int>() ?? 30,
                        Reg      = s["regulation"]?.ToString() ?? ""
                    });
            }

            bool TryNumber(JToken t, string name, out double v)
            {
                v = 0;
                if (t == null || t.Type == JTokenType.Null) return false;
                if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) { v = t.Value<double>(); return true; }
                if (t.Type == JTokenType.String && double.TryParse(t.ToString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v)) return true;
                Warnings.Add($"{sourceLabel}: {name} is not a number ('{t}') — ignored");
                return false;
            }
        }

        /// <summary>
        /// Corporate file, then (when it exists) the project override. A missing
        /// corporate file seeds the built-in defaults; an unreadable override is
        /// reported in <see cref="Warnings"/> and the corporate values stand.
        /// </summary>
        public static BS7671Thresholds LoadLayered(string corporatePath, string projectOverridePath)
        {
            var t = new BS7671Thresholds();
            if (string.IsNullOrEmpty(corporatePath) || !File.Exists(corporatePath))
                t.SeedDefaults();
            else
            {
                try { t.Apply(JObject.Parse(File.ReadAllText(corporatePath)), "corporate"); }
                catch (Exception ex)
                {
                    t.Warnings.Add($"corporate {Path.GetFileName(corporatePath)} unreadable: {ex.Message} — built-in defaults used");
                    t.SeedDefaults();
                }
            }
            if (!string.IsNullOrEmpty(projectOverridePath) && File.Exists(projectOverridePath))
            {
                try { t.Apply(JObject.Parse(File.ReadAllText(projectOverridePath)), "project"); }
                catch (Exception ex)
                {
                    t.Warnings.Add($"project override {Path.GetFileName(projectOverridePath)} unreadable: {ex.Message} — ignored");
                }
            }
            return t;
        }
    }

    public class RcdScenario { public string Scenario; public int IMaxMA; public string Reg; }
}
