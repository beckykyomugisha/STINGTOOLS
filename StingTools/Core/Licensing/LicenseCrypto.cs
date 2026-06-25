using System;
using System.Security.Cryptography;
using System.Text;

namespace StingTools.Core.Licensing
{
    public static class LicenseCrypto
    {
        /// <summary>"base64(payloadBytes).base64(signature)"</summary>
        public static string Sign(string payloadJson, string privateKeyPem)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            byte[] data = Encoding.UTF8.GetBytes(payloadJson);
            byte[] sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(data) + "." + Convert.ToBase64String(sig);
        }

        /// <summary>Returns payload JSON if signature valid, else null.</summary>
        public static string VerifyAndExtract(string licenseText, string publicKeyPem)
        {
            if (string.IsNullOrWhiteSpace(licenseText)) return null;
            int dot = licenseText.IndexOf('.');
            if (dot <= 0 || dot == licenseText.Length - 1) return null;
            byte[] data, sig;
            try
            {
                data = Convert.FromBase64String(licenseText.Substring(0, dot));
                sig = Convert.FromBase64String(licenseText.Substring(dot + 1));
            }
            catch { return null; }
            using var rsa = RSA.Create();
            try { rsa.ImportFromPem(publicKeyPem); } catch { return null; }
            try
            {
                return rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                    ? Encoding.UTF8.GetString(data) : null;
            }
            catch { return null; }
        }
    }
}
