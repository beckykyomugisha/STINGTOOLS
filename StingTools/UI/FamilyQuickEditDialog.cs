using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Tags;
// CS0104 — both System.Windows.Controls and Autodesk.Revit.DB define Grid.
// CS0104 — both System.Windows.Media and Autodesk.Revit.DB define Color.
// This dialog is WPF-only and never references Revit's Grid / Color types,
// so alias the bare names to the WPF equivalents. Revit grids / colors
// would be Autodesk.Revit.DB.Grid / .Color.
using Grid  = System.Windows.Controls.Grid;
using Color = System.Windows.Media.Color;

namespace StingTools.UI
{
    /// <summary>
    /// Lightweight launcher dialog for the family quick-edit commands. Shows the
    /// selected instance's family name, category, host, placement type, and type
    /// count — then offers one-click buttons that dispatch to the appropriate
    /// sub-command (<see cref="ChangeHostCommand"/>, <see cref="SwapCategoryCommand"/>,
    /// <see cref="InjectAutomationPackCommand"/>, <see cref="FamilyParamCreatorCommand"/>).
    ///
    /// The dialog itself does not mutate the model — it only resolves a user choice.
    /// The caller (<see cref="OpenFamilyQuickEditCommand"/>) inspects
    /// <see cref="ShowAndGetChoice"/>'s return value and runs the matching command.
    /// </summary>
    public class FamilyQuickEditDialog : Window
    {
        public enum ActionChoice
        {
            None = 0,
            ChangeHost,
            SwapCategory,
            InjectStingParamPack,
            InjectAutomationPack,
            OpenInFamilyEditor,
            ShowTypeProperties,
        }

        private static SolidColorBrush FZ(SolidColorBrush b) { b.Freeze(); return b; }

        // Palette matching StingListPicker / BulkOperationDialog so the family
        // quick-edit dialog visually fits next to the other STING dialogs.
        private static readonly SolidColorBrush BrushBg          = FZ(new SolidColorBrush(Color.FromRgb(250, 250, 252)));
        private static readonly SolidColorBrush BrushHeader      = FZ(new SolidColorBrush(Color.FromRgb(88, 44, 131)));   // STING purple
        private static readonly SolidColorBrush BrushHeaderFg    = FZ(new SolidColorBrush(Colors.White));
        private static readonly SolidColorBrush BrushGroupBorder = FZ(new SolidColorBrush(Color.FromRgb(210, 210, 220)));
        private static readonly SolidColorBrush BrushSectionLbl  = FZ(new SolidColorBrush(Color.FromRgb(90, 90, 110)));
        private static readonly SolidColorBrush BrushInfoKey     = FZ(new SolidColorBrush(Color.FromRgb(100, 100, 120)));
        private static readonly SolidColorBrush BrushInfoVal     = FZ(new SolidColorBrush(Color.FromRgb(40, 40, 50)));
        private static readonly SolidColorBrush BrushBtnBg       = FZ(new SolidColorBrush(Color.FromRgb(245, 245, 250)));
        private static readonly SolidColorBrush BrushBtnBgHover  = FZ(new SolidColorBrush(Color.FromRgb(235, 230, 245)));
        private static readonly SolidColorBrush BrushOrange      = FZ(new SolidColorBrush(Color.FromRgb(232, 145, 45)));
        private static readonly SolidColorBrush BrushOrangeFg    = FZ(new SolidColorBrush(Colors.White));
        private static readonly SolidColorBrush BrushFooterHint  = FZ(new SolidColorBrush(Color.FromRgb(130, 130, 150)));

        private ActionChoice _choice = ActionChoice.None;

        private readonly FamilyInstance _inst;

