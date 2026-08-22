// StingTools — Scope Box Manager & Renamer
//
// The Drawing Template Manager binds a scope box to a DrawingType through
// a "magic name": STING::<drawing-type-id>[::<level>][::<tag>]. The
// grammar is strict and unforgiving — a space, a slash, or a mistyped id
// and ScopeBoxBinder either warns or skips the box entirely.
//
// Until this dialog existed the only way to satisfy that grammar was to
// type it by hand into Revit's scope-box rename field, from memory,
// against a catalogue of 93 drawing-type ids that is nowhere on screen.
// That is how you get a silent skip.
//
// So the point of the whole tool is the drawing-type combo: you cannot
// mistype an id you picked from a list. Everything else here — live
// four-state validation, bulk fill, the pattern with a sequence, the
// two-pass rename — exists to make that pick survive contact with a real
// project.
//
// Modeless by construction (see ScopeBoxManagerCommand): a modal WPF
// window blocks Revit's ExternalEvent queue, so the write actions would
// never fire. Every model write therefore goes through the ExternalEvent
// created in the constructor — which is the only API context this class
// ever runs in.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
// Autodesk.Revit.UI defines ribbon TextBox / ComboBox, and Autodesk.Revit.DB
// defines Binding (parameter binding), Control (family control) and Grid
// (a datum line) — all colliding with the WPF types this file wants.
using TextBox  = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Binding  = System.Windows.Data.Binding;
using Control  = System.Windows.Controls.Control;
using Grid     = System.Windows.Controls.Grid;

namespace StingTools.UI
{
    // ═══════════════════════════════════════════════════════════════════
    //  ROW MODEL
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// One scope box. Shape follows <c>ProjectSetupWizard.ScopeBoxRow</c>
    /// (Include / CurrentName / NewName / Rotation / RevitIdValue) so the two
    /// grids read the same way, extended with the grammar fields the wizard
    /// row has no concept of.
    /// </summary>
    public sealed class ScopeBoxManagerRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        /// <summary>Called by the dialog after any edit so status + name recompute.</summary>
        internal Action Recompute;

        private void Edited(string n)
        {
            RawOverride = null;
            Raise(n);
            Recompute?.Invoke();
        }

        // Defaults true, matching ProjectSetupWizard.ScopeBoxRow. Nothing is
        // committed until "Rename checked", and every proposed name is on
        // screen before it is, so an over-broad tick is visible, not silent.
        private bool _include = true;
        public bool Include { get => _include; set { _include = value; Raise(nameof(Include)); } }

        /// <summary>
        /// A pattern result that does NOT parse. Held separately so Recompute
        /// shows the operator what their pattern actually produced instead of
        /// silently replacing it with a name composed from the fields — which
        /// would hide the very mistake the row is meant to flag. Cleared the
        /// moment any field is edited.
        /// </summary>
        internal string RawOverride { get; set; }

        /// <summary>The name the box has in the model right now.</summary>
        public string CurrentName { get; set; }

        private string _drawingTypeId;
        public string DrawingTypeId
        {
            get => _drawingTypeId;
            set { _drawingTypeId = value; Edited(nameof(DrawingTypeId)); }
        }

        private string _levelCode;
        public string LevelCode
        {
            get => _levelCode;
            set { _levelCode = value; Edited(nameof(LevelCode)); }
        }

        private string _tag;
        public string Tag
        {
            get => _tag;
            set { _tag = value; Edited(nameof(Tag)); }
        }

        private string _newName = "";
        /// <summary>Computed, read-only in the grid. Empty when no type is picked.</summary>
        public string NewName
        {
            get => _newName;
            internal set { _newName = value; Raise(nameof(NewName)); }
        }

        private string _status = "";
        public string Status { get => _status; internal set { _status = value; Raise(nameof(Status)); } }

        private string _statusTip = "";
        public string StatusTip { get => _statusTip; internal set { _statusTip = value; Raise(nameof(StatusTip)); } }

        /// <summary>Red rows cannot be committed. Drives both the badge and the rename filter.</summary>
        public bool IsBlocked { get; internal set; }

        public double RotationDegrees { get; set; }
        public string RotationText => RotationDegrees == 0 ? "0°" : $"{RotationDegrees:F1}°";

        /// <summary>Views already cropped to this box.</summary>
        public int ViewCount { get; set; }

        /// <summary>Revit ElementId.Value (Int64) for the scope box.</summary>
        public long RevitIdValue { get; set; }

        /// <summary>
        /// The name this row represents once the user's edits are taken into
        /// account — what the box will be called if the batch is committed.
        /// Falls back to the current name when nothing has been composed.
        /// </summary>
        public string EffectiveName =>
            string.IsNullOrWhiteSpace(NewName) ? (CurrentName ?? "") : NewName;

