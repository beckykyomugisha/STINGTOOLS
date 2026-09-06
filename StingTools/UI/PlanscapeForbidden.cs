// ══════════════════════════════════════════════════════════════════════
//  PlanscapeForbidden — the BCC's single forbidden treatment (#558).
//
//  THE FOUR STATES. Every server-backed pane in this client owes the user
//  four visually distinct answers:
//
//    loading    — a spinner / "Loading…"
//    empty      — grey italic, "No albums yet…"
//    error      — SitePhotosTabHelpers.BuildLoadFailure, red, the server's
//                 own reason, plus "this is not an empty result"
//    forbidden  — THIS FILE. Amber, a lock, and the capability named.
//
//  Before this, the BCC had three. A 403 fell through the generic failure
//  path and rendered as the couldn't-load treatment, so someone who simply
//  lacks a capability was told the system was broken. "Call IT" and "ask
//  your PM" are different actions; a UI that cannot tell them apart sends
//  people to the wrong one.
//
//  ONE TREATMENT, NOT ONE PER SCREEN. Every string here lives in
//  Describe() so the wording is changed in one place. Hand-rolling the
//  copy at each call site is what produced three clients that disagree
//  about who is allowed to do what.
//
//  THE ROLE NAMES BELOW ARE COPY, NOT A GATE. They exist to tell the user
//  who to ask. Nothing in this file decides anything — the server decides,
//  and CapabilityState carries its answer. If the server's rule changes,
//  this copy is stale text, not a broken permission check.
//
//  THREE STATES. Unknown is not denied. See
//  PlanscapeServerClient.Capabilities.cs for why, and #634.
// ══════════════════════════════════════════════════════════════════════

#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StingTools.BIMManager;

namespace StingTools.UI
{
    /// <summary>The capabilities this client knows how to talk about. One
    /// entry per boolean the server serves.</summary>
    internal enum PlanscapeCapability
    {
        CurateProject,
        ApproveSitePhotos,
    }

    internal static class PlanscapeForbidden
    {
        // Amber, deliberately NOT the crimson used by BuildLoadFailure. The
        // two states must be distinguishable at a glance, not only by reading.
        private static readonly Brush Amber     = new SolidColorBrush(Color.FromRgb(0xB2, 0x6A, 0x00));
        private static readonly Brush AmberFill = new SolidColorBrush(Color.FromRgb(0xFF, 0xF5, 0xE5));
        private static readonly Brush AmberLine = new SolidColorBrush(Color.FromRgb(0xF0, 0xD5, 0xA8));

        /// <summary>
        /// The user-facing sentence for a capability. Names the capability and
        /// who holds it — never the HTTP status.
        /// </summary>
        internal static string Describe(PlanscapeCapability cap) => cap switch
        {
            PlanscapeCapability.CurateProject =>
                "Only a project manager or BIM coordinator can curate albums, checklists and distribution groups.",
            PlanscapeCapability.ApproveSitePhotos =>
                "Only a project manager can approve site photos, issue share links, or change the photo policy.",
            _ => "You do not have this capability on this project.",
        };

        /// <summary>Short form for a tooltip on a disabled control.</summary>
        internal static string Tooltip(PlanscapeCapability cap)
            => Describe(cap) + "\n\nAsk a project manager to grant it.";

        /// <summary>
        /// The inline forbidden panel — use where a list or detail pane cannot
        /// be shown at all.
        /// </summary>
        internal static UIElement BuildPanel(string headline, PlanscapeCapability cap, string? serverDetail = null)
        {
            var border = new Border
            {
                Background      = AmberFill,
                BorderBrush     = AmberLine,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(10, 8, 10, 8),
                Margin          = new Thickness(6),
            };
            var sp = new StackPanel();

            sp.Children.Add(new TextBlock
            {
                Text            = "🔒 " + headline,
                Foreground      = Amber,
                FontWeight      = FontWeights.SemiBold,
                TextWrapping    = TextWrapping.Wrap,
            });
            sp.Children.Add(new TextBlock
            {
                Text            = Describe(cap),
                Foreground      = Amber,
                Opacity         = 0.9,
                TextWrapping    = TextWrapping.Wrap,
                Margin          = new Thickness(0, 3, 0, 0),
            });
            sp.Children.Add(new TextBlock
            {
                // Says plainly that this is a permission answer, not a fault.
                // Without this line the pane reads like something went wrong.
                Text            = "This is not a failure — the server refused the request because of your role on this project.",
                FontStyle       = FontStyles.Italic,
                Foreground      = Brushes.Gray,
                TextWrapping    = TextWrapping.Wrap,
                Margin          = new Thickness(0, 6, 0, 0),
            });
            if (!string.IsNullOrWhiteSpace(serverDetail))
            {
                sp.Children.Add(new TextBlock
                {
                    Text         = serverDetail,
                    FontSize     = 10,
                    Foreground   = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 4, 0, 0),
                });
            }

