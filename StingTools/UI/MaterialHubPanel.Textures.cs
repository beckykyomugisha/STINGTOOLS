using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Materials;
using StingTools.Core.Materials.Providers;

namespace StingTools.UI
{
    /// <summary>
    /// MaterialHubPanel — PBR Textures inspector card + dispatch.
    /// Split from <see cref="MaterialHubPanel.Builders"/> for readability;
    /// uses the same `MakeCard`, `KvRow`, and `HUB_*` dispatch conventions.
    /// </summary>
    public partial class MaterialHubPanel
    {
        // 10 slots: baseColor, normal, roughness, metalness, ao,
        // bump, displacement, opacity, emission, anisotropy.
        private static readonly (string Key, string Label)[] PbrSlots =
        {
            ("baseColor",    "Base color"),
            ("normal",       "Normal"),
            ("roughness",    "Roughness"),
            ("metalness",    "Metalness"),
            ("ao",           "AO"),
            ("bump",         "Bump"),
            ("displacement", "Displacement"),
            ("opacity",      "Opacity"),
            ("emission",     "Emission"),
            ("anisotropy",   "Anisotropy"),
        };

        private TexturePackManifest _activePackForInspector;

        internal UIElement BuildPbrTexturesCard(MaterialRow row)
        {
            var sp = new StackPanel();
            var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
            var mat = (row != null && doc != null) ? doc.GetElement(row.Id) as Material : null;

            // ── Header ────────────────────────────────────────────────
            bool isPrism = (doc != null && mat != null) && GenericToPrismConverter.IsPrism(doc, mat);
            var schemaPill = new Border
            {
                Background = isPrism ? Brushes.SeaGreen : Brushes.Goldenrod,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = isPrism ? "Prism (full PBR)" : "Generic (lossy)",
                    Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.SemiBold,
                },
            };
            sp.Children.Add(schemaPill);