        public FamilyQuickEditDialog(FamilyInstance inst)
        {
            _inst = inst;

            Title = "STING — Family Quick Edit";
            Width = 520;
            Height = 520;
            MinWidth = 420;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BrushBg;
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // info
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // actions
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // footer

            // ── HEADER ───────────────────────────────────────────────────────────
            var header = new Border
            {
                Background = BrushHeader,
                Padding = new Thickness(14, 10, 14, 10),
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Family Quick Edit",
                Foreground = BrushHeaderFg,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Change host, swap category, inject parameter packs — without opening the family editor.",
                Foreground = BrushHeaderFg,
                FontSize = 10,
                Opacity = 0.85,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── INFO PANEL (family / instance summary) ───────────────────────────
            var infoBorder = new Border
            {
                BorderBrush = BrushGroupBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 10, 14, 10),
                Background = Brushes.White,
            };
            var infoGrid = new Grid();
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddInfoRow(infoGrid, 0, "Family",     _inst?.Symbol?.Family?.Name ?? "(unknown)");
            AddInfoRow(infoGrid, 1, "Type",       _inst?.Symbol?.Name ?? "(unknown)");
            AddInfoRow(infoGrid, 2, "Category",   _inst?.Symbol?.Family?.FamilyCategory?.Name ?? "(unknown)");
            AddInfoRow(infoGrid, 3, "Placement",  _inst?.Symbol?.Family?.FamilyPlacementType.ToString() ?? "(unknown)");
            AddInfoRow(infoGrid, 4, "Host",       FamilyQuickEditHelpers.DescribeHost(_inst));
            AddInfoRow(infoGrid, 5, "Types",      CountFamilyTypes(_inst?.Symbol?.Family).ToString() + " defined");
            infoBorder.Child = infoGrid;
            Grid.SetRow(infoBorder, 1);
            root.Children.Add(infoBorder);

            // ── ACTIONS PANEL ────────────────────────────────────────────────────
            var actionsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(14, 10, 14, 10),
            };
            var actionsStack = new StackPanel();

            actionsStack.Children.Add(MakeSectionLabel("HOST & CATEGORY"));
            actionsStack.Children.Add(MakeActionButton(
                "Change Host / Delete",
                "Mode picker: rehost to Wall / Floor-Ceiling-Roof / Face / Work Plane, detach to free-standing, or delete the instance entirely. Preserves parameters on rehost.",
                ActionChoice.ChangeHost,
                enabled: _inst != null));
            actionsStack.Children.Add(MakeActionButton(
                "Swap Category",
                "Change the family's category (Generic Model ↔ Furniture ↔ Equipment etc.) via EditFamily + reload.",
                ActionChoice.SwapCategory,
                enabled: IsSwapCategoryEligible(_inst)));

            actionsStack.Children.Add(MakeSectionLabel("PARAMETER PACKS"));
            actionsStack.Children.Add(MakeActionButton(
                "Inject STING Param Pack",
                "Add STING shared parameters (tokens, tag containers, TAG_POS) from MR_PARAMETERS.txt.",
                ActionChoice.InjectStingParamPack,
                enabled: true));
            actionsStack.Children.Add(MakeActionButton(
                "Inject Automation + Presentation Pack",
                "Add clearance, fire rating, acoustic, cost, CO₂, manufacturer, model, datasheet URL, warranty, LOD switches, workset hint, OmniClass.",
                ActionChoice.InjectAutomationPack,
                enabled: _inst?.Symbol?.Family?.IsEditable == true));

            actionsStack.Children.Add(MakeSectionLabel("FAMILY EDITOR"));
            actionsStack.Children.Add(MakeActionButton(
                "Open in Family Editor",
                "Launch Revit's Edit Family on this family (uses the built-in PostCommand).",
                ActionChoice.OpenInFamilyEditor,
                enabled: _inst?.Symbol?.Family?.IsEditable == true));
            actionsStack.Children.Add(MakeActionButton(
                "Show Type Properties",
                "Display all type parameters for the selected family as a read-only list.",
                ActionChoice.ShowTypeProperties,
                enabled: _inst?.Symbol != null));

