using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Materials;
using StingTools.Core.Materials.Providers;

namespace StingTools.UI
{
    /// <summary>
    /// Modal browser for PBR texture providers. Left rail = providers
    /// (Poly Haven, ambientCG, Architextures Pro, user-folder, plus any
    /// project-added entries). Centre = thumbnail grid for the active
    /// provider. Bottom = filter strip + download button.
    ///
    /// Result on OK: a fully-ingested <see cref="TexturePackManifest"/>
    /// the caller can pipe straight to <see cref="PbrTextureApplier"/>.
    /// </summary>
    public sealed class MaterialHubProviderBrowserDialog : Window
    {
        private readonly Document _doc;
        private readonly List<IPbrProviderClient> _providers;
        private IPbrProviderClient _activeProvider;
        private CancellationTokenSource _cts;

        private readonly ListBox _providerList = new ListBox();
        private readonly TextBox _searchBox = new TextBox { Width = 240 };
        private readonly ComboBox _categoryCombo = new ComboBox { Width = 160 };
        private readonly ComboBox _resolutionCombo = new ComboBox { Width = 80 };
        private readonly ComboBox _formatCombo = new ComboBox { Width = 80 };
        private readonly WrapPanel _thumbWrap = new WrapPanel { Orientation = Orientation.Horizontal };
        private readonly TextBlock _statusText = new TextBlock { Foreground = Brushes.SlateGray, FontSize = 11 };
        private readonly Button _okButton;
        private readonly ScrollViewer _thumbScroll;

        private PbrAssetSummary _selectedAsset;
        public TexturePackManifest Result { get; private set; }

