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
    /// </summary>
    public static class ThemeManager
    {
        public static string CurrentTheme { get; private set; } = "Light";

        private static readonly string[] ThemeOrder = { "Dark", "Light", "Grey", "Corporate" };

        private static readonly Dictionary<string, Dictionary<string, string>> Themes =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Dark", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#1B2333" },
                        { "SecondaryBg", "#242D3E" },
                        { "PanelFg", "#E8EAF0" },
                        { "AccentBrush", "#2E75B6" },
                        { "ButtonBg", "#2D3748" },
                        { "ButtonFg", "#E2E8F0" },
                        { "HoverBg", "#3D4A5E" },
                        { "HeaderBg", "#151D2B" },
                        { "HeaderFg", "#F0F4F8" },
                        { "BorderColor", "#4A5568" },
                        { "SuccessColor", "#48BB78" },
                        { "WarningColor", "#ED8936" },
                        { "ErrorColor", "#FC8181" },
                    }
                },
                {
                    "Light", new Dictionary<string, string>
                    {
                        { "PrimaryBg", "#FFFFFF" },
                        { "SecondaryBg", "#F2F2F2" },
                        { "PanelFg", "#404040" },
                        { "AccentBrush", "#1B3A5C" },
                        { "ButtonBg", "#E8E8E8" },
                        { "ButtonFg", "#333333" },
                        { "HoverBg", "#D0D0D0" },
                        { "HeaderBg", "#1B3A5C" },
                        { "HeaderFg", "#FFFFFF" },
                        { "BorderColor", "#CCCCCC" },
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
                        { "AccentBrush", "#5E9FD4" },
                        { "ButtonBg", "#555555" },
                        { "ButtonFg", "#F0F0F0" },
                        { "HoverBg", "#666666" },
                        { "HeaderBg", "#333333" },
                        { "HeaderFg", "#FFFFFF" },
                        { "BorderColor", "#777777" },
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
                        { "ButtonBg", "#E0E0E0" },
                        { "ButtonFg", "#263238" },
                        { "HoverBg", "#BDBDBD" },
                        { "HeaderBg", "#0D47A1" },
                        { "HeaderFg", "#FFFFFF" },
                        { "BorderColor", "#B0BEC5" },
                        { "SuccessColor", "#2E7D32" },
                        { "WarningColor", "#E65100" },
                        { "ErrorColor", "#B71C1C" },
                    }
                },
            };

        /// <summary>Apply a named theme to the application resources.</summary>
        public static void ApplyTheme(string themeName)
        {
            if (!Themes.ContainsKey(themeName))
            {
                StingLog.Warn($"ThemeManager: unknown theme '{themeName}', falling back to Light");
                themeName = "Light";
            }

            var theme = Themes[themeName];
            var app = Application.Current;
            if (app == null) return;

            foreach (var kvp in theme)
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(kvp.Value);
                    app.Resources[kvp.Key] = new SolidColorBrush(color);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ThemeManager: failed to set {kvp.Key}={kvp.Value}: {ex.Message}");
                }
            }

            CurrentTheme = themeName;
            StingLog.Info($"ThemeManager: applied '{themeName}' theme");
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
