// ══════════════════════════════════════════════════════════════════════
//  SitePhotosNdaPrompt — Phase 180 BCC NDA acceptance modal.
//
//  Mirrors the mobile NdaPromptModal: shows the project's NdaText
//  (fetched lazily from the photo policy) plus a one-tap accept that
//  POSTs /accept-nda. After acceptance the caller refreshes its list
//  and the lock badge disappears.
// ══════════════════════════════════════════════════════════════════════

#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.UI
{
    internal static class SitePhotosNdaPrompt
    {
        private const string DefaultText =
            "This photo is provided under a non-disclosure agreement.\n\n" +
            "• You will not redistribute, reproduce, or publish the photo " +
            "or its derivatives outside the project team.\n" +
            "• You acknowledge the photo may contain commercially or " +
            "contractually sensitive content.\n" +
            "• Your acceptance is logged with timestamp, IP, and user-agent.\n\n" +
            "By clicking 'Accept & view' you confirm you have read and " +
            "accept these terms for this photo.";

        /// <summary>
        /// Show the NDA prompt for the given (project, photo). Returns
        /// true on accept (and a successful POST to /accept-nda),
        /// false on cancel or server error.
        /// </summary>
        public static async Task<bool> ShowAsync(
            Window owner, Guid projectId, Guid photoId, string? policyText = null)
        {
            var dlg = new Window
            {
                Title = "NDA acceptance required",
                Width = 520, Height = 380,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock {
                Text = "NDA acceptance required",
                FontSize = 15, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });
            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 220,
                BorderBrush = Brushes.Gainsboro, BorderThickness = new Thickness(1),
            };
            var body = new TextBlock
            {
                Text = policyText ?? DefaultText,
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(10),
            };
            sv.Content = body;
            sp.Children.Add(sv);

            // Lazy-fetch the project policy NdaText if the caller didn't
            // provide it. Failure is silent — the default text covers it.
            if (string.IsNullOrEmpty(policyText))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var pol = await PlanscapeServerClient.Instance.GetPhotoPolicyAsync(projectId);
                        if (!string.IsNullOrEmpty(pol?.NdaText))
                            owner.Dispatcher.Invoke(() => body.Text = pol!.NdaText);
                    }
                    catch (Exception ex) { StingLog.Warn($"NdaPrompt fetch policy: {ex.Message}"); }
                });
            }

            bool accepted = false;
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var cancel = new Button { Content = "Cancel", Width = 90, Height = 30, IsCancel = true,
                Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (_, _) => dlg.Close();
            var ok = new Button {
                Content = "Accept & view", Width = 130, Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
            };
            ok.Click += async (_, _) =>
            {
                ok.IsEnabled = false; cancel.IsEnabled = false;
                var rowOk = await PlanscapeServerClient.Instance.AcceptPhotoNdaAsync(projectId, photoId);
                accepted = rowOk;
                if (!rowOk)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("NDA",
                        "Could not record acceptance — see log.\n\n" +
                        (PlanscapeServerClient.Instance.LastError ?? "(no detail)"));
                    ok.IsEnabled = true; cancel.IsEnabled = true;
                    return;
                }
                dlg.Close();
            };
            btnRow.Children.Add(cancel);
            btnRow.Children.Add(ok);
            sp.Children.Add(btnRow);
            dlg.Content = sp;
            dlg.ShowDialog();
            return accepted;
        }
    }
}