            actionsScroll.Content = actionsStack;
            Grid.SetRow(actionsScroll, 2);
            root.Children.Add(actionsScroll);

            // ── FOOTER ───────────────────────────────────────────────────────────
            var footer = new Border
            {
                BorderBrush = BrushGroupBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 8, 14, 8),
                Background = Brushes.White,
            };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hint = new TextBlock
            {
                Text = "Select an action. The dialog closes and the selected command runs with the current selection.",
                Foreground = BrushFooterHint,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(hint, 0);
            footerGrid.Children.Add(hint);
            var closeBtn = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(18, 4, 18, 4),
                MinWidth = 80,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            closeBtn.Click += (s, e) => { _choice = ActionChoice.None; Close(); };
            Grid.SetColumn(closeBtn, 1);
            footerGrid.Children.Add(closeBtn);
            footer.Child = footerGrid;
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
        }

        private static int CountFamilyTypes(Family fam)
        {
            if (fam == null) return 0;
            try
            {
                var ids = fam.GetFamilySymbolIds();
                return ids?.Count ?? 0;
            }
            catch (Exception ex) { StingLog.Warn($"CountFamilyTypes: {ex.Message}"); return 0; }
        }

        private static bool IsSwapCategoryEligible(FamilyInstance inst)
        {
            var cat = inst?.Symbol?.Family?.FamilyCategory;
            if (cat == null) return false;
            try
            {
                var bic = (BuiltInCategory)cat.Id.Value;
                return FamilyCategoryCompatibility.ModelFamilyGroup.Contains(bic);
            }
            catch { return false; }
        }

        private void AddInfoRow(Grid grid, int row, string key, string value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = new TextBlock
            {
                Text = key,
                Foreground = BrushInfoKey,
                FontSize = 11,
                Margin = new Thickness(0, 2, 8, 2),
            };
            var v = new TextBlock
            {
                Text = value ?? "",
                Foreground = BrushInfoVal,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(k, row); Grid.SetColumn(k, 0);
            Grid.SetRow(v, row); Grid.SetColumn(v, 1);
            grid.Children.Add(k);
            grid.Children.Add(v);
        }

        private TextBlock MakeSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = BrushSectionLbl,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 4),
            };
        }

        private Border MakeActionButton(string title, string description, ActionChoice choice, bool enabled)
        {
            var outer = new Border
            {
                BorderBrush = BrushGroupBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = BrushBtnBg,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = enabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.No,
                Opacity = enabled ? 1.0 : 0.45,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel { Margin = new Thickness(12, 8, 8, 8) };
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = BrushInfoVal,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
            });
            textStack.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = BrushInfoKey,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
            Grid.SetColumn(textStack, 0);
            grid.Children.Add(textStack);

            var go = new Border
            {
                Background = BrushOrange,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 10, 12, 10),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            go.Child = new TextBlock
            {
                Text = "Run",
                Foreground = BrushOrangeFg,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            };
            Grid.SetColumn(go, 1);
            grid.Children.Add(go);

            outer.Child = grid;

            if (enabled)
            {
                outer.MouseEnter += (s, e) => outer.Background = BrushBtnBgHover;
                outer.MouseLeave += (s, e) => outer.Background = BrushBtnBg;
                outer.MouseLeftButtonUp += (s, e) =>
                {
                    _choice = choice;
                    DialogResult = true;
                    Close();
                };
            }

            return outer;
        }

        /// <summary>Show the dialog modally (ownership wired to the active Revit
        /// window) and return the user's choice. <see cref="ActionChoice.None"/>
        /// is returned on Cancel or window close.</summary>
        public ActionChoice ShowAndGetChoice()
        {
            try { StingWindowHelper.ApplyOwner(this); }
            catch (Exception ex) { StingLog.Warn($"FamilyQuickEditDialog.ApplyOwner: {ex.Message}"); }
            ShowDialog();
            return _choice;
        }
    }
}
