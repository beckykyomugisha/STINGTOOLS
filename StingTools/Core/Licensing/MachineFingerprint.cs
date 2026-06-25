using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace StingTools.Core.Licensing
{
    public static class MachineFingerprint
    {
        private static string _cached;
        public static string Current => _cached ??= FingerprintComposer.Compute(Factors());

        /// <summary>True when at least MachineGuid + 1 hardware factor are real.</summary>
        public static bool IsTrustworthy => FingerprintComposer.RealFactorCount(Factors()) >= 2;

        private static List<string> Factors() => new List<string>
        {
            MachineGuid(),
            Wmi("Win32_Processor", "ProcessorId"),
            Wmi("Win32_BaseBoard", "SerialNumber"),
            Wmi("Win32_BIOS", "SerialNumber"),
        };

        private static string MachineGuid()
        {
            try
            {
                using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return k?.GetValue("MachineGuid") as string;
            }
            catch { return null; }
        }

        private static string Wmi(string cls, string prop)
        {
            try
            {
                using var s = new ManagementObjectSearcher($"SELECT {prop} FROM {cls}");
                foreach (ManagementObject mo in s.Get())
                {
                    var v = mo[prop]?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch { }
            return null;
        }
    }
}