            border.Child = sp;
            return border;
        }

        /// <summary>
        /// Report a refused ACTION (as opposed to a refused pane). Same wording,
        /// delivered through the dialog the action would have used anyway.
        /// </summary>
        internal static void ShowDialog(string title, PlanscapeCapability cap, string? serverDetail = null)
        {
            var body = Describe(cap)
                     + "\n\nThis is not a failure — the server refused the request because of your role on this project."
                     + (string.IsNullOrWhiteSpace(serverDetail) ? "" : "\n\n" + serverDetail);
            Autodesk.Revit.UI.TaskDialog.Show(title, body);
        }

        /// <summary>
        /// Route a failed call to the right report. Call it immediately after
        /// the await that failed, while <see cref="PlanscapeServerClient.LastStatus"/>
        /// still describes that call.
        ///
        /// A 403 is a refusal and gets the forbidden treatment. Anything else —
        /// including a transport failure, where LastStatus is null — is a
        /// failure and keeps the red couldn't-load treatment. Unknown is never
        /// dressed up as denied.
        /// </summary>
        internal static UIElement BuildFailureOrForbidden(
            string failureHeadline, string forbiddenHeadline, PlanscapeCapability cap)
        {
            var client = PlanscapeServerClient.Instance;
            return client.LastStatus == 403
                ? BuildPanel(forbiddenHeadline, cap)
                : SitePhotosTabHelpers.BuildLoadFailure(failureHeadline, client.LastError);
        }

        /// <summary>Dialog counterpart of <see cref="BuildFailureOrForbidden"/>.</summary>
        internal static void ShowFailureOrForbidden(string title, PlanscapeCapability cap)
        {
            var client = PlanscapeServerClient.Instance;
            if (client.LastStatus == 403) { ShowDialog(title, cap); return; }
            Autodesk.Revit.UI.TaskDialog.Show(title, client.LastError ?? "(no detail)");
        }

        /// <summary>
        /// Apply a KNOWN-denied capability to a control: disable it and attach
        /// the reason.
        ///
        /// Deliberately does nothing when the state is Allowed **or Unknown**.
        /// Unknown leaves the control enabled so the attempt can report — the
        /// three-state rule. Callers must not pre-compute a boolean and pass it
        /// here, because that collapses the third state on the way in.
        /// </summary>
        internal static void ApplyIfDenied(Control control, CapabilityState state, PlanscapeCapability cap)
        {
            if (state != CapabilityState.Denied) return;
            control.IsEnabled = false;
            control.ToolTip   = Tooltip(cap);
            control.Opacity   = 0.55;
        }

        /// <summary>
        /// A one-line banner naming a capability the user is known to lack, for
        /// the top of a pane whose controls have just been disabled. Returns
        /// null for Allowed and for Unknown — there is nothing honest to say
        /// about a capability we could not determine.
        /// </summary>
        internal static UIElement? BuildBannerIfDenied(CapabilityState state, PlanscapeCapability cap)
        {
            if (state != CapabilityState.Denied) return null;
            return new Border
            {
                Background      = AmberFill,
                BorderBrush     = AmberLine,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(8, 5, 8, 5),
                Margin          = new Thickness(0, 0, 0, 8),
                Child = new TextBlock
                {
                    Text         = "🔒 " + Describe(cap),
                    Foreground   = Amber,
                    FontSize     = 11,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
        }
    }
}
