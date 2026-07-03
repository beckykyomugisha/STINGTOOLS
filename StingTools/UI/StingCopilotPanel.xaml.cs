using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        public StingCopilotPanel()
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); }
            catch { /* theme is non-fatal */ }
            _instance = this;

            AppendBubble("assistant",
                "Hi — I'm the STING Copilot. Ask me about the model or tell me what to do " +
                "(e.g. \"how many ducts are under 100mm?\" or \"tag the mechanical elements in this view\"). " +
                "I preview any change before running it.");
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

            Task.Run(async () =>
            {
                CopilotTurn turn;
                try
                {
                    turn = await StingLlmService.Instance
                        .RunCopilotTurnAsync(convo, ct, confirmCallback, onToolStart);
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
    }
}
