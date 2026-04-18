// ClashPersistence.cs — clashes.json I/O.
using System;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.Core.Clash
{
    public static class ClashPersistence
    {
        public static ClashRunRecord Load(string path)
        {
            if (!File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<ClashRunRecord>(File.ReadAllText(path)); }
            catch { return null; }
        }

        public static void Save(ClashRunRecord run, string path)
        {
            if (run == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(run, Formatting.Indented));
        }
    }
}
