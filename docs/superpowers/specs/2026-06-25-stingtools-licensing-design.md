# StingTools Offline Machine-Locked Licensing — Design Spec

**Date:** 2026-06-25
**Status:** Approved (brainstorm), pending spec review
**Scope:** v1 licensing for the StingTools Revit plugin

## 1. Goal

Stop a STING install from being copied/reused on a second machine, and
make each license valid for **one year**. Enforcement must work **fully
offline** (machines are often offline / on unreliable internet). When no
valid license is present the plugin **hard-locks** (no panels, no
commands).

This is deterrence, not unbreakable DRM — it pairs later with the
deferred obfuscation layer (see `[[project-stingtools-ip-protection]]`).

## 2. Requirements (locked)

| # | Requirement |
|---|---|
| R1 | License validated **100% offline** — no mandatory server contact, ever. |
| R2 | License is **machine-bound** — a `.lic` minted for machine A fails on machine B. |
| R3 | License has a **1-year expiry**; hard stop at expiry (see §12 limit re: clock). |
| R4 | No valid license ⇒ **hard lock**: panels + ribbon not registered, commands refuse to run; a dialog shows the machine code + how to activate. |
| R5 | **Strict multi-factor fingerprint** (exact match of a composite of hardware factors). False lockout on hardware change is accepted; a clean **reissue path** is required. |
| R6 | Tamper-proof: a user cannot edit the expiry or machine code without invalidating the license. |
| R7 | Issuance controlled solely by the vendor (Planscape) and works without any live infrastructure. |

Out of scope for v1 (YAGNI): clock-rollback guard, online activation, remote revoke, feature/edition flags, per-Revit-version locking, transfer self-service.

## 3. Architecture overview

Three units, clear boundaries:

1. **`LicenseGate`** (in `StingTools`, runtime) — the only thing the rest of the plugin talks to. Answers `IsLicensed`, exposes `MachineCode`, `Status`, `ExpiryUtc`, and `Apply(licenseText|path)`. Pure verification; holds the embedded **public** key. No private key, no signing.
2. **`MachineFingerprint`** (in `StingTools`, runtime) — produces the composite `machineCode` string. Depends only on OS/WMI/registry. No license knowledge.
3. **`StingTools.LicenseIssuer`** (separate console project, **never shipped**) — holds the **private** key; mints signed `.lic` files from a machine code + duration. The only component that can create licenses.

Enforcement is wired in `StingToolsApp.OnStartup` (registration gate) and `StingCommandHandler.Execute` (defense-in-depth).

```
[MachineFingerprint] --machineCode--> [LicenseGate] <--verify-- embedded public key
                                          ^
                                          | reads StingTools.lic (ProgramData)
                                          |
 vendor: [LicenseIssuer + private key] --mints--> StingTools.lic
```

## 4. Cryptographic model

- **RSA-2048** key pair. Private key generated once, stored offline by vendor, **never in the repo or shipped DLL**. Public key embedded in `StingTools` (constant or embedded resource).
- A license is a UTF-8 JSON payload:
  ```json
  {
    "licenseId": "GUID",
    "machineCode": "XXXX-XXXX-XXXX-XXXX-XXXX",
    "licensee": "Tester 1 - <name>",
    "issuedUtc": "2026-06-25T08:00:00Z",
    "expiryUtc": "2027-06-25T08:00:00Z",
    "schema": 1
  }
  ```
- The `.lic` file = base64(payload) + "." + base64(RSA-SHA256 signature of payload bytes). (Compact, paste-friendly, tamper-evident.)
- Validation order in `LicenseGate`:
  1. Parse + split payload / signature.
  2. **Verify signature** with embedded public key. Fail ⇒ invalid.
  3. `payload.machineCode == MachineFingerprint.Current` (exact). Fail ⇒ wrong machine.
  4. `nowUtc < expiryUtc`. Fail ⇒ expired.
  5. All pass ⇒ licensed. Cache result for the session.

## 5. Machine fingerprint (strict, multi-factor)

`machineCode` = first 20 hex chars of SHA-256 over a normalized composite string, grouped as `XXXX-XXXX-XXXX-XXXX-XXXX`. Factors (all exact-match; order fixed):

1. Windows `MachineGuid` — `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`.
2. CPU processor id — WMI `Win32_Processor.ProcessorId`.
3. Baseboard serial — WMI `Win32_BaseBoard.SerialNumber`.
4. BIOS serial — WMI `Win32_BIOS.SerialNumber`.

Normalization: trim, uppercase, drop blanks/`"To be filled by O.E.M."`-style junk; if a factor is empty it contributes a fixed `"NA"` token (so missing WMI fields are deterministic, not random). At least **MachineGuid + one hardware factor** must be non-NA or the gate reports an "unsupported machine" error rather than a weak code.

