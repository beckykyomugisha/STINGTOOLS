using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Materials.Providers
{
    public static class PbrProviderFactory
    {
        /// <summary>Resolve every enabled provider for the given document
        /// into an <see cref="IPbrProviderClient"/>.</summary>
        public static IReadOnlyList<IPbrProviderClient> AllForDocument(Document doc)
        {
            var cat = TextureProviderRegistry.Load(doc);
            string textureRoot = TextureProviderRegistry.ProjectTexturesRoot(doc);
            var list = new List<IPbrProviderClient>();
            if (cat?.Providers == null) return list;
            foreach (var p in cat.Providers.Where(p => p?.EnabledByDefault ?? true))
            {
                var c = Create(p, textureRoot);
                if (c != null) list.Add(c);
            }
            return list;
        }

        public static IPbrProviderClient Create(TextureProviderEntry entry, string projectTexturesRoot)
        {
            if (entry == null) return null;
            switch ((entry.Id ?? "").ToLowerInvariant())
            {
                case "polyhaven":   return new PolyHavenClient(entry);
                case "ambientcg":   return new AmbientCgClient(entry);
                case "user-folder": return new UserFolderClient(entry, projectTexturesRoot);
            }
            // Anything else falls back on kind.
            switch ((entry.Kind ?? "").ToLowerInvariant())
            {
                case "folder-watch": return new UserFolderClient(entry, projectTexturesRoot);
                case "url-launch":   return new UrlLaunchClient(entry);
                case "inline":       return new UrlLaunchClient(entry);   // unknown inline → fall back to URL launch
                default:             return new UrlLaunchClient(entry);
            }
        }

        public static IPbrProviderClient ForProviderId(Document doc, string providerId)
        {
            var cat = TextureProviderRegistry.Load(doc);
            var entry = cat?.Providers?.FirstOrDefault(p =>
                string.Equals(p?.Id, providerId, StringComparison.OrdinalIgnoreCase));
            return entry == null ? null : Create(entry, TextureProviderRegistry.ProjectTexturesRoot(doc));
        }
    }
}
