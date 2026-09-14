using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.V6
{
    // ══════════════════════════════════════════════════════════════════════
    //  QR_ScanCommission — advance commissioning from an actual SCAN.
    //
    //  WHY THIS EXISTS
    //  ---------------
    //  "QR commissioning" involved no QR. QRAdvanceCommissioningCommand reads the
    //  current Revit SELECTION; there was no scan path anywhere in the plugin. And
    //  it could not have had one: QRCommissioningWorkflow.Advance resolves elements
    //  by UniqueId, while QRCodeCommand encoded only ASS_TAG_1_TXT. The producer and
    //  its only intended consumer disagreed on the key, so even a working scanner
    //  could not have driven it.
    //
    //  Both halves are fixed: the payload now carries ?u={UniqueId} (see
    //  StingQrFormat.BuildElementUrl), and this command accepts a scanned payload
    //  and resolves it.
    //
    //  SCOPE, STATED HONESTLY. This is the DESKTOP scan path: a USB/Bluetooth
    //  barcode scanner (which types like a keyboard) or a pasted payload. The phone
    //  scanning straight into the Planscape app and advancing commissioning over the
    //  API is a SEPARATE build — Planscape.Server has no commissioning controller or
    //  entity at all, so there is nothing for the app to POST to. That gap is real
    //  and is logged; it is not papered over here.
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class QRScanCommissioningCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                var doc = ctx.Doc;

                string seed = ReadClipboardSafely();
                string scanned = PromptForScan(seed);
                if (scanned == null) return Result.Cancelled;

                var payload = StingQrFormat.Parse(scanned);
                if (payload == null)
                {
                    // Say WHAT was not understood. A bare "invalid code" leaves the
                    // operator unable to tell a mis-scan from an unsupported format.
                    TaskDialog.Show("STING — QR Commissioning",
                        "That is not a STING code.\n\n" +
                        $"Scanned:\n  {Truncate(scanned, 200)}\n\n" +
                        "Expected one of:\n" +
                        $"  {StingQrFormat.BaseUrl}/e/{{project}}/{{tag}}\n" +
                        "  a Revit UniqueId\n" +
                        "  an ISO 19650 tag, e.g. M-BLD1-Z01-L02-HVAC-SUP-AHU-0003");
                    return Result.Cancelled;
                }

                if (payload.Kind == StingQrKind.Sheet)
                {
                    TaskDialog.Show("STING — QR Commissioning",
                        $"That is a SHEET code ({payload.SheetNumber}), not an element.\n\n" +
                        "Commissioning advances a modelled element. Scan the code on the " +
                        "asset itself, not the one in the title block.");
                    return Result.Cancelled;
                }

                var (el, how) = Resolve(doc, payload);
                if (el == null)
                {
                    TaskDialog.Show("STING — QR Commissioning",
                        "Nothing in this model matches that code.\n\n" +
                        $"  UniqueId : {payload.UniqueId ?? "(not in the payload)"}\n" +
                        $"  Tag      : {payload.Tag ?? "(not in the payload)"}\n\n" +
                        "Is the right model open? A code minted from another project will " +
                        "parse correctly and still match nothing here.");
                    return Result.Cancelled;
                }

                string current = ParameterHelpers.GetString(el, ParamRegistry.COMM_STATE_TXT);
                string next = QRCommissioningWorkflow.NextState(current);

                var confirm = new TaskDialog("STING — QR Commissioning")
                {
                    MainInstruction = $"Advance to {next}?",
                    MainContent =
                        $"Element : {Describe(el)}\n" +
                        $"Matched : {how}\n" +
                        $"State   : {(string.IsNullOrWhiteSpace(current) ? "NOT_STARTED" : current)}  →  {next}\n" +
                        $"By      : {Environment.UserName}",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.Yes
                };
                if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                QRCommissioningWorkflow.TransitionResult tr;
                using (var t = new Transaction(doc, "STING QR Scan Commission"))
                {
                    t.Start();
                    tr = QRCommissioningWorkflow.Advance(doc, new QRCommissioningWorkflow.ScanPayload
                    {
                        ElementUniqueId = el.UniqueId,
                        Operative = Environment.UserName,
                        Notes = $"Scanned: {Truncate(payload.Raw, 160)}"
                    });
                    if (tr.Ok) t.Commit(); else t.RollBack();
                }

                if (tr.Ok)
                {
                    // Select it, so the operator can see on screen exactly what moved.
                    try { ctx.UIDoc?.Selection?.SetElementIds(new List<ElementId> { el.Id }); }
                    catch (Exception ex) { StingLog.Warn($"QR scan: could not select element: {ex.Message}"); }

                    TaskDialog.Show("STING — QR Commissioning",
                        $"{Describe(el)}\n\n{tr.FromState ?? "NOT_STARTED"}  →  {tr.ToState}\n\n" +
                        "The element is now selected in the model.");
                    StingLog.Info($"QR scan commission: {el.UniqueId} {tr.FromState} -> {tr.ToState} (matched by {how})");
                }
                else
                {
                    TaskDialog.Show("STING — QR Commissioning",
                        $"Not advanced.\n\n{Describe(el)}\n\n{tr.Reason}");
                    StingLog.Warn($"QR scan commission refused: {tr.Reason}");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("QRScanCommissioningCommand failed", ex);
                TaskDialog.Show("STING", $"QR scan failed: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>Find the element the payload names. UniqueId first because it is
        /// exact; tag second because it is what a printed label carries. Returns HOW
        /// it matched so the confirmation can show it — an operator seeing "matched by
        /// tag" knows a duplicate tag is possible, which "matched" alone hides.</summary>
        private static (Element el, string how) Resolve(Document doc, StingQrPayload payload)
        {
            if (!string.IsNullOrWhiteSpace(payload.UniqueId))
            {
                var byUid = doc.GetElement(payload.UniqueId);
                if (byUid != null) return (byUid, "UniqueId (exact)");
            }

            if (!string.IsNullOrWhiteSpace(payload.Tag))
            {
                var matches = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => string.Equals(
                        ParameterHelpers.GetString(e, ParamRegistry.TAG1),
                        payload.Tag, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToList();

                if (matches.Count == 1) return (matches[0], $"tag {payload.Tag}");
                if (matches.Count > 1)
                {
                    // Two elements carrying one tag is a data defect. Refuse rather
                    // than pick one — advancing the wrong asset's commissioning state
                    // is not something a later run can detect or undo.
                    TaskDialog.Show("STING — QR Commissioning",
                        $"More than one element carries the tag '{payload.Tag}'.\n\n" +
                        "Refusing to guess which one you scanned. Run RepairDuplicateSeq, " +
                        "or select the element and use 'QR Advance' instead.");
                    return (null, null);
                }
            }
            return (null, null);
        }

        private static string Describe(Element el)
        {
            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
            string cat = el.Category?.Name ?? "(no category)";
            return string.IsNullOrWhiteSpace(tag)
                ? $"{el.Name} · {cat} · id {el.Id.Value}"
                : $"{tag} · {el.Name} · {cat}";
        }

        private static string Truncate(string s, int n)
            => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";

        /// <summary>A handheld scanner usually types into the focused control, but
        /// site workflows also paste. Seeding from the clipboard saves a step and is
        /// harmless — the operator sees and can edit it before anything is written.</summary>
        private static string ReadClipboardSafely()
        {
            try
            {
                var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                if (string.IsNullOrWhiteSpace(text)) return "";
                text = text.Trim();
                // Only seed something that actually parses; a stray clipboard full of
                // an email would just be noise in the box.
                return StingQrFormat.Parse(text) != null ? text : "";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"QR scan: clipboard read failed: {ex.Message}");
                return "";
            }
        }

        /// <summary>Modal prompt. The textbox takes focus so a USB barcode scanner
        /// (which emits keystrokes + Enter) completes the dialog in one trigger pull.</summary>
        private static string PromptForScan(string seed)
        {
            string result = null;
            var win = new Window
            {
                Title = "STING — Scan to commission",
                Width = 560,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };
            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock
            {
                Text = "Scan the asset's QR code, or paste/type its payload, then press Enter.",
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{StingQrFormat.BaseUrl}/e/{{project}}/{{tag}}   ·   a UniqueId   ·   an ISO 19650 tag",
                Margin = new Thickness(0, 0, 0, 10),
                Opacity = 0.7,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap
            });

            // Qualified: Autodesk.Revit.UI also defines a TextBox (a ribbon control).
            var box = new System.Windows.Controls.TextBox { Text = seed ?? "", FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            stack.Children.Add(box);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var ok = new Button { Content = "Advance", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            ok.Click += (s, e) => { result = box.Text?.Trim(); win.DialogResult = true; };
            row.Children.Add(ok);
            row.Children.Add(cancel);
            stack.Children.Add(row);

            win.Content = stack;
            win.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            return win.ShowDialog() == true && !string.IsNullOrWhiteSpace(result) ? result : null;
        }
    }
}