        public MaterialHubProviderBrowserDialog(Document doc)
        {
            _doc = doc;
            _providers = PbrProviderFactory.AllForDocument(doc).ToList();

            Title = "STING PBR Texture Library";
            Width = 980; Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;

            // ── Layout ────────────────────────────────────────────────
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // toolbar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // status
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // button row

            // Toolbar row
            var tb = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            tb.Children.Add(new TextBlock { Text = "Search:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            tb.Children.Add(_searchBox);
            tb.Children.Add(new TextBlock { Text = "  Category:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            tb.Children.Add(_categoryCombo);
            tb.Children.Add(new TextBlock { Text = "  Resolution:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            foreach (var r in new[] { "1k", "2k", "4k", "8k" }) _resolutionCombo.Items.Add(new ComboBoxItem { Content = r });
            _resolutionCombo.SelectedIndex = 1;
            tb.Children.Add(_resolutionCombo);
            tb.Children.Add(new TextBlock { Text = "  Format:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            foreach (var f in new[] { "png", "jpg", "exr" }) _formatCombo.Items.Add(new ComboBoxItem { Content = f });
            _formatCombo.SelectedIndex = 0;
            tb.Children.Add(_formatCombo);
            var refreshBtn = new Button { Content = "Search", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 2, 12, 2) };
            refreshBtn.Click += (_, __) => _ = LoadAssetsAsync();
            tb.Children.Add(refreshBtn);
            var openSiteBtn = new Button { Content = "Open provider site", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            openSiteBtn.Click += (_, __) => _activeProvider?.OpenBrowser();
            tb.Children.Add(openSiteBtn);
            _searchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = LoadAssetsAsync(); };
            Grid.SetRow(tb, 0); root.Children.Add(tb);

            // Body: provider rail + thumbs
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _providerList.Margin = new Thickness(8, 0, 4, 0);
            _providerList.SelectionChanged += ProviderList_SelectionChanged;
            foreach (var p in _providers)
            {
                _providerList.Items.Add(new ListBoxItem
                {
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = p.DisplayName, FontWeight = FontWeights.SemiBold, FontSize = 12 },
                            new TextBlock
                            {
                                Text = ProviderSubtitle(p),
                                FontSize = 10, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap,
                            },
                        },
                    },
                    Tag = p,
                    Padding = new Thickness(6, 4, 6, 4),
                });
            }
            Grid.SetColumn(_providerList, 0); body.Children.Add(_providerList);

            _thumbScroll = new ScrollViewer
            {
                Content = _thumbWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(4, 0, 8, 0),
                Background = Brushes.White,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
            };
            Grid.SetColumn(_thumbScroll, 1); body.Children.Add(_thumbScroll);

            Grid.SetRow(body, 1); root.Children.Add(body);

            _statusText.Margin = new Thickness(8, 4, 8, 4);
            Grid.SetRow(_statusText, 2); root.Children.Add(_statusText);

            // Button row
            var br = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
            _okButton = new Button { Content = "Download + Apply", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, IsEnabled = false };
            _okButton.Click += async (_, __) => await DownloadAndCloseAsync();
            var cancelBtn = new Button { Content = "Cancel", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            br.Children.Add(_okButton);
            br.Children.Add(cancelBtn);
            Grid.SetRow(br, 3); root.Children.Add(br);

            Content = root;
            Closed += (_, __) => _cts?.Cancel();

            // Default-select the first inline-browsable provider
            int defaultIdx = _providers.FindIndex(p => p.SupportsInlineBrowse);
            if (defaultIdx < 0) defaultIdx = 0;
            if (_providers.Count > 0) _providerList.SelectedIndex = defaultIdx;
        }

        private static string ProviderSubtitle(IPbrProviderClient p)
        {
            switch (p.ProviderId.ToLowerInvariant())
            {
                case "polyhaven":    return "CC0 · free";
                case "ambientcg":    return "CC0 · free";
                case "architextures":return "Subscription · $55/yr";
                case "user-folder":  return "Local drops";
                default: return p.SupportsInlineBrowse ? "Inline browse" : "URL launch";
            }
        }

        private void ProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _activeProvider = (_providerList.SelectedItem as ListBoxItem)?.Tag as IPbrProviderClient;
            PopulateCategoryCombo();
            _thumbWrap.Children.Clear();
            _selectedAsset = null;
            _okButton.IsEnabled = false;
            if (_activeProvider == null) return;

            if (!_activeProvider.SupportsInlineBrowse)
            {
                _statusText.Text = $"{_activeProvider.DisplayName} requires a manual download. Click 'Open provider site', then drop the downloaded pack into _BIM_COORD/textures/ and pick it via 'Apply pack…' in the Inspector.";
                return;
            }

            _ = LoadAssetsAsync();
        }

        private void PopulateCategoryCombo()
        {
            _categoryCombo.Items.Clear();
            _categoryCombo.Items.Add(new ComboBoxItem { Content = "(all categories)", Tag = "" });
            if (_activeProvider == null) return;

            var entry = TextureProviderRegistry.Load(_doc).Providers?.FirstOrDefault(p =>
                string.Equals(p.Id, _activeProvider.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (entry?.Categories != null)
            {
                foreach (var c in entry.Categories)
                    _categoryCombo.Items.Add(new ComboBoxItem { Content = c, Tag = c });
            }
            _categoryCombo.SelectedIndex = 0;
        }

        private async Task LoadAssetsAsync()
        {
            if (_activeProvider == null || !_activeProvider.SupportsInlineBrowse) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            _thumbWrap.Children.Clear();
            _selectedAsset = null;
            _okButton.IsEnabled = false;
            _statusText.Text = "Loading…";

            string category = (_categoryCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            string search = _searchBox.Text?.Trim() ?? "";

            try
            {
                var assets = await _activeProvider.ListAssetsAsync(category, search, 120, ct);
                if (ct.IsCancellationRequested) return;
                if (assets == null || assets.Count == 0)
                {
                    _statusText.Text = "No assets found. Try clearing search or category filter.";
                    return;
                }
                _statusText.Text = $"{assets.Count} assets · {_activeProvider.DisplayName}";

                foreach (var a in assets)
                {
                    var tile = BuildTile(a);
                    _thumbWrap.Children.Add(tile);
                    _ = HydrateThumbAsync(a, tile, ct);
                }
            }
            catch (Exception ex)
            {
                _statusText.Text = "Load failed: " + ex.Message;
                StingLog.Warn($"LoadAssetsAsync: {ex.Message}");
            }
        }

        private Border BuildTile(PbrAssetSummary asset)
        {
            var img = new System.Windows.Controls.Image
            {
                Width = 140, Height = 140,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var label = new TextBlock
            {
                Text = asset.DisplayName ?? asset.Id,
                FontSize = 10, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 2, 4, 2), Width = 140,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                ToolTip = asset.AssetPageUrl,
            };
            var sp = new StackPanel();
            sp.Children.Add(img);
            sp.Children.Add(label);

            var br = new Border
            {
                Child = sp,
                Background = Brushes.White,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(4),
                Padding = new Thickness(2),
                Cursor = Cursors.Hand,
                Tag = asset,
            };
            br.MouseLeftButtonDown += (_, __) =>
            {
                foreach (var c in _thumbWrap.Children.OfType<Border>())
                    c.BorderBrush = Brushes.LightGray;
                br.BorderBrush = Brushes.SteelBlue;
                br.BorderThickness = new Thickness(2);
                _selectedAsset = asset;
                _okButton.IsEnabled = true;
                _statusText.Text = $"{asset.DisplayName} · {asset.ProviderId} · {asset.License} · {(asset.Resolution > 0 ? asset.Resolution + " px" : "—")}";
            };
            return br;
        }

        private async Task HydrateThumbAsync(PbrAssetSummary asset, Border tile, CancellationToken ct)
        {
            try
            {
                string path = await _activeProvider.DownloadThumbnailAsync(asset, ct);
                if (ct.IsCancellationRequested || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                var sp = tile.Child as StackPanel;
                if (!(sp?.Children[0] is System.Windows.Controls.Image img)) return;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();
                img.Source = bi;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("PbrThumb", $"HydrateThumb '{asset?.Id}': {ex.Message}"); }
        }

        private async Task DownloadAndCloseAsync()
        {
            if (_activeProvider == null || _selectedAsset == null) return;
            _okButton.IsEnabled = false;
            _statusText.Text = "Downloading pack…";
            try
            {
                string root = TextureProviderRegistry.ProjectTexturesRoot(_doc);
                if (string.IsNullOrEmpty(root))
                {
                    MessageBox.Show(this, "Save the Revit project first so STING can resolve a per-project _BIM_COORD/ folder.", "STING", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _okButton.IsEnabled = true;
                    return;
                }
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                string resHint = (_resolutionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
                string fmtHint = (_formatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
                var manifest = await _activeProvider.DownloadPackAsync(_selectedAsset, root, resHint, fmtHint, _cts.Token);
                if (manifest == null || manifest.Maps.FilledSlotCount == 0)
                {
                    MessageBox.Show(this, "Download finished but no PBR maps were detected. Check disk permissions and provider connectivity.", "STING", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _okButton.IsEnabled = true;
                    return;
                }
                Result = manifest;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Download failed: " + ex.Message, "STING", MessageBoxButton.OK, MessageBoxImage.Error);
                _okButton.IsEnabled = true;
                StingLog.Warn($"DownloadAndCloseAsync: {ex.Message}");
            }
        }
    }
}
