// ClashIdentity.cs — stable identity hash for a clash, used to match clashes across runs
// (for history/diff). Rounds centroid to 250 mm to avoid jitter-induced reintroduction.
using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace StingTools.Core.Clash
{
    public static class ClashIdentity
    {
        public static string Compute(ClashElementKey a, ClashElementKey b, string matrixPairId, Vector3 centroid)
        {
            // Order the two keys deterministically.
            string ka = a.IfcGuid ?? a.UniqueId ?? a.ToString();
            string kb = b.IfcGuid ?? b.UniqueId ?? b.ToString();
            if (string.CompareOrdinal(ka, kb) > 0) { var tmp = ka; ka = kb; kb = tmp; }

            int rx = (int)Math.Round(centroid.X * 304.8 / 250.0);   // feet → mm → 250mm bins
            int ry = (int)Math.Round(centroid.Y * 304.8 / 250.0);
            int rz = (int)Math.Round(centroid.Z * 304.8 / 250.0);
            var payload = $"{ka}|{kb}|{matrixPairId}|{rx},{ry},{rz}";

            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var sb = new StringBuilder(8);
            for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        public static string NewClashId(DateTime utcNow, int sequence)
        {
            return $"CLH-{utcNow:yyyyMMdd}-{sequence:D5}";
        }
    }
}
