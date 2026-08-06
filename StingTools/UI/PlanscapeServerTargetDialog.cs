#nullable enable
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.UI;

/// <summary>
/// Server target picker for the BCC (#563).
///
/// <para>Three things this dialog must get right, in order of how much damage getting
/// them wrong causes:</para>
///
/// <list type="number">
/// <item><b>Never appear to switch when it has not.</b>
/// <c>ResolveDefaultServerUrl</c> caches for the process lifetime, and the live client,
/// its SignalR hub and the current session are already bound to the old base. So the
/// restart requirement is stated on the dialog before the user commits, and again in the
/// confirmation after. A control that looks like it worked and did not is worse than no
/// control.</item>
///
/// <item><b>Never write a target the user did not choose.</b> The dialog is the only
/// caller of <see cref="PlanscapeServerTargets.SetActiveTarget"/>, and it calls it only
/// from the confirmed Switch action. Cancel writes nothing. Probing writes nothing.</item>
///
/// <item><b>Never hide that an env override is in effect.</b> If
/// <c>STING_PLANSCAPE_URL</c> is set it beats the saved value, so showing the saved value
/// as though it were live would be a confident lie. The dialog says so and keeps the
/// switch available (it still updates the saved value for when the override goes away)
/// while being explicit that it will not change this machine's behaviour today.</item>
/// </list>
/// </summary>
internal static class PlanscapeServerTargetDialog
{
    private static readonly Color CProd = Color.FromRgb(0x2E, 0x7D, 0x32);   // green
    private static readonly Color CDev = Color.FromRgb(0xE6, 0x51, 0x00);   // orange
    private static readonly Color CWarn = Color.FromRgb(0xB7, 0x1C, 0x1C);   // red
    private static readonly Color CMuted = Color.FromRgb(0x61, 0x61, 0x61);

    /// <summary>Show the picker. Returns true when the user committed a switch (so the
    /// caller can refresh its header), false on cancel or no-op.</summary>
    public static bool Show(Window? owner)
    {
        var active = PlanscapeServerTargets.GetActiveTarget();
        var targets = PlanscapeServerTargets.LoadTargets();

        var dlg = new Window
        {
            Title = "Planscape server",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
        };
        if (owner != null) dlg.Owner = owner;

        var root = new StackPanel { Margin = new Thickness(16) };

        // ── What is in effect RIGHT NOW, and why ──────────────────────────
        root.Children.Add(new TextBlock
        {
            Text = "CURRENTLY IN EFFECT",
            FontWeight = FontWeights.Bold, FontSize = 11,
            Foreground = new SolidColorBrush(CMuted),
            Margin = new Thickness(0, 0, 0, 4),
        });

        var activeBox = new Border
        {
            Background = new SolidColorBrush(active.IsNonProduction
                ? Color.FromRgb(0xFF, 0xF3, 0xE0)
                : Color.FromRgb(0xE8, 0xF5, 0xE9)),
            BorderBrush = new SolidColorBrush(active.IsNonProduction ? CDev : CProd),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 12),
        };
        var activeStack = new StackPanel();
        activeStack.Children.Add(new TextBlock
        {
            Text = (active.IsNonProduction ? "⚠ " : "") + active.Label,
            FontWeight = FontWeights.Bold, FontSize = 14,
            Foreground = new SolidColorBrush(active.IsNonProduction ? CDev : CProd),
        });
        activeStack.Children.Add(new TextBlock
        {
            Text = active.Url, FontSize = 11, Foreground = new SolidColorBrush(CMuted),
            Margin = new Thickness(0, 2, 0, 0),
        });
        activeStack.Children.Add(new TextBlock
        {
            // Naming the SOURCE is half the point — "which server" without "and why"
            // leaves the user unable to work out what to change.
            Text = active.Source switch
            {
                PlanscapeServerTargets.ActiveSource.EnvironmentVariable =>
                    $"Source: {PlanscapeServerClient.ServerUrlEnvVar} environment variable (overrides the saved setting)",
                PlanscapeServerTargets.ActiveSource.SavedSetting =>
                    "Source: saved setting on this machine",
                _ => "Source: built-in default (nothing saved on this machine)",
            },
            FontSize = 10, Foreground = new SolidColorBrush(CMuted),
            Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        activeBox.Child = activeStack;
        root.Children.Add(activeBox);

        // ── Env override warning ─────────────────────────────────────────
        if (active.EnvOverrideActive)
        {
            root.Children.Add(new TextBlock
            {
                Text = $"⚠ {PlanscapeServerClient.ServerUrlEnvVar} is set, and it wins over anything chosen here. "
                     + "Switching below updates the saved setting for when the variable is removed, "
                     + "but will NOT change which server this machine uses while it remains set.",
                FontSize = 11, Foreground = new SolidColorBrush(CWarn),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            });
        }

        // ── Choose ───────────────────────────────────────────────────────
        root.Children.Add(new TextBlock
        {
            Text = "SWITCH TO", FontWeight = FontWeights.Bold, FontSize = 11,
            Foreground = new SolidColorBrush(CMuted), Margin = new Thickness(0, 0, 0, 4),
        });

        var combo = new ComboBox { Height = 28, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var t in targets)
            combo.Items.Add(new ComboBoxItem { Content = t.ToString(), Tag = t });
        // Preselect whatever is active, so the dialog opens on the truth rather than on
        // the first row.
        combo.SelectedIndex = Math.Max(0, targets.FindIndex(t =>
            string.Equals(PlanscapeServerClient.NormalizeServerUrl(t.Url), active.Url,
                          StringComparison.OrdinalIgnoreCase)));
        root.Children.Add(combo);

        var status = new TextBlock
        {
            FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8), MinHeight = 32,
            Foreground = new SolidColorBrush(CMuted),
        };
        root.Children.Add(status);

        root.Children.Add(new TextBlock
        {
            Text = "Switching takes effect after Revit is restarted. The current session, "
                 + "its live connection and any open panels stay on the server above.",
            FontSize = 11, Foreground = new SolidColorBrush(CWarn),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        });

        // ── Buttons ──────────────────────────────────────────────────────
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var testBtn = new Button
        {
            Content = "Test connection", Height = 28, Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 0), FontSize = 11,
        };
        var switchBtn = new Button
        {
            Content = "Switch", Height = 28, Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(0, 0, 8, 0), FontSize = 11, IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
        };
        var cancelBtn = new Button
        {
            Content = "Cancel", Height = 28, Padding = new Thickness(16, 0, 16, 0),
            FontSize = 11, IsCancel = true,
        };

