using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using StingTools.Commands.Electrical.Photometric;
using StingTools.Core;
using StingTools.Photometrics;

namespace StingTools.UI
{
    /// <summary>
    /// Modal photometric-library viewer + assigner. The grid is populated
    /// in a background scan; each row is a <see cref="PhotometricRowVm"/>
    /// wrapping the parsed <see cref="PhotometricFile"/>. The Assign button
    /// dispatches through the IExternalEventHandler so the actual write
    /// runs on the Revit API thread.
    /// </summary>
    public partial class PhotometricLibraryDialog : Window
    {
        public ObservableCollection<PhotometricRowVm> Rows { get; }
            = new ObservableCollection<PhotometricRowVm>();

        private readonly PhotometricLibrary _library;
        private readonly Document _doc;
        private List<PhotometricRowVm> _allRows = new List<PhotometricRowVm>();

        public PhotometricLibraryDialog(PhotometricLibrary library, Document doc)
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            _library = library;
            _doc = doc;
            LibraryGrid.ItemsSource = Rows;
            UpdateRootSummary();
            RescanInBackground();
        }

        private void UpdateRootSummary()
        {
            try
            {
                int n = _library?.RootPaths?.Count ?? 0;
                txtRootSummary.Text = n == 0 ? "no root configured"
                    : n == 1 ? $"1 root: {_library.RootPaths[0]}"
                    : $"{n} roots configured";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private void RescanInBackground()
        {
            StatusLabel.Text = "Scanning…";
            Rows.Clear();
            _allRows.Clear();
            // Run synchronously — Phase 180 keeps the dialog modal and the
            // file-system scan is fast (a few hundred files in <500 ms).
            try
            {
                var files = _library.LoadAll();
                _allRows = files.Select(f => new PhotometricRowVm(f)).ToList();
                ApplyFilter();
                StatusLabel.Text = $"{_allRows.Count} luminaire(s)";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PhotometricLibrary scan: {ex.Message}");
                StatusLabel.Text = "Scan failed — see StingTools.log";
            }
        }

        private void ApplyFilter()
        {
            string f = (txtSearch?.Text ?? "").Trim();
            Rows.Clear();
            IEnumerable<PhotometricRowVm> q = _allRows;
            if (!string.IsNullOrEmpty(f))
                q = _allRows.Where(r => r.MatchesFilter(f));
            foreach (var r in q.Take(2000)) Rows.Add(r);
        }

        // ── event handlers ──────────────────────────────────────────────

        private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void Rescan_Click(object sender, RoutedEventArgs e)
        {
            PhotometricLibrary.InvalidateCache();
            RescanInBackground();
        }

        private void AddRoot_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick any IES/LDT file in the new library root",
                Filter = "Photometric files (*.ies;*.ldt)|*.ies;*.ldt|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog() != true) return;
            string root = Path.GetDirectoryName(ofd.FileName) ?? "";
            if (string.IsNullOrEmpty(root)) return;
            _library.AddRoot(root);
            try
            {
                var roots = PhotometricLibraryCommand.LoadRoots(_doc);
                if (!roots.Any(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase)))
                {
                    roots.Add(root);
                    PhotometricLibraryCommand.SaveRoots(_doc, roots);
                }
            }
            catch (Exception ex) { StingLog.Warn($"AddRoot persist: {ex.Message}"); }
            UpdateRootSummary();
            RescanInBackground();
        }

