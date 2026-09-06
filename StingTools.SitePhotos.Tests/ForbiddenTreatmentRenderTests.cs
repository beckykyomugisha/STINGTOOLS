#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace StingTools.SitePhotos.Tests;

/// <summary>
/// The BCC's four states, BUILT — loading / empty / error / forbidden.
///
/// SCOPE, STATED HONESTLY. This constructs the real WPF elements the panel
/// renders and inspects the resulting visual tree: brushes, glyphs, wording.
/// It does not photograph a running Revit session — the plugin is not deployed
/// here, deliberately, because the shared .addin slot currently belongs to
/// another session. What is verified is everything up to the moment WPF puts
/// pixels on a screen.
///
/// WHY IT EXISTS. #642's own CapabilityStateTests verify which state is
/// SELECTED (Allowed / Denied / Unknown). They say nothing about whether the
/// selected states LOOK different. The failure this file rules out is the one
/// #642 was written to fix in the first place: a refusal rendering in the
/// couldn't-load treatment, so someone who merely lacks a capability is told
/// the system is broken. Four states that all render red are not four states.
///
/// PlanscapeForbidden is `internal` and the plugin ships no InternalsVisibleTo,
/// so it is reached by reflection over the built StingTools.dll — the same
/// assembly the other tests in this project bind to.
/// </summary>
public class ForbiddenTreatmentRenderTests
{
    // ── One STA thread for the whole class ────────────────────────────────────
    //
    // WPF objects must be created on an STA thread, and every DispatcherObject
    // acquires affinity to the thread that created it. PlanscapeForbidden holds
    // its palette in `static readonly` SolidColorBrush fields, so those brushes
    // belong to whichever STA thread first touched the type — and reading
    // `.Color` from any other thread throws.
    //
    // A fresh thread per test therefore passes when a test is run alone and
    // fails when the class is run together, which is exactly the shape of a
    // harness bug masquerading as a product bug. One shared worker removes it.
    private static readonly System.Collections.Concurrent.BlockingCollection<Action> _work = new();
    private static readonly Thread _sta = StartSta();

    private static Thread StartSta()
    {
        var t = new Thread(() => { foreach (var job in _work.GetConsumingEnumerable()) job(); })
        {
            IsBackground = true,
            Name = "sitephotos-wpf-sta",
        };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return t;
    }

    private static T OnSta<T>(Func<T> f)
    {
        _ = _sta; // ensure started
        T result = default!;
        Exception? failure = null;
        using var done = new ManualResetEventSlim(false);
        _work.Add(() =>
        {
            try { result = f(); }
            catch (Exception e) { failure = e; }
            finally { done.Set(); }
        });
        done.Wait();
        if (failure != null)
            throw new Exception("STA worker threw: " + failure.Message, failure);
        return result;
    }

    private static Type ForbiddenType =>
        typeof(StingTools.BIMManager.PlanscapeServerClient).Assembly
            .GetType("StingTools.UI.PlanscapeForbidden", throwOnError: true)!;

    private static object CapabilityValue(string name) =>
        Enum.Parse(
            typeof(StingTools.BIMManager.PlanscapeServerClient).Assembly
                .GetType("StingTools.UI.PlanscapeCapability", throwOnError: true)!,
            name);

