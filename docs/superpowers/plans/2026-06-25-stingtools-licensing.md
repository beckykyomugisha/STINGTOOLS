# StingTools Licensing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Machine-locked, 1-year, offline-validated license that hard-locks the StingTools plugin when absent; plus a vendor-only console tool to mint license files.

**Architecture:** Pure crypto/payload/fingerprint logic lives in `StingTools/Core/Licensing/` as Revit-free `.cs` files (so it unit-tests via the repo's `<Compile Include>` pattern). `MachineFingerprint` (WMI/registry) and `LicenseGate` (file IO) sit on top in the same folder. Enforcement wires into `StingToolsApp.OnStartup` (hard-lock) and `StingCommandHandler.Execute` (defense-in-depth). A separate, never-shipped `StingTools.LicenseIssuer` console signs licenses with the RSA private key.

**Tech Stack:** .NET 8, `System.Security.Cryptography` (RSA-2048, SHA-256), `System.Text.Json`, `System.Management` (WMI), `Microsoft.Win32` (registry), xUnit 2.9.2.

Spec: `docs/superpowers/specs/2026-06-25-stingtools-licensing-design.md`

---

## File map

| File | Responsibility | Revit dep? | Tested? |
|---|---|---|---|
| `StingTools/Core/Licensing/FingerprintComposer.cs` | normalize + hash factors → grouped code | no | yes |
| `StingTools/Core/Licensing/LicensePayload.cs` | POCO + JSON to/from | no | yes |
| `StingTools/Core/Licensing/LicenseCrypto.cs` | RSA sign / verify+extract | no | yes |
| `StingTools/Core/Licensing/LicenseVerifier.cs` | full validation rules | no | yes |
| `StingTools/Core/Licensing/LicensePublicKey.cs` | embedded public key constant | no | no |
| `StingTools/Core/Licensing/MachineFingerprint.cs` | collect Windows factors | yes (Win) | no |
| `StingTools/Core/Licensing/LicenseGate.cs` | read `.lic`, cache, Apply | no (file IO) | no |
| `StingTools/UI/ActivationDialog.cs` | WPF activate window | yes (WPF) | no |
| `StingTools/Commands/Licensing/ActivateStingCommand.cs` | ribbon command → dialog | yes | no |
| `StingTools.Licensing.Tests/` | unit tests for the 4 pure files | no | — |
| `StingTools.LicenseIssuer/` | keygen + issue console (not shipped) | no | no |

Edits: `StingTools/Core/StingToolsApp.cs` (gate), `StingTools/UI/StingCommandHandler.cs` (guard), `StingTools/StingTools.csproj` (System.Management + keep-target), `.gitignore` (private.pem).

---

## Task 1: FingerprintComposer (pure) + test project

**Files:**
- Create: `StingTools/Core/Licensing/FingerprintComposer.cs`
- Create: `StingTools.Licensing.Tests/StingTools.Licensing.Tests.csproj`
- Test: `StingTools.Licensing.Tests/FingerprintComposerTests.cs`

- [ ] **Step 1: Create the implementation**

```csharp
// StingTools/Core/Licensing/FingerprintComposer.cs
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
```

- [ ] **Step 2: Create the test project**

```xml
<!-- StingTools.Licensing.Tests/StingTools.Licensing.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="..\StingTools\Core\Licensing\FingerprintComposer.cs" Link="Licensing\FingerprintComposer.cs" />
    <Compile Include="..\StingTools\Core\Licensing\LicensePayload.cs" Link="Licensing\LicensePayload.cs" />
    <Compile Include="..\StingTools\Core\Licensing\LicenseCrypto.cs" Link="Licensing\LicenseCrypto.cs" />
    <Compile Include="..\StingTools\Core\Licensing\LicenseVerifier.cs" Link="Licensing\LicenseVerifier.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the failing test**

```csharp
// StingTools.Licensing.Tests/FingerprintComposerTests.cs
using System.Collections.Generic;
using StingTools.Core.Licensing;
using Xunit;

public class FingerprintComposerTests
{
    [Fact] public void Compute_is_deterministic()
    {
        var a = FingerprintComposer.Compute(new[] { "GUID-1", "CPU-1", "BB-1", "BIOS-1" });
        var b = FingerprintComposer.Compute(new[] { "guid-1", " CPU-1 ", "BB-1", "BIOS-1" });
        Assert.Equal(a, b); // normalization makes these identical
    }

    [Fact] public void Compute_changes_when_any_factor_changes()
    {
        var a = FingerprintComposer.Compute(new[] { "G", "C", "B", "I" });
        var c = FingerprintComposer.Compute(new[] { "G", "C", "B", "X" });
        Assert.NotEqual(a, c);
    }

    [Fact] public void Compute_format_is_five_groups_of_four()
    {
        var code = FingerprintComposer.Compute(new[] { "G", "C", "B", "I" });
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", code);
    }

    [Fact] public void Empty_and_oem_junk_count_as_NA()
    {
        Assert.Equal(0, FingerprintComposer.RealFactorCount(new[] { "", "  ", "To be filled by O.E.M.", "Default string" }));
        Assert.Equal(1, FingerprintComposer.RealFactorCount(new[] { "REAL", "None", "0", "" }));
    }
}
```

- [ ] **Step 4: Run tests** — Run: `dotnet test StingTools.Licensing.Tests/StingTools.Licensing.Tests.csproj` — Expected: build fails first because `LicensePayload.cs`/`LicenseCrypto.cs`/`LicenseVerifier.cs` don't exist yet. To run Task 1 alone, temporarily comment the three missing `<Compile Include>` lines, run, see 4 PASS, then restore them. (They're added in Tasks 2–4.)

- [ ] **Step 5: Commit** — `git add StingTools/Core/Licensing/FingerprintComposer.cs StingTools.Licensing.Tests` then `git commit -m "feat(license): machine fingerprint composer + test project"`

---

## Task 2: LicensePayload (pure)

**Files:**
- Create: `StingTools/Core/Licensing/LicensePayload.cs`
- Test: `StingTools.Licensing.Tests/LicensePayloadTests.cs`

- [ ] **Step 1: Implementation**

```csharp
// StingTools/Core/Licensing/LicensePayload.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StingTools.Core.Licensing
{
    public sealed class LicensePayload
    {
        [JsonPropertyName("licenseId")]  public string LicenseId { get; set; }
        [JsonPropertyName("machineCode")] public string MachineCode { get; set; }
        [JsonPropertyName("licensee")]   public string Licensee { get; set; }
        [JsonPropertyName("issuedUnix")] public long IssuedUnix { get; set; }
        [JsonPropertyName("expiryUnix")] public long ExpiryUnix { get; set; }
        [JsonPropertyName("schema")]     public int Schema { get; set; } = 1;

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { WriteIndented = false };
        public string ToJson() => JsonSerializer.Serialize(this, Opts);
        public static LicensePayload FromJson(string json) => JsonSerializer.Deserialize<LicensePayload>(json);
    }
}
```

- [ ] **Step 2: Failing test**

```csharp
// StingTools.Licensing.Tests/LicensePayloadTests.cs
using StingTools.Core.Licensing;
using Xunit;

public class LicensePayloadTests
{
    [Fact] public void Roundtrips_through_json()
    {
        var p = new LicensePayload { LicenseId = "id1", MachineCode = "AAAA-BBBB", Licensee = "T1", IssuedUnix = 100, ExpiryUnix = 200, Schema = 1 };
        var back = LicensePayload.FromJson(p.ToJson());
        Assert.Equal("AAAA-BBBB", back.MachineCode);
        Assert.Equal(200, back.ExpiryUnix);
        Assert.Equal("T1", back.Licensee);
    }
}
```

- [ ] **Step 3: Run** — `dotnet test StingTools.Licensing.Tests/StingTools.Licensing.Tests.csproj --filter LicensePayloadTests` — Expected: PASS (re-add this `<Compile Include>` line if it was commented in Task 1).

- [ ] **Step 4: Commit** — `git add StingTools/Core/Licensing/LicensePayload.cs StingTools.Licensing.Tests/LicensePayloadTests.cs && git commit -m "feat(license): signed license payload model"`

---

## Task 3: LicenseCrypto (RSA sign/verify)

**Files:**
- Create: `StingTools/Core/Licensing/LicenseCrypto.cs`
- Test: `StingTools.Licensing.Tests/LicenseCryptoTests.cs`

- [ ] **Step 1: Implementation**

```csharp
// StingTools/Core/Licensing/LicenseCrypto.cs
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
```

- [ ] **Step 2: Failing test**

```csharp
// StingTools.Licensing.Tests/LicenseCryptoTests.cs
using System.Security.Cryptography;
using StingTools.Core.Licensing;
using Xunit;

public class LicenseCryptoTests
{
    private static (string priv, string pub) Keys()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }

    [Fact] public void Valid_signature_extracts_payload()
    {
        var (priv, pub) = Keys();
        var lic = LicenseCrypto.Sign("{\"x\":1}", priv);
        Assert.Equal("{\"x\":1}", LicenseCrypto.VerifyAndExtract(lic, pub));
    }

    [Fact] public void Tampered_payload_rejected()
    {
        var (priv, pub) = Keys();
        var lic = LicenseCrypto.Sign("{\"x\":1}", priv);
        var tampered = "eyJ4IjoyfQ==" + lic.Substring(lic.IndexOf('.')); // swap payload, keep sig
        Assert.Null(LicenseCrypto.VerifyAndExtract(tampered, pub));
    }

    [Fact] public void Wrong_public_key_rejected()
    {
        var (priv, _) = Keys();
        var (_, otherPub) = Keys();
        var lic = LicenseCrypto.Sign("{\"x\":1}", priv);
        Assert.Null(LicenseCrypto.VerifyAndExtract(lic, otherPub));
    }

    [Fact] public void Garbage_rejected()
    {
        var (_, pub) = Keys();
        Assert.Null(LicenseCrypto.VerifyAndExtract("not-a-license", pub));
        Assert.Null(LicenseCrypto.VerifyAndExtract("", pub));
    }
}
```

- [ ] **Step 3: Run** — `dotnet test StingTools.Licensing.Tests/StingTools.Licensing.Tests.csproj --filter LicenseCryptoTests` — Expected: 4 PASS.

- [ ] **Step 4: Commit** — `git add StingTools/Core/Licensing/LicenseCrypto.cs StingTools.Licensing.Tests/LicenseCryptoTests.cs && git commit -m "feat(license): RSA sign/verify"`

---

## Task 4: LicenseVerifier (rules)

**Files:**
- Create: `StingTools/Core/Licensing/LicenseVerifier.cs`
- Test: `StingTools.Licensing.Tests/LicenseVerifierTests.cs`

- [ ] **Step 1: Implementation**

```csharp
// StingTools/Core/Licensing/LicenseVerifier.cs
using System;

