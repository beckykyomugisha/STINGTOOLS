// STING Tools — Phase 116: 20 additional StandardsAPI + AECCalculations wrappers.
// Minimal body per wrapper — prompt + result panel + cited standard.
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Standards;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.StandardsExt
{
    internal static class Bk { public static StingResultPanel.Builder B(string t, string s) => StingResultPanel.Create(t).SetSubtitle(s); }

    // 1. Ventilation (ASHRAE 62.1)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class VentilationCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Ventilation","ASHRAE 62.1 + CIBSE TM60").AddSection("RATES").Metric("Office","10 L/s/person").Metric("Classroom","10 L/s/person").Metric("Corridor","0.5 L/s/m²").Metric("Toilet extract","50 L/s/wc").Show(); return Result.Succeeded; } }
    // 2. Plumbing pipe size (Hunter / BS 6700)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class PlumbingPipeSizeCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { if (!NumericPrompt.TryAsk("Plumbing pipe (BS 6700)", new[] {"Fixture units"}, new[] {20.0}, out var v)) return Result.Cancelled; double dn = v[0] < 5 ? 20 : v[0] < 15 ? 25 : v[0] < 40 ? 32 : v[0] < 100 ? 50 : 80; Bk.B("Plumbing pipe size","BS 6700 + Hunter curves").AddSection("").Metric("FU", $"{v[0]:F0}").Metric("DN",$"{dn:F0}").Show(); return Result.Succeeded; } }
    // 3. Duct size equal friction (CIBSE Guide B3)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class DuctEqualFrictionCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { if (!NumericPrompt.TryAsk("Equal friction duct", new[] {"Flow L/s","Friction Pa/m"}, new[] {500.0, 1.0}, out var v)) return Result.Cancelled; double d = Math.Pow((v[0] * 1e-3 * 4 * 0.02) / (Math.PI * Math.Sqrt(2 * v[1] / 1.2)), 0.4) * 1000; Bk.B("Equal friction duct","CIBSE Guide B3").AddSection("").Metric("Flow",$"{v[0]:F0} L/s").Metric("Dia mm",$"{d:F0}").Show(); return Result.Succeeded; } }
    // 4. Psychrometric
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class PsychrometricCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { if (!NumericPrompt.TryAsk("Moist air (ASHRAE)", new[] {"DBT °C","RH %","P kPa"}, new[] {25.0,60.0,101.3}, out var v)) return Result.Cancelled; double pws = 0.611 * Math.Exp(17.27 * v[0] / (v[0] + 237.3)); double pw = v[1] / 100 * pws; double w = 0.622 * pw / (v[2] - pw); Bk.B("Psychrometric","ASHRAE Handbook Chapter 1").AddSection("").Metric("DBT",$"{v[0]:F1} °C").Metric("RH",$"{v[1]:F0}%").Metric("W",$"{w*1000:F2} g/kg").Show(); return Result.Succeeded; } }
    // 5. Fault current + arc flash (IEEE 1584)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ArcFlashCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Arc flash","IEEE 1584 + NFPA 70E").AddSection("PPE CATEGORY").Metric("Cat 0","1.2 cal/cm²").Metric("Cat 1","4 cal/cm²").Metric("Cat 2","8 cal/cm²").Metric("Cat 3","25 cal/cm²").Metric("Cat 4","40 cal/cm²").Show(); return Result.Succeeded; } }
    // 6. Conduit fill (NEC Ch 9)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ConduitFillCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Conduit fill","NEC Chapter 9 Table 1").AddSection("").Metric("1 wire","53%").Metric("2 wires","31%").Metric("3+ wires","40%").Show(); return Result.Succeeded; } }
    // 7. Steel beam (AISC 360 / EC3)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SteelBeamCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { if (!NumericPrompt.TryAsk("Steel beam", new[] {"Span m","UDL kN/m"}, new[] {8.0,20.0}, out var v)) return Result.Cancelled; double mMax = v[1] * v[0] * v[0] / 8; Bk.B("Steel beam","AISC 360 LRFD / EC3").AddSection("").Metric("Mmax",$"{mMax:F0} kNm").Metric("Section (est)", mMax < 100 ? "UB 356x171x45" : mMax < 300 ? "UB 457x191x67" : "UB 533x210x82").Show(); return Result.Succeeded; } }
    // 8. Concrete beam (ACI 318 / EC2)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class ConcreteBeamCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Concrete beam","ACI 318 + EC2").AddSection("DEFAULT RATIOS").Metric("L/d simple span","20").Metric("L/d continuous","26").Metric("min steel ρmin","1.4/fy (ACI 10.5)").Show(); return Result.Succeeded; } }
    // 9. Foundation (ACI 318-14 / EC7)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class FoundationCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { if (!NumericPrompt.TryAsk("Pad foundation", new[] {"Column load kN","Bearing capacity kN/m²"}, new[] {1000.0,200.0}, out var v)) return Result.Cancelled; double a = v[0] * 1.35 / v[1]; double b = Math.Sqrt(a); Bk.B("Pad foundation","ACI 318-14 + EC7").AddSection("").Metric("Area required",$"{a:F2} m²").Metric("Square pad",$"{b*1000:F0} mm × {b*1000:F0} mm").Show(); return Result.Succeeded; } }
    // 10. Seismic (ASCE 7 Ch 12)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SeismicCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Seismic","ASCE 7 Chapter 12").AddSection("SDS SITE CLASS").Metric("A hard rock","Fa=0.8, Fv=0.8").Metric("B rock","Fa=1.0").Metric("C dense soil","Fa=1.2").Metric("D stiff soil","Fa=1.6").Metric("E soft soil","Fa=2.5").Show(); return Result.Succeeded; } }
}

