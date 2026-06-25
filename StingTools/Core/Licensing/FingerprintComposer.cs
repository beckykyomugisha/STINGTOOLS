using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace StingTools.Core.Licensing
{
    /// <summary>Pure: composite hardware factors -> stable grouped machine code.</summary>
    public static class FingerprintComposer
    {
        public static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string t = s.Trim().ToUpperInvariant();
            if (t.Contains("TO BE FILLED") || t == "DEFAULT STRING" || t == "NONE" ||
                t == "0" || t == "SYSTEM SERIAL NUMBER" || t == "NOT SPECIFIED") return "";
            return t;
        }

        public static int RealFactorCount(IEnumerable<string> factors)
        {
            int n = 0;
            foreach (var f in factors) if (!string.IsNullOrEmpty(Normalize(f))) n++;
            return n;
        }

        /// <summary>20 hex chars, grouped XXXX-XXXX-XXXX-XXXX-XXXX. Empty factor -> "NA".</summary>
        public static string Compute(IEnumerable<string> factors)
        {
            var parts = new List<string>();
            foreach (var f in factors)
            {
                var n = Normalize(f);
                parts.Add(string.IsNullOrEmpty(n) ? "NA" : n);
            }
            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", parts)));

            var hex = new StringBuilder();
            for (int i = 0; i < 10; i++) hex.Append(hash[i].ToString("X2")); // 20 chars

            var sb = new StringBuilder();
            string s = hex.ToString();
            for (int i = 0; i < s.Length; i += 4)
            {
                if (i > 0) sb.Append('-');
                sb.Append(s.Substring(i, 4));
            }
            return sb.ToString();
        }
    }
}
