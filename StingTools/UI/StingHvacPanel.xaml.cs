using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Code-behind for the STING HVAC Center dockable panel.
    /// 7 tabs: EQPT · SYS · CALCS · DUCT · LOADS · FAB · RPRT.
    /// Every button click dispatches via <see cref="StingHvacCommandHandler"/>
    /// so Revit API calls run on the Revit API thread.
    ///
    /// Mirrors the StingElectricalPanel / StingPlumbingPanel architecture:
    ///   - ObservableCollection per data-grid
    ///   - Header dropdowns drive a shared "current state" snapshot consumed
    ///     by the command handler
    ///   - Singleton instance exposed via <see cref="Instance"/> so command
    ///     classes can refresh grids after a run
    /// </summary>
    public partial class StingHvacPanel : Page
    {
        // ── Grid view-models ────────────────────────────────────────────
        public ObservableCollection<HvacEquipmentRow>  EquipmentRows  { get; } = new();
        public ObservableCollection<HvacSystemRow>     SystemRows     { get; } = new();
        public ObservableCollection<SizingRoleRow>     SizingRoleRows { get; } = new();
        public ObservableCollection<HvacIssueRow>      IssueRows      { get; } = new();
        public ObservableCollection<HvacDuctTypeRow>   DuctTypeRows   { get; } = new();
        public ObservableCollection<HvacStandardSizeRow> StandardSizeRows { get; } = new();
        public ObservableCollection<HvacSpaceLoadRow>  SpaceLoadRows  { get; } = new();
        public ObservableCollection<HvacSpoolRow>      SpoolRows      { get; } = new();
        public ObservableCollection<HvacDriftRow>      DriftRows      { get; } = new();
        public ObservableCollection<HvacWorkflowRunRow> WorkflowRows  { get; } = new();

        // ── Header state ────────────────────────────────────────────────
        public string SelectedStandard  { get; private set; } = "CIBSE";
        public string SelectedPressure  { get; private set; } = "low";
        public string SelectedRegion    { get; private set; } = "UK_SI";
        public double SelectedAirDensity { get; private set; } = 1.20;
        public string SelectedStrategy  { get; private set; } = "velocity";
        public string SelectedScope     { get; private set; } = "ActiveView";

        private static StingHvacPanel _instance;
        public static StingHvacPanel Instance => _instance;

        public StingHvacPanel()
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); }
            catch { /* theme is non-fatal */ }

            _instance = this;

            // Bind grids
            try { EquipmentGrid.ItemsSource    = EquipmentRows; }    catch (Exception ex) { StingLog.Warn($"EquipmentGrid bind: {ex.Message}"); }
            try { SystemGrid.ItemsSource       = SystemRows; }       catch (Exception ex) { StingLog.Warn($"SystemGrid bind: {ex.Message}"); }
            try { SizingRoleGrid.ItemsSource   = SizingRoleRows; }   catch (Exception ex) { StingLog.Warn($"SizingRoleGrid bind: {ex.Message}"); }
            try { IssueGrid.ItemsSource        = IssueRows; }        catch (Exception ex) { StingLog.Warn($"IssueGrid bind: {ex.Message}"); }
            try { DuctTypeGrid.ItemsSource     = DuctTypeRows; }     catch (Exception ex) { StingLog.Warn($"DuctTypeGrid bind: {ex.Message}"); }
            try { StandardSizeGrid.ItemsSource = StandardSizeRows; } catch (Exception ex) { StingLog.Warn($"StandardSizeGrid bind: {ex.Message}"); }
            try { SpaceLoadGrid.ItemsSource    = SpaceLoadRows; }    catch (Exception ex) { StingLog.Warn($"SpaceLoadGrid bind: {ex.Message}"); }
            try { SpoolGrid.ItemsSource        = SpoolRows; }        catch (Exception ex) { StingLog.Warn($"SpoolGrid bind: {ex.Message}"); }
            try { DriftGrid.ItemsSource        = DriftRows; }        catch (Exception ex) { StingLog.Warn($"DriftGrid bind: {ex.Message}"); }
            try { WorkflowGrid.ItemsSource     = WorkflowRows; }     catch (Exception ex) { StingLog.Warn($"WorkflowGrid bind: {ex.Message}"); }

            // Seed sizing-role rows from the registry on first show so the CALCS tab is non-empty.
            try { SeedSizingRolesFromRegistry(); }
            catch (Exception ex) { StingLog.Warn($"SeedSizingRoles: {ex.Message}"); }
        }

        private void SeedSizingRolesFromRegistry()
        {
            try
            {
                var rules = StingTools.Core.Mep.MepSizingRegistry.Get(null);
                SizingRoleRows.Clear();
                StandardSizeRows.Clear();
                foreach (var r in rules.DuctRoles)
                {
                    SizingRoleRows.Add(new SizingRoleRow
                    {
                        Role           = r.Label,
                        MaxVelocityMs  = r.MaxVelocityMs,
                        FrictionPaPerM = r.MaxFrictionPaPerM,
                        AspectMax      = r.AspectMax,
                        Source         = r.Source
                    });
                }
                foreach (var size in rules.DuctSizesForRegion(rules.DuctDefaultRegion))
                {
                    StandardSizeRows.Add(new HvacStandardSizeRow { IsEnabled = true, SizeMm = size });
                }
            }
            catch (Exception ex) { StingLog.Warn($"Registry seed failed: {ex.Message}"); }
        }

        /// <summary>
        /// Phase 182 — drop a row in the RPRT WorkflowGrid showing the most
        /// recent run, status dot and timestamp. Called by sizing / balancing /
        /// schedule commands so the panel stops feeling read-only (gap D9).
        /// Thread-safe via the Dispatcher.
        /// </summary>
        public void PushRunRow(string name, string statusDot)
        {
            try
            {
                Action act = () =>
                {
                    int next = WorkflowRows.Count + 1;
                    WorkflowRows.Insert(0, new HvacWorkflowRunRow
                    {
                        Number    = "#" + next,
                        Name      = name ?? "",
                        StatusDot = statusDot ?? "•",
                        Timestamp = DateTime.Now.ToString("HH:mm:ss")
                    });
                    // Cap log length so a long session doesn't keep growing.
                    while (WorkflowRows.Count > 100) WorkflowRows.RemoveAt(WorkflowRows.Count - 1);
                };
                if (Dispatcher?.CheckAccess() == true) act();
                else Dispatcher?.Invoke(act);
            }
            catch (Exception ex) { StingLog.Warn($"PushRunRow: {ex.Message}"); }
        }

        /// <summary>
        /// Phase 182 — re-seed the CALCS DataGrid after Hvac_ReloadRules fires
        /// (gap B9). Without this, "Reload rules" was a no-op visually.
        /// </summary>
        public void RefreshSizingRoles() => SeedSizingRolesFromRegistry();

        /// <summary>
        /// Phase 188 (Tier 2 / gap G1) — walk the model once and populate
        /// every data grid on the panel. Called automatically on document
        /// open (from <c>StingToolsApp.OnDocumentOpened</c>) and manually
        /// from the Hvac_RefreshPanel toolbar button.
        ///
        /// Reads exclusively via Element parameters + the connector graph,
        /// so it's safe to call from any read transaction. Does NOT write
        /// to the model.
        /// </summary>
        public void RefreshFromDoc(Document doc)
        {
            if (doc == null) return;
            try
            {
                Action act = () =>
                {
                    try { RefreshEquipmentRows(doc); }   catch (Exception ex) { StingLog.Warn($"EquipmentRows refresh: {ex.Message}"); }
                    try { RefreshSystemRows(doc); }      catch (Exception ex) { StingLog.Warn($"SystemRows refresh: {ex.Message}"); }
                    try { RefreshSpaceLoadRows(doc); }   catch (Exception ex) { StingLog.Warn($"SpaceLoadRows refresh: {ex.Message}"); }
                    try { RefreshSpoolRows(doc); }       catch (Exception ex) { StingLog.Warn($"SpoolRows refresh: {ex.Message}"); }
                    try { RefreshDriftRows(doc); }       catch (Exception ex) { StingLog.Warn($"DriftRows refresh: {ex.Message}"); }
                    PushRunRow("Panel refresh from document", "⬤");
                };
                if (Dispatcher?.CheckAccess() == true) act();
                else Dispatcher?.Invoke(act);
            }
            catch (Exception ex) { StingLog.Warn($"RefreshFromDoc: {ex.Message}"); }
        }

        private void RefreshEquipmentRows(Document doc)
        {
            EquipmentRows.Clear();
            var eq = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .WhereElementIsNotElementType();
            foreach (Element e in eq)
            {
                try
                {
                    string tag    = ReadString(e, "ASS_TAG_1");
                    if (string.IsNullOrEmpty(tag)) tag = e.Name ?? $"#{e.Id.Value}";
                    string family = (e is FamilyInstance fi) ? (fi.Symbol?.Family?.Name ?? "") : "";
                    string klass  = ClassifyHvacEquipment(family, e.Name ?? "", ReadString(e, "ASS_PRODCT_COD_TXT"));
                    double kw     = ReadDouble(e, "HVC_CAPACITY_KW");
                    double flowLs = ReadDouble(e, "HVC_FLOW_LS");
                    string system = ReadString(e, "HVC_SYS_TXT");
                    if (string.IsNullOrEmpty(system))
                        system = (e is FamilyInstance fi2)
                            ? (fi2.MEPModel?.ConnectorManager?.Connectors?.Size > 0 ? "(connected)" : "")
                            : "";
                    string dot = (kw > 0 && flowLs > 0) ? "⬤" : ((kw > 0 || flowLs > 0) ? "⬡" : "✗");
                    EquipmentRows.Add(new HvacEquipmentRow
                    {
                        IsSelected = false,
                        Tag = tag, Type = klass,
                        CapacityKw = kw, FlowLs = flowLs,
                        System = system, StatusDot = dot,
                        Manufacturer = ReadString(e, "MAN_NAME_TXT"),
                        Model        = ReadString(e, "MAN_MODEL_TXT")
                    });
                }
                catch (Exception ex) { StingLog.Warn($"Equipment row {e?.Id}: {ex.Message}"); }
            }
        }

        private void RefreshSystemRows(Document doc)
        {
            SystemRows.Clear();
            var sys = new FilteredElementCollector(doc)
                .OfClass(typeof(MEPSystem))
                .WhereElementIsNotElementType();
            foreach (Element s in sys)
            {
                try
                {
                    string name = s.Name ?? "";
                    string sysClass = "";
                    double flowLs = 0;
                    string equip = "";
                    if (s is MechanicalSystem ms)
                    {
                        sysClass = (ms.SystemType.ToString() ?? "").Replace("Mechanical", "");
                        var flowP = s.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                        if (flowP != null && flowP.StorageType == StorageType.Double)
                            flowLs = flowP.AsDouble() * 0.4719;
                        try { equip = ms.BaseEquipment?.Name ?? ""; } catch { }
                    }
                    else if (s is PipingSystem ps)
                    {
                        sysClass = (ps.SystemType.ToString() ?? "").Replace("Piping", "");
                        var flowP = s.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM);
                        if (flowP != null && flowP.StorageType == StorageType.Double)
                            flowLs = flowP.AsDouble();
                        try { equip = ps.BaseEquipment?.Name ?? ""; } catch { }
                    }
                    else continue;
                    SystemRows.Add(new HvacSystemRow
                    {
                        IsSelected = false,
                        Name = name, Class = sysClass,
                        Equipment = equip, FlowLs = flowLs,
                        DropPa = 0, Nc = 0,
                        StatusDot = flowLs > 0 ? "⬤" : "⬡"
                    });
                }
                catch (Exception ex) { StingLog.Warn($"System row {s?.Id}: {ex.Message}"); }
            }
        }

        private void RefreshSpaceLoadRows(Document doc)
        {
            SpaceLoadRows.Clear();
            var spaces = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType();
            foreach (Element sp in spaces)
            {
                try
                {
                    double area = ReadDouble(sp, "Area") * 0.092903;  // ft² → m²
                    int people  = (int)ReadDouble(sp, "Number of People");
                    double heat = ReadDouble(sp, "Design Heating Load") / 3.41214;  // BTU/h → W → /1000 = kW
                    double cool = ReadDouble(sp, "Design Cooling Load") / 3.41214;
                    if (heat > 0) heat /= 1000.0;
                    if (cool > 0) cool /= 1000.0;
                    double oa = ReadDouble(sp, "Specified Supply Airflow") * 0.4719;
                    string warn = (heat == 0 && cool == 0) ? "⚠ no loads" : "";
                    SpaceLoadRows.Add(new HvacSpaceLoadRow
                    {
                        IsSelected = false,
                        SpaceName = sp.Name ?? "",
                        SpaceType = ReadString(sp, "Space Type"),
                        AreaM2 = area, People = people,
                        HeatingKw = heat, CoolingKw = cool,
                        OAls = oa, Warning = warn
                    });
                }
                catch (Exception ex) { StingLog.Warn($"Space row {sp?.Id}: {ex.Message}"); }
            }
        }

        private void RefreshSpoolRows(Document doc)
        {
            SpoolRows.Clear();
            var assemblies = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .WhereElementIsNotElementType();
            foreach (Element a in assemblies)
            {
                try
                {
                    if (!(a is AssemblyInstance ai)) continue;
                    string cat = "";
                    try { cat = doc.Settings.Categories.get_Item(ai.AssemblyTypeName)?.Name ?? ""; } catch { }
                    string name = ai.AssemblyTypeName ?? "";
                    if (!(name.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0
                        || cat.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;
                    double weightKg = ReadDouble(a, "ASSY_WEIGHT_KG_NR");
                    int    fittings = (int)ReadDouble(a, "ASSY_FITTING_COUNT_NR");
                    double lengthM  = ReadDouble(a, "ASSY_LENGTH_M_NR");
                    SpoolRows.Add(new HvacSpoolRow
                    {
                        IsSelected = false,
                        Tag = ai.Name ?? $"S-{ai.Id.Value}",
                        System = ReadString(ai, "HVC_SYS_TXT"),
                        LengthM = lengthM, WeightKg = weightKg,
                        FittingCount = fittings,
                        ShopReady = weightKg > 0 ? "✔" : "?"
                    });
                }
                catch (Exception ex) { StingLog.Warn($"Spool row {a?.Id}: {ex.Message}"); }
            }
        }

        private void RefreshDriftRows(Document doc)
        {
            DriftRows.Clear();
            var ducts = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType();
            foreach (Element d in ducts)
            {
                try
                {
                    int stale = 0;
                    var p = d.LookupParameter("HVC_SIZE_STALE_BOOL");
                    if (p != null && p.HasValue)
                    {
                        if (p.StorageType == StorageType.Integer) stale = p.AsInteger();
                        else if (p.StorageType == StorageType.String
                              && int.TryParse(p.AsString(), out int v)) stale = v;
                    }
                    if (stale != 1) continue;

                    string prev = ReadString(d, "HVC_SIZE_PREV_TXT");
                    string ruleId = ReadString(d, "HVC_SIZE_RULE_ID_TXT");
                    string dateStr = ReadString(d, "HVC_SIZE_MODIFIED_DT");
                    double w = ReadDouble(d, "Width")  * 304.8;
                    double h = ReadDouble(d, "Height") * 304.8;
                    double dia = ReadDouble(d, "Diameter") * 304.8;
                    string now = (w > 0 && h > 0) ? $"{w:F0}x{h:F0}" : (dia > 0 ? $"Ø{dia:F0}" : "?");
                    DriftRows.Add(new HvacDriftRow
                    {
                        Severity = "⚠",
                        Element  = $"#{d.Id.Value}",
                        WasNow   = string.IsNullOrEmpty(prev) ? $"? → {now}" : $"{prev} → {now}",
                        Reason   = string.IsNullOrEmpty(ruleId)
                            ? (string.IsNullOrEmpty(dateStr) ? "stale" : $"stale @ {dateStr}")
                            : $"{ruleId} @ {dateStr}"
                    });
                }
                catch (Exception ex) { StingLog.Warn($"Drift row {d?.Id}: {ex.Message}"); }
            }
        }

        // ── small read helpers (mirror ParameterHelpers but local for the Dispatcher thread) ──
        private static string ReadString(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null) return "";
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                if (p.StorageType == StorageType.Double) return p.AsValueString() ?? "";
            }
            catch { }
            return "";
        }
        private static double ReadDouble(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v;
            }
            catch { }
            return 0;
        }

        private static string ClassifyHvacEquipment(string family, string typeName, string prodCode)
        {
            string all = $"{family}|{typeName}|{prodCode}".ToUpperInvariant();
            if (all.Contains("CHILLER"))      return "CHL";
            if (all.Contains("BOILER"))       return "BLR";
            if (all.Contains("AHU") || all.Contains("AIR HANDL")) return "AHU";
            if (all.Contains("FCU") || all.Contains("FAN COIL"))  return "FCU";
            if (all.Contains("VAV"))          return "VAV";
            if (all.Contains("VRF") || all.Contains("VRV")) return "VRF";
            if (all.Contains("HEAT PUMP") || all == "HP") return "HP";
            if (all.Contains("COOLING TOWER")) return "CT";
            if (all.Contains("FAN"))          return "FAN";
            return "EQP";
        }

        /// <summary>
        /// Phase 182 — serialise the current sizing-role grid to the project
        /// override JSON (gap D1 / A6). Returns the path written, or null
        /// on failure. The grid stays the source of truth; the registry
        /// reload then re-reads from disk so future commands honour the edits.
        /// </summary>
        public string SaveSizingRolesToProjectOverride(string projectFolder)
        {
            try
            {
                if (string.IsNullOrEmpty(projectFolder)) return null;
                string dir = System.IO.Path.Combine(projectFolder, "_BIM_COORD");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "mep_sizing_rules.json");

                var existing = System.IO.File.Exists(path)
                    ? Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path))
                    : new Newtonsoft.Json.Linq.JObject();

                var duct = existing["duct"] as Newtonsoft.Json.Linq.JObject;
                if (duct == null) { duct = new Newtonsoft.Json.Linq.JObject(); existing["duct"] = duct; }

                var roles = new Newtonsoft.Json.Linq.JArray();
                foreach (var r in SizingRoleRows)
                {
                    string id = (r.Role ?? "").Trim().ToLowerInvariant();
                    if (id.Contains(" ")) id = id.Split(' ')[0];
                    roles.Add(new Newtonsoft.Json.Linq.JObject
                    {
                        ["id"]                = id,
                        ["label"]             = r.Role,
                        ["maxVelocityMs"]     = r.MaxVelocityMs,
                        ["maxFrictionPaPerM"] = r.FrictionPaPerM,
                        ["aspectMax"]         = r.AspectMax,
                        ["source"]            = r.Source ?? "project override"
                    });
                }
                duct["roles"] = roles;

                System.IO.File.WriteAllText(path, existing.ToString(Newtonsoft.Json.Formatting.Indented));
                StingTools.Core.Mep.MepSizingRegistry.Reload();
                StingLog.Info($"SaveSizingRolesToProjectOverride wrote {path}");
                return path;
            }
            catch (Exception ex) { StingLog.Error("SaveSizingRolesToProjectOverride", ex); return null; }
        }

        // ── Click dispatch ──────────────────────────────────────────────
        private void Cmd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && !string.IsNullOrEmpty(tag))
            {
                PushSnapshotToHandler();
                StingHvacCommandHandler.Instance?.SetCommand(tag);
                UpdateStatus($"Running: {tag}");
            }
        }

        private void PushSnapshotToHandler()
        {
            try
            {
                StingHvacCommandHandler.CurrentRegion           = SelectedRegion;
                StingHvacCommandHandler.CurrentStandard         = SelectedStandard;
                StingHvacCommandHandler.CurrentPressureClassId  = SelectedPressure;
                StingHvacCommandHandler.CurrentAirDensityKgM3   = SelectedAirDensity;
                StingHvacCommandHandler.CurrentSizingStrategyId = SelectedStrategy;
                StingHvacCommandHandler.CurrentScope            = SelectedScope;
            }
            catch (Exception ex) { StingLog.Warn($"PushSnapshot: {ex.Message}"); }
        }

        private void UpdateStatus(string text)
        {
            try { if (txtHvacStatus != null) txtHvacStatus.Text = text; }
            catch (Exception ex) { StingLog.Warn($"Status update: {ex.Message}"); }
        }

        // ── Header combo handlers ───────────────────────────────────────
        private void cmbStandard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbStandard?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedStandard = tag;
        }

        private void cmbPressure_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPressure?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedPressure = tag;
        }

        private void cmbRegion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbRegion?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                SelectedRegion = tag;
        }

        private void cmbDensity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbDensity?.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && double.TryParse(tag, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                SelectedAirDensity = v;
            }
        }

        private void Strategy_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
                SelectedStrategy = tag;
        }

        private void Scope_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
                SelectedScope = tag;
        }
    }

    // ── View-model rows (POCOs) ─────────────────────────────────────────

    public class HvacEquipmentRow
    {
        public bool   IsSelected   { get; set; }
        public string Tag          { get; set; } = "";
        public string Type         { get; set; } = "";
        public double CapacityKw   { get; set; }
        public double FlowLs       { get; set; }
        public string System       { get; set; } = "";
        public string StatusDot    { get; set; } = "•";   // ⬤ ⬡ ✗
        public string Manufacturer { get; set; } = "";
        public string Model        { get; set; } = "";
    }

    public class HvacSystemRow
    {
        public bool   IsSelected { get; set; }
        public string Name       { get; set; } = "";
        public string Class      { get; set; } = "";
        public string Equipment  { get; set; } = "";
        public double FlowLs     { get; set; }
        public double DropPa     { get; set; }
        public int    Nc         { get; set; }
        public string StatusDot  { get; set; } = "•";
    }

    public class SizingRoleRow
    {
        public string Role           { get; set; } = "";
        public double MaxVelocityMs  { get; set; }
        public double FrictionPaPerM { get; set; }
        public double AspectMax      { get; set; }
        public string Source         { get; set; } = "";
    }

    public class HvacIssueRow
    {
        public bool   IsSelected { get; set; }
        public string Severity   { get; set; } = "";   // ⚠ / ✖
        public string Element    { get; set; } = "";
        public string Issue      { get; set; } = "";
        public string Suggestion { get; set; } = "";
    }

    public class HvacDuctTypeRow
    {
        public bool   IsSelected    { get; set; }
        public string Name          { get; set; } = "";
        public string Shape         { get; set; } = "";
        public string Material      { get; set; } = "";
        public string PressureClass { get; set; } = "";
    }

    public class HvacStandardSizeRow
    {
        public bool   IsEnabled { get; set; } = true;
        public double SizeMm    { get; set; }
    }

    public class HvacSpaceLoadRow
    {
        public bool   IsSelected { get; set; }
        public string SpaceName  { get; set; } = "";
        public string SpaceType  { get; set; } = "";
        public double AreaM2     { get; set; }
        public int    People     { get; set; }
        public double HeatingKw  { get; set; }
        public double CoolingKw  { get; set; }
        public double OAls       { get; set; }
        public string Warning    { get; set; } = "";
    }

    public class HvacSpoolRow
    {
        public bool   IsSelected   { get; set; }
        public string Tag          { get; set; } = "";
        public string System       { get; set; } = "";
        public double LengthM      { get; set; }
        public double WeightKg     { get; set; }
        public int    FittingCount { get; set; }
        public string ShopReady    { get; set; } = "";   // ✔ / ✗
    }

    public class HvacDriftRow
    {
        public string Severity { get; set; } = "";
        public string Element  { get; set; } = "";
        public string WasNow   { get; set; } = "";
        public string Reason   { get; set; } = "";
    }

    public class HvacWorkflowRunRow
    {
        public string Number    { get; set; } = "";
        public string Name      { get; set; } = "";
        public string StatusDot { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }
}
