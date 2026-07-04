using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        // Full text history for the current session (both roles) — this IS the LLM
        // conversation context passed to each turn. Clearing it resets the context.
        private readonly List<CopilotMessage> _history = new List<CopilotMessage>();

        // Rendered transcript (greeting + user + assistant + tool activity) for save /
        // screenshot. role is "user" | "assistant" | "activity".
        private readonly List<(string role, string text)> _transcript = new List<(string, string)>();

        private const string GreetingText =
            "Hi — I'm the STING Copilot. Ask me about the model or tell me what to do " +
            "(e.g. \"how many ducts are under 100mm?\" or \"tag the mechanical elements in this view\"). " +
            "I preview any change before running it.";

        private CancellationTokenSource _cts;
        private bool _busy;

        public StingCopilotPanel()
        {
            InitializeComponent();
            try { ThemeManager.RegisterTarget(this); ThemeManager.InitialiseResources(); }
            catch { /* theme is non-fatal */ }
            _instance = this;

            ShowGreeting();
        }

        private void ShowGreeting() => AppendBubble("assistant", GreetingText);

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
                    if (!string.IsNullOrEmpty(toolName))
                        AppendActivity($"🔧 {toolName}");
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
            _transcript.Add((isUser ? "user" : "assistant", text));
            try { ChatScroll.ScrollToEnd(); } catch { }
        }

        /// <summary>Append a dim tool-activity line to the chat (and the transcript).</summary>
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
            _transcript.Add(("activity", text));
            try { ChatScroll.ScrollToEnd(); } catch { }
        }

        // ── Header toolbar: clear / save / screenshot / settings ────────────────────

        /// <summary>Clear chat: reset messages AND the LLM conversation context, re-show greeting.</summary>
        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                TaskDialog.Show("STING Copilot", "A turn is still running — cancel or wait for it to finish first.");
                return;
            }
            if (_history.Count > 0)
            {
                var td = new TaskDialog("STING Copilot — clear chat")
                {
                    MainInstruction = "Clear the chat and reset the conversation?",
                    MainContent = "This removes the current messages and the AI's memory of this session. It cannot be undone.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                };
                if (td.Show() != TaskDialogResult.Yes) return;
            }

            _history.Clear();          // resets the LLM conversation context (each turn passes a snapshot of this)
            _transcript.Clear();
            ChatStack.Children.Clear();
            StatusText.Text = string.Empty;
            UsageText.Text = "Each turn calls the LLM API and consumes credits.";
            ShowGreeting();
        }

        /// <summary>Save chat: write the transcript to a Markdown (.md) file.</summary>
        private void SaveChatBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_transcript.Count == 0)
                {
                    TaskDialog.Show("STING Copilot", "Nothing to save yet.");
                    return;
                }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Copilot chat",
                    Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
                    DefaultExt = ".md",
                    FileName = $"Copilot_Chat_{DateStamp()}.md",
                };
                if (dlg.ShowDialog() != true) return;

                File.WriteAllText(dlg.FileName, BuildMarkdown(), Encoding.UTF8);
                StingLog.Info($"Copilot chat saved: {Path.GetFileName(dlg.FileName)} ({_transcript.Count} line(s)).");
            }
            catch (Exception ex)
            {
                StingLog.Error("Copilot chat save failed", ex);
                TaskDialog.Show("STING Copilot", "Could not save the chat: " + ex.Message);
            }
        }

        private string BuildMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# STING Copilot — Chat Transcript");
            sb.AppendLine();
            sb.AppendLine($"- Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
            try
            {
                sb.AppendLine($"- Model: {StingLlmService.Instance.ActiveModel} " +
                              $"(provider: {StingLlmService.Instance.ActiveProvider})");
            }
            catch { /* model line is best-effort */ }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            foreach (var (role, text) in _transcript)
            {
                switch (role)
                {
                    case "user":      sb.AppendLine($"**You:** {text}"); break;
                    case "assistant": sb.AppendLine($"**Copilot:** {text}"); break;
                    case "activity":  sb.AppendLine($"> {text}"); break;
                    default:          sb.AppendLine(text); break;
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>Screenshot the full chat to PNG (file or clipboard).</summary>
        private void ScreenshotBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var bmp = RenderChatToBitmap();
                if (bmp == null)
                {
                    TaskDialog.Show("STING Copilot", "Nothing to capture yet.");
                    return;
                }

                var td = new TaskDialog("STING Copilot — Screenshot")
                {
                    MainInstruction = "Chat screenshot ready.",
                    MainContent = "Save the full chat as a PNG, or copy it to the clipboard to paste elsewhere.",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Save as PNG…");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Copy to clipboard");
                var r = td.Show();

                if (r == TaskDialogResult.CommandLink1)
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Save Copilot screenshot",
                        Filter = "PNG image (*.png)|*.png",
                        DefaultExt = ".png",
                        FileName = $"Copilot_Chat_{DateStamp()}.png",
                    };
                    if (dlg.ShowDialog() != true) return;
                    using (var fs = File.Create(dlg.FileName))
                    {
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bmp));
                        enc.Save(fs);
                    }
                    StingLog.Info($"Copilot screenshot saved: {Path.GetFileName(dlg.FileName)}.");
                }
                else if (r == TaskDialogResult.CommandLink2)
                {
                    System.Windows.Clipboard.SetImage(bmp);
                    StatusText.Text = "screenshot copied to clipboard";
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Copilot screenshot failed", ex);
                TaskDialog.Show("STING Copilot", "Could not capture the chat: " + ex.Message);
            }
        }

        /// <summary>Render the FULL chat content (not just the visible viewport) to a bitmap.</summary>
        private RenderTargetBitmap RenderChatToBitmap()
        {
            double w = ChatStack.ActualWidth;
            double h = ChatStack.ActualHeight;
            if (w < 1 || h < 1) return null;

            // Solid dark canvas so white bubble text stays legible (chat bg is transparent).
            var bg = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
                var vb = new VisualBrush(ChatStack)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                };
                dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
            }

            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>Gear affordance — open the Copilot Settings (LLM config) dialog.</summary>
        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StingLlmConfigDialog.Show())
                    AppendActivity("⚙ Settings saved — the new provider/model is active now.");
            }
            catch (Exception ex)
            {
                StingLog.Error("Open Copilot Settings from panel failed", ex);
            }
        }

        private static string DateStamp() => DateTime.Now.ToString("yyyyMMdd_HHmm");
    }
}
