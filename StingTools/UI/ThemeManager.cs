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
        public static string CurrentTheme { get; private set; } = "Light";

        private static readonly string[] ThemeOrder = { "Light", "Warm", "Cool", "Corporate" };

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
                    }
                },
                {
                    // Cool blue-grey tint — light cool header
                    "Cool", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#F8FAFC" },
                        { "SecondaryBg", "#F2F6FA" },
                        { "PanelFg", "#2D3748" },
                        { "AccentBrush", "#3182CE" },   // Bright blue headers
                        { "ButtonBg", "#E8EEF4" },
                        { "ButtonFg", "#2D3748" },
                        { "HoverBg", "#D6DFE8" },
                        { "HeaderBg", "#E8EEF4" },       // Light cool header
                        { "HeaderFg", "#1A365D" },
                        { "BorderColor", "#D0DAE4" },
                        { "SuccessColor", "#276749" },
                        { "WarningColor", "#C05621" },
                        { "ErrorColor", "#C53030" },
                        { "TabBg", "#DCE4ED" },
                        { "TabFg", "#4A5568" },
                        { "TabSelectedBg", "#F8FAFC" },
                        { "TabSelectedFg", "#2D3748" },
                    }
                },
                {
                    // Corporate — light professional grey header
                    "Corporate", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#FAFAFA" },
                        { "SecondaryBg", "#F3F3F3" },
                        { "PanelFg", "#37474F" },
                        { "AccentBrush", "#1976D2" },   // Corporate blue headers
                        { "ButtonBg", "#E8E8E8" },
                        { "ButtonFg", "#37474F" },
                        { "HoverBg", "#D5D5D5" },
                        { "HeaderBg", "#ECEFF1" },       // Light slate header
                        { "HeaderFg", "#263238" },
                        { "BorderColor", "#CFD8DC" },
                        { "SuccessColor", "#2E7D32" },
                        { "WarningColor", "#E65100" },
                        { "ErrorColor", "#B71C1C" },
                        { "TabBg", "#CFD8DC" },
                        { "TabFg", "#455A64" },
                        { "TabSelectedBg", "#FAFAFA" },
                        { "TabSelectedFg", "#37474F" },
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

        /// <summary>Apply a named theme to both the panel and Application resources.</summary>
        public static void ApplyTheme(string themeName)
        {
            if (!Themes.ContainsKey(themeName))
            {
                StingLog.Warn($"ThemeManager: unknown theme '{themeName}', falling back to Light");
                themeName = "Light";
            }

            var theme = Themes[themeName];

            // Set resources on the host element FIRST (direct, always works in Revit)
            ApplyToTarget(theme, _targetElement?.Resources);

            // Also set on Application.Current for any child windows/dialogs
            ApplyToTarget(theme, Application.Current?.Resources);

            CurrentTheme = themeName;
            StingLog.Info($"ThemeManager: applied '{themeName}' theme");
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
        /// </summary>
        public static void InitialiseResources()
        {
            if (!Themes.ContainsKey(CurrentTheme)) CurrentTheme = "Light";
            ApplyTheme(CurrentTheme);
        }

        /// <summary>Cycle to the next theme in order: Light -> Warm -> Cool -> Corporate.</summary>
        public static string CycleTheme()
        {
            int idx = Array.IndexOf(ThemeOrder, CurrentTheme);
            int next = (idx + 1) % ThemeOrder.Length;
            string nextTheme = ThemeOrder[next];
            ApplyTheme(nextTheme);
            return nextTheme;
        }

        /// <summary>Apply corporate theme with custom accent/primary from config.</summary>
        public static void ApplyCorporateOverrides(string accentHex, string primaryHex)
        {
            if (!string.IsNullOrEmpty(accentHex))
                Themes["Corporate"]["AccentBrush"] = accentHex;
            if (!string.IsNullOrEmpty(primaryHex))
                Themes["Corporate"]["HeaderBg"] = primaryHex;
        }

        /// <summary>Get all available theme names.</summary>
        public static string[] GetThemeNames() => ThemeOrder;
    }
}
