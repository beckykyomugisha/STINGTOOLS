using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// UI-01: Theme engine for STING Tools dockable panel.
    /// All themes use light content areas (matching the TAGS sub-tabs style)
    /// with coloured header/tab bars. Clean white/off-white backgrounds,
    /// dark text, subtle borders.
    ///
    /// IMPORTANT: In Revit's dockable pane hosting, Application.Current.Resources
    /// may not propagate to the Page's DynamicResource lookups because the WPF
    /// resource tree can be broken. Resources are set on BOTH the Page and
    /// Application to ensure DynamicResource bindings resolve correctly.
    /// </summary>
    public static class ThemeManager
    {
        // Default theme is "Cool" — light blue-grey body with bright blue
        // accents. The BCC, Document Management Centre, and dockable panel
        // all open in the same house theme. Users can still cycle with the
        // theme toggle button; the order below starts with Cool.
        public static string CurrentTheme { get; private set; } = "Cool";

        private static readonly string[] ThemeOrder = { "Cool", "Light", "Warm", "Corporate" };

        /// <summary>
        /// Fires after a successful theme apply (or after a fallback to
        /// Corporate). Listeners — windows that bake colours into their
        /// visual tree at construction time, e.g. <c>BIMCoordinationCenter</c>
        /// and the document management dialog — can subscribe and rebuild
        /// themselves so theme cycling works for already-open windows. The
        /// payload is the theme name that ended up active.
        /// </summary>
        public static event Action<string> ThemeChanged;

        /// <summary>
        /// Reference to the host Page/FrameworkElement for direct resource setting.
        /// Set via RegisterTarget() from StingDockPanel constructor.
        /// </summary>
        private static FrameworkElement _targetElement;

        private static readonly Dictionary<string, Dictionary<string, string>> Themes =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    // Clean white — light header, no dark shade
                    "Light", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#FFFFFF" },
                        { "SecondaryBg", "#FAFAFA" },
                        { "PanelFg", "#333333" },
                        { "AccentBrush", "#E88A1A" },   // Bright orange section headers
                        { "ButtonBg", "#F0F0F0" },
                        { "ButtonFg", "#333333" },
                        { "HoverBg", "#E2E2E2" },
                        { "HeaderBg", "#F5F5F5" },       // Light header bar
                        { "HeaderFg", "#333333" },
                        { "BorderColor", "#E0E0E0" },
                        { "SuccessColor", "#2E7D32" },
                        { "WarningColor", "#F57C00" },
                        { "ErrorColor", "#D32F2F" },
                        { "TabBg", "#EEEEEE" },
                        { "TabFg", "#555555" },
                        { "TabSelectedBg", "#FFFFFF" },
                        { "TabSelectedFg", "#333333" },
                        // Brand + dashboard extras (shared with DMD / BCC)
                        { "NavyHeader", "#1A237E" },
                        { "OrangeAccent", "#E88A1A" },
                        { "CardBg", "#FFFFFF" },
                        { "AltRowBg", "#F5F5F5" },
                        { "SubtleFg", "#888888" },
                        { "InfoBlue", "#1976D2" },
                        { "RowHover", "#FDF0E0" },
                    }
                },
                {
                    // Warm cream tint — light warm header
                    "Warm", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#FFFDF8" },
                        { "SecondaryBg", "#FFF9F0" },
                        { "PanelFg", "#3E3428" },
                        { "AccentBrush", "#D46A14" },   // Warm orange headers
                        { "ButtonBg", "#F5EDE0" },
                        { "ButtonFg", "#3E3428" },
                        { "HoverBg", "#EBE0D0" },
                        { "HeaderBg", "#F5EDE0" },       // Light warm header
                        { "HeaderFg", "#4A3728" },
                        { "BorderColor", "#E5D8C8" },
                        { "SuccessColor", "#558B2F" },
                        { "WarningColor", "#E65100" },
                        { "ErrorColor", "#C62828" },
                        { "TabBg", "#EDE4D6" },
                        { "TabFg", "#5D4E3C" },
                        { "TabSelectedBg", "#FFFDF8" },
                        { "TabSelectedFg", "#3E3428" },
                        // Brand + dashboard extras (shared with DMD / BCC)
                        { "NavyHeader", "#5D4037" },
                        { "OrangeAccent", "#D46A14" },
                        { "CardBg", "#FFFDF8" },
                        { "AltRowBg", "#F5EDE0" },
                        { "SubtleFg", "#8D7B66" },
                        { "InfoBlue", "#5D7B9C" },
                        { "RowHover", "#F5E6D3" },
                    }
                },
                {
                    // Cool blue-grey — calm slate palette suited to long
                    // BIM-coordination sessions. Saturated blue header
                    // (#2C5282) for primary navigation, brighter blue
                    // accent (#3182CE) for buttons / RAG-positive cells.
                    "Cool", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#F8FAFC" },
                        { "SecondaryBg", "#F2F6FA" },
                        { "PanelFg", "#2D3748" },
                        { "AccentBrush", "#3182CE" },    // Bright blue (primary accent)
                        { "ButtonBg", "#E8EEF4" },
                        { "ButtonFg", "#2D3748" },
                        { "HoverBg", "#DBEAFE" },
                        { "HeaderBg", "#2C5282" },        // Saturated cool blue header
                        { "HeaderFg", "#FFFFFF" },        // White on dark blue
                        { "BorderColor", "#D0DAE4" },
                        { "SuccessColor", "#276749" },
                        { "WarningColor", "#C05621" },
                        { "ErrorColor", "#C53030" },
                        { "TabBg", "#DCE4ED" },
                        { "TabFg", "#4A5568" },
                        { "TabSelectedBg", "#F8FAFC" },
                        { "TabSelectedFg", "#2D3748" },
                        // Brand + dashboard extras (shared with DMD / BCC)
                        { "NavyHeader", "#1A365D" },
                        { "OrangeAccent", "#3182CE" },
                        { "CardBg", "#FFFFFF" },
                        { "AltRowBg", "#F2F6FA" },
                        { "SubtleFg", "#718096" },
                        { "InfoBlue", "#3182CE" },
                        { "RowHover", "#E3EEF8" },
                    }
                },
                {
                    // Corporate — light professional grey body + StingTools brand
                    // navy header (#1A237E) + orange accent (#E8912D) — matches
                    // the BCC and Document Management Centre.
                    "Corporate", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#FAFAFA" },
                        { "SecondaryBg", "#F3F3F3" },
                        { "PanelFg", "#37474F" },
                        { "AccentBrush", "#E8912D" },   // STING orange (primary accent)
                        { "ButtonBg", "#E8E8E8" },
                        { "ButtonFg", "#37474F" },
                        { "HoverBg", "#FDF0E0" },
                        { "HeaderBg", "#1A237E" },       // STING navy header
                        { "HeaderFg", "#FFFFFF" },       // white on navy
                        { "BorderColor", "#CFD8DC" },
                        { "SuccessColor", "#2E7D32" },
                        { "WarningColor", "#E65100" },
                        { "ErrorColor", "#B71C1C" },
                        { "TabBg", "#CFD8DC" },
                        { "TabFg", "#455A64" },
                        { "TabSelectedBg", "#FAFAFA" },
                        { "TabSelectedFg", "#37474F" },
                        // Brand + dashboard extras (used by GetBrush callers)
                        { "NavyHeader", "#1A237E" },
                        { "OrangeAccent", "#E8912D" },
                        { "CardBg", "#FFFFFF" },
                        { "AltRowBg", "#F5F5F5" },
                        { "SubtleFg", "#607D8B" },
                        { "InfoBlue", "#1976D2" },
                        { "RowHover", "#FDF0E0" },
                    }
                },
            };

        /// <summary>
        /// Register the panel element that will receive theme resources.
        /// Call once from the panel constructor after InitializeComponent().
        /// </summary>
        public static void RegisterTarget(FrameworkElement element)
        {
            _targetElement = element;
        }

        /// <summary>Alias for RegisterTarget for backwards compatibility.</summary>
        public static void RegisterHost(FrameworkElement host)
        {
            RegisterTarget(host);
        }

        /// <summary>Apply a named theme to both the panel and Application resources.
        /// On any failure (unknown theme, brush parse error, etc.) the manager
        /// silently falls back to the Corporate theme so callers always get a
        /// usable palette — windows must never render with a half-applied or
        /// empty resource dictionary.</summary>
        public static void ApplyTheme(string themeName)
        {
            // Unknown theme → silently switch to Corporate (was: Light). This
            // honours the "if it fails default to Corporate" rule.
            if (string.IsNullOrEmpty(themeName) || !Themes.ContainsKey(themeName))
            {
                StingLog.Warn($"ThemeManager: unknown theme '{themeName}', falling back to '{FallbackTheme}'");
                themeName = FallbackTheme;
            }

            try
            {
                var theme = Themes[themeName];

                // H-03: Merge corporate overrides at apply-time without mutating base theme
                if (themeName.Equals("Corporate", StringComparison.OrdinalIgnoreCase) && _corporateOverrides.Count > 0)
                {
                    theme = new Dictionary<string, string>(theme, StringComparer.OrdinalIgnoreCase);
                    foreach (var ov in _corporateOverrides)
                        theme[ov.Key] = ov.Value;
                }

                // Set resources on the host element FIRST (direct, always works in Revit)
                ApplyToTarget(theme, _targetElement?.Resources);

                // Also set on Application.Current for any child windows/dialogs
                ApplyToTarget(theme, Application.Current?.Resources);

                CurrentTheme = themeName;
                StingLog.Info($"ThemeManager: applied '{themeName}' theme");
            }
            catch (Exception ex)
            {
                StingLog.Error($"ThemeManager: ApplyTheme('{themeName}') failed — falling back to '{FallbackTheme}'", ex);
                if (!themeName.Equals(FallbackTheme, StringComparison.OrdinalIgnoreCase))
                {
                    // Recursive fall back to Corporate — guaranteed to exist in Themes.
                    ApplyTheme(FallbackTheme);
                    return;
                }
                // If even Corporate failed something is very wrong — leave
                // CurrentTheme alone so the fallback brushes from GetBrush()
                // still resolve against the in-memory Corporate map.
                CurrentTheme = FallbackTheme;
            }

            // Set resources on the host element FIRST (direct, always works in Revit)
            ApplyToTarget(theme, _targetElement?.Resources);

            // Also set on Application.Current for any child windows/dialogs
            ApplyToTarget(theme, Application.Current?.Resources);

            CurrentTheme = themeName;
            StingLog.Info($"ThemeManager: applied '{themeName}' theme");

            // Notify subscribers (modeless windows like BCC) so they can
            // refresh code-behind brushes that don't go through DynamicResource.
            try { ThemeChanged?.Invoke(null, EventArgs.Empty); }
            catch (Exception ex) { StingLog.Warn($"ThemeManager.ThemeChanged: {ex.Message}"); }
        }


        private static void ApplyToTarget(Dictionary<string, string> theme, ResourceDictionary resources)
        {
            if (resources == null) return;
            foreach (var kvp in theme)
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(kvp.Value);
                    var brush = new SolidColorBrush(color);
                    brush.Freeze(); // Thread safety for cross-thread WPF resource access
                    resources[kvp.Key] = brush;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ThemeManager: failed to set {kvp.Key}={kvp.Value}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Seed all theme resource keys at startup.
        /// Must be called after InitializeComponent() and RegisterTarget().
        /// Falls back to Corporate (not Light) if the active theme name is unknown.
        /// </summary>
        public static void InitialiseResources()
        {
            if (!Themes.ContainsKey(CurrentTheme)) CurrentTheme = FallbackTheme;
            ApplyTheme(CurrentTheme);
        }

        /// <summary>
        /// Seed Application + target resources to Corporate if they have not
        /// been initialised yet. Safe to call from any window constructor:
        /// no-op once a theme has been applied. Also wires Application.Current
        /// so child dialogs that don't register a target still get brushes.
        /// </summary>
        public static void EnsureInitialised()
        {
            try
            {
                var app = Application.Current?.Resources;
                if (app != null && app.Contains("AccentBrush")) return;
                ApplyTheme(string.IsNullOrEmpty(CurrentTheme) ? FallbackTheme : CurrentTheme);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ThemeManager.EnsureInitialised: {ex.Message}");
                try { ApplyTheme(FallbackTheme); } catch { /* nothing more we can do */ }
            }
        }

        /// <summary>Cycle to the next theme in order: Corporate -> Light -> Warm -> Cool.
        /// Returns the theme name that ended up active (Corporate if cycling failed).</summary>
        public static string CycleTheme()
        {
            try
            {
                int idx = Array.IndexOf(ThemeOrder, CurrentTheme);
                int next = (idx + 1) % ThemeOrder.Length;
                string nextTheme = ThemeOrder[next];
                ApplyTheme(nextTheme);
                return CurrentTheme; // ApplyTheme may have fallen back to Corporate
            }
            catch (Exception ex)
            {
                StingLog.Error("ThemeManager: CycleTheme failed — forcing Corporate", ex);
                ApplyTheme(FallbackTheme);
                return CurrentTheme;
            }
        }

        /// <summary>Apply corporate theme with custom accent/primary from config.</summary>
        public static void ApplyCorporateOverrides(string accentHex, string primaryHex)
        {
            // H-03: Store overrides separately, merge at apply-time instead of mutating base theme
            if (!string.IsNullOrEmpty(accentHex))
                _corporateOverrides["AccentBrush"] = accentHex;
            if (!string.IsNullOrEmpty(primaryHex))
                _corporateOverrides["HeaderBg"] = primaryHex;
        }

        // H-03: Separate overrides dictionary so base Corporate theme is never mutated
        private static readonly Dictionary<string, string> _corporateOverrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>H-02: Clear static reference to prevent memory leak on shutdown.</summary>
        public static void ClearTarget()
        {
            _targetElement = null;
        }

        /// <summary>Get all available theme names.</summary>
        public static string[] GetThemeNames() => ThemeOrder;

        /// <summary>
        /// Resolve a theme key to a frozen <see cref="SolidColorBrush"/>
        /// for code-behind use (where <see cref="DynamicResource"/> isn't
        /// convenient). Looks up the active theme's palette, falls back
        /// to the Corporate map for unknown keys, and finally returns a
        /// safe slate brush so callers never get null. Adding this single
        /// helper lets every dashboard route its palette through the
        /// ThemeManager instead of hardcoding hex values inline.
        /// </summary>
        public static SolidColorBrush GetBrush(string key)
        {
            if (string.IsNullOrEmpty(key)) return _fallbackBrush;
            try
            {
                var name = Themes.ContainsKey(CurrentTheme) ? CurrentTheme : "Corporate";
                if (Themes[name].TryGetValue(key, out string hex)
                    || Themes["Corporate"].TryGetValue(key, out hex))
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    if (name.Equals("Corporate", StringComparison.OrdinalIgnoreCase)
                        && _corporateOverrides.TryGetValue(key, out string ovHex))
                    {
                        try { color = (Color)ColorConverter.ConvertFromString(ovHex); }
                        catch { /* keep base colour */ }
                    }
                    var b = new SolidColorBrush(color);
                    b.Freeze();
                    return b;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ThemeManager.GetBrush('{key}'): {ex.Message}"); }
            return _fallbackBrush;
        }

        private static readonly SolidColorBrush _fallbackBrush =
            CreateFrozen(Color.FromRgb(0x37, 0x47, 0x4F));

        private static SolidColorBrush CreateFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