namespace StingTools.Core.Licensing
{
    public enum LicenseState { Valid, NoLicense, BadSignature, WrongMachine, Expired, Malformed }

    public sealed class LicenseResult
    {
        public LicenseState State;
        public string Licensee;
        public DateTimeOffset? Expiry;
        public string Message;
        public bool IsValid => State == LicenseState.Valid;
    }

    public static class LicenseVerifier
    {
        public static LicenseResult Verify(string licenseText, string publicKeyPem,
                                           string machineCode, DateTimeOffset nowUtc)
        {
            if (string.IsNullOrWhiteSpace(licenseText))
                return new LicenseResult { State = LicenseState.NoLicense, Message = "Not activated." };

            string json = LicenseCrypto.VerifyAndExtract(licenseText, publicKeyPem);
            if (json == null)
                return new LicenseResult { State = LicenseState.BadSignature, Message = "License signature invalid or corrupted." };

            LicensePayload p;
            try { p = LicensePayload.FromJson(json); } catch { p = null; }
            if (p == null || string.IsNullOrEmpty(p.MachineCode))
                return new LicenseResult { State = LicenseState.Malformed, Message = "License content unreadable." };

            if (!string.Equals(p.MachineCode, machineCode, StringComparison.OrdinalIgnoreCase))
                return new LicenseResult { State = LicenseState.WrongMachine, Licensee = p.Licensee,
                    Message = "This license is for a different machine." };

            var expiry = DateTimeOffset.FromUnixTimeSeconds(p.ExpiryUnix);
            if (nowUtc >= expiry)
                return new LicenseResult { State = LicenseState.Expired, Expiry = expiry, Licensee = p.Licensee,
                    Message = "License expired on " + expiry.UtcDateTime.ToString("yyyy-MM-dd") + "." };

            return new LicenseResult { State = LicenseState.Valid, Expiry = expiry, Licensee = p.Licensee,
                Message = "Active until " + expiry.UtcDateTime.ToString("yyyy-MM-dd") + "." };
        }
    }
}
```

- [ ] **Step 2: Failing test**

```csharp
// StingTools.Licensing.Tests/LicenseVerifierTests.cs
using System;
using System.Security.Cryptography;
using StingTools.Core.Licensing;
using Xunit;

