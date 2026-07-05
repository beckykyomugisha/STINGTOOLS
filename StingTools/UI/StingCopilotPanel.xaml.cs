using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Code-behind for the STING Copilot dockable panel — an in-Revit natural-language
    /// chat surface. The user types plain English; <see cref="StingLlmService.RunCopilotTurnAsync"/>
    /// runs an Anthropic tool-use loop that drives StingTools through the EXISTING MCP
    /// tools (shared <c>McpToolDispatcher</c>). Model-touching tools marshal onto the
    /// Revit API thread inside the dispatcher (via McpJobBridge), so this panel needs no
    /// IExternalEventHandler of its own.
    ///
    /// Threading: the Send handler snapshots the conversation on the UI thread, then runs
    /// the turn on a background Task. All UI mutations marshal back with Dispatcher.Invoke.
    /// The confirm dialog + running-tool indicator are supplied as callbacks the loop fires.
    /// </summary>
    public partial class StingCopilotPanel : Page
    {
        private static StingCopilotPanel _instance;
        public static StingCopilotPanel Instance => _instance;

        // Full text history for the current session (both roles).
        private readonly List<CopilotMessage> _history = new List<CopilotMessage>();

        private CancellationTokenSource _cts;
        private bool _busy;

        // One-click prompt starters (label → text placed in the input box).
        private static readonly (string label, string prompt)[] Suggestions =
        {
            ("Tag compliance",     "What is the current tag compliance?"),
            ("Untagged elements",  "Which elements are untagged, by discipline?"),
            ("Ducts under 100mm",  "How many ducts are under 100mm?"),
            ("Create a 5m wall",   "Create a 5 metre wall from 0,0 to 5000,0 that is 3000mm high. Dry-run it first."),
            ("Build a shell",      "Build an 8m x 6m building shell 3m high. Dry-run it first, then ask me to confirm."),
            ("What can you do?",   "What can you do? List your tool categories: read, tag, size, create, and discover."),
        };

        // Tools that mutate the model — used to decide whether to offer an Undo chip.
        private static readonly HashSet<string> MutatingTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "create_wall", "create_floor", "create_floor_in_room", "create_roof", "create_duct",
            "create_pipe", "create_room", "place_family", "building_shell",
            "set_parameter", "auto_tag", "tag_scheme_render", "size_ducts", "size_pipes",
            "size_cables", "build_panel_schedules", "invoke_capability",
        };

        public StingCopilotPanel()
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); }
            catch { /* theme is non-fatal */ }
            _instance = this;

            AppendBubble("assistant",
                "Hi — I'm the STING Copilot. Ask me about the model or tell me what to do " +
                "(e.g. \"how many ducts are under 100mm?\" or \"create a 5m wall in this view\"). " +
                "I preview any change before running it, and I can build walls, floors, ducts, pipes, " +
                "rooms, families and whole shells.");

            RefreshChips(showUndo: false);
        }

        // ── Send ─────────────────────────────────────────────────────────────────

        private void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            string text = InputBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            AppendBubble("user", text);
            _history.Add(new CopilotMessage("user", text));
            InputBox.Clear();

            // Snapshot the conversation for the background turn.
            var convo = new List<CopilotMessage>(_history);

            SetBusy(true);
            StatusText.Text = "thinking…";
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;

            Func<string, bool> confirmCallback = ConfirmOnUiThread;
            Action<string> onToolStart = ToolStartOnUiThread;
            Action<string, string> onToolResult = ToolResultOnUiThread;

            Task.Run(async () =>
            {
                CopilotTurn turn;
                try
                {
                    turn = await StingLlmService.Instance
                        .RunCopilotTurnAsync(convo, ct, confirmCallback, onToolStart, onToolResult);
                }
                catch (OperationCanceledException)
                {
                    turn = new CopilotTurn { FinalText = "(cancelled)" };
                }
                catch (Exception ex)
                {
                    StingLog.Error("Copilot panel turn failed", ex);
                    turn = new CopilotTurn { Error = true, ErrorMessage = ex.Message,
                                             FinalText = $"Copilot error: {ex.Message}" };
                }

                // Marshal ALL UI updates back to the UI thread.
                Dispatcher.Invoke(() =>
                {
                    string answer = string.IsNullOrEmpty(turn.FinalText)
                        ? "(no response)" : turn.FinalText;
                    AppendBubble("assistant", answer);
                    _history.Add(new CopilotMessage("assistant", answer));

                    if (turn.ToolsUsed != null && turn.ToolsUsed.Count > 0)
                        StatusText.Text = "tools used: " + string.Join(", ", turn.ToolsUsed);
                    else
                        StatusText.Text = string.Empty;

                    if (turn.InputTokens > 0 || turn.OutputTokens > 0)
                        UsageText.Text = $"~{turn.InputTokens} in / {turn.OutputTokens} out tokens. " +
                                         "Each turn calls the LLM API and consumes credits.";
                    else
                        UsageText.Text = "Each turn calls the LLM API and consumes credits.";

                    // Offer an Undo affordance when this turn mutated the model.
                    bool mutated = turn.ToolsUsed != null && turn.ToolsUsed.Any(MutatingTools.Contains);
                    RefreshChips(showUndo: mutated);

                    SetBusy(false);
                });
            });
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            try { _cts?.Cancel(); } catch { }
            StatusText.Text = "cancelling…";
            AppendBubble("assistant", "(cancelled)");
        }

        // ── Callbacks fired from the background loop ──────────────────────────────

        /// <summary>Progress: show which tool is currently running (marshalled to the UI thread).</summary>
        private void ToolStartOnUiThread(string toolName)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = string.IsNullOrEmpty(toolName)
                        ? "running tool…" : $"running tool: {toolName}";
                });
            }
            catch { /* best-effort progress */ }
        }

        /// <summary>Transparency: append a compact activity line to the chat as each tool completes.</summary>
        private void ToolResultOnUiThread(string toolName, string compactSummary)
        {
            try
            {
                Dispatcher.Invoke(() =>
                    AppendActivity($"🔧 {toolName}" +
                        (string.IsNullOrEmpty(compactSummary) ? "" : $" — {compactSummary}")));
            }
            catch { /* best-effort transparency */ }
        }

        /// <summary>
        /// Confirm-before-write: show a Yes/No TaskDialog (with the projected count carried
        /// in <paramref name="summary"/>) on the UI thread and return the user's choice.
        /// </summary>
        private bool ConfirmOnUiThread(string summary)
        {
            try
            {
                return Dispatcher.Invoke(() =>
                {
                    var td = new TaskDialog("STING Copilot — confirm write")
                    {
                        MainInstruction = "The Copilot wants to make a change to the model.",
                        MainContent = summary,
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No,
                    };
                    return td.Show() == TaskDialogResult.Yes;
                });
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Copilot confirm dialog failed: {ex.Message}");
                return false;
            }
        }

        // ── UI helpers ────────────────────────────────────────────────────────────

        private void SetBusy(bool busy)
        {
            _busy = busy;
            SendBtn.IsEnabled = !busy;
            CancelBtn.IsEnabled = busy;
            InputBox.IsEnabled = !busy;
        }

        private void AppendBubble(string role, string text)
        {
            bool isUser = role == "user";
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(isUser ? 40 : 0, 3, isUser ? 0 : 40, 3),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = new SolidColorBrush(isUser
                    ? Color.FromRgb(0x2D, 0x5B, 0x88)
                    : Color.FromRgb(0x3A, 0x3A, 0x3A)),
                Child = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.White,
                    FontSize = 11,
                },
            };
            ChatStack.Children.Add(bubble);
            try { ChatScroll.ScrollToEnd(); } catch { }
        }

        /// <summary>Append a small, dim activity line (tool call) — visually distinct from chat bubbles.</summary>
        private void AppendActivity(string text)
        {
            var line = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Opacity = 0.75,
                Margin = new Thickness(2, 1, 40, 1),
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xC4, 0xDE)),
            };
            ChatStack.Children.Add(line);
            try { ChatScroll.ScrollToEnd(); } catch { }
        }

        // ── Suggestion chips + undo ──────────────────────────────────────────────

        /// <summary>Rebuild the chip row: an optional Undo chip, then the prompt-starter chips.</summary>
        private void RefreshChips(bool showUndo)
        {
            ChipPanel.Children.Clear();

            if (showUndo)
                ChipPanel.Children.Add(MakeChip("↶ Undo last", isAccent: true, onClick: (_, __) =>
                {
                    AppendBubble("assistant",
                        "Press Ctrl+Z in Revit to undo the last change. Every write is transaction-wrapped, " +
                        "so undo reverts it cleanly.");
                }));

            foreach (var (label, prompt) in Suggestions)
            {
                string p = prompt;   // capture
                ChipPanel.Children.Add(MakeChip(label, isAccent: false, onClick: (_, __) =>
                {
                    InputBox.Text = p;
                    try { InputBox.Focus(); InputBox.CaretIndex = InputBox.Text.Length; } catch { }
                }));
            }
        }

        /// <summary>Build one clickable chip (rounded, theme-neutral).</summary>
        private static Button MakeChip(string label, bool isAccent, RoutedEventHandler onClick)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 10,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(8, 3, 8, 3),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0x80, 0x80, 0x80)),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(isAccent
                    ? Color.FromRgb(0x6E, 0x4A, 0x2A)   // warm accent for Undo
                    : Color.FromRgb(0x3A, 0x3A, 0x3A)),
            };
            btn.Click += onClick;
            return btn;
        }
    }
}
