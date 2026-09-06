// StingLink.exe — the registered handler for planscape:// URLs.
//
// Windows launches this with one argument, the whole URL. The job is small on
// purpose:
//
//   1. Drop the URL into the shared inbox (StingTools.Core.PlanscapeProtocol).
//   2. If a Revit window is up, bring it to the front — the plugin's Idling
//      watcher takes it from there, usually within a tick.
//   3. If Revit is NOT running, say so. Launching Revit from here is NOT
//      implemented; see the note at LaunchNote below, and
//      docs/PLANSCAPE_PROTOCOL.md for exactly how far this got.
//
// It must never hang: it runs on a user's click, and a stuck handler shows up
// as "the link does nothing" with a stray process in Task Manager.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using StingTools.Core;

namespace StingTools.LinkHandler
{
    internal static class Program
    {
        private const uint MB_OK = 0x0;
        private const uint MB_ICONINFORMATION = 0x40;
        private const uint MB_ICONERROR = 0x10;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        [STAThread]
        private static int Main(string[] args)
        {
            // No argument at all = someone ran the exe directly. Tell them what
            // it is instead of exiting silently, and offer the one thing that is
            // useful without a URL: re-registering the protocol.
            if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
                return Explain();

            string raw = args[0].Trim();

            if (string.Equals(raw, "--register", StringComparison.OrdinalIgnoreCase))
                return RegisterSelf();
            if (string.Equals(raw, "--unregister", StringComparison.OrdinalIgnoreCase))
            {
                PlanscapeProtocol.Unregister(out string undetail);
                Info("planscape:// links are no longer handled by StingTools."
                     + (string.IsNullOrEmpty(undetail) ? "" : "\n\n" + undetail));
                return 0;
            }

            var link = PlanscapeProtocol.Parse(raw);
            if (link == null)
            {
                Error($"That does not look like a Planscape link:\n\n{raw}\n\n"
                      + "Expected something like planscape://issue/<id>.");
                return 2;
            }

            if (PlanscapeProtocol.Drop(raw) == null)
            {
                Error("Could not hand the link to StingTools.\n\n"
                      + "The link inbox could not be written:\n"
                      + PlanscapeProtocol.InboxDir);
                return 3;
            }

            IntPtr revit = FindRevitWindow();
            if (revit != IntPtr.Zero)
            {
                // Restore first: a minimised window ignores SetForegroundWindow.
                if (IsIconic(revit)) ShowWindowAsync(revit, SW_RESTORE);
                SetForegroundWindow(revit);
                // Nothing more to say — the plugin picks the link up on its next
                // Idling tick and opens the Coordination Center itself. A dialog
                // here would be a second thing to dismiss.
                return 0;
            }

            Info(LaunchNote(link));
            return 0;
        }

        /// <summary>
        /// The honest version of the not-running case.
        ///
        /// Auto-launching Revit is NOT implemented: there are up to three
        /// versions installed (2025/2026/2027), the link carries no project file
        /// path, and starting the wrong Revit — or any Revit, unasked, because
        /// someone clicked a chat link — is worse than saying nothing happened.
        /// The link is already queued, so opening Revit by hand within
        /// PlanscapeProtocol.MaxAge completes the journey.
        /// </summary>
        private static string LaunchNote(PlanscapeLink link) =>
            "Revit does not appear to be running.\n\n"
            + $"The link ({link}) has been queued. Open Revit with the STING Tools "
            + $"plugin loaded within {(int)PlanscapeProtocol.MaxAge.TotalMinutes} minutes "
            + "and the Coordination Center will jump to it.";

        /// <summary>
        /// Revit's main window class is "Rvt_MainWindow" — the same string
        /// BIMCoordinationCenter.Show uses to anchor itself. Enumerating
        /// processes is the fallback because a Revit that is still starting up
        /// may not have registered the class yet.
        /// </summary>
        private static IntPtr FindRevitWindow()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("Revit"))
                {
                    using (p)
                    {
                        if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
                    }
                }
            }
            catch (Exception) { /* fall through — treated as "not running" */ }
            return IntPtr.Zero;
        }

        private static int Explain()
        {
            string current = PlanscapeProtocol.RegisteredCommand();
            Info("StingLink — the Windows handler for planscape:// links.\n\n"
                 + "It is not meant to be run directly; Windows launches it when you click a "
                 + "planscape:// link.\n\n"
                 + "Currently registered command:\n"
                 + (string.IsNullOrEmpty(current) ? "(none)" : current)
                 + "\n\nRun with --register to point the protocol at this copy, or --unregister to remove it.");
            return 0;
        }

        private static int RegisterSelf()
        {
            string exe = Process.GetCurrentProcess().MainModule?.FileName;
            bool changed = PlanscapeProtocol.EnsureRegistered(exe, out string detail);
            Info(changed
                ? "planscape:// links now open through StingTools.\n\n" + detail
                : "No change was made.\n\n" + detail);
            return changed ? 0 : 1;
        }

        private static void Info(string text) =>
            MessageBoxW(IntPtr.Zero, text, "Planscape link", MB_OK | MB_ICONINFORMATION);

        private static void Error(string text) =>
            MessageBoxW(IntPtr.Zero, text, "Planscape link", MB_OK | MB_ICONERROR);
    }
}
