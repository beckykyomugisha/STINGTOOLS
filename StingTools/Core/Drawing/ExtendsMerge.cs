// StingTools — Drawing Template Manager
//
// ExtendsMerge — shared field overlay for the `extends` chain folds in
// DrawingTypeRegistry and ViewStylePackRegistry.
//
// Both registries used to fold a chain by hand-enumerating the fields to
// copy into a fresh instance. Every field the enumeration missed was
// silently dropped — including the leaf's own value, because the leaf is
// just the last link of the same chain. DrawingTypeRegistry dropped 12
// fields (titleBlockParams, isoNaming, packageId, …) and
// ViewStylePackRegistry dropped 14 (templateMode, managedFields,
// worksetVisibility, …). Since all 35 shipped style packs declare
// `extends`, the pack fold ran on every Get() and `templateMode:
// "managed"` could never survive it — the managed-template branch in
// DrawingTypePresentation was unreachable.
//
// Rather than re-enumerate (and drift again on the next field added),
// the folds now overlay every remaining public read/write property
// generically. A property is treated as "set" when its value differs
// from the value a freshly-constructed instance carries, so a child that
// simply omits a key does not clobber its parent with a C# default or a
// POCO default such as Discipline = "*" or PaperSize = "A1".
//
// Fields whose merge semantics are not a plain overwrite — collections
// that accumulate (Filters) or merge by key (VgOverrides, TagFamilies)
// — stay explicitly handled by the callers and are passed in the skip
// set.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StingTools.Core.Drawing
{
    internal static class ExtendsMerge
    {
        private static readonly object _lock = new object();

        // Keyed by type only: each POCO has exactly one fold site, so its
        // skip set is fixed. Callers pass a static readonly set.
        private static readonly Dictionary<Type, PropertyInfo[]> _propCache
            = new Dictionary<Type, PropertyInfo[]>();
        private static readonly Dictionary<Type, object> _defaultCache
            = new Dictionary<Type, object>();

        /// <summary>
        /// Public instance properties of <paramref name="t"/> that the
        /// generic overlay should carry: readable, writable, non-indexed,
        /// and not explicitly handled by the caller.
        /// </summary>
        internal static PropertyInfo[] OverlayProps(Type t, ISet<string> skip)
        {
            lock (_lock)
            {
                if (_propCache.TryGetValue(t, out var cached)) return cached;
                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead
                             && p.CanWrite
                             && p.GetIndexParameters().Length == 0
                             && !skip.Contains(p.Name))
                    .ToArray();
                _propCache[t] = props;
                return props;
            }
        }

        /// <summary>
        /// Copy every property in <paramref name="props"/> from
        /// <paramref name="src"/> onto <paramref name="dst"/> when the
        /// source value differs from a fresh instance's value for that
        /// property. Call parent-first so later (child) links win.
        /// </summary>
        internal static void Overlay(object src, object dst, PropertyInfo[] props)
        {
            if (src == null || dst == null || props == null) return;
            var blank = Blank(src.GetType());
            foreach (var pi in props)
            {
                try
                {
                    var v = pi.GetValue(src);
                    if (v == null) continue;
                    if (v is string s && s.Length == 0) continue;
                    if (blank != null && Equals(v, pi.GetValue(blank))) continue; // unset
                    pi.SetValue(dst, v);
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn(
                        $"ExtendsMerge: could not carry '{pi.Name}' on {src.GetType().Name} — {ex.Message}");
                }
            }
        }

        /// <summary>A cached freshly-constructed instance used as the
        /// "unset" reference. Null when the type has no usable
        /// parameterless constructor, in which case only null / empty
        /// -string values are treated as unset.</summary>
        private static object Blank(Type t)
        {
            lock (_lock)
            {
                if (_defaultCache.TryGetValue(t, out var cached)) return cached;
                object inst = null;
                try { inst = Activator.CreateInstance(t); }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn(
                        $"ExtendsMerge: no default instance for {t.Name} — {ex.Message}");
                }
                _defaultCache[t] = inst;
                return inst;
            }
        }
    }
}
