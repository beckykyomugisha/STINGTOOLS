using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ExportCenterNwcExporter — NWC export via reflection.
    //
    //  Revit's NavisworksExportOptions only exists when the Navisworks NWC
    //  Export Utility is installed (it ships its own assembly that surfaces
    //  the type to the Revit API). We probe for the type at runtime so that
    //  the StingTools assembly itself has no compile-time dependency on it.
    // ════════════════════════════════════════════════════════════════════════════

    internal static class ExportCenterNwcExporter
    {
        private static Type _optionsType;
        private static MethodInfo _exportMethod;
        private static bool _probed;

        internal static bool IsAvailable()
        {
            // NW-1: NavisworksExportOptions is in RevitAPI whether or not the exporter is
            // installed, so finding the type proved nothing - Revit's own check decides.
            try { if (!OptionalFunctionalityUtils.IsNavisworksExporterAvailable()) return false; }
            catch (Exception ex) { StingLog.Warn("NwcExporter: IsNavisworksExporterAvailable failed: " + ex.Message); return false; }
            if (_probed) return _optionsType != null;
            _probed = true;
            try
            {
                // Try a few well-known assembly names — older Revit versions ship
                // the type in different locations.
                foreach (string asm in new[] { "RevitAPI", "Autodesk.Revit.DB.RevitAPI", "RevitNwcExporter" })
                {
                    var t = Type.GetType($"Autodesk.Revit.DB.NavisworksExportOptions, {asm}", false);
                    if (t != null) { _optionsType = t; break; }
                }

                if (_optionsType == null)
                {
                    // Fall back: scan loaded assemblies.
                    _optionsType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => SafeGetTypes(a))
                        .FirstOrDefault(t => t?.FullName == "Autodesk.Revit.DB.NavisworksExportOptions");
                }

                if (_optionsType != null)
                {
                    _exportMethod = typeof(Document).GetMethods()
                        .FirstOrDefault(m => m.Name == "Export"
                            && m.GetParameters().Length == 3
                            && m.GetParameters()[0].ParameterType == typeof(string)
                            && m.GetParameters()[1].ParameterType == typeof(string)
                            && m.GetParameters()[2].ParameterType == _optionsType);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn("NwcExporter.IsAvailable probe: " + ex.Message);
            }
            return _optionsType != null && _exportMethod != null;
        }

        internal static bool Export(Document doc, string folder, string nameNoExt, NwcExportSettings settings)
            => Export(doc, folder, nameNoExt, settings, out _, out _);

        /// <summary>NW-1: <paramref name="error"/> says why nothing was written;
        /// <paramref name="notes"/> lists where the profile was read differently from its label.</summary>
        internal static bool Export(Document doc, string folder, string nameNoExt, NwcExportSettings settings,
            out string error, out List<string> notes)
        {
            error = null;
            var plan = NwcExportPlan.For(settings?.Scope, settings?.CoordinateSystem, settings?.ExportElementIdsForClash ?? true);
            notes = plan.Notes;
            if (!IsAvailable()) { error = "Navisworks NWC Export Utility not detected."; return false; }
            try
            {
                object opts = Activator.CreateInstance(_optionsType);

                // NW-1: enums are set by MEMBER NAME. The old code set ordinals with the
                // members' order guessed in comments, so 'Shared' could land as Internal.
                if (!SetEnum(opts, "ExportScope", plan.Scope, out error)) return false;
                if (!SetEnum(opts, "Coordinates", plan.Coordinates, out error)) return false;
                if (plan.Scope == "View")
                {
                    var view = NavisworksView(doc);
                    if (view == null)
                    {
                        error = "Scope 'CurrentView' needs a 3D view: open one, or name a 3D view 'Navisworks'.";
                        return false;
                    }
                    Set(opts, "ViewId", view.Id);
                }
                Set(opts, "ExportElementIds", plan.ExportElementIds);
                Set(opts, "ConvertElementProperties", true);
                SetEnum(opts, "Parameters", "All", out _);   // all parameters, for search sets on STING tokens

                foreach (var n in plan.Notes) StingLog.Info("NwcExporter: " + n);
                _exportMethod.Invoke(doc, new object[] { folder, nameNoExt, opts });

                string path = Path.Combine(folder, nameNoExt + ".nwc");
                if (!File.Exists(path)) error = "Revit reported no error but wrote no .nwc file.";
                return error == null;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                error = inner.Message;
                StingLog.Warn("NwcExporter.Export: " + inner.Message);
                return false;
            }
        }

        /// <summary>A 3D view named "Navisworks" (the exporter's own convention), else the active
        /// view when it is a non-template 3D view; null otherwise.</summary>
        private static View3D NavisworksView(Document doc)
        {
            var named = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, "Navisworks", StringComparison.OrdinalIgnoreCase));
            if (named != null) return named;
            return doc.ActiveView is View3D a && !a.IsTemplate ? a : null;
        }

        private static bool SetEnum(object target, string propName, string member, out string error)
        {
            error = null;
            var p = target.GetType().GetProperty(propName);
            if (p == null || !p.CanWrite || !p.PropertyType.IsEnum)
            {
                error = $"this Revit's Navisworks exporter has no settable '{propName}'";
                StingLog.Warn("NwcExporter: " + error);
                return false;
            }
            string name = Enum.GetNames(p.PropertyType)
                .FirstOrDefault(n => string.Equals(n, member, StringComparison.OrdinalIgnoreCase));
            if (name == null)
            {
                error = $"'{member}' is not a {p.PropertyType.Name} value here ({string.Join(", ", Enum.GetNames(p.PropertyType))})";
                StingLog.Warn("NwcExporter: " + error);
                return false;
            }
            p.SetValue(target, Enum.Parse(p.PropertyType, name));
            return true;
        }

        private static void Set(object target, string propName, object value)
        {
            try
            {
                var p = target.GetType().GetProperty(propName);
                if (p == null || !p.CanWrite) return;
                p.SetValue(target, value);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"NwcExporter.Set {propName}: {ex.Message}");
            }
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return Array.Empty<Type>(); }
        }
    }
}
