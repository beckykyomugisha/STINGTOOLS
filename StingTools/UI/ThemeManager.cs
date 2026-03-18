using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// UI-01: Theme engine for STING Tools dockable panel.
    /// Provides Dark, Light, Grey, and Corporate themes with
    /// dynamic resource switching.
    ///
    /// IMPORTANT: In Revit's dockable pane hosting, Application.Current.Resources
    /// may not propagate to the Page's DynamicResource lookups because the WPF
    /// resource tree can be broken. Resources are set on BOTH the Page and
    /// Application to ensure DynamicResource bindings resolve correctly.
    /// </summary>
    public static class ThemeManager
    {
        public static string CurrentTheme { get; private set; } = "Dark";

        private static readonly string[] ThemeOrder = { "Dark", "Light", "Grey", "Corporate" };

        /// <summary>
        /// Reference to the host Page/FrameworkElement for direct resource setting.
        /// Set via RegisterHost() from StingDockPanel constructor.
        /// </summary>
        private static FrameworkElement _host;

        private static readonly Dictionary<string, Dictionary<string, string>> Themes =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Dark", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#1E1E2E" },
                        { "SecondaryBg", "#2A2A3D" },
                        { "PanelFg", "#E8EAF0" },
                        { "AccentBrush", "#4FC3F7" },
                        { "ButtonBg", "#3A3A50" },
                        { "ButtonFg", "#F0F0F5" },
                        { "HoverBg", "#4A4A62" },
                        { "HeaderBg", "#14141F" },
                        { "HeaderFg", "#F0F4F8" },
                        { "BorderColor", "#5A5A72" },
                        { "SuccessColor", "#48BB78" },
                        { "WarningColor", "#ED8936" },
                        { "ErrorColor", "#FC8181" },
                    }
                },
                {
                    "Light", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#FFFFFF" },
                        { "SecondaryBg", "#F5F5F5" },
                        { "PanelFg", "#333333" },
                        { "AccentBrush", "#1565C0" },
                        { "ButtonBg", "#E0E4E8" },
                        { "ButtonFg", "#2C2C2C" },
                        { "HoverBg", "#C8CCD0" },
                        { "HeaderBg", "#1B3A5C" },
                        { "HeaderFg", "#FFFFFF" },
                        { "BorderColor", "#B8BCC0" },
                        { "SuccessColor", "#2E7D32" },
                        { "WarningColor", "#F57C00" },
                        { "ErrorColor", "#D32F2F" },
                    }
                },
                {
                    "Grey", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#3C3C3C" },
                        { "SecondaryBg", "#4A4A4A" },
                        { "PanelFg", "#E0E0E0" },
                        { "AccentBrush", "#64B5F6" },
                        { "ButtonBg", "#5A5A5A" },
                        { "ButtonFg", "#F0F0F0" },
                        { "HoverBg", "#6A6A6A" },
                        { "HeaderBg", "#2C2C2C" },
                        { "HeaderFg", "#FFFFFF" },
                        { "BorderColor", "#808080" },
                        { "SuccessColor", "#66BB6A" },
                        { "WarningColor", "#FFA726" },
                        { "ErrorColor", "#EF5350" },
                    }
                },
                {
                    "Corporate", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#F5F5F5" },
                        { "SecondaryBg", "#EEEEEE" },
                        { "PanelFg", "#37474F" },
                        { "AccentBrush", "#1565C0" },
                        { "ButtonBg", "#DADFE3" },
                        { "ButtonFg", "#263238" },
                        { "HoverBg", "#B0BEC5" },
                        { "HeaderBg", "#0D47A1" },
                        { "HeaderFg", "#FFFFFF" },
                        { "BorderColor", "#90A4AE" },
                        { "SuccessColor", "#2E7D32" },
                        { "WarningColor", "#E65100" },
                        { "ErrorColor", "#B71C1C" },
                    }
                },
            };

        /// <summary>
        /// Register the host Page/FrameworkElement so theme resources can be
        /// set directly on it. This is CRITICAL for Revit dockable pane hosting
        /// where Application.Current.Resources may not propagate through the
        /// visual tree to the Page's DynamicResource bindings.
        /// </summary>
        public static void RegisterHost(FrameworkElement host)
        {
            _host = host;
        }

        /// <summary>Apply a named theme to both the host element and application resources.</summary>
        public static void ApplyTheme(string themeName)
        {
            if (!Themes.ContainsKey(themeName))
            {
                StingLog.Warn($"ThemeManager: unknown theme '{themeName}', falling back to Dark");
                themeName = "Dark";
            }

            var theme = Themes[themeName];

            // Set resources on the host element FIRST (direct, always works in Revit)
            if (_host != null)
            {
                foreach (var kvp in theme)
                {
                    try
                    {
                        var color = (Color)ColorConverter.ConvertFromString(kvp.Value);
                        _host.Resources[kvp.Key] = new SolidColorBrush(color);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ThemeManager: failed to set {kvp.Key}={kvp.Value} on host: {ex.Message}");
                    }
                }
            }

            // Also set on Application.Current for any child windows/dialogs
            var app = Application.Current;
            if (app != null)
            {
                foreach (var kvp in theme)
                {
                    try
                    {
                        var color = (Color)ColorConverter.ConvertFromString(kvp.Value);
                        app.Resources[kvp.Key] = new SolidColorBrush(color);
                    }
                    catch { }
                }
            }

            CurrentTheme = themeName;
            StingLog.Info($"ThemeManager: applied '{themeName}' theme");
        }

        /// <summary>
        /// Seed all theme resource keys at startup. Must be called AFTER
        /// RegisterHost() and BEFORE the visual tree resolves DynamicResource bindings.
        /// </summary>
        public static void InitialiseResources()
        {
            if (!Themes.ContainsKey(CurrentTheme)) CurrentTheme = "Dark";
            ApplyTheme(CurrentTheme);
        }

        /// <summary>Cycle to the next theme in order: Dark -> Light -> Grey -> Corporate.</summary>
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