public class LicenseVerifierTests
{
    private const string Machine = "AAAA-BBBB-CCCC-DDDD-EEEE";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

    private static (string priv, string pub) Keys()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }
    private static string Mint(string priv, string machine, long expiryUnix) =>
        LicenseCrypto.Sign(new LicensePayload { LicenseId = "id", MachineCode = machine,
            Licensee = "T", IssuedUnix = 0, ExpiryUnix = expiryUnix, Schema = 1 }.ToJson(), priv);

    [Fact] public void Valid_license_accepted()
    {
        var (priv, pub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, Machine, 2_000_000), pub, Machine, Now);
        Assert.Equal(LicenseState.Valid, r.State);
    }
    [Fact] public void Empty_is_NoLicense()
    {
        var (_, pub) = Keys();
        Assert.Equal(LicenseState.NoLicense, LicenseVerifier.Verify("", pub, Machine, Now).State);
    }
    [Fact] public void Wrong_machine_rejected()
    {
        var (priv, pub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, "ZZZZ", 2_000_000), pub, Machine, Now);
        Assert.Equal(LicenseState.WrongMachine, r.State);
    }
    [Fact] public void Expired_rejected()
    {
        var (priv, pub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, Machine, 500_000), pub, Machine, Now);
        Assert.Equal(LicenseState.Expired, r.State);
    }
    [Fact] public void Bad_signature_rejected()
    {
        var (priv, _) = Keys();
        var (_, otherPub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, Machine, 2_000_000), otherPub, Machine, Now);
        Assert.Equal(LicenseState.BadSignature, r.State);
    }
}
```

- [ ] **Step 3: Run** — `dotnet test StingTools.Licensing.Tests/StingTools.Licensing.Tests.csproj` — Expected: ALL tests across all 4 files PASS.

- [ ] **Step 4: Commit** — `git add StingTools/Core/Licensing/LicenseVerifier.cs StingTools.Licensing.Tests/LicenseVerifierTests.cs && git commit -m "feat(license): verifier rules + full unit coverage"`

---

## Task 5: LicenseIssuer console (keygen + issue)

**Files:**
- Create: `StingTools.LicenseIssuer/StingTools.LicenseIssuer.csproj`
- Create: `StingTools.LicenseIssuer/Program.cs`
- Modify: `.gitignore` (append `private.pem` + `issued-licenses.csv`)

- [ ] **Step 1: csproj (links the two pure files, ships nothing)**

```xml
<!-- StingTools.LicenseIssuer/StingTools.LicenseIssuer.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <AssemblyName>StingLicenseIssuer</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\StingTools\Core\Licensing\LicensePayload.cs" Link="LicensePayload.cs" />
    <Compile Include="..\StingTools\Core\Licensing\LicenseCrypto.cs" Link="LicenseCrypto.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Program.cs**

