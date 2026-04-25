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
            // rec-17: Widen from 4 bytes (32 bits) to 8 bytes (64 bits).
            // Birthday-collision horizon:
            //   4 bytes → ~65k clashes before ~50% collision probability
            //   8 bytes → ~4.3B clashes before ~50% collision probability
            // A medium federated model can produce tens of thousands of raw
            // hits pre-filter, so 4 bytes was a real risk. 8 bytes is a free
            // safety margin — still compact (16 hex chars) and non-truncating
            // for anything short of a building-lifetime coordination archive.
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        public static string NewClashId(DateTime utcNow, int sequence)
        {
            return $"CLH-{utcNow:yyyyMMdd}-{sequence:D5}";
        }
    }
}
