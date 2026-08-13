using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Copilot Settings — a WPF dialog for editing STING_LLM_CONFIG.json without hand-
    /// editing JSON. Mirrors the MCP Server toggle's clobber-safe persistence: it loads
    /// the whole JObject, sets ONLY the LLM fields (enabled / provider / claude_* /
    /// azure_*), and writes it back indented so mcp_* keys and notes are preserved.
    ///
    /// Security: the API key lives in a masked PasswordBox and is NEVER written to the log.
    /// Test connection uses the dialog's CURRENT field values (not the saved config) so the
    /// user can verify before saving. On Save, StingLlmService.ReloadConfig() picks up the
    /// change live — no Revit restart required.
    /// </summary>
    public class StingLlmConfigDialog : Window
    {
        private static readonly Color BrandPurple = Color.FromRgb(88, 44, 131);
        private static readonly Color BrandPurpleLight = Color.FromRgb(206, 147, 216);
        private static readonly Color TextPrimary = Color.FromRgb(40, 40, 50);
        private static readonly Color TextSecondary = Color.FromRgb(120, 120, 140);
        private static readonly Color FieldBorder = Color.FromRgb(200, 200, 210);

        private static readonly string[] ClaudeModels =
            { "claude-haiku-4-5-20251001", "claude-sonnet-5", "claude-opus-4-8" };

        private readonly CheckBox _enabled;
        private readonly RadioButton _providerClaude;
        private readonly RadioButton _providerAzure;
        private readonly PasswordBox _claudeKey;
        private readonly ComboBox _claudeModel;
        private readonly TextBox _azureEndpoint;
        private readonly PasswordBox _azureKey;
        private readonly TextBox _azureDeployment;
        private readonly Border _claudeGroup;
        private readonly Border _azureGroup;
        private readonly TextBlock _status;
        private readonly Button _testBtn;

        private StingLlmConfigDialog()
        {
            Title = "Copilot Settings";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 720;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252));
            FontFamily = new FontFamily("Segoe UI");

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // body
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // footer

            // ── Header ──
            var header = new Border { Background = new SolidColorBrush(BrandPurple), Padding = new Thickness(16, 12, 16, 12) };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Copilot Settings",
                FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Configure the STING Copilot's AI provider. Stored in STING_LLM_CONFIG.json.",
                FontSize = 11, Foreground = new SolidColorBrush(BrandPurpleLight),
                Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Body ──
            var body = new StackPanel { Margin = new Thickness(16, 14, 16, 8) };

            _enabled = new CheckBox
            {
                Content = "Enable the Copilot (calls the LLM API — consumes credits)",
                FontSize = 12, Foreground = new SolidColorBrush(TextPrimary),
                Margin = new Thickness(0, 0, 0, 12)
            };
            body.Children.Add(_enabled);

            body.Children.Add(SectionLabel("Provider"));
            var providerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
            _providerClaude = new RadioButton { Content = "Claude (Anthropic)", FontSize = 12, GroupName = "prov", Margin = new Thickness(0, 0, 18, 0), Foreground = new SolidColorBrush(TextPrimary) };
            _providerAzure = new RadioButton { Content = "Azure OpenAI", FontSize = 12, GroupName = "prov", Foreground = new SolidColorBrush(TextPrimary) };
            providerRow.Children.Add(_providerClaude);
            providerRow.Children.Add(_providerAzure);
            body.Children.Add(providerRow);

            // ── Claude group ──
            var claudeStack = new StackPanel();
            claudeStack.Children.Add(SectionLabel("Claude API key"));
            _claudeKey = new PasswordBox { FontSize = 12, Height = 26, Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(4, 2, 4, 2) };
            claudeStack.Children.Add(_claudeKey);
            claudeStack.Children.Add(SectionLabel("Model"));
            _claudeModel = new ComboBox { FontSize = 12, Height = 26, IsEditable = true, Margin = new Thickness(0, 2, 0, 2) };
            foreach (var m in ClaudeModels) _claudeModel.Items.Add(m);
            claudeStack.Children.Add(_claudeModel);
            _claudeGroup = GroupBox("Claude", claudeStack);
            body.Children.Add(_claudeGroup);

            // ── Azure group ──
            var azureStack = new StackPanel();
            azureStack.Children.Add(SectionLabel("Azure endpoint"));
            _azureEndpoint = MakeTextBox();
            azureStack.Children.Add(_azureEndpoint);
            azureStack.Children.Add(SectionLabel("Azure API key"));
            _azureKey = new PasswordBox { FontSize = 12, Height = 26, Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(4, 2, 4, 2) };
            azureStack.Children.Add(_azureKey);
            azureStack.Children.Add(SectionLabel("Deployment name"));
            _azureDeployment = MakeTextBox();
            azureStack.Children.Add(_azureDeployment);
            _azureGroup = GroupBox("Azure OpenAI", azureStack);
            body.Children.Add(_azureGroup);

            // ── Test connection ──
            var testRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };
            _testBtn = new Button
            {
                Content = "Test connection", MinWidth = 120, Height = 28, FontSize = 12,
                Padding = new Thickness(10, 3, 10, 3), Cursor = Cursors.Hand,
                Background = Brushes.White, BorderBrush = new SolidColorBrush(FieldBorder)
            };
            _testBtn.Click += TestBtn_Click;
            testRow.Children.Add(_testBtn);
            body.Children.Add(testRow);

            _status = new TextBlock
            {
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 2, 0),
                Foreground = new SolidColorBrush(TextSecondary), Text = ""
            };
            body.Children.Add(_status);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            // ── Footer ──
            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 248)),
                BorderBrush = new SolidColorBrush(FieldBorder), BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var footerStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = new Button
            {
                Content = "Cancel", MinWidth = 80, Height = 30, FontSize = 12, Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 4, 12, 4), Cursor = Cursors.Hand, Background = Brushes.White,
                BorderBrush = new SolidColorBrush(FieldBorder)
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            var saveBtn = new Button
            {
                Content = "Save", MinWidth = 90, Height = 30, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(12, 4, 12, 4), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(BrandPurple), Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(BrandPurple)
            };
            saveBtn.Click += SaveBtn_Click;
            footerStack.Children.Add(cancelBtn);
            footerStack.Children.Add(saveBtn);
            footer.Child = footerStack;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };

            _providerClaude.Checked += (s, e) => UpdateProviderVisibility();
            _providerAzure.Checked += (s, e) => UpdateProviderVisibility();

            LoadFromConfig();
            UpdateProviderVisibility();
        }

        // ── Load ─────────────────────────────────────────────────────────────

        private void LoadFromConfig()
        {
            try
            {
                string path = ConfigPath();
                if (!File.Exists(path))
                {
                    _providerClaude.IsChecked = true;
                    _claudeModel.SelectedItem = ClaudeModels[0];
                    return;
                }

                var cfg = JObject.Parse(File.ReadAllText(path));
                _enabled.IsChecked = cfg["enabled"]?.Value<bool>() ?? false;

                string ck = cfg["claude_api_key"]?.Value<string>() ?? "";
                _claudeKey.Password = ck;
                string model = cfg["claude_model"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    if (!_claudeModel.Items.Contains(model)) _claudeModel.Items.Add(model);
                    _claudeModel.SelectedItem = model;
                }
                else _claudeModel.SelectedItem = ClaudeModels[0];

                _azureEndpoint.Text = cfg["azure_endpoint"]?.Value<string>() ?? "";
                _azureKey.Password = cfg["azure_api_key"]?.Value<string>() ?? "";
                _azureDeployment.Text = cfg["azure_deployment"]?.Value<string>() ?? "";

                string provider = cfg["provider"]?.Value<string>()?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(provider))
                    provider = !string.IsNullOrWhiteSpace(ck) ? "claude"
                             : (!string.IsNullOrWhiteSpace(_azureEndpoint.Text) && !string.IsNullOrWhiteSpace(_azureKey.Password)) ? "azure"
                             : "claude";
                if (provider == "azure") _providerAzure.IsChecked = true; else _providerClaude.IsChecked = true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Copilot Settings load failed: {ex.Message}");
                _providerClaude.IsChecked = true;
                _claudeModel.SelectedItem = ClaudeModels[0];
            }
        }

        // ── Save (clobber-safe) ────────────────────────────────────────────────

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = ConfigPath();
                JObject cfg = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();

                // Set ONLY the LLM fields — every other key (mcp_*, notes, timeout_seconds…) is preserved.
                cfg["enabled"]          = _enabled.IsChecked == true;
                cfg["provider"]         = _providerAzure.IsChecked == true ? "azure" : "claude";
                cfg["claude_api_key"]   = _claudeKey.Password ?? "";
                cfg["claude_model"]     = CurrentModel();
                cfg["azure_endpoint"]   = _azureEndpoint.Text?.Trim() ?? "";
                cfg["azure_api_key"]    = _azureKey.Password ?? "";
                cfg["azure_deployment"] = _azureDeployment.Text?.Trim() ?? "";

                File.WriteAllText(path, cfg.ToString(Formatting.Indented));
                StingLog.Info($"Copilot Settings saved (enabled={cfg["enabled"]}, provider={cfg["provider"]}, model={cfg["claude_model"]}).");

                // Live pick-up — no Revit restart needed.
                try { StingLlmService.Instance.ReloadConfig(); }
                catch (Exception rex) { StingLog.Warn($"Copilot Settings reload after save failed: {rex.Message}"); }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                StingLog.Error("Copilot Settings save failed", ex);
                _status.Foreground = new SolidColorBrush(Color.FromRgb(180, 40, 40));
                _status.Text = "Save failed: " + ex.Message;
            }
        }

        // ── Test connection ────────────────────────────────────────────────────

        private async void TestBtn_Click(object sender, RoutedEventArgs e)
        {
            _testBtn.IsEnabled = false;
            _status.Foreground = new SolidColorBrush(TextSecondary);
            _status.Text = "Testing…";
            try
            {
                bool useClaude = _providerClaude.IsChecked == true;
                var (ok, msg) = await StingLlmService.Instance.TestConnectionAsync(
                    useClaude,
                    _claudeKey.Password, CurrentModel(),
                    _azureEndpoint.Text?.Trim(), _azureKey.Password, _azureDeployment.Text?.Trim());

                _status.Foreground = new SolidColorBrush(ok ? Color.FromRgb(30, 130, 60) : Color.FromRgb(180, 40, 40));
                _status.Text = (ok ? "✓ " : "✗ ") + msg;
            }
            catch (Exception ex)
            {
                _status.Foreground = new SolidColorBrush(Color.FromRgb(180, 40, 40));
                _status.Text = "✗ " + ex.Message;
                StingLog.Warn($"Copilot Settings test failed: {ex.Message}");
            }
            finally { _testBtn.IsEnabled = true; }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private void UpdateProviderVisibility()
        {
            bool azure = _providerAzure.IsChecked == true;
            _azureGroup.Visibility = azure ? Visibility.Visible : Visibility.Collapsed;
            _claudeGroup.Visibility = azure ? Visibility.Collapsed : Visibility.Visible;
        }

        private string CurrentModel()
        {
            string m = (_claudeModel.SelectedItem as string) ?? _claudeModel.Text;
            return string.IsNullOrWhiteSpace(m) ? ClaudeModels[0] : m.Trim();
        }

        private static string ConfigPath() =>
            Path.Combine(StingToolsApp.DataPath, "STING_LLM_CONFIG.json");

        private static TextBlock SectionLabel(string text) => new TextBlock
        {
            Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(TextSecondary), Margin = new Thickness(0, 4, 0, 0)
        };

        private static TextBox MakeTextBox() => new TextBox
        {
            FontSize = 12, Height = 26, Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(4, 2, 4, 2)
        };

        private static Border GroupBox(string title, UIElement content)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title, FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(BrandPurple), Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(content);
            return new Border
            {
                BorderBrush = new SolidColorBrush(FieldBorder), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 0, 8), Child = stack
            };
        }

        /// <summary>Open the Copilot Settings dialog modally. Returns true when saved.
        /// Intentionally hides Window.Show() — this static helper is the only entry point.</summary>
        public static new bool Show()
        {
            var dlg = new StingLlmConfigDialog();
            try { StingWindowHelper.ApplyOwner(dlg); } catch { /* owner is best-effort */ }
            return dlg.ShowDialog() == true;
        }
    }
}
