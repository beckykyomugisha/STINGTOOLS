using System.Windows;
using System.Windows.Controls;
using StingTools.Core.Licensing;

namespace StingTools.UI
{
    public static class ActivationDialog
    {
        public static void ShowModal()
        {
            var win = new Window
            {
                Title = "Activate STING Tools",
                Width = 540, Height = 460, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                // Topmost so the dialog is never hidden behind the "Always on top" STING
                // dock panel — otherwise an unlicensed state reads as a frozen/"Busy"
                // plugin because the modal is waiting off-screen for input.
                Topmost = true,
                ShowInTaskbar = true
            };
            var root = new StackPanel { Margin = new Thickness(16) };
            var current = LicenseGate.Status;

            string heading =
                current.IsTrial ? "STING Tools trial — " + current.DaysLeft + " day" + (current.DaysLeft == 1 ? "" : "s") + " left." :
                current.IsValid ? "STING Tools is activated on this machine." :
                current.State == LicenseState.TrialExpired ? "Your STING Tools trial has ended." :
                                  "STING Tools is not activated on this machine.";
            root.Children.Add(new TextBlock {
                Text = heading, TextWrapping = TextWrapping.Wrap,
                FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            root.Children.Add(new TextBlock {
                Text = "Paste a licence below. A portable licence from Planscape works on any machine " +
                       "as-is. For a licence tied to this machine, get it at https://planscape.build/licences — " +
                       "sign in, paste the machine code below, and download the file.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            var codeBox = new TextBox {
                // Stable, not LicenseGate.MachineCode (== MachineFingerprint.Current).
                // Current mixes in three WMI factors that fail transiently and flip the
                // code, silently invalidating a valid licence. VerifyEither accepts
                // either, so licences already issued against Current keep working.
                Text = MachineFingerprint.Stable, IsReadOnly = true,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 16, Margin = new Thickness(0, 0, 0, 4) };
            root.Children.Add(codeBox);

            var copyBtn = new Button {
                Content = "Copy machine code", Width = 160,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
            copyBtn.Click += (s, e) => { try { Clipboard.SetText(MachineFingerprint.Stable); } catch { } };
            root.Children.Add(copyBtn);

            root.Children.Add(new TextBlock { Text = "Paste your license below, then click Apply:", Margin = new Thickness(0, 0, 0, 4) });
            var licBox = new TextBox {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(licBox);

            var status = new TextBlock {
                Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap,
                Text = current.Message };
            root.Children.Add(status);

            var applyBtn = new Button { Content = "Apply license", Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
            applyBtn.Click += (s, e) =>
            {
                string err = LicenseGate.Apply(licBox.Text);
                if (err == null)
                {
                    // Already running (trial or a licence being renewed): nothing to reload.
                    status.Text = current.IsValid
                        ? "Activated. " + LicenseGate.Status.Message
                        : "Activated. Please restart Revit to load STING.";
                    status.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    status.Text = err;
                    status.Foreground = System.Windows.Media.Brushes.Red;
                }
            };
            root.Children.Add(applyBtn);

            win.Content = root;
            // Parent to the Revit main window so the modal centres on Revit and stays in
            // front (belt-and-braces with Topmost).
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(win);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { /* owner is best-effort */ }
            win.Loaded += (s, e) => { try { win.Activate(); } catch { } };
            win.ShowDialog();
        }
    }
}
