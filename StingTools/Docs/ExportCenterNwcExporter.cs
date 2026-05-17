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
        {
            if (!IsAvailable()) return false;
            try
            {
                object opts = Activator.CreateInstance(_optionsType);

                // Apply settings via reflection — properties may not exist on every
                // version of the exporter, so each set is wrapped in try/catch.
                Set(opts, "ExportScope", MapScope(settings.Scope));
                Set(opts, "Coordinates", MapCoords(settings.CoordinateSystem));
                Set(opts, "ExportElementIds", settings.ExportElementIdsForClash);
                Set(opts, "ConvertElementProperties", true);

                _exportMethod.Invoke(doc, new object[] { folder, nameNoExt, opts });

                string path = Path.Combine(folder, nameNoExt + ".nwc");
                return File.Exists(path);
            }
            catch (Exception ex)
            {
                StingLog.Warn("NwcExporter.Export: " + ex.Message);
                return false;
            }
        }

        private static object MapScope(string scope)
        {
            // NavisworksExportScope enum values: Model = 0, View = 1, Selection = 2
            return scope switch
            {
                "CurrentView" => 1,
                "Selected"    => 2,
                _             => 0,
            };
        }

        private static object MapCoords(string coords)
        {
            // NavisworksCoordinates enum: Shared = 0, Internal = 1
            return coords switch { "Shared" => 0, _ => 1 };
        }

        private static void Set(object target, string propName, object value)
        {
            try
            {
                var p = target.GetType().GetProperty(propName);
                if (p == null || !p.CanWrite) return;
                // Convert numeric-as-int to the enum type if the property is an enum.
                object converted = value;
                if (p.PropertyType.IsEnum && value is int iv)
                    converted = Enum.ToObject(p.PropertyType, iv);
                p.SetValue(target, converted);
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