        private void Library_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = LibraryGrid.SelectedItem as PhotometricRowVm;
            if (row == null)
            {
                txtDetailHeader.Text = "Select a row above";
                txtDetailPath.Text = "";
                txtDetailMetrics.Text = "";
                txtDetailWarnings.Text = "";
                btnAssign.IsEnabled = false;
                return;
            }
            txtDetailHeader.Text = row.Header;
            txtDetailPath.Text = row.Source.FilePath ?? "";
            txtDetailMetrics.Text =
                $"Lumens: {row.Source.TotalLumens:0}  ·  Watts: {row.Source.TotalWatts:0.0}  ·  " +
                $"Efficacy: {row.Source.Efficacy:0.0} lm/W\n" +
                $"Beam angle: {row.Source.BeamAngleDeg:0.0}°  ·  Field angle: {row.Source.FieldAngleDeg:0.0}°  ·  " +
                $"Symmetry: {row.Source.Symmetry}\n" +
                $"Lamp count: {row.Source.LampCount}  ·  CCT: {(row.Source.CCT > 0 ? row.Source.CCT.ToString("0") + " K" : "—")}  ·  " +
                $"CRI: {(row.Source.CRI > 0 ? row.Source.CRI.ToString("0") : "—")}\n" +
                $"Dimensions: {row.Source.WidthM * 1000:0} × {row.Source.LengthM * 1000:0} × {row.Source.HeightM * 1000:0} mm";
            txtDetailWarnings.Text = row.Source.Warnings.Count == 0 ? ""
                : "⚠ " + string.Join(" · ", row.Source.Warnings);
            UpdateAssignTargetsLabel();
            btnAssign.IsEnabled = true;
        }

        private void UpdateAssignTargetsLabel()
        {
            try
            {
                var ids = StingTools.UI.StingElectricalCommandHandler.ActivePanel != null
                    ? CollectSelectedFixtureTypeIds()
                    : new List<ElementId>();
                txtAssignTargets.Text = ids.Count == 0
                    ? "(no fixtures selected — pick one or more lighting fixtures in the model and click Assign)"
                    : $"{ids.Count} luminaire type(s) will receive the photometric data";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private List<ElementId> CollectSelectedFixtureTypeIds()
        {
            var typeIds = new List<ElementId>();
            try
            {
                var uiapp = StingTools.UI.StingCommandHandler.CurrentApp;
                var sel = uiapp?.ActiveUIDocument?.Selection?.GetElementIds();
                if (sel == null) return typeIds;
                foreach (var id in sel)
                {
                    var el = _doc.GetElement(id);
                    if (el is FamilyInstance fi
                        && fi.Category?.Id?.Value == (long)BuiltInCategory.OST_LightingFixtures)
                    {
                        var t = fi.GetTypeId();
                        if (t != null && t != ElementId.InvalidElementId && !typeIds.Contains(t))
                            typeIds.Add(t);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"CollectSelectedFixtureTypeIds: {ex.Message}"); }
            return typeIds;
        }

        private void Assign_Click(object sender, RoutedEventArgs e)
        {
            var row = LibraryGrid.SelectedItem as PhotometricRowVm;
            if (row == null) return;
            AssignPhotometricCommand.PendingFile = row.Source;
            AssignPhotometricCommand.PendingTargetTypeIds = CollectSelectedFixtureTypeIds();
            try { StingTools.UI.StingElectricalCommandHandler.Instance?.SetCommand("Photo_Assign"); }
            catch (Exception ex) { StingLog.Warn($"Assign dispatch: {ex.Message}"); }
            StatusLabel.Text = $"Assigning '{row.Header}'…";
        }

        private void Preflight_Click(object sender, RoutedEventArgs e)
        {
            try { StingTools.UI.StingElectricalCommandHandler.Instance?.SetCommand("Photo_Preflight"); }
            catch (Exception ex) { StingLog.Warn($"Preflight dispatch: {ex.Message}"); }
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }

    public class PhotometricRowVm
    {
        public PhotometricFile Source { get; }
        public PhotometricRowVm(PhotometricFile src) { Source = src; }
        public string FileName        => Path.GetFileName(Source.FilePath ?? "");
        public string FileFormat      => Source.FileFormat;
        public string Manufacturer    => Source.Manufacturer;
        public string LuminaireName   => Source.LuminaireName;
        public string LumensDisplay   => Source.TotalLumens > 0 ? $"{Source.TotalLumens:0}" : "—";
        public string WattsDisplay    => Source.TotalWatts  > 0 ? $"{Source.TotalWatts:0.0}" : "—";
        public string EfficacyDisplay => Source.Efficacy    > 0 ? $"{Source.Efficacy:0.0}"   : "—";
        public string BeamDisplay     => Source.BeamAngleDeg > 0 ? $"{Source.BeamAngleDeg:0.0}" : "—";
        public string CctDisplay      => Source.CCT > 0 ? $"{Source.CCT:0}" : "—";
        public string CriDisplay      => Source.CRI > 0 ? $"{Source.CRI:0}" : "—";
        public string Symmetry        => Source.Symmetry ?? "";
        public string Header
        {
            get
            {
                string nm = string.IsNullOrEmpty(LuminaireName) ? FileName : LuminaireName;
                return string.IsNullOrEmpty(Manufacturer) ? nm : $"{Manufacturer} — {nm}";
            }
        }
        public bool MatchesFilter(string f)
        {
            if (string.IsNullOrEmpty(f)) return true;
            string blob = ($"{Manufacturer} {LuminaireName} {FileName}").ToLowerInvariant();
            return blob.Contains(f.ToLowerInvariant());
        }
    }
}