    private static UIElement BuildForbiddenPanel(string headline, string capability, string? detail = null)
    {
        var m = ForbiddenType.GetMethod("BuildPanel", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (UIElement)m.Invoke(null, new object?[] { headline, CapabilityValue(capability), detail })!;
    }

    /// <summary>Every TextBlock in a built subtree, in order.</summary>
    private static List<TextBlock> TextBlocks(DependencyObject root)
    {
        var found = new List<TextBlock>();
        void Walk(DependencyObject d)
        {
            if (d is TextBlock tb) found.Add(tb);
            if (d is Border b && b.Child != null) Walk(b.Child);
            else if (d is Panel p) foreach (UIElement c in p.Children) Walk(c);
        }
        Walk(root);
        return found;
    }

    private static (byte R, byte G, byte B)? Rgb(Brush? b) =>
        b is SolidColorBrush s ? (s.Color.R, s.Color.G, s.Color.B) : null;

    // ── The assertion this whole file is for ──────────────────────────────────

    [Fact]
    public void The_forbidden_treatment_is_not_the_error_treatment()
    {
        var (forbiddenFg, forbiddenFill, text) = OnSta(() =>
        {
            var el = BuildForbiddenPanel("Albums unavailable", "CurateProject");
            var border = Assert.IsType<Border>(el);
            var blocks = TextBlocks(border);
            return (Rgb(blocks[0].Foreground), Rgb(border.Background),
                    string.Join(" | ", blocks.Select(b => b.Text)));
        });

        // ERROR uses Brushes.Crimson (SitePhotosAlbumsSubTab.BuildLoadFailure).
        var crimson = (Colors.Crimson.R, Colors.Crimson.G, Colors.Crimson.B);

        Assert.NotNull(forbiddenFg);
        Assert.NotEqual(crimson, forbiddenFg!.Value);

        // And it is the amber the file documents, not merely "some other colour"
        // — pinned so a future edit toward red is caught rather than tolerated.
        Assert.Equal(((byte)0xB2, (byte)0x6A, (byte)0x00), forbiddenFg!.Value);
        Assert.Equal(((byte)0xFF, (byte)0xF5, (byte)0xE5), forbiddenFill!.Value);

        // A second, non-colour channel: the lock, where the error uses "⚠".
        Assert.Contains("🔒", text);
        Assert.DoesNotContain("⚠", text);
    }

    [Fact]
    public void The_panel_says_in_words_that_this_is_not_a_failure()
    {
        // The colour is the glance; this sentence is the answer. Without it the
        // pane still reads like something went wrong to anyone who reads it.
        var text = OnSta(() =>
            string.Join(" ", TextBlocks(BuildForbiddenPanel("Albums unavailable", "CurateProject"))
                .Select(b => b.Text)));

        Assert.Contains("This is not a failure", text);
        Assert.Contains("refused the request because of your role", text);
    }

    [Fact]
    public void The_panel_names_the_capability_and_never_the_status_code()
    {
        var text = OnSta(() =>
            string.Join(" ", TextBlocks(BuildForbiddenPanel("Approval unavailable", "ApproveSitePhotos"))
                .Select(b => b.Text)));

        Assert.Contains("Only a project manager can approve site photos", text);
        Assert.DoesNotContain("403", text);
        Assert.DoesNotContain("HTTP", text);
        Assert.DoesNotContain("Forbidden", text);
    }

    [Fact]
    public void A_server_supplied_reason_is_shown_when_there_is_one()
    {
        var withDetail = OnSta(() =>
            TextBlocks(BuildForbiddenPanel("Albums unavailable", "CurateProject",
                "Your role on this project is Viewer.")).Count);
        var without = OnSta(() =>
            TextBlocks(BuildForbiddenPanel("Albums unavailable", "CurateProject")).Count);

        // The server's own words are additive, never a replacement for the
        // capability sentence — so the user gets both.
        Assert.Equal(without + 1, withDetail);
    }

    [Fact]
    public void Capability_copy_never_puts_an_ISO_role_letter_in_front_of_a_user()
    {
        // "role K" is a derivation detail. It tells a user nothing and is the
        // habit that produced three clients disagreeing about who may do what.
        var describe = ForbiddenType.GetMethod("Describe", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var cap in new[] { "CurateProject", "ApproveSitePhotos" })
        {
            var s = (string)describe.Invoke(null, new[] { CapabilityValue(cap) })!;
            Assert.DoesNotContain("role K", s);
            Assert.DoesNotContain("role C", s);
            Assert.False(string.IsNullOrWhiteSpace(s));
        }
    }
}