**Consequence (accepted):** swapping the motherboard / CPU / re-imaging the OS changes the code ⇒ existing `.lic` stops validating ⇒ user must request a reissue. The activation dialog always shows the current code so reissue is a copy-paste round trip.

## 6. License storage & application

- Active license: `%ProgramData%\Planscape\StingTools\StingTools.lic` (per-machine, all Windows users). Created if missing.
- **Apply** paths:
  - Drop `StingTools.lic` into that folder, or
  - Paste the license string into the activation dialog → `LicenseGate.Apply` writes it to the canonical path after a successful validate.

## 7. Enforcement points

1. **`StingToolsApp.OnStartup`**:
   - Compute fingerprint, run `LicenseGate`.
   - **Licensed** ⇒ register panels + ribbon as today; if within 30 days of expiry, set a status-bar "STING license expires in N days" note.
   - **Not licensed** ⇒ do **not** register the dockable panels or the full ribbon. Register only a single ribbon button **"Activate STING"** that opens the activation dialog. Log the reason.
2. **`StingCommandHandler.Execute`** and command entry points: first line checks `LicenseGate.IsLicensed`; if false, show the activation dialog and abort. Prevents running commands via journal/API while unlicensed.

## 8. Activation dialog (WPF)

Single modal window, shown on hard-lock or from "Activate STING":
- Title + short explanation.
- **Machine code** in a large read-only field + **Copy** button + **Save to file** button.
- Instruction line: "Send this code to Planscape (support@planscape.app) to receive your license."
- **Apply license**: a paste box + **Browse for .lic** button → calls `LicenseGate.Apply` → on success, shows "Activated — restart Revit" (panels register on next start).
- Status line showing current state (Not activated / Expired on <date> / Wrong machine / Active until <date>).

## 9. Issuer tool (`StingTools.LicenseIssuer`)

Separate console project, excluded from the shipped package and from the addin.
- `keygen` → generates the RSA key pair; writes `private.pem` (vendor-only, git-ignored) and prints/writes the public key in the format the plugin embeds.
- `issue --code <machineCode> --name "<licensee>" --days 365 [--out StingTools.lic]` → builds payload, signs with `private.pem`, writes the `.lic`. Appends a row to `issued-licenses.csv` (machineCode, licensee, issued, expiry, licenseId) for the vendor's records.
- Refuses to run without `private.pem`. README warns: never commit the private key; keep a backup (lost key ⇒ cannot mint or renew).

## 10. Rollout for today's two testers

1. Implement gate + dialog + issuer; generate the RSA key pair (private key kept by vendor, public key embedded).
2. Build + repackage the deploy zip (gate enforced).
3. Each tester installs → launches Revit → STING is **hard-locked**, dialog shows their **machine code** → they send the code back.
4. Vendor runs `issue --code <theirs> --days 365` twice → two `StingTools.lic`.
5. Testers apply the `.lic` (paste or drop) → restart → unlocked for one year. Testing proceeds.

## 11. Testing strategy

- **Unit (LicenseIssuer + gate logic, no Revit):** sign a payload, verify; tamper a byte ⇒ invalid; wrong machineCode ⇒ rejected; expiry in past ⇒ rejected; valid ⇒ accepted. Run via the existing test project pattern (`StingTools.*.Tests`).
- **Fingerprint determinism:** same machine returns same code across runs; missing WMI factor ⇒ deterministic `NA` token, not a random code.
- **Manual in Revit:** no license ⇒ panels absent + only "Activate STING"; apply valid ⇒ panels appear after restart; corrupt `.lic` ⇒ clear error.

## 12. Limits (honest)

- Decompiling + patching out the check defeats it — mitigated later by obfuscation (deferred).
- A **cloned disk image** reproduces all factors ⇒ shares the license. Multi-factor narrows but doesn't eliminate this.
- **No clock-rollback guard (v1, by choice):** a user can set the system clock back to before `expiryUtc` to keep running past one year. Accepted for v1; can be added later if it becomes a problem.
- Strict fingerprint ⇒ legitimate hardware changes need a reissue (by design).

## 13. Files (planned)

- `StingTools/Core/Licensing/LicenseGate.cs`
- `StingTools/Core/Licensing/MachineFingerprint.cs`
- `StingTools/Core/Licensing/LicensePayload.cs` (POCO + parse/format)
- `StingTools/UI/ActivationDialog.cs` (WPF)
- `StingTools/Core/StingToolsApp.cs` (enforcement wiring — edit)
- `StingTools/UI/StingCommandHandler.cs` (guard — edit)
- `StingTools.LicenseIssuer/` (new console project, not shipped)
- Public key embedded in `StingTools`; private key stored offline by vendor, git-ignored.
