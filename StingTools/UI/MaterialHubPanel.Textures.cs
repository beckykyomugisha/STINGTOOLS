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
using StingTools.Core.Storage;
using Grid = System.Windows.Controls.Grid;     // disambiguate vs Autodesk.Revit.DB.Grid

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

        // Per-material working-copy state — keyed by (document path, ElementId)
        // so two open documents never collide. UV / slider / displacement
        // toggle edits live here in memory; persistence to disk uses
        // StingPbrStateSchema (extensible storage on the Material element),
        // committed only on successful Apply.
        private static readonly Dictionary<(string DocKey, long ElementId), TexturePackManifest> _packStatePerMaterial
            = new Dictionary<(string, long), TexturePackManifest>();
        private static readonly object _packStateLock = new object();

        private static (string, long) PackStateKey(Document doc, Material mat)
            => ((doc?.PathName ?? "<no-path>"), (mat?.Id?.Value ?? 0));

        // Session-scoped memory of the user's last Generic→Prism decision so
        // we don't ask the same question every apply. Reset on plugin reload.
        private static System.Windows.MessageBoxResult _lastGenericToPrismChoice
            = System.Windows.MessageBoxResult.None;

        private TexturePackManifest _activePackForInspector;

        internal UIElement BuildPbrTexturesCard(MaterialRow row)
        {
            var sp = new StackPanel();
            var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
            var mat = (row != null && doc != null) ? doc.GetElement(row.Id) as Material : null;

            // Hydrate the active pack from (doc-scoped) memory first; fall
            // back to extensible-storage on the Material so state survives
            // a Revit restart / project re-open. A fresh project gets a
            // null pack and the card uses TexturePackDefaults values.
            _activePackForInspector = null;
            if (mat != null)
            {
                var key = PackStateKey(doc, mat);
                lock (_packStateLock)
                    _packStatePerMaterial.TryGetValue(key, out _activePackForInspector);
                if (_activePackForInspector == null)
                {
                    _activePackForInspector = StingPbrStateSchema.Read(mat);
                    if (_activePackForInspector != null)
                        lock (_packStateLock) _packStatePerMaterial[key] = _activePackForInspector;
                }
            }

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
                if (_lastGenericToPrismChoice != MessageBoxResult.None)
                    convertActions.Children.Add(MakeAction("Choose differently…", "HUB_PBR_ResetDecision"));
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
                AddSlotRow(grid, i, label, key, key == "baseColor" ? currentBaseColor : null, doc, row, mat);
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
            // Only meaningful when the active pack carries a displacement
            // map. Disable the checkbox + grey out the label when there's
            // nothing to displace, so the toggle never claims to be doing
            // something the apply path would silently skip.
            bool hasDispMap = !string.IsNullOrEmpty(_activePackForInspector?.Maps?.Displacement);
            var dispRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var dispCheck = new CheckBox
            {
                Content = hasDispMap
                    ? "Displacement (raytrace only)"
                    : "Displacement (no map in pack)",
                IsChecked = hasDispMap && (_activePackForInspector?.Defaults?.DisplacementEnabled ?? false),
                IsEnabled = hasDispMap,
                FontSize = 10,
                Foreground = hasDispMap ? Brushes.Black : Brushes.Gray,
                ToolTip = hasDispMap
                    ? "Adds true geometric displacement on raytraced render. Slow (30–120 s/frame) but accurate."
                    : "The active pack doesn't include a displacement map. Pick a pack with a _disp / _height file to enable.",
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

        private void AddSlotRow(Grid g, int row, string label, string slotKey, string currentValue,
            Document doc = null, MaterialRow matRow = null, Material mat = null)
        {
            var lab = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lab, row); Grid.SetColumn(lab, 0); g.Children.Add(lab);

            bool has = !string.IsNullOrEmpty(currentValue);
            var idleBrush = has ? Brushes.AliceBlue : Brushes.WhiteSmoke;
            var preview = new Border
            {
                Background = idleBrush,
                BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2), Height = 18, Margin = new Thickness(0, 1, 4, 1),
                AllowDrop = true,
                ToolTip = (has ? currentValue + "\n\n" : "") + "Drop an image to set this map, or a folder / multiple files to auto-assign by name.",
                Child = new TextBlock
                {
                    Text = has ? Path.GetFileName(currentValue) : "— drop map —",
                    FontSize = 9, Foreground = has ? Brushes.SteelBlue : Brushes.SlateGray,
                    Margin = new Thickness(4, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            // ── Drag-and-drop wiring (visual feedback + apply) ──
            void SetDragEffect(DragEventArgs ev)
            {
                bool ok = ev.Data.GetDataPresent(DataFormats.FileDrop);
                ev.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
                ev.Handled = true;
            }
            preview.DragEnter += (s, ev) =>
            {
                SetDragEffect(ev);
                if (ev.Effects == DragDropEffects.Copy)
                {
                    preview.BorderBrush = Brushes.DodgerBlue;
                    preview.BorderThickness = new Thickness(2);
                    preview.Background = Brushes.LightCyan;
                }
            };
            preview.DragOver += (s, ev) => SetDragEffect(ev);
            void RestoreIdle()
            {
                preview.BorderBrush = Brushes.LightGray;
                preview.BorderThickness = new Thickness(1);
                preview.Background = idleBrush;
            }
            preview.DragLeave += (s, ev) => RestoreIdle();
            preview.Drop += (s, ev) =>
            {
                RestoreIdle();
                ev.Handled = true;
                OnSlotDrop(doc, matRow, mat, slotKey, ev);
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
                    case "HUB_PBR_ResetDecision":
                        ResetGenericToPrismDecision();
                        if (row != null) RebuildInspector(row);
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
            var siblingIds = (mode == GenericToPrismConverter.ConvertMode.InPlace)
                ? FindMaterialsSharingAppearance(doc, mat).ToList()
                : new List<ElementId>();

            using (var t = new Transaction(doc, "STING Convert material to Prism"))
            {
                t.Start();
                var r = GenericToPrismConverter.Convert(doc, mat, mode);
                if (r.Success) t.Commit(); else t.RollBack();
                Toast(r.Note, r.Success ? "ok" : "warn");
                if (!r.Success) return;

                // Bring cache/feed/grid into sync with the new appearance state.
                try { MaterialNameCache.Invalidate(doc); } catch { /* non-fatal */ }
                try { MaterialUsageIndex.Invalidate(doc); } catch { /* non-fatal */ }
                try { MaterialActivityFeed.Add("MAT_PrismConvert", r.ResultMaterial?.Name ?? mat.Name,
                    mode == GenericToPrismConverter.ConvertMode.DuplicateMaterial ? "duplicated → Prism" : "converted in place"); }
                catch { /* non-fatal */ }

                if (mode == GenericToPrismConverter.ConvertMode.DuplicateMaterial && r.ResultMaterial != null)
                    InsertOrReloadRowForMaterial(r.ResultMaterial);
                else
                {
                    ReloadSingleRow(mat.Id);
                    foreach (var id in siblingIds) ReloadSingleRow(id);
                }

                if (row != null) RebuildInspector(row);
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
                // Per-material gate veto — lifecycle Frozen, healthcare-block
                // findings, anything a project plugs in via BlockerChain.
                var (allow, reason) = MaterialBlockerChain.CheckPbrApply(doc, mat, m.PackId);
                if (!allow) { Toast(reason ?? "Blocked by gate.", "warn"); return; }

                if (!GenericToPrismConverter.IsPrism(doc, mat))
                {
                    var dr = ResolveGenericToPrismChoice(mat, m);
                    if (dr == MessageBoxResult.Cancel) return;

                    // Snapshot peer materials sharing this asset BEFORE
                    // conversion. After Convert(InPlace), target's
                    // AppearanceAssetId points at a NEW asset — the siblings
                    // still point at the ORIGINAL asset, which is now
                    // unmodified. So we don't refresh the siblings' rows;
                    // their visual state is correct. We only need to refresh
                    // the target row. The snapshot stays for diagnostic /
                    // future-policy hooks but is no longer consumed for
                    // grid refresh (this fixes Phase 190 Finding 12).
                    var siblingIds = (dr == MessageBoxResult.Yes)
                        ? FindMaterialsSharingAppearance(doc, mat).ToList()
                        : new List<ElementId>();
                    if (siblingIds.Count > 0)
                        StingLog.Info($"PBR convert (InPlace): {siblingIds.Count} sibling material(s) keep original asset → no refresh needed");

                    Material target = mat;
                    bool createdNewMaterial = (dr != MessageBoxResult.Yes);
                    PbrTextureApplier.ApplyResult ar = null;
                    bool committed = false;
                    using (var t = new Transaction(doc, "STING PBR convert + apply"))
                    {
                        t.Start();
                        var conv = GenericToPrismConverter.Convert(doc, mat,
                            dr == MessageBoxResult.Yes
                                ? GenericToPrismConverter.ConvertMode.InPlace
                                : GenericToPrismConverter.ConvertMode.DuplicateMaterial);
                        if (!conv.Success) { t.RollBack(); Toast(conv.Note, "error"); return; }
                        target = conv.ResultMaterial;
                        ar = PbrTextureApplier.Apply(doc, target, m);
                        if (ar.Success)
                        {
                            // ES write must happen inside the transaction.
                            PersistPackState(doc, target, m);
                            t.Commit();
                            committed = true;
                        }
                        else t.RollBack();
                    }

                    Toast(ar?.Success == true
                        ? $"Applied {ar.SlotsWritten} maps ({ar.SchemaUsed}) → {target?.Name}"
                        : "Apply failed: " + string.Join("; ", ar?.Warnings ?? new List<string>()),
                        ar?.Success == true ? "ok" : "error");

                    if (committed)
                    {
                        // Fire deferred side effects only after a clean commit.
                        try { ar?.PostCommit?.Invoke(doc, target); } catch (Exception ex) { StingLog.Warn($"PostCommit: {ex.Message}"); }
                        if (createdNewMaterial) InsertOrReloadRowForMaterial(target);
                        ReloadSingleRow(target?.Id);
                    }
                    return;
                }

                PbrTextureApplier.ApplyResult r = null;
                bool prismCommitted = false;
                using (var t = new Transaction(doc, "STING PBR apply"))
                {
                    t.Start();
                    r = PbrTextureApplier.Apply(doc, mat, m);
                    if (r.Success)
                    {
                        PersistPackState(doc, mat, m);
                        t.Commit();
                        prismCommitted = true;
                    }
                    else t.RollBack();
                }

                Toast(r?.Success == true
                    ? $"Applied {r.SlotsWritten} maps ({r.SchemaUsed})."
                    : "Apply failed: " + string.Join("; ", r?.Warnings ?? new List<string>()),
                    r?.Success == true ? "ok" : "error");

                if (prismCommitted)
                {
                    try { r?.PostCommit?.Invoke(doc, mat); } catch (Exception ex) { StingLog.Warn($"PostCommit: {ex.Message}"); }
                    ReloadSingleRow(mat.Id);
                }
            }
            catch (Exception ex) { Toast($"Apply failed: {ex.Message}", "error"); StingLog.Warn($"ApplyManifest: {ex.Message}"); }
        }

        /// <summary>Reuse the user's last Generic→Prism decision for this
        /// session; only prompt when they've never picked. Reset via the
        /// schema pill's "Choose differently…" button.</summary>
        private MessageBoxResult ResolveGenericToPrismChoice(Material mat, TexturePackManifest m)
        {
            if (_lastGenericToPrismChoice == MessageBoxResult.Yes ||
                _lastGenericToPrismChoice == MessageBoxResult.No)
                return _lastGenericToPrismChoice;

            var dr = MessageBox.Show(Window.GetWindow(this) ?? Application.Current?.MainWindow,
                $"'{mat.Name}' uses the legacy Generic appearance schema, which can't hold the full PBR pack ({m.Maps.FilledSlotCount} maps). " +
                "Yes  → convert in-place (mutates the appearance asset; may affect other materials that share it).\n" +
                "No   → duplicate the material to a new Prism copy and apply there.\n" +
                "Cancel → abort.\n\n" +
                "(STING remembers your answer for the rest of this session. " +
                "Use 'Choose differently…' on the schema pill to reset.)",
                "Generic → Prism conversion", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (dr != MessageBoxResult.Cancel) _lastGenericToPrismChoice = dr;
            return dr;
        }

        /// <summary>Materials whose <see cref="Material.AppearanceAssetId"/>
        /// equals <paramref name="mat"/>'s. Empty list when nothing shared.</summary>
        private static IEnumerable<ElementId> FindMaterialsSharingAppearance(Document doc, Material mat)
        {
            if (doc == null || mat?.AppearanceAssetId == null || mat.AppearanceAssetId.Value <= 0) yield break;
            long targetAsset = mat.AppearanceAssetId.Value;
            foreach (Material m in new FilteredElementCollector(doc).OfClass(typeof(Material)))
            {
                if (m.AppearanceAssetId != null && m.AppearanceAssetId.Value == targetAsset && m.Id.Value != mat.Id.Value)
                    yield return m.Id;
            }
        }

        /// <summary>Commit per-material PBR state both to in-memory cache
        /// (fast re-open) and to extensible storage (survives Revit restart).
        /// Must be called inside an open Revit transaction so the ES write
        /// commits atomically with the rest of the Apply.</summary>
        private static void PersistPackState(Document doc, Material mat, TexturePackManifest m)
        {
            if (mat == null || m == null) return;
            try
            {
                lock (_packStateLock) _packStatePerMaterial[PackStateKey(doc, mat)] = m;
            }
            catch { /* non-fatal */ }
            try { StingPbrStateSchema.Write(mat, m); }
            catch (Exception ex) { StingLog.WarnRateLimited("PbrStateWrite", $"PersistPackState: {ex.Message}"); }
        }

        /// <summary>Drop a document's worth of cache entries — wired to
        /// <see cref="MaterialHubPanel"/>'s document-close hook to prevent
        /// long-running sessions from accumulating dead entries.</summary>
        internal static void DropDocumentCache(Document doc)
        {
            if (doc == null) return;
            string key = doc.PathName ?? "<no-path>";
            lock (_packStateLock)
            {
                var dead = new List<(string, long)>();
                foreach (var k in _packStatePerMaterial.Keys)
                    if (string.Equals(k.DocKey, key, StringComparison.OrdinalIgnoreCase))
                        dead.Add(k);
                foreach (var k in dead) _packStatePerMaterial.Remove(k);
            }
        }

        /// <summary>Insert a freshly-created Material into the grid (no F5
        /// required) or refresh its row if it already exists.</summary>
        private void InsertOrReloadRowForMaterial(Material mat)
        {
            try
            {
                if (mat == null || _rows == null) return;
                var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc == null) return;
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i].Id?.Value == mat.Id.Value)
                    {
                        _rows[i] = MaterialRowBuilder.BuildOne(doc, mat);
                        return;
                    }
                }
                // Not present → append; sort key preserved by Refresh later.
                _rows.Add(MaterialRowBuilder.BuildOne(doc, mat));
            }
            catch (Exception ex) { StingLog.Warn($"InsertOrReloadRowForMaterial: {ex.Message}"); }
        }

        /// <summary>Public reset of the Generic→Prism decision memory — wired
        /// to the 'Choose differently…' link in the inspector.</summary>
        internal void ResetGenericToPrismDecision()
        {
            _lastGenericToPrismChoice = MessageBoxResult.None;
            Toast("Will prompt again on the next Generic-material apply.", "info");
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

            ApplyManifest(doc, mat, BuildSingleSlotManifest(slotKey, ofd.FileName));
            if (row != null) RebuildInspector(row);
        }

        /// <summary>Build a one-slot manifest assigning <paramref name="filePath"/>
        /// to <paramref name="slotKey"/>. Shared by the "Map…" picker and the
        /// per-slot drag-drop handler.</summary>
        private TexturePackManifest BuildSingleSlotManifest(string slotKey, string filePath)
        {
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
                case "baseColor":    m.Maps.BaseColor = filePath; break;
                case "normal":       m.Maps.Normal = filePath; break;
                case "roughness":    m.Maps.Roughness = filePath; break;
                case "metalness":    m.Maps.Metalness = filePath; break;
                case "ao":           m.Maps.Ao = filePath; break;
                case "bump":         m.Maps.Bump = filePath; break;
                case "displacement": m.Maps.Displacement = filePath; m.Defaults.DisplacementEnabled = true; break;
                case "opacity":      m.Maps.Opacity = filePath; break;
                case "emission":     m.Maps.Emission = filePath; break;
                case "anisotropy":   m.Maps.Anisotropy = filePath; break;
            }
            return m;
        }

        /// <summary>
        /// Drop handler for a PBR map slot. A single image → that slot; a
        /// folder or multi-file selection → auto-assigned by filename
        /// convention (reuses TexturePackIngester). Re-applies + refreshes.
        /// </summary>
        private void OnSlotDrop(Document doc, MaterialRow row, Material mat, string slotKey, DragEventArgs e)
        {
            try
            {
                if (mat == null) { Toast("Pick a material first.", "warn"); return; }
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0) return;

                TexturePackManifest m;
                if (paths.Length == 1 && Directory.Exists(paths[0]))
                {
                    m = TexturePackIngester.ReIngest(paths[0], providerId: "user-drop");
                    if (m == null) { Toast("No recognised PBR maps in that folder.", "warn"); return; }
                }
                else if (paths.Length > 1)
                {
                    m = TexturePackIngester.BuildFromFiles(paths, "user-drop");
                    if (m == null) { Toast("No recognised PBR maps in the dropped files.", "warn"); return; }
                }
                else
                {
                    if (!TexturePackIngester.IsImageFile(paths[0]))
                    { Toast("Drop an image file (png/jpg/tga/exr…).", "warn"); return; }
                    m = BuildSingleSlotManifest(slotKey, paths[0]);
                }
                if (m.Defaults == null) m.Defaults = _activePackForInspector?.Defaults ?? new TexturePackDefaults();

                ApplyManifest(doc, mat, m);
                if (row != null) RebuildInspector(row);
                Toast($"Applied {m.Maps.FilledSlotCount} map(s) to '{mat.Name}'.", "ok");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PBR slot drop: {ex.Message}");
                Toast("Drop failed: " + ex.Message, "warn");
            }
        }

        private void ClearPbrMaps(Document doc, Material mat)
        {
            if (mat == null) { Toast("Pick a material first.", "warn"); return; }
            try
            {
                PbrTextureApplier.ClearResult cr = null;
                bool committed = false;
                using (var t = new Transaction(doc, "STING PBR clear"))
                {
                    t.Start();
                    cr = PbrTextureApplier.ClearAllSlotsWithResult(doc, mat);
                    if (cr.SlotsCleared > 0) { t.Commit(); committed = true; }
                    else t.RollBack();
                }
                Toast(cr?.SlotsCleared > 0
                    ? $"Disconnected {cr.SlotsCleared} PBR slot(s) from '{mat.Name}'."
                    : "Nothing to clear (no connected bitmaps).",
                    cr?.SlotsCleared > 0 ? "ok" : "info");
                if (committed)
                {
                    try { cr?.PostCommit?.Invoke(doc, mat); } catch (Exception ex) { StingLog.Warn($"Clear PostCommit: {ex.Message}"); }
                    // Also wipe the in-memory + ES state so the inspector
                    // doesn't re-hydrate a stale pack on next selection.
                    lock (_packStateLock) _packStatePerMaterial.Remove(PackStateKey(doc, mat));
                    ReloadSingleRow(mat.Id);
                }
            }
            catch (Exception ex) { Toast($"Clear failed: {ex.Message}", "error"); }
        }
    }
}
