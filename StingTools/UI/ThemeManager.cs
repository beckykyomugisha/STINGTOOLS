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
    /// </summary>
    public static class ThemeManager
    {
        public static string CurrentTheme { get; private set; } = "Light";

        private static readonly string[] ThemeOrder = { "Light", "Warm", "Cool", "Corporate" };

        /// <summary>The registered panel element whose Resources dictionary receives theme brushes.</summary>
        private static FrameworkElement _targetElement;

        private static readonly Dictionary<string, Dictionary<string, string>> Themes =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    // Clean white — matches TAGS sub-tabs exactly
                    "Light", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#FFFFFF" },
                        { "SecondaryBg", "#F7F7F7" },
                        { "PanelFg", "#333333" },
                        { "AccentBrush", "#D4760A" },   // Orange section headers
                        { "ButtonBg", "#F0F0F0" },
                        { "ButtonFg", "#333333" },
                        { "HoverBg", "#E2E2E2" },
                        { "HeaderBg", "#2D2D2D" },       // Dark header bar
                        { "HeaderFg", "#FFFFFF" },
                        { "BorderColor", "#D0D0D0" },
                        { "SuccessColor", "#2E7D32" },
                        { "WarningColor", "#F57C00" },
                        { "ErrorColor", "#D32F2F" },
                        { "TabBg", "#E8E8E8" },
                        { "TabFg", "#444444" },
                        { "TabSelectedBg", "#FFFFFF" },
                        { "TabSelectedFg", "#333333" },
                    }
                },
                {
                    // Warm cream tint — still light, subtle warm tone
                    "Warm", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#FFFDF8" },
                        { "SecondaryBg", "#FFF8EE" },
                        { "PanelFg", "#3E3428" },
                        { "AccentBrush", "#C75B12" },   // Warm orange headers
                        { "ButtonBg", "#F5EDE0" },
                        { "ButtonFg", "#3E3428" },
                        { "HoverBg", "#EBE0D0" },
                        { "HeaderBg", "#4A3728" },       // Dark brown header
                        { "HeaderFg", "#FFF5E6" },
                        { "BorderColor", "#D9CCBB" },
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
                    // Cool blue-grey tint — light, clean, subtle cool tone
                    "Cool", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#F8FAFC" },
                        { "SecondaryBg", "#F0F4F8" },
                        { "PanelFg", "#2D3748" },
                        { "AccentBrush", "#2B6CB0" },   // Blue headers
                        { "ButtonBg", "#E8EEF4" },
                        { "ButtonFg", "#2D3748" },
                        { "HoverBg", "#D6DFE8" },
                        { "HeaderBg", "#1A365D" },       // Dark navy header
                        { "HeaderFg", "#EBF4FF" },
                        { "BorderColor", "#C5D1DC" },
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
                    // Corporate — professional light grey
                    "Corporate", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#FAFAFA" },
                        { "SecondaryBg", "#F0F0F0" },
                        { "PanelFg", "#37474F" },
                        { "AccentBrush", "#1565C0" },   // Corporate blue headers
                        { "ButtonBg", "#E8E8E8" },
                        { "ButtonFg", "#37474F" },
                        { "HoverBg", "#D5D5D5" },
                        { "HeaderBg", "#263238" },       // Dark slate header
                        { "HeaderFg", "#ECEFF1" },
                        { "BorderColor", "#B0BEC5" },
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

        /// <summary>Apply a named theme to both the panel and Application resources.</summary>
        public static void ApplyTheme(string themeName)
        {
            if (!Themes.ContainsKey(themeName))
            {
                StingLog.Warn($"ThemeManager: unknown theme '{themeName}', falling back to Light");
                themeName = "Light";
            }

            var theme = Themes[themeName];

            // Write to both targets for reliable DynamicResource resolution
            ApplyToTarget(theme, _targetElement?.Resources);
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
                    resources[kvp.Key] = new SolidColorBrush(color);
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
            var theme = Themes[CurrentTheme];
            ApplyToTarget(theme, _targetElement?.Resources);
            ApplyToTarget(theme, Application.Current?.Resources);
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
