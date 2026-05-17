// StingTools v4 MVP — shop-drawing options dialog.
//
// Replaces the hardcoded STING_TB_ASSEMBLY_* title-block lookup in
// ShopDrawingComposer with a runtime pick. Two dropdowns:
//
//   Title block:    every loaded title-block FamilySymbol
//                   (OST_TitleBlocks → OfClass(FamilySymbol)).
//                   Adds an "Auto (per-discipline STING_TB_ASSEMBLY_*)"
//                   entry as the default.
//
//   View template:  every ViewTemplate in the project, grouped by
//                   ViewType. "Auto (no template)" is the default —
//                   AssemblyViewUtils creates views with the project
//                   default graphic style.
//
//   Sheet-name pattern (optional free-text):
//                   mask like "{spool} - {disc}" that overrides the
//                   ShopDrawingComposer default.
//
// The dialog is modal and deterministic: no Revit API writes, only
// reads + returns a ShopDrawingOptions record the calling command
// passes into the engine.

using Autodesk.Revit.DB;
using Autodesk.Revit.DB;
// Autodesk.Revit.DB and System.Windows.* share a batch of type names
// (Grid line, Color, Binding parameter, …). Alias the WPF ones so
// every control / binding / colour ref in this file binds to WPF.
using Colors       = System.Windows.Media.Colors;
using Colors       = System.Windows.Media.Colors;
using StingTools.Core;
namespace StingTools.UI
{
    public class ShopDrawingOptions
    {
        /// <summary>Null/Invalid → use per-discipline default.</summary>
        public ElementId TitleBlockSymbolId { get; set; } = ElementId.InvalidElementId;
        /// <summary>Null/Invalid → no template applied.</summary>
        public ElementId ViewTemplateId     { get; set; } = ElementId.InvalidElementId;
        /// <summary>Free-text mask; empty → engine default.</summary>
        public string SheetNumberPattern    { get; set; } = "";
        /// <summary>Free-text mask for sheet name; empty → engine default.</summary>
        public string SheetNamePattern      { get; set; } = "";
    }

    public class ShopDrawingOptionsDialog : Window
    {
        private readonly Document _doc;
        private ComboBox _cmbTitleBlock;
        private ComboBox _cmbViewTemplate;
        private TextBox  _txtNumberPattern;
        private TextBox  _txtNamePattern;

        public ShopDrawingOptions Result { get; private set; }

