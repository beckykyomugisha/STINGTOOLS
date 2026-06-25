using System;
using System.IO;
using System.Security.Cryptography;
using StingTools.Core.Licensing;

if (args.Length == 0) { Help(); return; }

switch (args[0].ToLowerInvariant())
{
    case "keygen": KeyGen(); break;
    case "issue":  Issue(args); break;
    case "selfcode": Console.WriteLine(MachineFingerprint.Current); break;
    default: Help(); break;
}

static void Help()
{
    Console.WriteLine("StingLicenseIssuer keygen");
    Console.WriteLine("StingLicenseIssuer selfcode");
    Console.WriteLine("StingLicenseIssuer issue --code <machineCode> --name \"<licensee>\" --days 365 [--out StingTools.lic]");
}

static void KeyGen()
{
    if (File.Exists("private.pem"))
    {
        Console.WriteLine("private.pem already exists — refusing to overwrite. Delete it manually if you really mean to.");
        return;
    }
    using var rsa = RSA.Create(2048);
    File.WriteAllText("private.pem", rsa.ExportPkcs8PrivateKeyPem());
    string pub = rsa.ExportSubjectPublicKeyInfoPem();
    File.WriteAllText("public.pem", pub);
    Console.WriteLine("Wrote private.pem (KEEP SECRET, BACK UP) and public.pem.");
    Console.WriteLine("Paste this public key into StingTools/Core/Licensing/LicensePublicKey.cs:\n");
    Console.WriteLine(pub);
}

static void Issue(string[] args)
{
    if (!File.Exists("private.pem")) { Console.WriteLine("private.pem not found — run keygen first."); return; }
    string code = Arg(args, "--code"), name = Arg(args, "--name") ?? "Unnamed",
           daysS = Arg(args, "--days") ?? "365", outPath = Arg(args, "--out") ?? "StingTools.lic";
    if (string.IsNullOrWhiteSpace(code)) { Console.WriteLine("--code is required."); return; }
    int days = int.Parse(daysS);

    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var payload = new LicensePayload
    {
        LicenseId = Guid.NewGuid().ToString("N"),
        MachineCode = code.Trim().ToUpperInvariant(),
        Licensee = name,
        IssuedUnix = now,
        ExpiryUnix = now + (long)days * 86400,
        Schema = 1
    };
    string lic = LicenseCrypto.Sign(payload.ToJson(), File.ReadAllText("private.pem"));
    File.WriteAllText(outPath, lic);

    bool header = !File.Exists("issued-licenses.csv");
    using (var w = new StreamWriter("issued-licenses.csv", append: true))
    {
        if (header) w.WriteLine("licenseId,machineCode,licensee,issuedUtc,expiryUtc,out");
        w.WriteLine($"{payload.LicenseId},{payload.MachineCode},\"{name}\"," +
                    $"{DateTimeOffset.FromUnixTimeSeconds(payload.IssuedUnix).UtcDateTime:yyyy-MM-dd}," +
                    $"{DateTimeOffset.FromUnixTimeSeconds(payload.ExpiryUnix).UtcDateTime:yyyy-MM-dd},{outPath}");
    }
    Console.WriteLine($"Wrote {outPath} for {payload.MachineCode}, expires " +
        $"{DateTimeOffset.FromUnixTimeSeconds(payload.ExpiryUnix).UtcDateTime:yyyy-MM-dd}.");
}

static string Arg(string[] a, string key)
{
    for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
    return null;
}