        /// <summary>True when committing this row would actually change the model.</summary>
        public bool IsRenamePending =>
            !string.IsNullOrWhiteSpace(NewName) &&
            !string.Equals(NewName, CurrentName, StringComparison.Ordinal);
    }

    /// <summary>Drawing-type entry shown in the per-row combo.</summary>
    public sealed class DrawingTypeChoice
    {
        public string Id { get; set; }
        public string Display { get; set; }
        public override string ToString() => Display;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DIALOG
    // ═══════════════════════════════════════════════════════════════════

    public sealed class ScopeBoxManagerDialog : StingDataGridDialog
    {
        // Status badges. Four states, per the register:
        //   🟢 valid · 🟡 grammar fine, unknown drawing-type id
        //   🔴 STING:: prefix but fails the regex, or collides
        //   ⚪ not a STING box
        private const string BadgeOk       = "🟢 valid";
        private const string BadgeUnknown  = "🟡 unknown type";
        private const string BadgeBad      = "🔴 invalid";
        private const string BadgeNotSting = "⚪ not STING";

        private readonly UIApplication _uiApp;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        private readonly List<ScopeBoxManagerRow> _rows = new List<ScopeBoxManagerRow>();
        private List<ScopeBoxManagerRow> _visible = new List<ScopeBoxManagerRow>();
        private readonly List<DrawingTypeChoice> _types = new List<DrawingTypeChoice>();

        private TextBox _patternBox;
        private ComboBox _bulkType;
        private ComboBox _bulkLevel;
        private bool _recomputing;

        // ── Model writes from a modeless window ──────────────────────────
        // ExternalEvent.Create must run in a Revit API context. The ctor is
        // invoked from ScopeBoxManagerCommand.Execute, which IS one; a button
        // click is NOT. Create eagerly here or every write action is dead.
        private readonly ExternalEvent _actionEvent;
        private readonly ActionHandler _actionHandler;
        private Func<UIApplication, string> _pendingAction;
        private string _pendingTitle;

        public ScopeBoxManagerDialog(UIApplication uiApp)
            : base("Scope Box Manager & Renamer",
                   "Bind scope boxes to drawing types with the STING:: grammar — pick the type, never type it.",
                   1180, 700)
        {
            _uiApp = uiApp;
            _uiDoc = uiApp?.ActiveUIDocument;
            _doc   = _uiDoc?.Document;

            // Modeless: suppress the base class's DialogResult writes, which
            // throw on a window that was not shown with ShowDialog().
            IsModeless = true;

            try
            {
                _actionHandler = new ActionHandler(this);
                _actionEvent   = ExternalEvent.Create(_actionHandler);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ScopeBoxManager: ExternalEvent.Create at ctor: {ex.Message}");
            }

            LoadDrawingTypes();
            BuildColumns();
            BuildToolbar();
            BuildFooter();
            LoadRows();

            DataGrid.SelectionChanged += OnRowSelected;
            SearchChanged += _ => ApplyFilter();
        }

        // ── Catalogue ────────────────────────────────────────────────────

        private void LoadDrawingTypes()
        {
            _types.Clear();
            // A blank first entry so a row can be cleared back to "no type".
            _types.Add(new DrawingTypeChoice { Id = "", Display = "— (no drawing type) —" });
            try
            {
                foreach (var dt in DrawingTypeRegistry.ListAll(_doc) ?? new List<DrawingType>())
                {
                    if (dt == null || string.IsNullOrWhiteSpace(dt.Id)) continue;
                    var scale = dt.Scale > 0 ? $"1:{dt.Scale}" : "NA";
                    _types.Add(new DrawingTypeChoice
                    {
                        Id = dt.Id,
                        // Everything needed to choose without opening the editor.
                        Display = $"{dt.Id}  ·  {dt.Name}  ·  {dt.Purpose}  ·  {dt.PaperSize}  ·  {scale}",
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ScopeBoxManager.LoadDrawingTypes: {ex.Message}");
            }
        }

        private bool TypeExists(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            // The row combo is fed from ListAll; Get(doc, id) is the resolved
            // lookup the producer will actually perform, so ask that.
            try { return DrawingTypeRegistry.Get(_doc, id) != null; }
            catch (Exception ex)
            {
                StingLog.Warn($"ScopeBoxManager.TypeExists('{id}'): {ex.Message}");
                return _types.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }

        // ── Grid ─────────────────────────────────────────────────────────

        private void BuildColumns()
        {
            AddCheckColumn("✔", nameof(ScopeBoxManagerRow.Include), 34);
            AddTextColumn("Current name", nameof(ScopeBoxManagerRow.CurrentName), 250);

            var status = new DataGridTextColumn
            {
                Header = "Status",
                Binding = new Binding(nameof(ScopeBoxManagerRow.Status)),
                IsReadOnly = true,
                Width = 118,
            };
            var statusStyle = new Style(typeof(TextBlock));
            statusStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
                new Binding(nameof(ScopeBoxManagerRow.StatusTip))));
            status.ElementStyle = statusStyle;
            DataGrid.Columns.Add(status);

            DataGrid.Columns.Add(MakeTypeColumn());

            DataGrid.Columns.Add(MakeLevelColumn());

            AddTextColumn("Tag", nameof(ScopeBoxManagerRow.Tag), 90, isReadOnly: false);

            AddTextColumn("New name", nameof(ScopeBoxManagerRow.NewName), 280);
            AddTextColumn("Rot", nameof(ScopeBoxManagerRow.RotationText), 58);
            AddTextColumn("Views", nameof(ScopeBoxManagerRow.ViewCount), 52);
        }

        /// <summary>
        /// A template column, not a DataGridComboBoxColumn: the latter only
        /// shows its combo once the cell is in edit mode, which costs a click
        /// per row and hides the very list this tool exists to show.
        /// </summary>
        private DataGridTemplateColumn MakeTypeColumn()
        {
            var factory = new FrameworkElementFactory(typeof(ComboBox));
            factory.SetValue(ItemsControl.ItemsSourceProperty, _types);
            factory.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(DrawingTypeChoice.Display));
            factory.SetValue(Selector.SelectedValuePathProperty, nameof(DrawingTypeChoice.Id));
            factory.SetBinding(Selector.SelectedValueProperty,
                new Binding(nameof(ScopeBoxManagerRow.DrawingTypeId))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                });
            // 93 entries — type-ahead is the difference between usable and not.
            factory.SetValue(ItemsControl.IsTextSearchEnabledProperty, true);
            factory.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
            factory.SetValue(Control.FontSizeProperty, 11.0);
            factory.SetValue(FrameworkElement.ToolTipProperty,
                "Pick the drawing type. Typing jumps to the matching id — this list is the whole point of the tool.");

            return new DataGridTemplateColumn
            {
                Header = "Drawing type",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 240,
                CellTemplate = new DataTemplate { VisualTree = factory },
            };
        }

        /// <summary>Level: combo from the ISO vocabulary, but editable — a
        /// project may legitimately use a code the corporate list has not
        /// seen, and the grammar only cares about the charset.</summary>
        private DataGridTemplateColumn MakeLevelColumn()
        {
            var levels = new List<string> { "" };
            levels.AddRange(Iso19650Vocabulary.LevelCodes);

            var factory = new FrameworkElementFactory(typeof(ComboBox));
            factory.SetValue(ItemsControl.ItemsSourceProperty, levels);
            factory.SetValue(ComboBox.IsEditableProperty, true);
            factory.SetValue(ComboBox.IsTextSearchEnabledProperty, true);
            factory.SetBinding(ComboBox.TextProperty,
                new Binding(nameof(ScopeBoxManagerRow.LevelCode))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                });
            factory.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
            factory.SetValue(Control.FontSizeProperty, 11.0);
            factory.SetValue(FrameworkElement.ToolTipProperty,
                "ISO 19650 level code. ZZ = multiple, XX = none / whole site. Free text is allowed.");

            return new DataGridTemplateColumn
            {
                Header = "Level",
                Width = 92,
                CellTemplate = new DataTemplate { VisualTree = factory },
            };
        }

        // ── Toolbar (bulk fill + pattern) ────────────────────────────────

        private void BuildToolbar()
        {
            _bulkType = AddFilter("Set type on checked",
                _types.Select(t => t.Display), _ => { });
            _bulkType.Width = 300;
            var applyType = MakeToolButton("Apply", "Set the chosen drawing type on every checked row");
            applyType.Click += (s, e) => BulkSetType();
            InsertIntoFilterBar(applyType);

            _bulkLevel = AddFilter("Level",
                new[] { "" }.Concat(Iso19650Vocabulary.LevelCodes), _ => { });
            _bulkLevel.Width = 80;
            _bulkLevel.IsEditable = true;
            var applyLevel = MakeToolButton("Apply", "Set the chosen level code on every checked row");
            applyLevel.Click += (s, e) => BulkSetLevel();
            InsertIntoFilterBar(applyLevel);

            InsertIntoFilterBar(new TextBlock
            {
                Text = "Pattern",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 4, 0),
            });
            _patternBox = new TextBox
            {
                Width = 240,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Text = "STING::{TYPE}::{LEVEL}::COT{INDEX:D2}",
                ToolTip = PatternTokenHelp,
            };
            InsertIntoFilterBar(_patternBox);
            var applyPattern = MakeToolButton("Apply pattern",
                "Regenerate 'New name' from the pattern for every checked row");
            applyPattern.Click += (s, e) => ApplyPattern();
            InsertIntoFilterBar(applyPattern);
        }

        private const string PatternTokenHelp =
            "Tokens: {TYPE} = the row's drawing-type id · {LEVEL} = the row's level code · "
          + "{TAG} = the row's tag · {NAME} = current name · {INDEX} = 1,2,3… over checked rows "
          + "({INDEX:D2} zero-pads). Anything else is reported, not left literal.";

        private void InsertIntoFilterBar(UIElement el)
        {
            // The base class exposes AddFilter but not the panel; reach the
            // WrapPanel through the search box's parent rather than widening
            // the shared dialog's surface for one caller.
            if (DataGrid?.Parent is Grid root)
            {
                foreach (var child in root.Children)
                    if (child is WrapPanel wp) { wp.Children.Add(el); return; }
            }
        }

        private static Button MakeToolButton(string text, string tip) => new Button
        {
            Content = text,
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(2, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tip,
        };

        // ── Footer ───────────────────────────────────────────────────────

        private void BuildFooter()
        {
            AddActionButton("Validate all", "Validate");
            AddActionButton("Audit view usage", "Audit");
            AddActionButton("Clear assignments", "Clear");
            AddActionButton("Auto-assign to views", "AutoAssign");
            AddActionButton("Rename checked", "Rename", isPrimary: true);
            AddActionButton("Generate views now", "Generate");
            AddActionButton("Close", "CloseOnly");

            ActionClicked += tag =>
            {
                switch (tag)
                {
                    case "Validate":   Recompute(); ReportValidation();       break;
                    case "Audit":      RunAudit();                            break;
                    case "Clear":      RunClearAssignments();                 break;
                    case "AutoAssign": RunAutoAssign();                       break;
                    case "Rename":     RunRename();                           break;
                    case "Generate":   RunGenerate();                         break;
                    case "CloseOnly":  Close();                               break;
                }
            };
        }

        // ── Load ─────────────────────────────────────────────────────────

        private void LoadRows()
        {
            _rows.Clear();
            if (_doc == null) { SetStatus("No document open."); return; }

            var usage = BuildViewUsage();

            foreach (var sb in Docs.DocAutomationHelper.GetScopeBoxes(_doc))
            {
                var name = sb.Name ?? "";
                var row = new ScopeBoxManagerRow
                {
                    CurrentName     = name,
                    RevitIdValue    = sb.Id.Value,
                    RotationDegrees = ProjectSetupWizard.GetScopeBoxRotationDegrees(sb),
                    ViewCount       = usage.TryGetValue(sb.Id, out var n) ? n : 0,
                };

                // Seed the editable fields from the existing name where it
                // already parses, so an operator fixing one box in fifty does
                // not have to re-pick the other forty-nine.
                if (ScopeBoxBinder.TryParseName(name, out var b, out _))
                {
                    row.SetQuiet(b.DrawingTypeId, b.LevelCode, b.Tag);
                }
                row.Recompute = Recompute;
                _rows.Add(row);
            }

            Recompute();
            ApplyFilter();
        }

        private Dictionary<ElementId, int> BuildViewUsage()
        {
            var usage = new Dictionary<ElementId, int>();
            try
            {
                foreach (var v in new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>())
                {
                    if (v.IsTemplate || v is ViewSheet) continue;
                    var p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    var id = p?.AsElementId();
                    if (id == null || id == ElementId.InvalidElementId) continue;
                    usage[id] = usage.TryGetValue(id, out var c) ? c + 1 : 1;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ScopeBoxManager.BuildViewUsage: {ex.Message}"); }
            return usage;
        }

        // ── Validation ───────────────────────────────────────────────────

        /// <summary>
        /// Recompute every row's proposed name and badge. Runs on every edit,
        /// so it is deliberately allocation-light and never touches the model.
        /// </summary>
        internal void Recompute()
        {
            if (_recomputing) return;
            _recomputing = true;
            try
            {
                foreach (var r in _rows)
                    r.NewName = r.RawOverride
                             ?? ScopeBoxBinder.ComposeName(r.DrawingTypeId, r.LevelCode, r.Tag);

                // Collisions are judged across EFFECTIVE names — what the
                // project will contain after the batch — not just proposed
                // ones. Revit rejects a duplicate outright, so a proposal that
                // lands on a box nobody is renaming is just as fatal.
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var r in _rows)
                {
                    var n = r.EffectiveName;
                    if (string.IsNullOrEmpty(n)) continue;
                    counts[n] = counts.TryGetValue(n, out var c) ? c + 1 : 1;
                }

                foreach (var r in _rows) Judge(r, counts);
                SetStatus(SummaryLine());
            }
            finally { _recomputing = false; }
        }

        private void Judge(ScopeBoxManagerRow r, Dictionary<string, int> counts)
        {
            var name = r.EffectiveName;
            bool proposed = r.IsRenamePending;
            string which = proposed ? "proposed name" : "current name";

            // A tag without a level has no representation in the grammar —
            // ComposeName drops it, which would silently discard the operator's
            // typing. Say so rather than quietly losing it.
            if (!string.IsNullOrWhiteSpace(r.Tag) && string.IsNullOrWhiteSpace(r.LevelCode)
                && !string.IsNullOrWhiteSpace(r.DrawingTypeId))
            {
                r.Status = BadgeBad;
                r.StatusTip = "A tag requires a level: the grammar is "
                            + "STING::<id>::<level>::<tag> and has no slot for a tag on its own. "
                            + "Set a level code (ZZ = multiple, XX = none) or clear the tag.";
                r.IsBlocked = true;
                return;
            }

            foreach (var field in new[] { r.DrawingTypeId, r.LevelCode, r.Tag })
            {
                if (string.IsNullOrWhiteSpace(field)) continue;
                if (ScopeBoxBinder.IsValidSegment(field)) continue;
                r.Status = BadgeBad;
                r.StatusTip = $"'{field}' contains characters the grammar rejects. "
                            + "Allowed: A-Z a-z 0-9 . _ -  (no spaces, no slashes).";
                r.IsBlocked = true;
                return;
            }

            if (counts.TryGetValue(name, out var c) && c > 1)
            {
                r.Status = BadgeBad;
                r.StatusTip = $"'{name}' is used by {c} boxes. Revit rejects duplicate scope-box "
                            + "names outright, so this batch would fail.";
                r.IsBlocked = true;
                return;
            }

            if (!ScopeBoxBinder.TryParseName(name, out var b, out var reason))
            {
                if (reason == null)
                {
                    // No STING:: prefix at all. Not an error — plenty of scope
                    // boxes are just scope boxes.
                    r.Status = BadgeNotSting;
                    r.StatusTip = "Not a STING scope box — the drawing-type binder ignores it. "
                                + "Pick a drawing type to bring it in.";
                    r.IsBlocked = false;
                }
                else
                {
                    r.Status = BadgeBad;
                    r.StatusTip = $"The {which} claims to be a STING box but fails the grammar.\n{reason}";
                    r.IsBlocked = true;
                }
                return;
            }

            if (!TypeExists(b.DrawingTypeId))
            {
                // Grammar is fine; the id is not in the registry. The producer
                // would skip this box without a word, which is exactly the
                // failure this tool exists to prevent.
                r.Status = BadgeUnknown;
                r.StatusTip = $"'{b.DrawingTypeId}' is not a drawing type in this project's registry. "
                            + "The generator will skip this box. Pick from the combo, or add the type "
                            + "in the Drawing Type editor.";
                r.IsBlocked = false;
                return;
            }

            r.Status = BadgeOk;
            r.StatusTip = $"Binds to '{b.DrawingTypeId}'"
                        + (b.LevelCode != null ? $", level {b.LevelCode}" : "")
                        + (b.Tag != null ? $", tag {b.Tag}" : "") + ".";
            r.IsBlocked = false;
        }

        private string SummaryLine()
        {
            int ok = _rows.Count(r => r.Status == BadgeOk);
            int unk = _rows.Count(r => r.Status == BadgeUnknown);
            int bad = _rows.Count(r => r.Status == BadgeBad);
            int non = _rows.Count(r => r.Status == BadgeNotSting);
            int pend = _rows.Count(r => r.Include && r.IsRenamePending && !r.IsBlocked);
            return $"{_rows.Count} scope boxes — 🟢 {ok} valid · 🟡 {unk} unknown type · "
                 + $"🔴 {bad} invalid · ⚪ {non} not STING   |   {pend} rename(s) ready";
        }

        private void ReportValidation()
        {
            var sb = new StringBuilder();
            sb.AppendLine(SummaryLine()).AppendLine();
            foreach (var r in _rows.Where(r => r.Status == BadgeBad || r.Status == BadgeUnknown))
            {
                sb.AppendLine($"{r.Status}  {r.CurrentName}");
                if (r.IsRenamePending) sb.AppendLine($"      → {r.NewName}");
                sb.AppendLine($"      {r.StatusTip.Replace("\n", "\n      ")}");
                sb.AppendLine();
            }
            if (sb.Length < 120) sb.AppendLine("Nothing to fix — every box is valid or deliberately non-STING.");
            ShowReport("Scope Box Validation", sb.ToString());
        }

        // ── Bulk fill + pattern ──────────────────────────────────────────

        private IEnumerable<ScopeBoxManagerRow> Checked() => _rows.Where(r => r.Include);

        private void BulkSetType()
        {
            var display = _bulkType?.SelectedItem as string;
            var choice = _types.FirstOrDefault(t => t.Display == display);
            if (choice == null) { SetStatus("Pick a drawing type first."); return; }
            int n = 0;
            foreach (var r in Checked()) { r.SetQuiet(choice.Id, r.LevelCode, r.Tag); n++; }
            Recompute();
            SetStatus(n == 0 ? "No rows checked." : $"Set drawing type on {n} row(s). {SummaryLine()}");
        }

        private void BulkSetLevel()
        {
            var lvl = (_bulkLevel?.Text ?? "").Trim();
            int n = 0;
            foreach (var r in Checked()) { r.SetQuiet(r.DrawingTypeId, lvl, r.Tag); n++; }
            Recompute();
            SetStatus(n == 0 ? "No rows checked." : $"Set level on {n} row(s). {SummaryLine()}");
        }

        private static readonly Regex _patternToken =
            new Regex(@"\{([A-Za-z]+)(?::D(\d+))?\}", RegexOptions.Compiled);

        /// <summary>
        /// Render the pattern into each checked row's New name. Unknown tokens
        /// are reported rather than left as literal braces — a literal "{BLD}"
        /// in a scope-box name fails the grammar, and leaving it there would
        /// reproduce exactly the class of bug K-8 records.
        /// </summary>
        private void ApplyPattern()
        {
            var pattern = _patternBox?.Text ?? "";
            if (string.IsNullOrWhiteSpace(pattern)) { SetStatus("Pattern is empty."); return; }

            var unknown = new List<string>();
            int index = 0, applied = 0;

            foreach (var r in Checked())
            {
                index++;
                int i = index;
                var rendered = _patternToken.Replace(pattern, m =>
                {
                    var key = m.Groups[1].Value.ToUpperInvariant();
                    var width = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                    switch (key)
                    {
                        case "TYPE":  return r.DrawingTypeId ?? "";
                        case "LEVEL": return r.LevelCode ?? "";
                        case "TAG":   return r.Tag ?? "";
                        case "NAME":  return r.CurrentName ?? "";
                        case "INDEX": return width > 0 ? i.ToString("D" + width) : i.ToString(CultureInfo.InvariantCulture);
                        default:
                            if (!unknown.Contains(m.Value)) unknown.Add(m.Value);
                            return m.Value;
                    }
                });

                // Feed the result back through the parser so the row's fields
                // stay the single source of the name — otherwise the grid would
                // show a name the combo does not agree with.
                if (ScopeBoxBinder.TryParseName(rendered, out var b, out _))
                {
                    r.SetQuiet(b.DrawingTypeId, b.LevelCode, b.Tag);
                    r.RawOverride = null;
                    applied++;
                }
                else
                {
                    // Not parseable — hold the attempt verbatim so the operator
                    // sees what their pattern produced and Judge can paint it
                    // red. Recomposing from the fields here would quietly show
                    // a different, valid-looking name and hide the mistake.
                    r.RawOverride = rendered;
                }
            }

            Recompute();

            var msg = applied == 0 && index == 0
                ? "No rows checked."
                : $"Pattern applied to {index} row(s).";
            if (unknown.Count > 0)
                msg += $"  ⚠ unknown token(s) {string.Join(", ", unknown)} left literal — "
                     + "they will fail the grammar. " + PatternTokenHelp;
            SetStatus(msg);
        }

        // ── Filter ───────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            var q = SearchText;
            _visible = string.IsNullOrEmpty(q)
                ? _rows.ToList()
                : _rows.Where(r =>
                        (r.CurrentName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (r.NewName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (r.DrawingTypeId ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            RefreshItems(_visible);
            SetStatus(SummaryLine());
        }

        // ── Select in model ──────────────────────────────────────────────

        private void OnRowSelected(object sender, SelectionChangedEventArgs e)
        {
            // RefreshItems nulls and re-sets ItemsSource, which fires this with
            // items REMOVED and none added. Zooming the model on a filter
            // keystroke would be maddening — act only on a real user pick.
            if (e.AddedItems == null || e.AddedItems.Count != 1) return;
            if (!(e.AddedItems[0] is ScopeBoxManagerRow row)) return;
            SelectInModel(new ElementId(row.RevitIdValue));
        }

        /// <summary>
        /// Select + zoom the box in Revit. Selection / ShowElements need no
        /// transaction and are safe from a modeless window — the pattern is
        /// lifted verbatim from StingPlacementCenter.SelectInModel.
        /// </summary>
        private void SelectInModel(ElementId id)
        {
            if (_uiDoc == null || id == null || id == ElementId.InvalidElementId) return;
            try
            {
                _uiDoc.Selection.SetElementIds(new List<ElementId> { id });
                try { _uiDoc.ShowElements(id); } catch { }
            }
            catch (Exception ex) { StingLog.Warn($"ScopeBoxManager.SelectInModel: {ex.Message}"); }
        }

        // ── Model writes ─────────────────────────────────────────────────

        private void RunInline(string title, Func<UIApplication, string> work)
        {
            if (_actionEvent == null)
            {
                SetStatus($"{title} unavailable — close and reopen the Scope Box Manager.");
                return;
            }
            _pendingTitle = title;
            _pendingAction = work;
            SetStatus($"{title}…");
            try { _actionEvent.Raise(); }
            catch (Exception ex)
            {
                StingLog.Error($"ScopeBoxManager.RunInline {title}", ex);
                SetStatus($"{title} could not start: {ex.Message}");
            }
        }

        private sealed class ActionHandler : IExternalEventHandler
        {
            private readonly ScopeBoxManagerDialog _o;
            public ActionHandler(ScopeBoxManagerDialog o) { _o = o; }
            public string GetName() => "STING Scope Box Manager Action";
            public void Execute(UIApplication app)
            {
                var work = _o._pendingAction;
                var title = _o._pendingTitle;
                _o._pendingAction = null;
                if (work == null) return;
                string report;
                try { report = work(app); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { report = "Cancelled."; }
                catch (Exception ex)
                {
                    StingLog.Error($"ScopeBoxManager action '{title}'", ex);
                    report = $"ERROR: {ex.Message}";
                }
                var r = report;
                try { _o.Dispatcher.BeginInvoke(new Action(() => _o.AfterAction(title, r))); } catch { }
            }
        }

        private void AfterAction(string title, string report)
        {
            LoadRows();
            SetStatus($"{title}: {report.Split('\n')[0]}");
            ShowReport(title, report);
        }

        // ── Rename (two-pass) ────────────────────────────────────────────

        private void RunRename()
        {
            var todo = _rows.Where(r => r.Include && r.IsRenamePending && !r.IsBlocked).ToList();
            int blocked = _rows.Count(r => r.Include && r.IsRenamePending && r.IsBlocked);

            if (todo.Count == 0)
            {
                SetStatus(blocked > 0
                    ? $"Nothing to rename — {blocked} checked row(s) are 🔴 invalid. Fix them first."
                    : "Nothing to rename — check some rows and give them a drawing type.");
                return;
            }

            var plan = todo.ToDictionary(r => r.RevitIdValue, r => r.NewName);
            int blockedCount = blocked;

            RunInline("Rename scope boxes", app =>
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null) return "No document.";
                return TwoPassRename(doc, plan, blockedCount);
            });
        }

        /// <summary>
        /// Two-pass rename. The single-pass implementation in
        /// ProjectSetupCommand walks a takenNames HashSet and SKIPS on
        /// collision, so an A→B / B→A swap half-fails and only warns — the
        /// user gets a project where one box took the other's name and the
        /// other kept its own. Renaming everything to a temporary first
        /// removes every transient collision, which is what
        /// BatchRenumberSheets already does for sheet numbers.
        /// </summary>
        private static string TwoPassRename(Document doc, Dictionary<long, string> plan, int blocked)
        {
            int renamed = 0, failed = 0;
            var log = new List<string>();

            using (var tx = new Transaction(doc, "STING Rename Scope Boxes"))
            {
                tx.Start();

                // Pass 1 — park every subject on a name nothing can collide with.
                var parked = new List<(Element el, string target, string original)>();
                foreach (var kv in plan)
                {
                    var el = doc.GetElement(new ElementId(kv.Key));
                    if (el == null) { failed++; log.Add($"  ✗ element {kv.Key} no longer exists"); continue; }
                    var original = el.Name;
                    try
                    {
                        el.Name = $"__STING_TMP_{kv.Key}";
                        parked.Add((el, kv.Value, original));
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        log.Add($"  ✗ '{original}' could not be parked: {ex.Message}");
                    }
                }

                // Pass 2 — every target name is now free of subject-vs-subject
                // conflict. A failure here is a genuine clash with a box that
                // is NOT part of this batch, so report it and restore.
                foreach (var (el, target, original) in parked)
                {
                    try { el.Name = target; renamed++; }
                    catch (Exception ex)
                    {
                        failed++;
                        log.Add($"  ✗ '{original}' → '{target}': {ex.Message}");
                        // Never leave a box sitting on __STING_TMP_… .
                        try { el.Name = original; }
                        catch (Exception ex2)
                        {
                            log.Add($"     ⚠ and could not be restored to '{original}': {ex2.Message}");
                        }
                    }
                }

                if (renamed > 0 || failed == 0) tx.Commit(); else tx.RollBack();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Renamed {renamed} scope box(es).");
            if (failed > 0)
            {
                sb.AppendLine($"{failed} failed:");
                foreach (var l in log) sb.AppendLine(l);
            }
            if (blocked > 0)
                sb.AppendLine($"\n{blocked} checked row(s) were skipped as 🔴 invalid and never attempted.");
            StingLog.Info($"ScopeBoxManager: renamed {renamed}, failed {failed}, blocked {blocked}");
            return sb.ToString();
        }

        // ── Absorbed stub actions ────────────────────────────────────────

        /// <summary>Read-only usage report — absorbed from the TaskDialog stub
        /// this dialog replaces, so nothing that existed is lost.</summary>
        private void RunAudit()
        {
            if (_doc == null) return;
            var sb = new StringBuilder();
            var boxes = Docs.DocAutomationHelper.GetScopeBoxes(_doc);
            var byId = new Dictionary<ElementId, List<string>>();
            foreach (var b in boxes) byId[b.Id] = new List<string>();

            int assigned = 0, unassigned = 0;
            foreach (var v in new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>())
            {
                if (v.IsTemplate || v is ViewSheet || !v.CanBePrinted) continue;
                var p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                var id = p?.AsElementId();
                if (id != null && id != ElementId.InvalidElementId)
                {
                    assigned++;
                    if (byId.TryGetValue(id, out var l)) l.Add(v.Name);
                }
                else unassigned++;
            }

            sb.AppendLine($"Scope boxes: {boxes.Count} · views assigned: {assigned} · unassigned: {unassigned}");
            sb.AppendLine();
            foreach (var b in boxes)
            {
                var views = byId[b.Id];
                sb.AppendLine($"[{b.Name}] — {views.Count} view(s)");
                foreach (var vn in views.Take(5)) sb.AppendLine($"    • {vn}");
                if (views.Count > 5) sb.AppendLine($"    … and {views.Count - 5} more");
            }
            var unused = boxes.Where(b => byId[b.Id].Count == 0).ToList();
            if (unused.Count > 0)
            {
                sb.AppendLine().AppendLine("── UNUSED SCOPE BOXES ──");
                foreach (var b in unused) sb.AppendLine($"  {b.Name} (0 views)");
            }
            ShowReport("Scope Box Usage Audit", sb.ToString());
        }

        private void RunClearAssignments()
        {
            var confirm = new TaskDialog("Clear scope-box assignments")
            {
                MainInstruction = "Remove the scope box from every view?",
                MainContent = "This clears the crop-region binding on all views in the project. "
                            + "It does not delete any scope box and does not change any name.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) { SetStatus("Clear cancelled."); return; }

            RunInline("Clear scope-box assignments", app =>
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null) return "No document.";
                int cleared = 0;
                using (var tx = new Transaction(doc, "STING Clear Scope Box Assignments"))
                {
                    tx.Start();
                    foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                    {
                        if (v.IsTemplate || v is ViewSheet) continue;
                        try
                        {
                            var p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                            if (p == null || p.IsReadOnly) continue;
                            if (p.AsElementId() == ElementId.InvalidElementId) continue;
                            p.Set(ElementId.InvalidElementId);
                            cleared++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Clear scope box on '{v.Name}': {ex.Message}"); }
                    }
                    tx.Commit();
                }
                return $"Cleared the scope box from {cleared} view(s).";
            });
        }

        private void RunAutoAssign()
        {
            RunInline("Auto-assign scope boxes", app =>
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null) return "No document.";
                var boxes = Docs.DocAutomationHelper.GetScopeBoxes(doc);
                int assigned = 0;
                using (var tx = new Transaction(doc, "STING Auto-Assign Scope Boxes"))
                {
                    tx.Start();
                    foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                    {
                        if (v.IsTemplate || v is ViewSheet || !v.CanBePrinted) continue;
                        try
                        {
                            var p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                            if (p == null || p.IsReadOnly) continue;
                            if (p.AsElementId() != ElementId.InvalidElementId) continue;
                            var best = Docs.DocAutomationHelper.FindBestScopeBox(v, boxes);
                            if (best == null) continue;
                            p.Set(best.Id);
                            assigned++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Auto-assign on '{v.Name}': {ex.Message}"); }
                    }
                    tx.Commit();
                }
                return $"Auto-assigned a scope box to {assigned} view(s).";
            });
        }

        private void RunGenerate()
        {
            int ready = _rows.Count(r => r.Status == BadgeOk);
            if (ready == 0)
            {
                SetStatus("No 🟢 valid boxes to generate from. Fix the names first.");
                return;
            }
            int pending = _rows.Count(r => r.IsRenamePending);
            if (pending > 0)
            {
                var td = new TaskDialog("Generate from scope boxes")
                {
                    MainInstruction = $"{pending} row(s) have an uncommitted rename.",
                    MainContent = "Generation reads names from the MODEL, not from this grid, so "
                                + "those boxes will be generated under their current names. "
                                + "Rename first?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Yes,
                };
                var res = td.Show();
                if (res == TaskDialogResult.Cancel) { SetStatus("Generate cancelled."); return; }
                if (res == TaskDialogResult.Yes) { RunRename(); return; }
            }
            // Hand straight to the existing producer — this dialog fixes names,
            // it does not duplicate view production.
            StingDockPanel.DispatchCommand("DrawingTypes_FromScopeBoxes");
            SetStatus($"Dispatched Generate From Scope Boxes for {ready} valid box(es).");
        }

        // ── Reporting ────────────────────────────────────────────────────

        private static void ShowReport(string title, string body)
        {
            try
            {
                var td = new TaskDialog(title)
                {
                    MainInstruction = title,
                    MainContent = body.Length > 4000 ? body.Substring(0, 4000) + "\n… (truncated — see StingTools.log)" : body,
                };
                td.Show();
                if (body.Length > 4000) StingLog.Info($"{title}\n{body}");
            }
            catch (Exception ex) { StingLog.Warn($"ScopeBoxManager.ShowReport: {ex.Message}"); }
        }
    }

    internal static class ScopeBoxManagerRowExtensions
    {
        /// <summary>
        /// Set the three grammar fields without triggering a recompute per
        /// field — bulk fill and pattern apply touch every checked row, and
        /// recomputing collisions three times per row is O(3n²) for no gain.
        /// The caller recomputes once at the end.
        /// </summary>
        internal static void SetQuiet(this ScopeBoxManagerRow row, string type, string level, string tag)
        {
            var saved = row.Recompute;
            row.Recompute = null;
            try
            {
                row.DrawingTypeId = type;
                row.LevelCode = level;
                row.Tag = tag;
            }
            finally { row.Recompute = saved; }
        }
    }
}