        public ShopDrawingOptionsDialog(Document doc)
        {
            _doc = doc;
            Title = "STING v4 — Configure Title Block";
            Width = 540;
            Height = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(DarkDialogTheme.LightPalette.WindowBg);
            Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg);
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.NoResize;
            // Force-reset inherited theme resources so this dialog
            // always renders light, matching the Fabrication Workspace.
            Resources["PrimaryBg"]   = new SolidColorBrush(DarkDialogTheme.LightPalette.WindowBg);
            Resources["SecondaryBg"] = new SolidColorBrush(DarkDialogTheme.LightPalette.CardBg);
            Resources["AccentBrush"] = new SolidColorBrush(DarkDialogTheme.LightPalette.Accent);
            Resources["BorderColor"] = new SolidColorBrush(DarkDialogTheme.LightPalette.Border);
            Resources["ButtonBg"]    = new SolidColorBrush(DarkDialogTheme.LightPalette.SecondaryBtn);
            Resources["ButtonFg"]    = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg);
            DarkDialogTheme.ApplyComboBoxFix(this,
                DarkDialogTheme.LightPalette.CardBg,
                DarkDialogTheme.LightPalette.BodyFg,
                DarkDialogTheme.LightPalette.AltRowBg);
            BuildUi();
        }

        private void BuildUi()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            AddLabel(grid, 0, "Title block family:");
            _cmbTitleBlock = AddCombo(grid, 0);

            AddLabel(grid, 1, "View template:");
            _cmbViewTemplate = AddCombo(grid, 1);

            AddLabel(grid, 2, "Sheet number pattern:");
            _txtNumberPattern = AddTextBox(grid, 2, "",
                "Tokens: {disc} {sys} {lvl} {seq} {spool}. Empty → engine default.");

            AddLabel(grid, 3, "Sheet name pattern:");
            _txtNamePattern = AddTextBox(grid, 3, "",
                "Tokens: {disc} {spool} {sys} {lvl}. Empty → engine default.");

            // Hint row
            var hint = new TextBlock
            {
                Text = "Dropdowns read the project's loaded title blocks and view templates. " +
                       "If you leave them on Auto, ShopDrawingComposer falls back to per-discipline " +
                       "STING_TB_ASSEMBLY_* resolution.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
                Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.SubtleFg),
                FontSize = 11,
            };
            Grid.SetRow(hint, 6);
            Grid.SetColumnSpan(hint, 2);
            grid.Children.Add(hint);

            // Buttons
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var btnOk = new Button
            {
                Content = "Compose",
                Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(DarkDialogTheme.LightPalette.Accent),
                Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.AccentFg),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
            };
            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 90, Height = 30,
                Background = new SolidColorBrush(DarkDialogTheme.LightPalette.SecondaryBtn),
                Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(DarkDialogTheme.LightPalette.Border),
            };
            btnOk.Click     += (_, __) => { CaptureResult(); DialogResult = true; Close(); };
            btnCancel.Click += (_, __) => { DialogResult = false; Close(); };
            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCancel);
            Grid.SetRow(btnRow, 7);
            Grid.SetColumnSpan(btnRow, 2);
            grid.Children.Add(btnRow);

            Content = grid;

            PopulateTitleBlocks();
            PopulateViewTemplates();
        }

        private void PopulateTitleBlocks()
        {
            _cmbTitleBlock.Items.Clear();
            _cmbTitleBlock.Items.Add(new ComboBoxItem
            {
                Content = "— Auto (per-discipline STING_TB_ASSEMBLY_*) —",
                Tag     = ElementId.InvalidElementId,
            });
            _cmbTitleBlock.SelectedIndex = 0;

            try
            {
                var col = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .OrderBy(fs => fs.FamilyName)
                    .ThenBy (fs => fs.Name);
                foreach (var fs in col)
                {
                    _cmbTitleBlock.Items.Add(new ComboBoxItem
                    {
                        Content = $"{fs.FamilyName} : {fs.Name}",
                        Tag     = fs.Id,
                    });
                }
            }
            catch (Exception ex) { Core.StingLog.Warn($"TitleBlock populate: {ex.Message}"); }
        }

        private void PopulateViewTemplates()
        {
            _cmbViewTemplate.Items.Clear();
            _cmbViewTemplate.Items.Add(new ComboBoxItem
            {
                Content = "— Auto (no template applied) —",
                Tag     = ElementId.InvalidElementId,
            });
            _cmbViewTemplate.SelectedIndex = 0;

            try
            {
                var col = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate)
                    .OrderBy(v => v.ViewType.ToString())
                    .ThenBy (v => v.Name);
                foreach (var v in col)
                {
                    _cmbViewTemplate.Items.Add(new ComboBoxItem
                    {
                        Content = $"{v.ViewType} : {v.Name}",
                        Tag     = v.Id,
                    });
                }
            }
            catch (Exception ex) { Core.StingLog.Warn($"ViewTemplate populate: {ex.Message}"); }
        }

        private void CaptureResult()
        {
            Result = new ShopDrawingOptions
            {
                TitleBlockSymbolId = ((_cmbTitleBlock.SelectedItem as ComboBoxItem)?.Tag as ElementId)
                                      ?? ElementId.InvalidElementId,
                ViewTemplateId     = ((_cmbViewTemplate.SelectedItem as ComboBoxItem)?.Tag as ElementId)
                                      ?? ElementId.InvalidElementId,
                SheetNumberPattern = _txtNumberPattern.Text?.Trim() ?? "",
                SheetNamePattern   = _txtNamePattern.Text?.Trim()   ?? "",
            };
        }

        private static void AddLabel(Grid grid, int row, string text)
        {
            var lbl = new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 6, 8, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg),
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);
        }
        private static ComboBox AddCombo(Grid grid, int row)
        {
            var cb = new ComboBox
            {
                Margin = new Thickness(0, 6, 0, 6),
                Background = new SolidColorBrush(DarkDialogTheme.LightPalette.CardBg),
                Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg),
                BorderBrush = new SolidColorBrush(DarkDialogTheme.LightPalette.Border),
            };
            Grid.SetRow(cb, row);
            Grid.SetColumn(cb, 1);
            grid.Children.Add(cb);
            return cb;
        }
        private static TextBox AddTextBox(Grid grid, int row, string initial, string tooltip)
        {
            var tb = new TextBox
            {
                Margin = new Thickness(0, 6, 0, 6),
                Text = initial,
                ToolTip = tooltip,
                Background = new SolidColorBrush(DarkDialogTheme.LightPalette.CardBg),
                Foreground = new SolidColorBrush(DarkDialogTheme.LightPalette.BodyFg),
                BorderBrush = new SolidColorBrush(DarkDialogTheme.LightPalette.Border),
            };
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);
            return tb;
        }
    }
}