```csharp
// StingTools.LicenseIssuer/Program.cs
using System;
using System.IO;
using System.Security.Cryptography;
using StingTools.Core.Licensing;

if (args.Length == 0) { Help(); return; }

switch (args[0].ToLowerInvariant())
{
    case "keygen": KeyGen(); break;
    case "issue":  Issue(args); break;
    default: Help(); break;
}

static void Help()
{
    Console.WriteLine("StingLicenseIssuer keygen");
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
```

- [ ] **Step 3: Append to `.gitignore`**

```
# Licensing — never commit the signing key or the issued-license log
private.pem
public.pem
issued-licenses.csv
StingTools.lic
```

- [ ] **Step 4: Build + generate keypair** — Run: `dotnet build StingTools.LicenseIssuer/StingTools.LicenseIssuer.csproj -c Release` (Expected: Build succeeded), then from `StingTools.LicenseIssuer/`: `dotnet run -c Release -- keygen` (Expected: writes private.pem + public.pem, prints the public key). **Save a backup of `private.pem` outside the repo.**

- [ ] **Step 5: Commit** — `git add StingTools.LicenseIssuer/Program.cs StingTools.LicenseIssuer/StingTools.LicenseIssuer.csproj .gitignore && git commit -m "feat(license): vendor issuer console (keygen + issue)"` (private.pem/public.pem are git-ignored and won't be added)

---

## Task 6: Embed the public key

**Files:**
- Create: `StingTools/Core/Licensing/LicensePublicKey.cs`

- [ ] **Step 1: Create the file with the key printed by Task 5 Step 4**

```csharp
// StingTools/Core/Licensing/LicensePublicKey.cs
namespace StingTools.Core.Licensing
{
    /// <summary>Verify-only RSA public key. Safe to ship. Paste from `LicenseIssuer keygen`.</summary>
    internal static class LicensePublicKey
    {
        public const string Pem =
@"-----BEGIN PUBLIC KEY-----
PASTE_THE_PUBLIC_KEY_LINES_FROM_KEYGEN_HERE
-----END PUBLIC KEY-----";
    }
}
```

- [ ] **Step 2: Commit** — `git add StingTools/Core/Licensing/LicensePublicKey.cs && git commit -m "feat(license): embed verification public key"`

---

## Task 7: MachineFingerprint (Windows collector) + csproj wiring

**Files:**
- Create: `StingTools/Core/Licensing/MachineFingerprint.cs`
- Modify: `StingTools/StingTools.csproj` (add `System.Management`; keep it in output)

- [ ] **Step 1: Implementation**

```csharp
// StingTools/Core/Licensing/MachineFingerprint.cs
using System;
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
```

- [ ] **Step 2: Add the package** — in `StingTools/StingTools.csproj`, inside the main `<ItemGroup>` of PackageReferences (after the `PdfSharp` line), add:

```xml
    <!-- Licensing: WMI hardware factors for the machine fingerprint. -->
    <PackageReference Include="System.Management" Version="8.0.0" />
```

- [ ] **Step 3: Keep System.Management in output** — in the `RemoveConflictingAssemblies` target, the condition strips files whose name starts with `System.` except Packaging/Pipelines. Add `System.Management` to the keep-list by editing the first sub-condition. Change:

```
($([System.String]::new('%(Filename)').StartsWith('System.')) And '%(Filename)' != 'System.IO.Packaging' And '%(Filename)' != 'System.IO.Pipelines') Or
```
to:
```
($([System.String]::new('%(Filename)').StartsWith('System.')) And '%(Filename)' != 'System.IO.Packaging' And '%(Filename)' != 'System.IO.Pipelines' And '%(Filename)' != 'System.Management') Or
```

- [ ] **Step 4: Build to verify** — Run: `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025" --nologo -v minimal` — Expected: Build succeeded, 0 errors. Then confirm `ls StingTools/bin/Release/System.Management.dll` exists.

- [ ] **Step 5: Commit** — `git add StingTools/Core/Licensing/MachineFingerprint.cs StingTools/StingTools.csproj && git commit -m "feat(license): Windows machine fingerprint + ship System.Management"`

---

## Task 8: LicenseGate

**Files:**
- Create: `StingTools/Core/Licensing/LicenseGate.cs`

- [ ] **Step 1: Implementation**

```csharp
// StingTools/Core/Licensing/LicenseGate.cs
using System;
using System.IO;

namespace StingTools.Core.Licensing
{
    public static class LicenseGate
    {
        private static LicenseResult _cached;

        public static string MachineCode => MachineFingerprint.Current;
        public static string LicenseDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Planscape", "StingTools");
        public static string LicensePath => Path.Combine(LicenseDir, "StingTools.lic");

        public static LicenseResult Status => _cached ??= Evaluate();
        public static bool IsLicensed => Status.IsValid;
        public static void Invalidate() => _cached = null;

        private static LicenseResult Evaluate()
        {
            string text = null;
            try { if (File.Exists(LicensePath)) text = File.ReadAllText(LicensePath).Trim(); }
            catch { /* unreadable => NoLicense */ }
            return LicenseVerifier.Verify(text, LicensePublicKey.Pem, MachineCode, DateTimeOffset.UtcNow);
        }

        /// <summary>Validate + persist a license string. Returns null on success, else error message.</summary>
        public static string Apply(string licenseText)
        {
            licenseText = (licenseText ?? "").Trim();
            var r = LicenseVerifier.Verify(licenseText, LicensePublicKey.Pem, MachineCode, DateTimeOffset.UtcNow);
            if (!r.IsValid) return r.Message;
            try
            {
                Directory.CreateDirectory(LicenseDir);
                File.WriteAllText(LicensePath, licenseText);
                Invalidate();
                return null;
            }
            catch (Exception ex) { return "Could not save license: " + ex.Message; }
        }
    }
}
```

- [ ] **Step 2: Build** — Run: `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025" --nologo -v minimal` — Expected: Build succeeded.

- [ ] **Step 3: Commit** — `git add StingTools/Core/Licensing/LicenseGate.cs && git commit -m "feat(license): license gate (read/cache/apply)"`

---

## Task 9: Activation dialog + command

**Files:**
- Create: `StingTools/UI/ActivationDialog.cs`
- Create: `StingTools/Commands/Licensing/ActivateStingCommand.cs`

- [ ] **Step 1: Dialog (WPF, code-only to match repo style)**

```csharp
// StingTools/UI/ActivationDialog.cs
using System.Windows;
using System.Windows.Controls;
using StingTools.Core.Licensing;

namespace StingTools.UI
{
    public static class ActivationDialog
    {
        public static void ShowModal()
        {
            var win = new Window
            {
                Title = "Activate STING Tools",
                Width = 540, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };
            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(new TextBlock {
                Text = "STING Tools is not activated on this machine.",
                FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,8) });
            root.Children.Add(new TextBlock {
                Text = "Send this machine code to Planscape (support@planscape.app) to receive your license file.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,8) });

            var codeBox = new TextBox {
                Text = LicenseGate.MachineCode, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 16, Margin = new Thickness(0,0,0,4) };
            root.Children.Add(codeBox);

            var copyBtn = new Button { Content = "Copy machine code", Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,0,0,12) };
            copyBtn.Click += (s, e) => { try { Clipboard.SetText(LicenseGate.MachineCode); } catch { } };
            root.Children.Add(copyBtn);

            root.Children.Add(new TextBlock { Text = "Paste your license below, then click Apply:", Margin = new Thickness(0,0,0,4) });
            var licBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(licBox);

            var status = new TextBlock { Margin = new Thickness(0,8,0,8), TextWrapping = TextWrapping.Wrap, Text = LicenseGate.Status.Message };
            root.Children.Add(status);

            var applyBtn = new Button { Content = "Apply license", Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
            applyBtn.Click += (s, e) =>
            {
                string err = LicenseGate.Apply(licBox.Text);
                if (err == null) { status.Text = "Activated. Please restart Revit to load STING."; status.Foreground = System.Windows.Media.Brushes.Green; }
                else { status.Text = err; status.Foreground = System.Windows.Media.Brushes.Red; }
            };
            root.Children.Add(applyBtn);

            win.Content = root;
            win.ShowDialog();
        }
    }
}
```

- [ ] **Step 2: Command**

```csharp
// StingTools/Commands/Licensing/ActivateStingCommand.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Commands.Licensing
{
    [Transaction(TransactionMode.ReadOnly)]
    public class ActivateStingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            StingTools.UI.ActivationDialog.ShowModal();
            return Result.Succeeded;
        }
    }
}
```

- [ ] **Step 3: Build** — Run: `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025" --nologo -v minimal` — Expected: Build succeeded.

- [ ] **Step 4: Commit** — `git add StingTools/UI/ActivationDialog.cs StingTools/Commands/Licensing/ActivateStingCommand.cs && git commit -m "feat(license): activation dialog + command"`

---

## Task 10: Enforcement wiring (hard-lock)

**Files:**
- Modify: `StingTools/Core/StingToolsApp.cs` (gate around panel registration; add `RegisterActivationButton`)
- Modify: `StingTools/UI/StingCommandHandler.cs` (guard in `Execute`)

- [ ] **Step 1: Gate in `OnStartup`** — in `StingTools/Core/StingToolsApp.cs`, immediately after `LogAssemblyEnvironment();` (line ~60), insert the early hard-lock:

```csharp
                // --- LICENSE GATE (hard lock) -------------------------------
                if (!StingTools.Core.Licensing.LicenseGate.IsLicensed)
                {
                    StingLog.Warn("STING not licensed (" + StingTools.Core.Licensing.LicenseGate.Status.Message +
                                  ") machineCode=" + StingTools.Core.Licensing.LicenseGate.MachineCode +
                                  " — panels withheld; Activate button only.");
                    try { EnsureStingRibbonTab(application); RegisterActivationButton(application); }
                    catch (System.Exception lex) { StingLog.Warn("Activation button: " + lex.Message); }
                    return Result.Succeeded;
                }
                // -----------------------------------------------------------
```

- [ ] **Step 2: Add `RegisterActivationButton`** — add this method to the `StingToolsApp` class (next to `EnsureStingRibbonTab`):

```csharp
        private void RegisterActivationButton(UIControlledApplication application)
        {
            var panel = application.CreateRibbonPanel("STING Tools", "Activation");
            var data = new PushButtonData(
                "STING_Activate", "Activate\nSTING",
                Assembly.GetExecutingAssembly().Location,
                "StingTools.Commands.Licensing.ActivateStingCommand")
            { ToolTip = "Activate STING Tools on this machine." };
            panel.AddItem(data);
        }
```

- [ ] **Step 3: Guard in `StingCommandHandler.Execute`** — in `StingTools/UI/StingCommandHandler.cs`, immediately after the empty-tag guard (`if (string.IsNullOrEmpty(tag)) return;`, line ~118), insert:

```csharp
            // License hard-lock: block every command except activation.
            if (!Core.Licensing.LicenseGate.IsLicensed && tag != "STING_Activate")
            {
                StingTools.UI.ActivationDialog.ShowModal();
                return;
            }
```

- [ ] **Step 4: Build** — Run: `dotnet build StingTools/StingTools.csproj -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025" --nologo -v minimal` — Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** — `git add StingTools/Core/StingToolsApp.cs StingTools/UI/StingCommandHandler.cs && git commit -m "feat(license): hard-lock enforcement in startup + command handler"`

---

## Task 11: Package + rollout the two testers

- [ ] **Step 1: Repackage** — Run: `bash extract_plugin.sh` then the staging+zip step (as used previously) to produce a fresh `StingTools_Deploy_<date>.zip`. Verify `System.Management.dll` is present in `CompiledPlugin/`.

- [ ] **Step 2: Install on each tester PC** — extract, run `install.bat`, restart Revit. Expected: STING is hard-locked; only an **Activate STING** ribbon button; the dialog shows that machine's code.

- [ ] **Step 3: Mint** — each tester sends their machine code. From `StingTools.LicenseIssuer/`: `dotnet run -c Release -- issue --code <THEIR-CODE> --name "Tester 1" --days 365 --out Tester1.lic`. Send `Tester1.lic` back.

- [ ] **Step 4: Activate** — tester pastes the license into the dialog → Apply → restart Revit → panels appear. Confirm "Active until <date>".

- [ ] **Step 5 (negative check):** copy one tester's `StingTools.lic` to the *other* machine → it must report "different machine" and stay locked.

---

## Self-review notes (author)

- Spec R1 offline ✓ (Tasks 4/8, no network). R2 machine-bound ✓ (Task 4 WrongMachine + Task 7). R3 1-year ✓ (issuer `--days 365`, Task 4 Expired). R4 hard-lock ✓ (Task 10). R5 strict multi-factor ✓ (Task 7, exact match). R6 tamper-proof ✓ (Task 3). R7 vendor-only offline issuance ✓ (Task 5).
- Method/type names consistent across tasks: `LicenseGate.IsLicensed`/`.Status`/`.Apply`/`.MachineCode`; `LicenseVerifier.Verify(text, pem, machineCode, nowUtc)`; `FingerprintComposer.Compute/Normalize/RealFactorCount`; tag string `STING_Activate` used identically in Tasks 9/10.
- No clock-rollback (removed by decision) — see spec §12 limit.