            if (!isPrism)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "Generic schema: roughness / metalness / AO will be dropped on apply. Convert to Prism for full 10-slot PBR.",
                    FontSize = 10, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
                });
                var convertActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                convertActions.Children.Add(MakeAction("Convert in place", "HUB_PBR_ConvertInPlace"));
                convertActions.Children.Add(MakeAction("Duplicate → new", "HUB_PBR_DuplicateToPrism"));
                sp.Children.Add(convertActions);
            }

            // ── 10-slot grid ──────────────────────────────────────────
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < PbrSlots.Length; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Read whatever slots the material currently advertises (best-
            // effort — we have a stand-alone reader for base color only).
            string currentBaseColor = MaterialAppearanceActions.ReadCurrentTexturePath(doc, mat);

            for (int i = 0; i < PbrSlots.Length; i++)
            {
                var (key, label) = PbrSlots[i];
                AddSlotRow(grid, i, label, key, key == "baseColor" ? currentBaseColor : null);
            }
            sp.Children.Add(grid);

            // ── UV controls ───────────────────────────────────────────
            sp.Children.Add(new TextBlock { Text = "UV (real-world)", FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            var uv = new Grid();
            uv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            uv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++) uv.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddNumericRow(uv, 0, "Scale X (mm)", "HUB_PBR_UvScaleX", _activePackForInspector?.Defaults?.RealWorldScaleXMm ?? 1000.0);
            AddNumericRow(uv, 1, "Scale Y (mm)", "HUB_PBR_UvScaleY", _activePackForInspector?.Defaults?.RealWorldScaleYMm ?? 1000.0);
            AddNumericRow(uv, 2, "Rotation (°)", "HUB_PBR_UvRotation", _activePackForInspector?.Defaults?.UvRotationDeg ?? 0.0);
            sp.Children.Add(uv);

            // ── Sliders ───────────────────────────────────────────────
            sp.Children.Add(MakeSliderRow("Bump amount",       0, 10, _activePackForInspector?.Defaults?.BumpAmount      ?? 1.0, "HUB_PBR_BumpAmount"));
            sp.Children.Add(MakeSliderRow("Normal intensity",  0, 4,  _activePackForInspector?.Defaults?.NormalIntensity ?? 1.0, "HUB_PBR_NormalIntensity"));

            // ── Displacement toggle ───────────────────────────────────
            var dispRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var dispCheck = new CheckBox
            {
                Content = "Displacement (raytrace only)",
                IsChecked = _activePackForInspector?.Defaults?.DisplacementEnabled ?? false,
                FontSize = 10,
            };
            dispCheck.Checked   += (_, __) => { if (_activePackForInspector?.Defaults != null) _activePackForInspector.Defaults.DisplacementEnabled = true;  Toast("Displacement enabled — slow but accurate. Re-apply pack to commit.", "info"); };
            dispCheck.Unchecked += (_, __) => { if (_activePackForInspector?.Defaults != null) _activePackForInspector.Defaults.DisplacementEnabled = false; };
            dispRow.Children.Add(dispCheck);
            sp.Children.Add(dispRow);

            // ── Action bar ────────────────────────────────────────────
            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            actions.Children.Add(MakeAction("Browse library…", "HUB_PBR_Browse"));
            actions.Children.Add(MakeAction("Apply pack…",     "HUB_PBR_ApplyFolder"));
            actions.Children.Add(MakeAction("Re-apply",         "HUB_PBR_ReApply"));
            actions.Children.Add(MakeAction("Clear maps",       "HUB_PBR_Clear"));
            actions.Children.Add(MakeAction("Open folder",      "HUB_PBR_OpenFolder"));
            sp.Children.Add(actions);

            // Footer caveat — the Realistic view in Revit only approximates
            // PBR; raytraced render shows the true material.
            sp.Children.Add(new TextBlock
            {
                Text = "Realistic view approximates PBR. Raytraced render shows true bump + displacement + AO.",
                FontSize = 9, FontStyle = FontStyles.Italic, Foreground = Brushes.SlateGray,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            });

            return MakeCard("PBR Textures", sp);
        }

        private void AddSlotRow(Grid g, int row, string label, string slotKey, string currentValue)
        {
            var lab = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lab, row); Grid.SetColumn(lab, 0); g.Children.Add(lab);

            bool has = !string.IsNullOrEmpty(currentValue);
            var preview = new Border
            {
                Background = has ? Brushes.AliceBlue : Brushes.WhiteSmoke,
                BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2), Height = 18, Margin = new Thickness(0, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = has ? Path.GetFileName(currentValue) : "—",
                    FontSize = 9, Foreground = has ? Brushes.SteelBlue : Brushes.SlateGray,
                    Margin = new Thickness(4, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = has ? currentValue : null,
                },
            };
            Grid.SetRow(preview, row); Grid.SetColumn(preview, 1); g.Children.Add(preview);

            var btn = new Button
            {
                Content = "Map…", Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 1, 0, 1), FontSize = 10,
                Tag = "HUB_PBR_Slot|" + slotKey,
            };
            btn.Click += HubBtn_Click;
            Grid.SetRow(btn, row); Grid.SetColumn(btn, 2); g.Children.Add(btn);
        }

        private void AddNumericRow(Grid g, int row, string label, string tag, double value)
        {
            var lab = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lab, row); Grid.SetColumn(lab, 0); g.Children.Add(lab);
            var tb = new TextBox
            {
                Text = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 11, Margin = new Thickness(0, 1, 0, 1), Tag = tag,
            };
            tb.LostFocus += (_, __) =>
            {
                if (_activePackForInspector?.Defaults == null) return;
                if (!double.TryParse(tb.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v)) return;
                switch (tag)
                {
                    case "HUB_PBR_UvScaleX":    _activePackForInspector.Defaults.RealWorldScaleXMm = v; break;
                    case "HUB_PBR_UvScaleY":    _activePackForInspector.Defaults.RealWorldScaleYMm = v; break;
                    case "HUB_PBR_UvRotation":  _activePackForInspector.Defaults.UvRotationDeg = v; break;
                }
            };
            Grid.SetRow(tb, row); Grid.SetColumn(tb, 1); g.Children.Add(tb);
        }

        private UIElement MakeSliderRow(string label, double min, double max, double value, string tag)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            var lab = new TextBlock { Text = label, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            var sl = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = (max - min) / 20.0, IsSnapToTickEnabled = false, Tag = tag, Margin = new Thickness(4, 0, 4, 0) };
            var read = new TextBlock { Text = value.ToString("0.##"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            sl.ValueChanged += (_, e) =>
            {
                read.Text = e.NewValue.ToString("0.##");
                if (_activePackForInspector?.Defaults == null) return;
                switch (tag)
                {
                    case "HUB_PBR_BumpAmount":      _activePackForInspector.Defaults.BumpAmount = e.NewValue; break;
                    case "HUB_PBR_NormalIntensity": _activePackForInspector.Defaults.NormalIntensity = e.NewValue; break;
                }
            };
            Grid.SetColumn(lab, 0);  Grid.SetColumn(sl, 1); Grid.SetColumn(read, 2);
            g.Children.Add(lab); g.Children.Add(sl); g.Children.Add(read);
            return g;
        }

        // ── Dispatch ──────────────────────────────────────────────────
        /// <summary>Handle HUB_PBR_* tags. Returns false if the tag isn't ours
        /// so the existing <see cref="HandleHubLocal"/> can deal with it.</summary>
        internal bool HandlePbrTag(string tag, Document doc, MaterialRow row, Material mat)
        {
            if (tag == null || !tag.StartsWith("HUB_PBR_", StringComparison.Ordinal)) return false;
            if (doc == null) { Toast("No active document.", "warn"); return true; }

            try
            {
                switch (tag)
                {
                    case "HUB_PBR_ConvertInPlace":
                        if (mat == null) { Toast("Pick a material first.", "warn"); return true; }
                        DoConvert(doc, row, mat, GenericToPrismConverter.ConvertMode.InPlace);
                        return true;
                    case "HUB_PBR_DuplicateToPrism":
                        if (mat == null) { Toast("Pick a material first.", "warn"); return true; }
                        DoConvert(doc, row, mat, GenericToPrismConverter.ConvertMode.DuplicateMaterial);
                        return true;
                    case "HUB_PBR_Browse":
                        ShowProviderBrowser(doc, mat);
                        return true;
                    case "HUB_PBR_ApplyFolder":
                        ApplyFromFolderPicker(doc, mat);
                        return true;
                    case "HUB_PBR_ReApply":
                        ReApplyActivePack(doc, mat);
                        return true;
                    case "HUB_PBR_Clear":
                        ClearPbrMaps(doc, mat);
                        return true;
                    case "HUB_PBR_OpenFolder":
                        {
                            string root = TextureProviderRegistry.ProjectTexturesRoot(doc);
                            if (string.IsNullOrEmpty(root)) { Toast("Save the project first so STING can resolve _BIM_COORD/.", "warn"); return true; }
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(root) { UseShellExecute = true }); }
                            catch (Exception ex) { Toast($"Open folder failed: {ex.Message}", "error"); }
                            return true;
                        }
                }
                if (tag.StartsWith("HUB_PBR_Slot|", StringComparison.Ordinal))
                {
                    string slotKey = tag.Substring("HUB_PBR_Slot|".Length);
                    PickSingleSlot(doc, row, mat, slotKey);
                    return true;
                }
            }
            catch (Exception ex) { Toast($"PBR dispatch '{tag}': {ex.Message}", "error"); StingLog.Warn($"HandlePbrTag '{tag}': {ex.Message}"); }
            return true;
        }

        private void DoConvert(Document doc, MaterialRow row, Material mat, GenericToPrismConverter.ConvertMode mode)
        {
            using (var t = new Transaction(doc, "STING Convert material to Prism"))
            {
                t.Start();
                var r = GenericToPrismConverter.Convert(doc, mat, mode);
                if (r.Success) t.Commit(); else t.RollBack();
                Toast(r.Note, r.Success ? "ok" : "warn");
                if (r.Success && row != null) RebuildInspector(row);
            }
        }

        private void ShowProviderBrowser(Document doc, Material mat)
        {
            if (mat == null) { Toast("Pick a material first.", "warn"); return; }
            var dlg = new MaterialHubProviderBrowserDialog(doc);
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                _activePackForInspector = dlg.Result;
                ApplyManifest(doc, mat, _activePackForInspector);
            }
        }

        private void ApplyFromFolderPicker(Document doc, Material mat)
        {
            if (mat == null) { Toast("Pick a material first.", "warn"); return; }
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick any file in the pack folder (folder is what counts)",
                CheckFileExists = false, Multiselect = false,
                Filter = "Any file|*.*",
            };
            string root = TextureProviderRegistry.ProjectTexturesRoot(doc);
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) ofd.InitialDirectory = root;
            if (ofd.ShowDialog() != true) return;

            string folder = Path.GetDirectoryName(ofd.FileName);
            if (string.IsNullOrEmpty(folder)) { Toast("Couldn't resolve folder.", "warn"); return; }

            var rules = TextureProviderRegistry.SuffixRulesFor(doc);
            var m = TexturePackIngester.LoadOrIngest(folder, providerId: "user-folder", suffixRules: rules);
            if (m == null || m.Maps.FilledSlotCount == 0)
            {
                Toast("No PBR maps detected. Check that files follow standard suffix conventions (_basecolor, _normal, etc.).", "warn");
                return;
            }
            _activePackForInspector = m;
            ApplyManifest(doc, mat, m);
        }

        private void ReApplyActivePack(Document doc, Material mat)
        {
            if (mat == null) { Toast("Pick a material first.", "warn"); return; }
            if (_activePackForInspector == null) { Toast("No active pack — pick a pack first.", "warn"); return; }
            ApplyManifest(doc, mat, _activePackForInspector);
        }

        private void ApplyManifest(Document doc, Material mat, TexturePackManifest m)
        {
            if (m == null) return;
            try
            {
                if (!GenericToPrismConverter.IsPrism(doc, mat))
                {
                    var dr = MessageBox.Show(Window.GetWindow(this),
                        $"'{mat.Name}' uses the legacy Generic appearance schema, which can't hold the full PBR pack ({m.Maps.FilledSlotCount} maps). " +
                        "Yes  → convert in-place (mutates the appearance asset; may affect other materials that share it).\n" +
                        "No   → duplicate the material to a new Prism copy and apply there.\n" +
                        "Cancel → abort.",
                        "Generic → Prism conversion", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (dr == MessageBoxResult.Cancel) return;

                    Material target = mat;
                    using (var t = new Transaction(doc, "STING PBR convert + apply"))
                    {
                        t.Start();
                        var conv = GenericToPrismConverter.Convert(doc, mat,
                            dr == MessageBoxResult.Yes
                                ? GenericToPrismConverter.ConvertMode.InPlace
                                : GenericToPrismConverter.ConvertMode.DuplicateMaterial);
                        if (!conv.Success) { t.RollBack(); Toast(conv.Note, "error"); return; }
                        target = conv.ResultMaterial;
                        var ar = PbrTextureApplier.Apply(doc, target, m);
                        if (ar.Success) t.Commit(); else t.RollBack();
                        Toast(ar.Success
                            ? $"Applied {ar.SlotsWritten} maps ({ar.SchemaUsed}) → {target.Name}"
                            : "Apply failed: " + string.Join("; ", ar.Warnings),
                            ar.Success ? "ok" : "error");
                    }
                    return;
                }

                using (var t = new Transaction(doc, "STING PBR apply"))
                {
                    t.Start();
                    var r = PbrTextureApplier.Apply(doc, mat, m);
                    if (r.Success) t.Commit(); else t.RollBack();
                    Toast(r.Success
                        ? $"Applied {r.SlotsWritten} maps ({r.SchemaUsed})."
                        : "Apply failed: " + string.Join("; ", r.Warnings),
                        r.Success ? "ok" : "error");
                }
            }
            catch (Exception ex) { Toast($"Apply failed: {ex.Message}", "error"); StingLog.Warn($"ApplyManifest: {ex.Message}"); }
        }

        private void PickSingleSlot(Document doc, MaterialRow row, Material mat, string slotKey)
        {
            if (mat == null) { Toast("Pick a material first.", "warn"); return; }
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Pick {slotKey} image",
                Filter = "Image|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.exr;*.hdr;*.tga;*.bmp",
            };
            string root = TextureProviderRegistry.ProjectTexturesRoot(doc);
            if (!string.IsNullOrEmpty(root)) ofd.InitialDirectory = root;
            if (ofd.ShowDialog() != true) return;

            // Build a single-slot manifest and apply it.
            var m = new TexturePackManifest
            {
                PackId = "single-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                DisplayName = slotKey + " (single map)",
                ProviderId = "user-folder",
                License = "varies",
                Maps = new TexturePackMaps(),
                Defaults = _activePackForInspector?.Defaults ?? new TexturePackDefaults(),
            };
            switch (slotKey)
            {
                case "baseColor":    m.Maps.BaseColor = ofd.FileName; break;
                case "normal":       m.Maps.Normal = ofd.FileName; break;
                case "roughness":    m.Maps.Roughness = ofd.FileName; break;
                case "metalness":    m.Maps.Metalness = ofd.FileName; break;
                case "ao":           m.Maps.Ao = ofd.FileName; break;
                case "bump":         m.Maps.Bump = ofd.FileName; break;
                case "displacement": m.Maps.Displacement = ofd.FileName; m.Defaults.DisplacementEnabled = true; break;
                case "opacity":      m.Maps.Opacity = ofd.FileName; break;
                case "emission":     m.Maps.Emission = ofd.FileName; break;
                case "anisotropy":   m.Maps.Anisotropy = ofd.FileName; break;
            }
            ApplyManifest(doc, mat, m);
            if (row != null) RebuildInspector(row);
        }

        private void ClearPbrMaps(Document doc, Material mat)
        {
            if (mat == null) { Toast("Pick a material first.", "warn"); return; }
            try
            {
                int cleared;
                using (var t = new Transaction(doc, "STING PBR clear"))
                {
                    t.Start();
                    cleared = PbrTextureApplier.ClearAllSlots(doc, mat);
                    if (cleared > 0) t.Commit(); else t.RollBack();
                }
                Toast(cleared > 0
                    ? $"Disconnected {cleared} PBR slot(s) from '{mat.Name}'."
                    : "Nothing to clear (no connected bitmaps).",
                    cleared > 0 ? "ok" : "info");
            }
            catch (Exception ex) { Toast($"Clear failed: {ex.Message}", "error"); }
        }
    }
}
