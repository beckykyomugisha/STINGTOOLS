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

    public CompanionTray(CompanionService service)
    {
        _service = service;

        var menu = new ContextMenuStrip();
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
    /// State → colour. Offline is grey, not red: it is expected and self-healing,
    /// and colouring it as a fault is what makes users stop reading the icon.
    /// </summary>
    private Icon IconFor(SyncState state)
    {
        if (_icons.TryGetValue(state, out var cached)) return cached;

        var colour = state switch
        {
            SyncState.Idle => Color.FromArgb(0x2E, 0x7D, 0x32),    // green — synced
            SyncState.Syncing => Color.FromArgb(0x15, 0x65, 0xC0), // blue — working
            SyncState.Offline => Color.FromArgb(0x9E, 0x9E, 0x9E), // grey — quiet
            SyncState.Error => Color.FromArgb(0xC6, 0x28, 0x28),   // red — needs a human
            _ => Color.Gray,
        };

        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(colour);
            g.FillEllipse(brush, 2, 2, 12, 12);
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