namespace StingTools.Commands.StandardsExt
{
    // 11. Occupant load (IBC / NFPA 101)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class OccupantLoadCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { if (!NumericPrompt.TryAsk("Occupant load", new[] {"Area m²","Load factor m²/occ"}, new[] {500.0,9.3}, out var v)) return Result.Cancelled; Bk.B("Occupant load","IBC 1004 + NFPA 101").AddSection("").Metric("Occupants", $"{(int)Math.Ceiling(v[0]/v[1])}").Show(); return Result.Succeeded; } }
    // 12. Travel distance
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class TravelDistanceCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Travel distance","IBC 1017 + BS 9999").AddSection("B-Office").Metric("Sprinklered","91 m").Metric("Not sprinklered","61 m").Metric("Common path","30 m").Metric("Dead-end","15 m").Show(); return Result.Succeeded; } }
    // 13. Egress width
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class EgressWidthCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { if (!NumericPrompt.TryAsk("Egress width", new[] {"Occupant load"}, new[] {300.0}, out var v)) return Result.Cancelled; Bk.B("Egress width","IBC 1005 + NFPA 101").AddSection("").Metric("Required width",$"{v[0] * 5.1:F0} mm at 5.1 mm/occupant").Metric("Min door","915 mm (32 in)").Metric("Min corridor","1120 mm (44 in)").Show(); return Result.Succeeded; } }
    // 14. Space utilization (IFMA)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SpaceUtilizationCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Space utilisation","IFMA + BOMA").AddSection("TARGETS").Metric("Usable / Gross","≥ 0.85").Metric("Rentable / Gross","0.80-0.88").Metric("Density (office)","10-15 m²/person").Show(); return Result.Succeeded; } }
    // 15. Hydrant (NFPA 24)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class HydrantCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Hydrant","NFPA 24 + BS EN 14384").AddSection("PLACEMENT").Metric("Max spacing","100 m (NFPA 1)").Metric("From building","≥ 12 m").Metric("Flow test","946 L/min @ 137 kPa min").Show(); return Result.Succeeded; } }
    // 16. Maintenance cost (BSRIA)
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class MaintenanceCostCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { if (!NumericPrompt.TryAsk("Maintenance cost", new[] {"GFA m²","Building type (1=Off, 2=Hosp, 3=School)"}, new[] {2000.0,1.0}, out var v)) return Result.Cancelled; double rate = v[1] <= 1.5 ? 55 : v[1] <= 2.5 ? 90 : 45; Bk.B("Maintenance cost","BSRIA BG 21 + CIBSE TM56").AddSection("").Metric("Rate",$"£{rate:F0}/m²/yr").Metric("Annual",$"£{v[0]*rate:F0}").Show(); return Result.Succeeded; } }
    // 17. Accessible toilet
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class AccessibleToiletCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Accessible toilet","BS 8300 + Part M + ADA").AddSection("MIN DIMS").Metric("Cubicle","2200 × 1500 mm").Metric("Door clear","850 mm outward").Metric("WC centreline","450 mm from sidewall").Metric("Grab rails","H = 200 mm above seat").Show(); return Result.Succeeded; } }
    // 18. Accessible fixtures
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class AccessibleFixturesCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Accessible fixtures","ADA 309 + BS 8300").AddSection("REACH").Metric("Forward reach","≤ 1200 mm").Metric("Side reach","≤ 1400 mm").Metric("Low reach","≥ 400 mm").Metric("Switch height","1000 mm (standard 1100 mm)").Show(); return Result.Succeeded; } }
    // 19. Energy analysis
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class EnergyAnalysisCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Energy analysis","Part L + ASHRAE 90.1 + CIBSE TM54").AddSection("FABRIC").Metric("External wall U","≤ 0.18 W/m²K").Metric("Roof U","≤ 0.11").Metric("Floor U","≤ 0.13").Metric("Glazing U","≤ 1.4").Metric("Airtightness","≤ 5 m³/hr/m² @ 50 Pa").Show(); return Result.Succeeded; } }
    // 20. Sprinkler design criteria
    [Transaction(TransactionMode.ReadOnly)][Regeneration(RegenerationOption.Manual)]
    public class SprinklerCriteriaCommand : IExternalCommand { public Result Execute(ExternalCommandData cd, ref string m, ElementSet e) { Bk.B("Sprinkler criteria","NFPA 13 + BS EN 12845").AddSection("DENSITY mm/min × AREA m²").Metric("LH","2.25 × 84").Metric("OH1","5 × 72").Metric("OH2","5 × 144").Metric("OH3","5 × 216").Metric("HHP1","7.5 × 260").Metric("HHS1","7.5 × 260").Show(); return Result.Succeeded; } }
}
