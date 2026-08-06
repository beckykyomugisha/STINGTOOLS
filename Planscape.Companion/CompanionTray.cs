using System.Drawing;
using System.Windows.Forms;

namespace Planscape.Companion;

/// <summary>
/// The tray icon. Deliberately minimal this pass: an icon whose state reflects
/// <see cref="SyncStatus"/>, a tooltip, and three menu items.
///
/// The design's richer tray surface — a checked-out count, click-to-expand into a
/// document list — is a later slice. What is here is the part that carries the
/// error-visibility decision (plan §1c): <b>Offline is quiet, Error is not.</b>
/// No toasts, from anywhere, ever: the most common failure is a closed laptop,
/// and an app that notifies about that gets muted, taking the real errors with it.
///
/// The icon is drawn in code rather than shipped as a .ico — three coloured dots
/// is not worth a binary asset in the repo, and drawing it keeps the state→colour
/// mapping in one readable place.
/// </summary>
internal sealed class CompanionTray : IDisposable
{
    private readonly CompanionService _service;
    private readonly NotifyIcon _icon;
    private readonly Dictionary<SyncState, Icon> _icons = new();
    private ToolStripMenuItem _checkedOutItem = null!;

    public CompanionTray(CompanionService service)
    {
        _service = service;

        var menu = new ContextMenuStrip();
        // Click-to-expand: the count on the tooltip, the names one click away.
        // Rebuilt each time it opens so it never shows a stale list.
        _checkedOutItem = new ToolStripMenuItem("Checked out") { Enabled = false };
        menu.Opening += (_, _) => RebuildCheckedOutMenu();
        menu.Items.Add(_checkedOutItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sync now", null, (_, _) => _ = SyncNowAsync());
        menu.Items.Add("Open sync folder", null, (_, _) => OpenFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("View log", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());

        _icon = new NotifyIcon
        {
            Icon = IconFor(service.Status.State),
            Text = service.Status.Summary(),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenFolder();

        _service.StatusChanged += OnStatusChanged;
    }

    private void OnStatusChanged()
    {
        // Raised from the sync loop, which is not the UI thread. Touching
        // NotifyIcon from there throws intermittently rather than reliably —
        // the worst kind of bug to leave in a background app.
        if (_icon.ContextMenuStrip is { } menu && menu.InvokeRequired)
        {
            menu.BeginInvoke(new Action(Repaint));
            return;
        }
        Repaint();
    }

    private void Repaint()
    {
        try
        {
            _icon.Icon = IconFor(_service.Status.State);
            // NotifyIcon.Text throws above 63 characters; Summary() is built to fit.
            _icon.Text = _service.Status.Summary();
        }
        catch (Exception ex)
        {
            CompanionLog.Warn($"tray repaint failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Slice E — "N checked out", expanding to the file names.
    ///
    /// Status only, no editing: the design is explicit that the tray is not a
    /// second UI surface to maintain. Clicking a name opens the containing folder
    /// with the file selected, which is the one thing a user actually wants from
    /// here and costs no UI of our own.
    /// </summary>
    private void RebuildCheckedOutMenu()
    {
        try
        {
            var st = _service.Status;
            _checkedOutItem.DropDownItems.Clear();
            _checkedOutItem.Text = st.CheckedOutCount == 0
                ? "No working copies"
                : $"{st.CheckedOutCount} checked out";
            _checkedOutItem.Enabled = st.CheckedOutCount > 0;
            if (st.CheckedOutCount == 0) return;

            var settings = CompanionSettings.Load();
            foreach (var entry in st.CheckedOut)
            {
                // Entries are "PROJECTCODE/filename" — see RefreshCheckedOut.
                var slash = entry.IndexOf('/');
                var code = slash > 0 ? entry.Substring(0, slash) : null;
                var name = slash > 0 ? entry.Substring(slash + 1) : entry;

                var item = new ToolStripMenuItem(entry);
                item.Click += (_, _) => RevealInExplorer(settings, code, name);
                _checkedOutItem.DropDownItems.Add(item);
            }

            if (st.CheckedOutCount > st.CheckedOut.Count)
                _checkedOutItem.DropDownItems.Add(new ToolStripMenuItem(
                    $"… and {st.CheckedOutCount - st.CheckedOut.Count} more") { Enabled = false });
        }
        catch (Exception ex)
        {
            CompanionLog.Warn($"checked-out menu: {ex.Message}");
        }
    }

    private static void RevealInExplorer(CompanionSettings settings, string? projectCode, string fileName)
    {
        try
        {
            var folder = CompanionPaths.ResolveProjectFolder(projectCode);
            if (string.IsNullOrEmpty(folder)) return;
            var path = Path.Combine(folder, fileName);
            // /select, puts the file itself under the cursor rather than dumping
            // the user in a folder to hunt through.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                Arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{folder}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { CompanionLog.Warn($"reveal '{fileName}': {ex.Message}"); }
    }

    private async Task SyncNowAsync()
    {
        try
        {
            var result = await _service.SyncNowAsync(null);
            CompanionLog.Info($"tray sync-now: {result}");
        }
        catch (Exception ex)
        {
            CompanionLog.Error("tray sync-now failed", ex);
        }
    }

    private void OpenFolder()
    {
        try
        {
            var settings = CompanionSettings.Load();
            var root = string.IsNullOrWhiteSpace(settings.RootFolder)
                ? CompanionSettings.DefaultRoot
                : settings.RootFolder!;
            Directory.CreateDirectory(root);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(root) { UseShellExecute = true });
        }
        catch (Exception ex) { CompanionLog.Warn($"could not open the sync folder: {ex.Message}"); }
    }

    private void OpenLog()
    {
        try
        {
            if (File.Exists(CompanionLog.Path))
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(CompanionLog.Path) { UseShellExecute = true });
        }
        catch (Exception ex) { CompanionLog.Warn($"could not open the log: {ex.Message}"); }
    }

    /// <summary>
    /// State → glyph. Slice E.
    ///
    /// <para><b>Shape as well as colour.</b> At 16×16 in a crowded notification
    /// area, colour alone is a weak signal, and for a colour-blind user it is no
    /// signal at all — so each state also differs in form: a filled disc when
    /// idle, a ring while syncing, a hollow outline when offline, and a disc with
    /// a bar through it on error.</para>
    ///
    /// <para><b>Offline is grey and quiet</b>, not red. It is expected and
    /// self-healing, and painting it as a fault is exactly what makes a user stop
    /// reading the icon — at which point the red one means nothing either
    /// (plan §1c). Only Error is red, and Error is the only state that has an
    /// accompanying message naming what a human has to fix.</para>
    /// </summary>
    private Icon IconFor(SyncState state)
    {
        if (_icons.TryGetValue(state, out var cached)) return cached;

        var colour = state switch
        {
            SyncState.Idle    => Color.FromArgb(0x2E, 0x7D, 0x32),   // green — synced
            SyncState.Syncing => Color.FromArgb(0x15, 0x65, 0xC0),   // blue  — working
            SyncState.Offline => Color.FromArgb(0x9E, 0x9E, 0x9E),   // grey  — quiet
            SyncState.Error   => Color.FromArgb(0xC6, 0x28, 0x28),   // red   — needs a human
            _ => Color.Gray,
        };

        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(colour);
            using var pen = new Pen(colour, 2f);

            switch (state)
            {
                case SyncState.Offline:
                    // Hollow: "nothing is flowing" reads at a glance.
                    g.DrawEllipse(pen, 3, 3, 10, 10);
                    break;

                case SyncState.Syncing:
                    // Ring with a gap — a progress arc, not a full stop.
                    g.DrawArc(pen, 3, 3, 10, 10, 45, 270);
                    break;

                case SyncState.Error:
                    // Disc with a bar. Distinct in silhouette from every other
                    // state even in a monochrome or high-contrast theme.
                    g.FillEllipse(brush, 2, 2, 12, 12);
                    using (var bar = new SolidBrush(Color.White))
                        g.FillRectangle(bar, 7, 4, 2, 6);
                    using (var dot = new SolidBrush(Color.White))
                        g.FillRectangle(dot, 7, 11, 2, 2);
                    break;

                default: // Idle
                    g.FillEllipse(brush, 2, 2, 12, 12);
                    break;
            }
        }
        var icon = Icon.FromHandle(bmp.GetHicon());
        _icons[state] = icon;
        return icon;
    }

    public void Dispose()
    {
        _service.StatusChanged -= OnStatusChanged;
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var icon in _icons.Values) icon.Dispose();
    }
}