        PlanscapeServerTargets.ServerTarget? Selected() =>
            (combo.SelectedItem as ComboBoxItem)?.Tag as PlanscapeServerTargets.ServerTarget;

        testBtn.Click += async (_, _) =>
        {
            var t = Selected();
            if (t == null) return;
            testBtn.IsEnabled = false;
            status.Text = $"Contacting {t.Url}…";
            status.Foreground = new SolidColorBrush(CMuted);
            try
            {
                var (ok, detail) = await PlanscapeServerTargets.ProbeAsync(t.Url).ConfigureAwait(true);
                status.Text = detail;
                status.Foreground = new SolidColorBrush(ok ? CProd : CWarn);
            }
            catch (Exception ex)
            {
                // Never let a probe failure look like a probe success.
                status.Text = $"Test failed: {ex.Message}";
                status.Foreground = new SolidColorBrush(CWarn);
                StingLog.Warn($"[server-picker] probe error: {ex.Message}");
            }
            finally { testBtn.IsEnabled = true; }
        };

        bool switched = false;
        switchBtn.Click += async (_, _) =>
        {
            var t = Selected();
            if (t == null) return;

            if (string.Equals(PlanscapeServerClient.NormalizeServerUrl(t.Url), active.Url,
                              StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "That is already the active target — nothing to change.";
                status.Foreground = new SolidColorBrush(CMuted);
                return;
            }

            // Probe BEFORE committing, so a dead URL is refused at the point of choosing
            // rather than surfacing after a Revit restart as a confusing in-app error.
            switchBtn.IsEnabled = false;
            status.Text = $"Checking {t.Url}…";
            status.Foreground = new SolidColorBrush(CMuted);
            var (ok, detail) = await PlanscapeServerTargets.ProbeAsync(t.Url).ConfigureAwait(true);
            switchBtn.IsEnabled = true;

            if (!ok)
            {
                // Offer the override rather than blocking outright — a local stack that
                // is merely not started yet is a legitimate thing to point at. But the
                // user has to say so explicitly, having been told.
                var proceed = MessageBox.Show(dlg,
                    $"{detail}\n\nSwitch to it anyway?\n\n"
                    + "Reasonable if the server is simply not running yet (a local docker stack, say). "
                    + "If you did not expect this, check the URL first.",
                    "Server did not respond",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (proceed != MessageBoxResult.Yes)
                {
                    status.Text = detail + " — not switched.";
                    status.Foreground = new SolidColorBrush(CWarn);
                    return;
                }
            }

            if (t.IsNonProduction)
            {
                var confirm = MessageBox.Show(dlg,
                    $"Switch to a NON-PRODUCTION server?\n\n{t.Label}\n{t.Url}\n\n"
                    + "The BCC will show data from this server, which can look just like real "
                    + "project data. The header will mark the session, but you should switch back "
                    + "to Production when you are done.",
                    "Confirm non-production target",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
                if (confirm != MessageBoxResult.OK) { status.Text = "Not switched."; return; }
            }

            // The single deliberate write.
            if (!PlanscapeServerTargets.SetActiveTarget(t.Url))
            {
                status.Text = "Could not save the target — see StingTools.log.";
                status.Foreground = new SolidColorBrush(CWarn);
                return;
            }

            switched = true;
            MessageBox.Show(dlg,
                $"Saved: {t.Label}\n{t.Url}\n\n"
                + "RESTART REVIT for this to take effect.\n\n"
                + "This session is still connected to " + active.Url + ".",
                "Restart required", MessageBoxButton.OK, MessageBoxImage.Information);
            dlg.Close();
        };

        cancelBtn.Click += (_, _) => dlg.Close();

        row.Children.Add(testBtn);
        row.Children.Add(switchBtn);
        row.Children.Add(cancelBtn);
        root.Children.Add(row);

        dlg.Content = root;
        dlg.ShowDialog();
        return switched;
    }
}
